using System.Text;
using System.Text.Json;

namespace ProofLab.Api;

public sealed record SamAdaptiveSeam(
    int LeftPart,int RightPart,int CommonMasks,int IdenticalMasks,
    int ContradictoryMasks,bool Verified,double? MinimumMaskIou=null);

public sealed record SamAdaptivePartOutput(
    int Part,int InputStart,int InputEndExclusive,
    int CoreStart,int CoreEndExclusive,string ResponseJsonSha256,
    string OriginalProviderResponseJson);

public sealed record SamAdaptiveMaskResult(
    string Provenance,int SourceFrames,int FramesWithMasks,int MissingCoreFrames,
    bool SeamsVerified,IReadOnlyList<SamAdaptiveSeam> Seams,
    string DerivedResponseJson,string DerivedResponseSha256)
{
    public bool SuitableForScientificView =>
        SeamsVerified && FramesWithMasks>0 && MissingCoreFrames<SourceFrames;
}

/// <summary>
/// Authenticated parts retain their pixels and core ownership. Stream arrival order
/// is not frame order. Overlaps require at least two matching observations and
/// raster IoU >= 0.90 on EVERY shared frame. This is a continuity check, not
/// scientific or human approval.
/// </summary>
public static class SamAdaptiveMaskStitcher
{
    public const double MinimumOverlapIou=0.90;
    public sealed record MaskPair(string Left,string Right);
    public static async Task<SamAdaptiveMaskResult> StitchAsync(
        SamAdaptiveSegmentPlan plan,IReadOnlyList<SamAdaptivePartOutput> parts,
        Microsoft.Extensions.Configuration.IConfiguration config,CancellationToken ct)
    {
        // Strict parsing/hash/window validation runs before the local worker.
        var pairs=new List<MaskPair>();
        _=Stitch(plan,parts,(a,b)=>{pairs.Add(new(a,b));return 0;});
        if(pairs.Count==0)return Stitch(plan,parts);
        var info=new System.Diagnostics.ProcessStartInfo(config["Media:NodeExecutable"]??"node"){
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,
            RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"Workers","sam-seam-worker.cjs"));
        using var process=new System.Diagnostics.Process{StartInfo=info};
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        process.Start();
        try {
            var output=process.StandardOutput.ReadToEndAsync(deadline.Token);
            var errors=process.StandardError.ReadToEndAsync(deadline.Token);
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(pairs).AsMemory(),deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            _=await errors;
            var text=await output;
            if(process.ExitCode!=0 || text.Length>100_000)
                throw new InvalidDataException("Segment raster verification failed.");
            var values=JsonSerializer.Deserialize<double[]>(text);
            if(values is null || values.Length!=pairs.Count ||
               values.Any(x=>!double.IsFinite(x)||x<0||x>1))
                throw new InvalidDataException("Invalid segment overlap metrics.");
            var index=0;
            return Stitch(plan,parts,(a,b)=>{
                if(pairs[index]!=new MaskPair(a,b))throw new InvalidDataException("Overlap order changed.");
                return values[index++];
            });
        } finally {if(!process.HasExited)try{process.Kill(entireProcessTree:true);}catch{}}
    }

    public static SamAdaptiveMaskResult Stitch(
        SamAdaptiveSegmentPlan plan,IReadOnlyList<SamAdaptivePartOutput> parts,
        Func<string,string,double>? overlapIou=null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(parts);
        if(parts.Count!=plan.PartCount)
            throw new InvalidDataException("Adaptive inference batch is incomplete.");
        var frameMaps=new Dictionary<int,string>[plan.PartCount];
        var output=new SortedDictionary<int,string>();
        for(var part=0;part<plan.PartCount;part++)
        {
            var window=plan.Windows[part];
            var source=parts.SingleOrDefault(p=>p.Part==part) ??
                throw new InvalidDataException("Missing provider part.");
            if(source.InputStart!=window.InputStart ||
               source.InputEndExclusive!=window.InputEndExclusive ||
               source.CoreStart!=window.CoreStart ||
               source.CoreEndExclusive!=window.CoreEndExclusive ||
               source.ResponseJsonSha256!=SamVideoArchive.Hash(
                   source.OriginalProviderResponseJson))
                throw new InvalidDataException("Part receipt, SHA or frame window mismatch.");
            var wire=SamFourWayMaskAssembler.OutputWire(
                source.OriginalProviderResponseJson);
            var parsed=Parse(wire,window);
            frameMaps[part]=parsed;
            foreach(var (frame,payload) in parsed)
            {
                if(frame<window.CoreStart || frame>=window.CoreEndExclusive)
                    continue;
                if(!output.TryAdd(frame,"<"+frame+"f>1"+payload+"\n"))
                    throw new InvalidDataException("Duplicate rebased core frame.");
            }
        }
        var seams=new List<SamAdaptiveSeam>(plan.PartCount-1);
        for(var i=0;i<plan.PartCount-1;i++)
        {
            var a=frameMaps[i];var b=frameMaps[i+1];
            var common=a.Keys.Intersect(b.Keys).ToArray();
            var equal=common.Count(frame=>
                string.Equals(a[frame],b[frame],StringComparison.Ordinal));
            var metrics=common.Select(frame=>a[frame]==b[frame]?1:
                overlapIou?.Invoke(a[frame],b[frame])??0).ToArray();
            var contradictory=metrics.Count(x=>!double.IsFinite(x)||x<MinimumOverlapIou||x>1);
            seams.Add(new(i,i+1,common.Length,equal,contradictory,
                common.Length>=2 && contradictory==0,
                metrics.Length>0?metrics.Min():null));
        }
        var allVerified=seams.All(s=>s.Verified);
        var wireCombined=string.Concat(output.Values);
        // A derived composition is NEVER a single authenticated native
        // provider response; each source receipt must remain linked.
        var response=JsonSerializer.Serialize(new {
            @object="response",model="sam-3.1",status="completed",
            output=new[]{new{
                type="message",content=new[]{
                    new {type="output_text",text=wireCombined}
                }
            }}
        });
        return new("DERIVED_FROM_INDEPENDENT_AUTHENTICATED_SAM_PARTS_PENDING_HUMAN_REVIEW",
            plan.SourceFrames,output.Count,plan.SourceFrames-output.Count,
            allVerified,seams,response,SamVideoArchive.Hash(response));
    }

    private static Dictionary<int,string> Parse(string wire,SamAdaptiveWindow window)
    {
        var result=new Dictionary<int,string>();
        string? objectId=null;
        foreach(var lineText in wire.Split('\n'))
        {
            var line=lineText.TrimEnd('\r');
            if(line.Length==0)continue;
            if(line.Length>200_000 || line[0]!='<')
                throw new InvalidDataException("SAM frame line is too large or malformed.");
            var end=line.IndexOf("f>",StringComparison.Ordinal);
            if(end is <2 or >8 ||
               !int.TryParse(line.AsSpan(1,end-1),out var localFrame) ||
               localFrame<0 ||
               localFrame>=window.InputFrames)
                throw new InvalidDataException("SAM local frame index outside the submitted segment.");
            var box=line.IndexOf("<|box;",end+2,StringComparison.Ordinal);
            var mask=line.IndexOf("<|mask;",end+2,StringComparison.Ordinal);
            if(box<=end+2 || mask<=box || mask>=line.Length-3 ||
               !line.EndsWith("|>",StringComparison.Ordinal))
                throw new InvalidDataException("SAM mask/box record malformed.");
            var id=line[(end+2)..box];
            if(id.Length is <1 or >32 ||
               id.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not ('-' or '_')) ||
               line.IndexOf("<|box;",box+1,StringComparison.Ordinal)>=0 ||
               line.IndexOf("<|mask;",mask+1,StringComparison.Ordinal)>=0)
                throw new InvalidDataException("Ambiguous SAM target in fragment.");
            if(objectId is not null && objectId!=id)
                throw new InvalidDataException("SAM object identity changed within a fragment.");
            objectId=id;
            var global=window.GlobalFrame(localFrame);
            if(!result.TryAdd(global,line[box..]))
                throw new InvalidDataException("SAM duplicate mask in global frame.");
        }
        return result;
    }
}
