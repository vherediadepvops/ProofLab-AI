using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ProofLab.Api;
namespace ProofLab.Tests;

public sealed class VideoTimingCatalogTests
{
    private static readonly CancellationToken Ct=CancellationToken.None;
    private sealed class Env(string root):IWebHostEnvironment {
        public string ApplicationName{get;set;}="test";
        public string EnvironmentName{get;set;}="Testing";
        public string ContentRootPath{get;set;}=root;
        public string WebRootPath{get;set;}=root;
        public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
        public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
    }
    private sealed class Lifetime:IHostApplicationLifetime {
        public CancellationToken ApplicationStarted=>Ct;
        public CancellationToken ApplicationStopping=>Ct;
        public CancellationToken ApplicationStopped=>Ct;
        public void StopApplication(){}
    }
    private sealed class Reader:IVideoTimingReader {
        public int? Count=60;public int Probes,Scans,Active,Peak;
        public bool Fail;public TaskCompletionSource? Release;
        public readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public double[] Times=Enumerable.Range(0,60).Select(i=>i/59.94).ToArray();
        public Task<int?> ReadVideoFrameCountAsync(string path,CancellationToken ct) {
            Interlocked.Increment(ref Probes);
            if(Fail)throw new InvalidOperationException("Unexpected probe");
            return Task.FromResult(Count);
        }
        public async Task<double[]> ReadVideoFramePtsAsync(string path,CancellationToken ct) {
            Interlocked.Increment(ref Scans);
            var active=Interlocked.Increment(ref Active);lock(this)Peak=Math.Max(Peak,active);
            Entered.TrySetResult();
            try {
                if(Fail)throw new InvalidOperationException("Unexpected scan");
                if(Release is not null)await Release.Task.WaitAsync(ct);
                return Times.ToArray();
            }finally{Interlocked.Decrement(ref Active);}
        }
    }
    private sealed class Fixture:IDisposable {
        public string Root=Path.Combine(Path.GetTempPath(),"prooflab-timing-"+Guid.NewGuid().ToString("N"));
        public Reader Reader=new();public SamVideoSource Video;
        public Fixture() {
            Directory.CreateDirectory(Root);
            Video=Add("native original bytes");
        }
        public SamVideoSource Add(string content) {
            var bytes=System.Text.Encoding.UTF8.GetBytes(content);
            var sha=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var path=Path.Combine(Root,sha+".mov");File.WriteAllBytes(path,bytes);
            return new(sha,"test-video",path,"hevc",3840,2160,1.001);
        }
        public VideoTimingCatalog Open()=>new(new Env(Root),Reader,new Lifetime(),NullLogger<VideoTimingCatalog>.Instance);
        public void Dispose(){Directory.Delete(Root,true);}
    }
    [Fact] public async Task MetadataQuoteReturnsWhileNativeScanIsBlockedAndSharesWork() {
        using var f=new Fixture();f.Reader.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var c=f.Open();
        var quote=await c.CountAsync(f.Video,Ct).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(60,quote.Frames);Assert.Empty(quote.PtsSha256);
        Assert.Equal("CONTAINER_DECLARED_FRAME_COUNT_ESTIMATE",quote.Source);
        await f.Reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var requests=Enumerable.Range(0,8).Select(_=>c.PtsAsync(f.Video,Ct)).ToArray();
        Assert.All(requests,t=>Assert.False(t.IsCompleted));Assert.Equal(1,f.Reader.Scans);
        f.Reader.Release.SetResult();await Task.WhenAll(requests);
        var exact=await c.CountAsync(f.Video,Ct);
        Assert.NotEmpty(exact.PtsSha256);Assert.Contains("SOURCE_FRAME_PTS",exact.Source);
        Assert.Equal(1,f.Reader.Probes);Assert.Equal(1,f.Reader.Scans);
        Assert.Equal(f.Reader.Times,await requests[0]);
    }
    [Fact] public async Task ExactTimelineSurvivesRestartAndArraysCannotMutateCache() {
        using var f=new Fixture();var first=await f.Open().PtsAsync(f.Video,Ct);
        first[1]=999;f.Reader.Fail=true;
        var reopened=f.Open();var exact=await reopened.CountAsync(f.Video,Ct);
        Assert.Equal(60,exact.Frames);Assert.NotEmpty(exact.PtsSha256);
        Assert.Equal(f.Reader.Times,await reopened.PtsAsync(f.Video,Ct));
        Assert.Equal(1,f.Reader.Scans);Assert.Equal(0,f.Reader.Probes);
    }
    [Fact] public async Task MissingContainerCountFallsBackOnceToNativeTimes() {
        using var f=new Fixture();f.Reader.Count=null;
        var c=f.Open();var quote=await c.CountAsync(f.Video,Ct);
        Assert.Equal(60,quote.Frames);Assert.NotEmpty(quote.PtsSha256);
        await c.CountAsync(f.Video,Ct);
        Assert.Equal(1,f.Reader.Probes);Assert.Equal(1,f.Reader.Scans);
    }
    [Fact] public async Task CancellationOfOneWaiterDoesNotCancelSharedPreparation() {
        using var f=new Fixture();f.Reader.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var c=f.Open();using var cancel=new CancellationTokenSource();
        var cancelled=c.PtsAsync(f.Video,cancel.Token);
        await f.Reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var remaining=c.PtsAsync(f.Video,Ct);cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>cancelled);
        f.Reader.Release.SetResult();Assert.Equal(60,(await remaining).Length);
        Assert.Equal(1,f.Reader.Scans);
    }
    [Fact] public async Task ChangedOriginalCannotReuseOldTimeline() {
        using var f=new Fixture();var c=f.Open();await c.PtsAsync(f.Video,Ct);
        await File.AppendAllTextAsync(f.Video.Path,"changed");
        await Assert.ThrowsAsync<InvalidDataException>(()=>c.PtsAsync(f.Video,Ct));
        Assert.Equal(1,f.Reader.Scans); // SHA mismatch found before decoder
    }
    [Fact] public async Task CorruptPersistentTimelineIsRegeneratedAndInvalidTimesRejected() {
        using var f=new Fixture();await f.Open().PtsAsync(f.Video,Ct);
        var path=Directory.GetFiles(Path.Combine(f.Root,".private","physics","video-timing"),"*.pts.json").Single();
        await File.WriteAllTextAsync(path,"invalid json");
        f.Reader.Times[10]=f.Reader.Times[9];
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Open().PtsAsync(f.Video,Ct));
        Assert.Equal(2,f.Reader.Scans);
    }
    [Fact] public async Task AtMostTwoNativeScansAndMetadataUsesAnIndependentLane() {
        using var f=new Fixture();f.Reader.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var c=f.Open();
        var tasks=new[]{f.Video,f.Add("second"),f.Add("third")}.Select(v=>c.PtsAsync(v,Ct)).ToArray();
        await f.Reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var outside=f.Add("fourth");f.Reader.Count=3000; // no background warm beyond request budget
        var quote=await c.CountAsync(outside,Ct).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(3000,quote.Frames);Assert.InRange(f.Reader.Peak,1,2);
        f.Reader.Release.SetResult();await Task.WhenAll(tasks);
        Assert.Equal(3,f.Reader.Scans);Assert.Equal(2,f.Reader.Peak);
    }
}
