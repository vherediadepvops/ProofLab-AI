using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ProofLab.Api;

namespace ProofLab.Tests;

public sealed class LocalVideoPreparationTests
{
    private static readonly CancellationToken Ct=CancellationToken.None;
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed class Env(string root):IWebHostEnvironment
    {
        public string ApplicationName{get;set;}="test";
        public string EnvironmentName{get;set;}="Testing";
        public string ContentRootPath{get;set;}=root;
        public string WebRootPath{get;set;}=root;
        public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
        public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
    }
    private sealed class Lifetime:IHostApplicationLifetime
    {
        public CancellationTokenSource Stopping=new();
        public CancellationToken ApplicationStarted=>Ct;
        public CancellationToken ApplicationStopping=>Stopping.Token;
        public CancellationToken ApplicationStopped=>Ct;
        public void StopApplication()=>Stopping.Cancel();
    }
    private sealed class Runtime:ISamVideoRuntime
    {
        public double[] Pts=Enumerable.Range(0,100).Select(i=>i/60d).ToArray();
        public int Reads;
        public bool LiveCallsPermitted=>false;
        public Task<SamVideoSource> GetVerifiedSourceAsync(string id,string role,CancellationToken ct)=>throw new InvalidOperationException("No study/paid preparation gate in warmup.");
        public Task<double[]> ReadSourceFramePtsAsync(SamVideoSource source,CancellationToken ct){Reads++;return Task.FromResult(Pts.ToArray());}
        public Task<double[]> ReadFramePtsAsync(string path,CancellationToken ct)=>throw new InvalidOperationException("No second decode permitted.");
        public Task CreateMp4Async(string source,string destination,CancellationToken ct)=>throw new InvalidOperationException("Use clip builder exactly once.");
        public void ReserveOrThrow()=>throw new InvalidOperationException("No provider reservation permitted.");
    }
    private sealed class Clips:ISamAdaptiveClipBuilder
    {
        public int Calls,Active,Peak;
        public bool Fail;
        public TaskCompletionSource? Release;
        public readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<SamAdaptiveClipManifest> PrepareAsync(SamVideoSource source,IReadOnlyList<double> pts,SamAdaptiveSegmentPlan plan,string root,CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);var active=Interlocked.Increment(ref Active);lock(this)Peak=Math.Max(Peak,active);
            Entered.TrySetResult();
            try
            {
                if(Release is not null)await Release.Task.WaitAsync(ct);
                if(Fail)throw new InvalidDataException("Synthetic local failure, no provider.");
                Directory.CreateDirectory(root);var bytes=Encoding.UTF8.GetBytes("MP4-COPY-"+source.Sha256);
                await File.WriteAllBytesAsync(Path.Combine(root,"part-00.mp4"),bytes,ct);
                return new(source.Sha256,plan.VerifySourcePts(pts),pts.Count,pts.Count,plan.BilledFrames,plan.EstimatedSamCostUsd,
                    "SAM_CONTINUOUS_ORIGINAL_REMUX_FILE_SHA_PTS_V2",
                    [new(0,0,pts.Count,0,pts.Count,pts.Count,"part-00.mp4",bytes.Length,Hash(bytes),null,0,plan.BilledFrames)],1);
            }
            finally{Interlocked.Decrement(ref Active);}
        }
    }
    private sealed class Frames:IKnotFrames
    {
        public int Calls,Batches;
        public Task<KnotFrame> ExtractAsync(string path,string output,int index,double time,CancellationToken ct)=>throw new InvalidOperationException("Batch extraction expected.");
        public async Task<KnotFrame[]> ExtractBatchAsync(string source,string root,(int Index,double Time)[] samples,CancellationToken ct)
        {
            Batches++;var result=new List<KnotFrame>();
            for(var i=0;i<samples.Length;i++)
            {
                Calls++;var bytes=Encoding.UTF8.GetBytes(new string('f',200)+samples[i].Index);
                await File.WriteAllBytesAsync(Path.Combine(root,$"frame-{i}.jpg"),bytes,ct);
                result.Add(new(samples[i].Index,samples[i].Time,1080,1920,Hash(bytes)));
            }
            return result.ToArray();
        }
    }
    private sealed class Fixture:IDisposable
    {
        public readonly string Root=Path.Combine(Path.GetTempPath(),"prooflab-local-media-"+Guid.NewGuid().ToString("N"));
        public readonly Runtime Runtime=new();public readonly Clips Clips=new();public readonly Frames Frames=new();public readonly Lifetime Lifetime=new();
        public SamVideoSource Video;
        public Fixture(){Directory.CreateDirectory(Root);Video=Add("original source");}
        public SamVideoSource Add(string content)
        {
            var bytes=Encoding.UTF8.GetBytes(content);var sha=Hash(bytes);var path=Path.Combine(Root,sha+".mov");File.WriteAllBytes(path,bytes);
            return new(sha,Guid.NewGuid().ToString("N"),path,"hevc",1920,1080,2);
        }
        public LocalVideoPreparation Open()=>new(new Env(Root),Runtime,Frames,Clips,Lifetime,NullLogger<LocalVideoPreparation>.Instance);
        public string Batch(string name)=>Path.Combine(Root,".private","batches",name);
        public SamAdaptiveSegmentPlan Plan()=>SamAdaptiveSegmentPlan.RestoreNative(Runtime.Pts.Length,53,1,[new(0,0,Runtime.Pts.Length,0,Runtime.Pts.Length)]);
        public string Index=>Path.Combine(Root,".private","physics","local-media","v1",Video.Sha256,"ready.json");
        public void Dispose(){Lifetime.Stopping.Cancel();Directory.Delete(Root,true);Lifetime.Stopping.Dispose();}
    }
    [Fact] public async Task WarmupAndProcessingShareProducerAndCancelledWaiterDoesNotCancelIt()
    {
        using var f=new Fixture();f.Clips.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);var service=f.Open();
        service.Queue(f.Video);await f.Clips.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("RUNNING",(await service.StatusAsync(f.Video,Ct))!.State);
        using var cancel=new CancellationTokenSource();var cancelled=service.PrepareAsync(f.Video,cancel.Token);var waiting=service.PrepareAsync(f.Video,Ct);
        cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>cancelled);Assert.False(waiting.IsCompleted);
        f.Clips.Release.TrySetResult();var media=await waiting;
        Assert.True(media.Reused);Assert.Equal(1,f.Clips.Calls);Assert.Equal(50,f.Frames.Calls);Assert.Equal(1,f.Frames.Batches);
        Assert.Equal("READY",(await service.StatusAsync(f.Video,Ct))!.State);
        var restored=await f.Open().PrepareAsync(f.Video,Ct);
        Assert.True(restored.Reused);Assert.Equal(media.Sha256,restored.Sha256);Assert.Equal(1,f.Clips.Calls);
    }
    [Fact] public async Task SamCopiesVerifiedMediaWithoutAnotherRemuxOrFrameDecode()
    {
        using var f=new Fixture();var service=f.Open();var media=await service.PrepareAsync(f.Video,Ct);
        var result=await service.PrepareSamAsync(f.Video,f.Runtime.Pts,f.Plan(),f.Batch("first"),Ct);
        Assert.Equal(1,f.Clips.Calls);Assert.Equal(50,f.Frames.Calls);
        Assert.Equal(media.Sha256,result.Clips[0].VideoSha256);Assert.Equal(f.Plan().VerifySourcePts(f.Runtime.Pts),result.OriginalPtsSha256);
        Assert.Equal(await File.ReadAllBytesAsync(media.Path),await File.ReadAllBytesAsync(Path.Combine(f.Batch("first"),"part-00.mp4")));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.PrepareSamAsync(f.Video,f.Runtime.Pts,f.Plan(),f.Batch("first"),Ct));
    }
    [Theory]
    [InlineData("mp4")]
    [InlineData("frame")]
    [InlineData("original")]
    public async Task TamperingBlocksReuseBeforeSamManifest(string target)
    {
        using var f=new Fixture();var service=f.Open();var media=await service.PrepareAsync(f.Video,Ct);
        var path=target=="mp4"?media.Path:target=="frame"?Path.Combine(media.Directory,"frame-0.jpg"):f.Video.Path;
        var bytes=await File.ReadAllBytesAsync(path);bytes[0]^=0x01;await File.WriteAllBytesAsync(path,bytes);
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.PrepareSamAsync(f.Video,f.Runtime.Pts,f.Plan(),f.Batch("bad"),Ct));
        Assert.False(File.Exists(Path.Combine(f.Batch("bad"),"clip-manifest.json")));Assert.Equal(1,f.Clips.Calls);
        Assert.Equal("FAILED",(await service.StatusAsync(f.Video,Ct))!.State);
        var preserved=await File.ReadAllBytesAsync(f.Index);
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.PrepareAsync(f.Video,Ct));
        Assert.Equal(1,f.Clips.Calls);Assert.Equal(preserved,await File.ReadAllBytesAsync(f.Index));
    }
    [Fact] public async Task ReplacingOriginalDuringWarmupCannotPublishReady()
    {
        using var f=new Fixture();var service=f.Open();f.Clips.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparing=service.PrepareAsync(f.Video,Ct);await f.Clips.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await File.WriteAllTextAsync(f.Video.Path,"replaced source");f.Clips.Release.TrySetResult();
        await Assert.ThrowsAsync<InvalidDataException>(()=>preparing);
        Assert.False(File.Exists(f.Index));Assert.Equal("FAILED",(await service.StatusAsync(f.Video,Ct))!.State);
    }
    [Fact] public async Task FailedWarmupIsNotRetriedByQueueButExplicitPreparationCanRetry()
    {
        using var f=new Fixture();var service=f.Open();f.Clips.Fail=true;
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.PrepareAsync(f.Video,Ct));
        service.Queue(f.Video);Assert.Equal(1,f.Clips.Calls);Assert.False(File.Exists(f.Index));
        f.Clips.Fail=false;var result=await service.PrepareAsync(f.Video,Ct);
        Assert.Equal(2,f.Clips.Calls);Assert.Equal(50,result.Frames.Length);Assert.True(File.Exists(f.Index));
    }
    [Fact] public async Task BackgroundQueueIsBoundedAndOnlyOneProducerRuns()
    {
        using var f=new Fixture();var service=f.Open();f.Clips.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var sources=Enumerable.Range(0,6).Select(i=>f.Add("video"+i)).ToArray();
        foreach(var source in sources)service.Queue(source);
        await f.Clips.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(await service.StatusAsync(sources[5],Ct));Assert.Equal(1,f.Clips.Calls);
        f.Clips.Release.TrySetResult();await Task.WhenAll(sources.Take(5).Select(s=>service.PrepareAsync(s,Ct)));
        Assert.Equal(5,f.Clips.Calls);Assert.Equal(1,f.Clips.Peak);
    }
    [Fact] public async Task SeparateServiceInstancesShareAtomicCompletedArtifacts()
    {
        using var f=new Fixture();f.Clips.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first=f.Open().PrepareAsync(f.Video,Ct);await f.Clips.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second=f.Open().PrepareAsync(f.Video,Ct);f.Clips.Release.TrySetResult();
        var results=await Task.WhenAll(first,second);
        Assert.Equal(results[0].Sha256,results[1].Sha256);Assert.Equal(1,f.Clips.Calls);Assert.Equal(1,f.Frames.Batches);
    }
    [Fact] public async Task SamRejectsDifferentTimelineAndSegmentedPlan()
    {
        using var f=new Fixture();var service=f.Open();await service.PrepareAsync(f.Video,Ct);
        var changed=f.Runtime.Pts.ToArray();changed[10]+=.001;
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.PrepareSamAsync(f.Video,changed,f.Plan(),f.Batch("shift"),Ct));
        var segmented=SamAdaptiveSegmentPlan.RestoreNative(100,50,1,[new(0,0,50,0,52),new(1,50,100,48,100)]);
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.PrepareSamAsync(f.Video,f.Runtime.Pts,segmented,f.Batch("split"),Ct));
    }
    [Fact] public async Task ConsumerMutationCannotChangeTheReusableManifestOrReferenceArrays()
    {
        using var f=new Fixture();var service=f.Open();var first=await service.PrepareAsync(f.Video,Ct);
        var expectedFrame=first.Frames[0];var expectedClip=first.Manifest.Clips[0];
        first.SourcePts[0]=99;first.Frames[0]=expectedFrame with {Index=999};
        ((SamAdaptiveClip[])first.Manifest.Clips)[0]=expectedClip with {VideoSha256=new string('a',64)};
        var second=await service.PrepareAsync(f.Video,Ct);
        Assert.Equal(0,second.SourcePts[0]);Assert.Equal(expectedFrame,second.Frames[0]);
        Assert.Equal(expectedClip,second.Manifest.Clips[0]);Assert.Equal(1,f.Clips.Calls);
    }
    [Fact] public async Task ApplicationShutdownCancelsProducerAndNeverPublishesReady()
    {
        using var f=new Fixture();var service=f.Open();f.Clips.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparing=service.PrepareAsync(f.Video,Ct);await f.Clips.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Lifetime.StopApplication();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>preparing);
        Assert.False(File.Exists(f.Index));Assert.Equal("FAILED",(await service.StatusAsync(f.Video,Ct))!.State);
    }

}
