using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ProofLab.Api;

// Standalone OFFLINE QA: no HTTP, no Meta API, no keys or spending.
if(args.Length<1 || !new[]{"--clips","--fast-clips","--benchmark"}.Contains(args[0]))
{
    Usage();return 2;
}
var ffmpeg=Path.Combine(Environment.GetFolderPath(
    Environment.SpecialFolder.LocalApplicationData),
    "Microsoft","WinGet","Links",
    OperatingSystem.IsWindows()?"ffmpeg.exe":"ffmpeg");
if(!File.Exists(ffmpeg))
{
    Console.Error.WriteLine("No local FFmpeg available.");
    return 2;
}
var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
    ["Media:FfmpegDirectory"]=Path.GetDirectoryName(ffmpeg)
}).Build();
var decoder=new VideoMediaDecoder(config);
if(!decoder.Available)
{
    Console.Error.WriteLine("FFmpeg/FFprobe unavailable.");
    return 2;
}

try
{
    if((args[0]=="--clips" || args[0]=="--fast-clips") && args.Length==4)
    {
        var source=Path.GetFullPath(args[1]);
        var sha=args[2];
        var folder=PrivatePath(args[3]);
        var manifest=args[0]=="--fast-clips"
            ? await new SamFourWayOnePassClips(decoder,ffmpeg).BuildAsync(
                source,folder,sha,CancellationToken.None)
            : await new SamFourWayClipBuilder(decoder,ffmpeg).BuildAsync(
                source,folder,sha,CancellationToken.None);
        var path=Path.Combine(folder,"frame-exact-validation.json");
        await WriteExclusive(path,JsonSerializer.Serialize(manifest,new JsonSerializerOptions{
            WriteIndented=true
        }));
        Console.WriteLine("OFFLINE_QA_COMPLETE SOURCE_FRAMES="+manifest.SourceFrames+
            " TOTAL_SUBMITTED_FRAMES="+manifest.TotalSubmittedFrames+
            " SAM_ESTIMATE_USD="+manifest.EstimatedSamCostUsd.ToString("F4",
                System.Globalization.CultureInfo.InvariantCulture));
        foreach(var clip in manifest.Clips)
            Console.WriteLine("PART="+clip.Part+" FRAMES="+clip.FrameCount+
                " PTS_MAX_ERROR_MS="+(clip.MaxAbsPtsErrorSeconds*1000).ToString("F3",
                    System.Globalization.CultureInfo.InvariantCulture)+
                " PIXEL_SHA_EXACT=true BYTES="+clip.FileBytes+
                " LOCAL_ENCODE_MS="+clip.EncodeElapsedMs);
        Console.WriteLine("PROCESSING_METHOD="+manifest.ProcessingMethod+
            " LOCAL_END_TO_END_MS="+manifest.EndToEndElapsedMs);
        Console.WriteLine("NO_META_CALLS=true");
        return 0;
    }
    if(args[0]=="--benchmark" && args.Length==7)
    {
        var source=Path.GetFullPath(args[1]);
        var sha=args[2].ToLowerInvariant();
        var responsePath=Path.GetFullPath(args[3]);
        var geometryPath=Path.GetFullPath(args[4]);
        var manifestPath=Path.GetFullPath(args[5]);
        var outputDir=PrivatePath(args[6]);
        Directory.CreateDirectory(outputDir);
        var pts=await decoder.ReadVideoFramePtsAsync(source,CancellationToken.None);
        var plan=SamFourWayPlan.Create(pts);
        var raw=await File.ReadAllTextAsync(responsePath);
        var text=SamFourWayMaskAssembler.OutputWire(raw);
        var geometry=JsonSerializer.Deserialize<OfflineReference>(
            await File.ReadAllTextAsync(geometryPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Geometry reference missing.");
        var manifest=JsonSerializer.Deserialize<SamFourWayClipManifest>(
            await File.ReadAllTextAsync(manifestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Clip manifest missing.");
        if(manifest.SourceFrames!=pts.Length ||
           manifest.SourcePtsSha256!=plan.SourcePtsSha256 ||
           !manifest.OriginalVideoSha256.Equals(sha,StringComparison.Ordinal) ||
           manifest.Clips.Count!=4 ||
           geometry.SourceFrameCount!=pts.Length ||
           geometry.Warnings!=0 || geometry.Rows.Count!=geometry.DetectionCount)
            throw new InvalidDataException("Reference video, clip, or mask metadata mismatch.");

        const char fakeSceneCharacter='b';
        var fakeScene=new string(fakeSceneCharacter,64); // NOT a Spark scene!
        var rawParts=SamFourWayMaskAssembler.PartitionReferenceFixture(
            plan,sha,fakeScene,raw);
        var segments=plan.Windows.Select(w=>new SamFourWaySegment(
            w.Part,sha,fakeScene,true,
            geometry.Rows
                .Where(r=>r.FrameIndex>=w.InputStart &&
                          r.FrameIndex<w.InputEndExclusive)
                .Select(r=>new SamFourWayDetection(
                    r.FrameIndex-w.InputStart,"p"+w.Part,
                    r.CentroidX,r.CentroidY,
                    r.Left,r.Top,r.Right,r.Bottom))
                .ToArray())).ToArray();
        // Exercise the same strict SSE event parser used by Meta transport.
        // Each of the four streams is reconstituted from the ONE archived
        // complete response: THIS IS AN OFFLINE REPLAY, not four Meta calls.
        var replayProgress=new System.Collections.Concurrent.ConcurrentDictionary<
            int,SamFourWayProgress>();
        var sseProgressMessages=0;
        var replayClock=System.Diagnostics.Stopwatch.StartNew();
        var merged=await SamFourWayStreaming.RunAsync(
            plan,pts,sha,fakeScene,
            async (window,publish,token)=>{
                await Task.Yield(); // start all four local replay tasks
                var textPart=SamFourWayMaskAssembler.OutputWire(
                    rawParts[window.Part].RawResponseJson);
                var parsed=await SamFourWayOfflineSseReplay.ReplayAsync(
                    rawParts[window.Part].RawResponseJson,publish,token);
                if(parsed.OutputText!=textPart)
                    throw new InvalidDataException(
                        "Offline replay stream changed provider mask bytes.");
                return segments[window.Part];
            },
            progress=>{
                replayProgress[progress.Part]=progress;
                if(progress.Stage=="SSE_DELTA_PROGRESS")
                    Interlocked.Increment(ref sseProgressMessages);
                return ValueTask.CompletedTask;
            },CancellationToken.None,offlineFixture:true);
        replayClock.Stop();
        if(replayProgress.Count!=4 || replayProgress.Values.Any(
            p=>p.Stage!="OFFLINE_CHUNK_REPLAY_COMPLETED") ||
            sseProgressMessages<4)
            throw new InvalidDataException(
                "Four offline SSE replays did not finish independently.");
        var assembly=SamFourWayMaskAssembler.Assemble(plan,merged,rawParts);
        var equal=string.Equals(text,assembly.WireOutputText,StringComparison.Ordinal);
        if(!equal || assembly.MaskFrames!=geometry.DetectionCount)
            throw new InvalidDataException("Full mask-wire payload did not round-trip.");
        var full=geometry.Rows.Select(r=>new SamFourWayFrame(
            r.FrameIndex,pts[r.FrameIndex],r.ObjectId,
            r.CentroidX,r.CentroidY,0)).ToArray();
        var benchmark=SamFourWayBenchmark.Compare(merged,full);
        if(benchmark.ComparedFrames!=geometry.DetectionCount ||
           !benchmark.SeamsVerified ||
           benchmark.MissingCandidateFrames!=0 ||
           benchmark.MissingReferenceFrames!=0 ||
           benchmark.P95CentroidErrorPx!=0)
            throw new InvalidDataException("Offline full-run mask geometry did not round-trip.");
        // This is a derived OFFLINE simulated repartition; never import it as
        // four provider responses or claim a live speed-up from it.
        await WriteExclusive(Path.Combine(outputDir,
            "reassembled-offline-reference-masks.json"),
            SamFourWayMaskAssembler.OfflineParserEnvelope(assembly));
        var wireSha=Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(assembly.WireOutputText))).ToLowerInvariant();
        var result=new {
            status="OFFLINE_REFERENCE_REPARTITION_VALIDATED",
            providerCalls=0,liveFourPartInference=false,
            interpretation="Masks were split and recomposed from ONE prior complete SAM provider result; no independent part provider inference was performed.",
            sourceSha256=sha,sourceFrames=pts.Length,
            maskFrames=assembly.MaskFrames,coverage=merged.Coverage,
            seams=merged.Seams,rawMasksUnmodified=true,fullWireHashMatch=equal,
            fullWireSha256=wireSha,comparison=benchmark,
            originalFullSamEstimatedUsd=Math.Max(50,pts.Length)*0.0002,
            parallelFourSamEstimatedUsd=plan.EstimatedSamCostUsd,
            fourClipLocalPrepareMeasuredMs=manifest.EndToEndElapsedMs>0
                ?manifest.EndToEndElapsedMs:manifest.Clips.Sum(c=>c.EncodeElapsedMs),
            fourClipProcessingMethod=manifest.ProcessingMethod,
            fourClipBytes=manifest.Clips.Select(c=>c.FileBytes).ToArray(),
            offlineFourPartSseReplay=true,
            offlineFourPartSseParser="SamVideoSseParser",
            offlineSseMessages=sseProgressMessages,
            offlineSseReplayElapsedMs=replayClock.ElapsedMilliseconds,
            fourOfflineSseReplaysCompleted=replayProgress.Count,
            fullReferenceUsedWithoutNewInference=true
        };
        await WriteExclusive(Path.Combine(outputDir,
            "offline-benchmark.json"),
            JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("OFFLINE_REFERENCE_REPARTITION_VALIDATED masks="+
            assembly.MaskFrames+" coverage="+merged.Coverage.ToString("F5",
                System.Globalization.CultureInfo.InvariantCulture)+
            " full_wire_sha256_match="+equal+" seams="+
            merged.Seams.Count(s=>s.IdentityVerified)+"/3 mean_pixel_error="+
            benchmark.MeanCentroidErrorPx+" real_parallel_provider_runs=0");
        Console.WriteLine("NO_META_CALLS=true FOUR_WAY_SPEEDUP_NOT_MEASURED=true");
        return 0;
    }
    Usage();return 2;
}
catch(Exception e)
{
    Console.Error.WriteLine("OFFLINE_QA_FAILED="+e.GetType().Name+
        " (private files and pending attempts remain untouched)");
    return 1;
}

static string PrivatePath(string input)
{
    var path=Path.GetFullPath(input);
    if(!path.Split(Path.DirectorySeparatorChar)
        .Contains(".private",StringComparer.OrdinalIgnoreCase))
        throw new ArgumentException("Output must stay inside ignored .private storage.");
    return path;
}
static async Task WriteExclusive(string filename,string text)
{
    await using var stream=new FileStream(filename,FileMode.CreateNew,
        FileAccess.Write,FileShare.None,65536,useAsync:true);
    var bytes=Encoding.UTF8.GetBytes(text);
    await stream.WriteAsync(bytes);
    await stream.FlushAsync();
    stream.Flush(flushToDisk:true);
}
static void Usage() =>
    Console.Error.WriteLine("Usage: --clips|--fast-clips <source> <sha> <private-dir> OR --benchmark <source> <sha> <full-response> <geometry> <clip-manifest> <private-dir>");

public sealed record OfflineGeometryRow(
    int FrameIndex,string ObjectId,double CentroidX,double CentroidY,
    double Left,double Top,double Right,double Bottom,int ForegroundPixels);
public sealed record OfflineReference(
    string Provenance,string SourceModel,int SourceFrameCount,
    int FrameWidth,int FrameHeight,int FramesWithMasks,int DetectionCount,
    int Warnings,IReadOnlyList<OfflineGeometryRow> Rows);
