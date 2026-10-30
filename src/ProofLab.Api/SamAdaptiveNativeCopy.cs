using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace ProofLab.Api;
public sealed partial class SamAdaptiveClipBuilder {
    public const string NativeProtocol="SAM_ORIGINAL_PACKET_COPY_KEYFRAMES_V1";
    private sealed record NativeTimeline(int[] Keys,int[] SafeEnds,double FirstPts);
    private readonly ConcurrentDictionary<string,NativeTimeline> nativeTimelines=new();
    private readonly ConcurrentDictionary<string,string[]> nativeHashes=new();
    private async Task<NativeTimeline> Timeline(SamVideoSource source,IReadOnlyList<double> pts,CancellationToken ct) {
        if(nativeTimelines.TryGetValue(source.Sha256,out var known))return known;
        var raw=await Call(Ffprobe,new[]{"-v","error","-select_streams","v:0","-show_entries","packet=pts_time,flags","-of","json","-i",source.Path},2_000_000,ct);
        using var doc=JsonDocument.Parse(raw);
        var packets=doc.RootElement.GetProperty("packets").EnumerateArray().Select(x=>(Time:double.Parse(x.GetProperty("pts_time").GetString()!,Inv),Key:x.GetProperty("flags").GetString()!.Contains('K'))).ToArray();
        var sorted=packets.OrderBy(p=>p.Time).ToArray();
        if(sorted.Length!=pts.Count || sorted.Where((p,i)=>Math.Abs(p.Time-sorted[0].Time-pts[i])>.005).Any())
            throw new InvalidDataException("Packet presentation timeline differs from original frames.");
        var indices=sorted.Select((p,i)=>(p.Time,Index:i)).ToDictionary(p=>p.Time,p=>p.Index);
        var keys=sorted.Select((p,i)=>(p.Key,i)).Where(p=>p.Key).Select(p=>p.i).ToArray();
        var safe=new List<int>();int maximum=-1,seen=0;
        foreach(var packet in packets) {maximum=Math.Max(maximum,indices[packet.Time]);seen++;if(seen==maximum+1)safe.Add(seen);}
        if(keys.Length==0 || keys[0]!=0)throw new InvalidDataException("Original has no initial random-access frame.");
        known=new(keys,safe.ToArray(),sorted[0].Time);nativeTimelines[source.Sha256]=known;return known;
    }
    public async Task<SamAdaptiveSegmentPlan> PlanAsync(SamVideoSource source,IReadOnlyList<double> pts,
        SamSceneDefinition? scene,int target,int concurrency,CancellationToken ct) {
        var timeline=await Timeline(source,pts,ct);
        return SamAdaptiveSegmentPlan.Native(pts.Count,timeline.Keys,timeline.SafeEnds,
            scene?.Seeds.Where(s=>s.Role=="object").Select(s=>s.Frame).ToArray(),target,Math.Min(concurrency,SamAdaptiveSegmentPlan.MaximumNewNativeParts),
            maximumParts:SamAdaptiveSegmentPlan.MaximumNewNativeParts);
    }
    private async Task<SamAdaptiveClipManifest> PrepareNativeAsync(SamVideoSource source,IReadOnlyList<double> pts,
        SamAdaptiveSegmentPlan plan,string folder,string sourceSha,SamOrientedFrameGeometry display,string ptsSha,
        Stopwatch timer,CancellationToken ct) {
        var timeline=await Timeline(source,pts,ct);
        if(plan.Windows.Any(w=>!timeline.Keys.Contains(w.InputStart)||!timeline.SafeEnds.Contains(w.InputEndExclusive)))
            throw new InvalidDataException("Native cuts must preserve original random-access and decode-order boundaries.");
        var args=new List<string>{"-hide_banner","-loglevel","error","-nostdin","-protocol_whitelist","file,pipe","-i",source.Path};
        // One compressed read for all planned original packet copies. Bitstream filters only
        // select packets and rebase timestamps; no decoder, encoder, scale or FPS filter.
        foreach(var w in plan.Windows) {
            var start=pts[w.InputStart]+timeline.FirstPts;
            var end=w.InputEndExclusive==pts.Count?pts[^1]+timeline.FirstPts+1:pts[w.InputEndExclusive]+timeline.FirstPts;
            var bsf="noise=drop='lt(pts*tb,"+(start-.000001).ToString("R",Inv)+")+gte(pts*tb,"+(end-.000001).ToString("R",Inv)+")',setts=pts=PTS-"+start.ToString("R",Inv)+"/TB:dts=DTS-"+start.ToString("R",Inv)+"/TB";
            args.AddRange(new[]{"-map","0:v:0","-an","-sn","-dn","-c:v","copy","-bsf:v",bsf,"-avoid_negative_ts","disabled","-movflags","+faststart","-n","-f","mp4",Path.Combine(folder,$"part-{w.Part:D2}.mp4")});
        }
        await Call(Ffmpeg,args,10000,ct);
        if(!nativeHashes.TryGetValue(source.Sha256,out var originals)) {
            originals=await FrameHashes(source.Path,ct);nativeHashes[source.Sha256]=originals;
        }
        if(originals.Length!=pts.Count)throw new InvalidDataException("Original pixel-frame count mismatch.");
        var clips=new SamAdaptiveClip[plan.PartCount];
        await Parallel.ForEachAsync(plan.Windows,new ParallelOptions {MaxDegreeOfParallelism=2,CancellationToken=ct},async(w,c)=> {
            var name=$"part-{w.Part:D2}.mp4";var path=Path.Combine(folder,name);var info=new FileInfo(path);
            if(info.Length is <1000 or >240_000_000 || (await ReadGeometry(path,c)).Display!=display)
                throw new InvalidDataException("Copied clip size or geometry mismatch.");
            var actual=await decoder.ReadVideoFramePtsAsync(path,c);
            if(actual.Length!=w.InputFrames)throw new InvalidDataException($"Native clip {w.Part} frame count {actual.Length} != {w.InputFrames}.");
            var worst=actual.Select((p,i)=>Math.Abs(p-(pts[w.InputStart+i]-pts[w.InputStart]))).Max();
            if(worst>.005)throw new InvalidDataException("Native clip changed original presentation times.");
            var hashes=await FrameHashes(path,c);var expected=originals.Skip(w.InputStart).Take(w.InputFrames).ToArray();
            if(!hashes.SequenceEqual(expected))throw new InvalidDataException($"Native clip {w.Part} changed original decoded pixels.");
            await using var stream=File.OpenRead(path);
            var sha=Convert.ToHexString(await SHA256.HashDataAsync(stream,c)).ToLowerInvariant();
            var hashSha=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",expected)))).ToLowerInvariant();
            clips[w.Part]=new(w.Part,w.InputStart,w.InputEndExclusive,w.CoreStart,w.CoreEndExclusive,w.InputFrames,name,info.Length,sha,hashSha,worst,w.BilledFrames);
        });
        var manifest=new SamAdaptiveClipManifest(sourceSha,ptsSha,pts.Count,plan.SubmittedFrames,plan.BilledFrames,plan.EstimatedSamCostUsd,NativeProtocol,clips,timer.ElapsedMilliseconds);
        await File.WriteAllTextAsync(Path.Combine(folder,"clip-manifest.json"),JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=true}),ct);
        return manifest;
    }
}
