using System.Text;
using System.Text.Json;

namespace ProofLab.Api;

public sealed record SamAdaptiveSeam(
    int LeftPart,int RightPart,int CommonMasks,int IdenticalMasks,
    int ContradictoryMasks,bool Verified);

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
/// Only runs over separately authenticated, completed provider responses,
/// after each part has a durable immutable HTTPS receipt. It does not make
/// model calls or infer masks. Exact overlap equality is a CONSERVATIVE
/// scientific criterion; differing genuine masks remain inconclusive until
/// raster IoU / human review is implemented.
/// </summary>
public static class SamAdaptiveMaskStitcher
{
    private sealed record WireFrame(int Global,int Local,string Payload);

    public static SamAdaptiveMaskResult Stitch(
        SamAdaptiveSegmentPlan plan,IReadOnlyList<SamAdaptivePartOutput> parts)
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
                if(!output.TryAdd(frame,"<"+frame+"f>0"+payload+"\n"))
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
            seams.Add(new(i,i+1,common.Length,equal,common.Length-equal,
                common.Length>=2 && common.Length==equal));
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
        var previous=-1;
        foreach(var lineText in wire.Split('\n'))
        {
            var line=lineText.TrimEnd('\r');
            if(line.Length==0)continue;
            if(line.Length>200_000 || line[0]!='<')
                throw new InvalidDataException("SAM frame line is too large or malformed.");
            var end=line.IndexOf("f>",StringComparison.Ordinal);
            if(end is <2 or >8 ||
               !int.TryParse(line.AsSpan(1,end-1),out var localFrame) ||
               localFrame<=previous || localFrame<0 ||
               localFrame>=window.InputFrames)
                throw new InvalidDataException("SAM local frame order/index mismatch.");
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
            var global=window.GlobalFrame(localFrame);
            if(!result.TryAdd(global,line[box..]))
                throw new InvalidDataException("SAM duplicate mask in global frame.");
            previous=localFrame;
        }
        return result;
    }
}
