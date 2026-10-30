using Microsoft.Extensions.Configuration;
using ProofLab.Api;

// Local-only clip preparation; NEVER contacts Meta or reserves credits.
var anchorsOnly=args.Length==3 && args[0]=="--verify-anchors";
if(!anchorsOnly && (args.Length is not (4 or 5) || args[0]!="--verify-clips"))
{
    Console.Error.WriteLine("Usage: --verify-clips <source> <SHA256> <private-target> [3] OR --verify-anchors <source> <private-target>");
    return 2;
}
var input=Path.GetFullPath(args[1]);
var sha=anchorsOnly?"":args[2].ToLowerInvariant();
var target=Path.GetFullPath(args[anchorsOnly?2:3]);
if(!target.Split(Path.DirectorySeparatorChar)
    .Contains(".private",StringComparer.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Invalid private output path.");
    return 2;
}
var ffDir=Path.Combine(Environment.GetFolderPath(
    Environment.SpecialFolder.LocalApplicationData),"Microsoft","WinGet","Links");
var config=new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string,string?>{
        ["Media:FfmpegDirectory"]=ffDir
    }).Build();
var decoder=new VideoMediaDecoder(config);
try
{
    var pts=await decoder.ReadVideoFramePtsAsync(input,CancellationToken.None);
    if(anchorsOnly)
    {
        if(Directory.Exists(target))throw new InvalidOperationException("Use a fresh private output directory.");
        var single=Path.Combine(target,"single");var batch=Path.Combine(target,"batch");
        Directory.CreateDirectory(single);Directory.CreateDirectory(batch);
        var samples=new[]{.1,.5,.9}.Select(f=>{var n=(int)Math.Round((pts.Length-1)*f);return(n,pts[n]);}).ToArray();
        var clock=System.Diagnostics.Stopwatch.StartNew();var expected=new KnotFrame[3];
        for(var i=0;i<3;i++)expected[i]=await decoder.ExtractAsync(input,Path.Combine(single,$"frame-{i}.jpg"),samples[i].n,samples[i].Item2,CancellationToken.None);
        var sequentialMs=clock.ElapsedMilliseconds;clock.Restart();
        var actual=await decoder.ExtractBatchAsync(input,batch,samples,CancellationToken.None);
        var batchMs=clock.ElapsedMilliseconds;
        if(!expected.SequenceEqual(actual))throw new InvalidDataException("Native batch reference bytes/indices/orientation changed.");
        Console.WriteLine("NATIVE_ANCHORS_MATCH=true SEQUENTIAL_MS="+sequentialMs+" BATCH_MS="+batchMs+
            " INDICES="+string.Join(",",actual.Select(f=>f.Index))+" WIDTH="+actual[0].Width+" HEIGHT="+actual[0].Height);
        Console.WriteLine("OFFLINE_ONLY NO_META_CALLS=true");return 0;
    }
    if(args.Length==5 && args[4]!="3")
        throw new ArgumentException("Native QA uses at most three keyframe-aligned parts.");
    var source=new SamVideoSource(sha,"offline",input,(await decoder.ProbeAsync(input,CancellationToken.None)).Codec,1920,1080,pts[^1]);
    var builder=new SamAdaptiveClipBuilder(decoder,config);
    var plan=await builder.PlanAsync(source,pts,null,53,8,CancellationToken.None);
    var manifest=await new SamAdaptiveClipBuilder(decoder,config).PrepareAsync(
        new SamVideoSource(sha,"offline-verified-source",input,"hevc",
            1920,1080,pts[^1]),
        pts,plan,target,CancellationToken.None);
    Console.WriteLine("FRAME_EXACT_ADAPTIVE_CLIPS_OK source="+manifest.OriginalFrames+
        " clips="+manifest.Clips.Count+
        " submitted="+manifest.SubmittedFrames+
        " billed="+manifest.BilledFrames+
        " cost="+manifest.EstimatedSamUsd.ToString("F4",
            System.Globalization.CultureInfo.InvariantCulture)+
        " prepareMs="+manifest.PrepareElapsedMs);
    foreach(var clip in manifest.Clips)
        Console.WriteLine("PART="+clip.Part+" FRAMES="+clip.Frames+
            " BYTES="+clip.Bytes+" PIXEL_SHA_MATCH=true"+
            " PTS_MAX_MS="+(clip.MaximumPtsErrorSeconds*1000).ToString(
                "F3",System.Globalization.CultureInfo.InvariantCulture));
    Console.WriteLine("OFFLINE_ONLY NO_META_CALLS=true");
    return 0;
}
catch(Exception ex)
{
    Console.Error.WriteLine("LOCAL_CLIP_QA_FAILED="+ex.GetType().Name+": "+ex.Message);
    return 1;
}
