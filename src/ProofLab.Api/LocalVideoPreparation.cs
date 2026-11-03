using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ProofLab.Api;

/// <summary>
/// Local, provider-free preparation shared by upload warmup, Spark and SAM.
/// One producer works at a time; cancelling an HTTP waiter never cancels it.
/// Only an atomic completed manifest makes immutable media reusable.
/// </summary>
public sealed class LocalVideoPreparation(IWebHostEnvironment environment,ISamVideoRuntime runtime,
    IKnotFrames frames,ISamAdaptiveClipBuilder clips,IHostApplicationLifetime lifetime,
    ILogger<LocalVideoPreparation> logger) : ILocalVideoPreparation
{
    public const string Version="LOCAL_FULL_VIDEO_FIFTY_BOUNDARIES_V1";
    private const string ContinuousProtocol="SAM_CONTINUOUS_ORIGINAL_REMUX_FILE_SHA_PTS_V2";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private readonly object sync=new();
    private readonly Dictionary<string,Job> jobs=new(StringComparer.Ordinal);
    private readonly SemaphoreSlim worker=new(1,1);
    private sealed class Job
    {
        public readonly Stopwatch Clock=Stopwatch.StartNew();
        public readonly TaskCompletionSource<LocalPreparedMedia> Completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string State="QUEUED",Phase="video";
        public int References;
    }
    private sealed record Manifest(string Version,string SourceSha256,string Generation,
        double[] SourcePts,KnotFrame[] Frames,SamAdaptiveClipManifest Video,long ElapsedMs);
    private string Root(SamVideoSource source)
    {
        if(source.Sha256.Length!=64 || source.Sha256.Any(c=>!Uri.IsHexDigit(c)))
            throw new InvalidDataException("Invalid original video identity.");
        return System.IO.Path.Combine(environment.ContentRootPath,".private","physics","local-media","v1",source.Sha256.ToLowerInvariant());
    }
    private static async Task<string> Hash(string path,CancellationToken ct)
    {
        await using var stream=File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream,ct)).ToLowerInvariant();
    }
    private static async Task VerifyOriginal(SamVideoSource source,CancellationToken ct)
    {
        if(await Hash(source.Path,ct)!=source.Sha256.ToLowerInvariant())
            throw new InvalidDataException("Original video changed during local preparation.");
    }
    private static SamAdaptiveSegmentPlan FullPlan(int count)=>SamAdaptiveSegmentPlan.RestoreNative(count,53,1,
        [new SamAdaptiveWindow(0,0,count,0,count)]);
    private static void RequireWhole(SamAdaptiveSegmentPlan plan,IReadOnlyList<double> pts)
    {
        _=plan.VerifySourcePts(pts);
        if(plan.PartCount!=1 || plan.Windows[0] is not {Part:0,InputStart:0,CoreStart:0} w ||
            w.InputEndExclusive!=pts.Count || w.CoreEndExclusive!=pts.Count || plan.SubmittedFrames!=pts.Count)
            throw new InvalidDataException("Shared local preparation requires the complete original video.");
    }
    private Job? GetJob(SamVideoSource source,bool queued,out bool reused)
    {
        var key=Root(source);
        lock(sync)
        {
            reused=jobs.TryGetValue(key,out var job);
            if(reused && (queued || job!.State!="FAILED"))return job;
            // Upload warmup is bounded to one active producer and four waiting jobs.
            // Explicit processing may wait for the worker, but never loses its request.
            if(queued && jobs.Values.Count(x=>!x.Completion.Task.IsCompleted)>=5)return null;
            if(jobs.Count>=64)
                foreach(var old in jobs.Where(p=>p.Value.Completion.Task.IsCompleted).Take(32).Select(p=>p.Key).ToArray())jobs.Remove(old);
            job=new Job();jobs[key]=job;reused=false;
            _=Task.Run(()=>Produce(source,job));
            return job;
        }
    }
    public void Queue(SamVideoSource source)
    {
        _=GetJob(source,true,out _);
    }
    public async Task<LocalPreparedMedia> PrepareAsync(SamVideoSource source,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var job=GetJob(source,false,out var reused)!;
        var media=await job.Completion.Task.WaitAsync(ct);
        // Recheck identities on every consumer, including completed in-memory jobs.
        // Polling status performs no hashes; use of the media always does.
        try {
            await VerifyOriginal(source,ct);
            await VerifyMedia(media,ct);
        } catch(Exception error) when(error is not OperationCanceledException) {
            Stage(job,"FAILED",job.Phase);
            throw;
        }
        return media with {Reused=reused || media.Reused,Frames=media.Frames.ToArray(),SourcePts=media.SourcePts.ToArray(),
            Manifest=media.Manifest with {Clips=media.Manifest.Clips.ToArray()}};
    }
    private void Stage(Job job,string state,string phase,int references=0)
    {
        lock(sync){job.State=state;job.Phase=phase;job.References=references;}
    }
    private async Task Produce(SamVideoSource source,Job job)
    {
        bool entered=false;
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
            deadline.CancelAfter(TimeSpan.FromMinutes(10));
            var ct=deadline.Token;
            await worker.WaitAsync(ct);entered=true;Stage(job,"RUNNING","video");
            var root=Root(source);System.IO.Directory.CreateDirectory(root);
            await using var held=await Lock(root,ct);
            var index=System.IO.Path.Combine(root,"ready.json");
            await VerifyOriginal(source,ct);
            LocalPreparedMedia result;
            if(File.Exists(index))
            {
                var saved=await Read(index,ct);result=Restore(root,source,saved,true);
                await VerifyMedia(result,ct);
            }
            else
            {
                var pts=await runtime.ReadSourceFramePtsAsync(source,ct);
                var plan=await clips.PlanAsync(source,pts,null,53,8,ct);RequireWhole(plan,pts);
                var generation=Guid.NewGuid().ToString("N");
                var directory=System.IO.Path.Combine(root,generation);System.IO.Directory.CreateDirectory(directory);
                var video=await clips.PrepareAsync(source,pts,plan,System.IO.Path.Combine(directory,"video"),ct);
                ValidateVideo(source,pts,video);
                Stage(job,"RUNNING","references");
                var selected=AssemblyPolicy.ReferenceIndices(pts.Length,plan);
                var images=await frames.ExtractBatchAsync(source.Path,directory,selected.Select(i=>(i,pts[i])).ToArray(),ct);
                var saved=new Manifest(Version,source.Sha256,generation,pts,images,video,job.Clock.ElapsedMilliseconds);
                result=Restore(root,source,saved,false);
                await VerifyMedia(result,ct);await VerifyOriginal(source,ct);
                ct.ThrowIfCancellationRequested();
                await SaveNew(index,saved,ct);
            }
            job.Clock.Stop();Stage(job,"READY","references",50);job.Completion.TrySetResult(result);
        }
        catch(Exception error)
        {
            job.Clock.Stop();Stage(job,"FAILED",job.Phase);
            job.Completion.TrySetException(error);
            _=job.Completion.Task.Exception; // Queue callers have no waiter to observe failure.
            logger.LogDebug("Local video preparation failed; preserved completed artifacts must pass integrity checks before reuse.");
        }
        finally {if(entered)worker.Release();}
    }
    private static async Task<FileStream> Lock(string root,CancellationToken ct)
    {
        while(true)
        {
            ct.ThrowIfCancellationRequested();
            try{return new FileStream(System.IO.Path.Combine(root,".lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException){await Task.Delay(200,ct);}
        }
    }
    private static void ValidateVideo(SamVideoSource source,IReadOnlyList<double> pts,SamAdaptiveClipManifest manifest)
    {
        var plan=FullPlan(pts.Count);var digest=plan.VerifySourcePts(pts);
        if(manifest.Clips is null || manifest.OriginalVideoSha256!=source.Sha256 || manifest.OriginalPtsSha256!=digest ||
            manifest.OriginalFrames!=pts.Count || manifest.SubmittedFrames!=pts.Count || manifest.Clips.Count!=1 ||
            manifest.CutProtocol!=ContinuousProtocol || manifest.BilledFrames!=plan.BilledFrames)
            throw new InvalidDataException("Prepared video provenance mismatch.");
        var clip=manifest.Clips[0];
        if(clip is not {Part:0,InputStart:0,CoreStart:0,RelativePath:"part-00.mp4"} ||
            clip.InputEndExclusive!=pts.Count || clip.CoreEndExclusive!=pts.Count || clip.Frames!=pts.Count ||
            clip.Bytes is <1 or >240_000_000 || clip.VideoSha256 is not {Length:64} || clip.VideoSha256.Any(c=>!Uri.IsHexDigit(c)) ||
            !double.IsFinite(clip.MaximumPtsErrorSeconds) || clip.MaximumPtsErrorSeconds is <0 or >.005)
            throw new InvalidDataException("Invalid complete-video preparation manifest.");
    }
    private static LocalPreparedMedia Restore(string root,SamVideoSource source,Manifest saved,bool reused)
    {
        if(saved.SourcePts is null || saved.Video is null || saved.Frames is null || saved.Generation is null ||
            saved.Version!=Version || saved.SourceSha256!=source.Sha256 || saved.Generation.Length!=32 ||
            saved.Generation.Any(c=>!Uri.IsHexDigit(c)))throw new InvalidDataException("Local preparation identity mismatch.");
        ValidateVideo(source,saved.SourcePts,saved.Video);
        var directory=System.IO.Path.Combine(root,saved.Generation);var clip=saved.Video.Clips[0];
        return new(System.IO.Path.Combine(directory,"video","part-00.mp4"),directory,clip.VideoSha256,clip.Bytes,
            saved.Frames,saved.SourcePts,reused,saved.Video);
    }
    private static async Task VerifyMedia(LocalPreparedMedia media,CancellationToken ct)
    {
        if(new FileInfo(media.Path).Length!=media.Bytes || await Hash(media.Path,ct)!=media.Sha256)
            throw new InvalidDataException("Prepared local MP4 integrity mismatch.");
        var indices=AssemblyPolicy.ReferenceIndices(media.SourcePts.Length,FullPlan(media.SourcePts.Length));
        if(media.Frames.Length!=50)throw new InvalidDataException("Fifty prepared references required.");
        for(var i=0;i<50;i++)
        {
            var frame=media.Frames[i];
            if(frame is null || frame.Index!=indices[i] || frame.TimeSeconds!=media.SourcePts[frame.Index] ||
                frame.Width is <2 or >8192 || frame.Height is <2 or >8192 ||
                frame.Width!=media.Frames[0].Width || frame.Height!=media.Frames[0].Height)
                throw new InvalidDataException("Prepared original reference coordinates changed.");
            var path=System.IO.Path.Combine(media.Directory,$"frame-{i}.jpg");
            if(new FileInfo(path).Length is <100 or >8_000_000 || await Hash(path,ct)!=frame.Sha256)
                throw new InvalidDataException("Prepared original reference integrity mismatch.");
        }
    }
    private static async Task<Manifest> Read(string path,CancellationToken ct)
    {
        if(new FileInfo(path).Length>500_000)throw new InvalidDataException("Oversized local preparation manifest.");
        await using var input=File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Manifest>(input,Json,ct) ?? throw new InvalidDataException("Invalid local preparation manifest.");
    }
    private static async Task SaveNew<T>(string path,T value,CancellationToken ct)
    {
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(value,Json),ct);
            File.Move(temporary,path,false);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public async Task<LocalPreparationStatus?> StatusAsync(SamVideoSource source,CancellationToken ct)
    {
        var root=Root(source);ct.ThrowIfCancellationRequested();
        lock(sync)
            if(jobs.TryGetValue(root,out var job))
                return new(source.Sha256,job.State,job.Phase,job.Clock.ElapsedMilliseconds,job.References);
        var index=System.IO.Path.Combine(root,"ready.json");if(!File.Exists(index))return null;
        try
        {
            var saved=await Read(index,ct);var media=Restore(root,source,saved,true);
            var complete=new FileInfo(media.Path).Length==media.Bytes && media.Frames.Length==50 &&
                Enumerable.Range(0,50).All(i=>File.Exists(System.IO.Path.Combine(media.Directory,$"frame-{i}.jpg")));
            return new(source.Sha256,complete?"READY":"FAILED","references",saved.ElapsedMs,complete?50:0);
        }
        catch(Exception error) when(error is IOException or JsonException or InvalidDataException or ArgumentException)
        {return new(source.Sha256,"FAILED","video",0,0);}
    }
    public async Task<SamAdaptiveClipManifest> PrepareSamAsync(SamVideoSource source,IReadOnlyList<double> pts,
        SamAdaptiveSegmentPlan plan,string freshRunDirectory,CancellationToken ct)
    {
        var timer=Stopwatch.StartNew();RequireWhole(plan,pts);
        var folder=System.IO.Path.GetFullPath(freshRunDirectory);
        if(!folder.Split(System.IO.Path.DirectorySeparatorChar).Contains(".private",StringComparer.OrdinalIgnoreCase) ||
            System.IO.Directory.Exists(folder) && System.IO.Directory.EnumerateFileSystemEntries(folder).Any())
            throw new InvalidOperationException("SAM local media destination must be a fresh private directory.");
        var media=await PrepareAsync(source,ct);
        if(!pts.SequenceEqual(media.SourcePts) || plan.VerifySourcePts(pts)!=media.Manifest.OriginalPtsSha256)
            throw new InvalidDataException("SAM plan does not match the prepared original timeline.");
        System.IO.Directory.CreateDirectory(folder);var destination=System.IO.Path.Combine(folder,"part-00.mp4");
        await using(var input=File.OpenRead(media.Path))
        await using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            await input.CopyToAsync(output,ct);
        if(new FileInfo(destination).Length!=media.Bytes || await Hash(destination,ct)!=media.Sha256)
            throw new InvalidDataException("Copied SAM media integrity mismatch.");
        await VerifyOriginal(source,ct);
        var clip=media.Manifest.Clips[0] with {BilledFrames=plan.BilledFrames};
        var result=new SamAdaptiveClipManifest(source.Sha256,plan.VerifySourcePts(pts),pts.Count,
            plan.SubmittedFrames,plan.BilledFrames,plan.EstimatedSamCostUsd,ContinuousProtocol,[clip],timer.ElapsedMilliseconds);
        await SaveNew(System.IO.Path.Combine(folder,"clip-manifest.json"),result,ct);
        return result;
    }
}
