using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using ProofLab.Api;
using ProofLab.Science;

namespace ProofLab.Tests;
public sealed class KnotTests
{
    private const string Sha="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Token="LLM_SYNTHETIC_OFFLINE_TEST_ONLY";
    private static readonly CancellationToken Ct=CancellationToken.None;
    private static string Points(double middleX=500,bool visible=true,double confidence=.95)=>
        JsonSerializer.Serialize(new {points=Enumerable.Range(0,3).Select(i=>new {
            frame=i,visible,x=i==1?middleX:500,y=133.3,confidence})});
    private static KnotFrame[] Frames()=>Enumerable.Range(0,3).Select(i=>new KnotFrame(i,i,1080,1920,Sha)).ToArray();
    private static string Envelope(string output)=>JsonSerializer.Serialize(new {
        id="synthetic-not-provider",model=KnotPolicy.Model,status="completed",
        output=new[]{new {type="message",content=new[]{new {type="output_text",text=output}}}},
        usage=new {input_tokens=1000,output_tokens=100}
    });
    [Fact] public void NormalizedCoordinatesUseOrientedFrameAndRemainUnconfirmed() {
        var p=KnotPolicy.Parse(Points(),Frames());
        Assert.Equal("CANDIDATE_REQUIRES_HUMAN_CONFIRMATION",p.Status);
        Assert.Equal(500,p.X);Assert.Equal(133.3,p.Y);
        Assert.Equal(255.936,p.Y!.Value*1920/1000,6);
    }
    [Theory][InlineData(false,.95)][InlineData(true,.6)]
    public void LowConfidenceOrOcclusionHasNoUsablePivot(bool visible,double confidence) {
        var p=KnotPolicy.Parse(Points(500,visible,confidence),Frames());
        Assert.Null(p.X);Assert.Contains("UNCERTAIN",p.Status);
    }
    [Fact] public void MovingOrInconsistentSuspensionIsRejected() {
        var p=KnotPolicy.Parse(Points(530),Frames());
        Assert.Null(p.X);Assert.Contains("INCONSISTENT",p.Status);
    }
    [Theory][InlineData(-1)][InlineData(1000)][InlineData(1200)]
    public void OutOfBoundsCoordinatesAreNeverClamped(double value)=>
        Assert.Throws<FormatException>(()=>KnotPolicy.Parse(Points(value),Frames()));
    [Fact] public void DuplicateOrMissingFramesAreRejected() {
        Assert.Throws<FormatException>(()=>KnotPolicy.Parse(Points().Replace("\"frame\":1","\"frame\":0"),Frames()));
        Assert.Throws<FormatException>(()=>KnotPolicy.Parse("{\"points\":[]}",Frames()));
    }
    [Fact] public void DifferentRotationsAreNotCombined() {
        var f=Frames();f[1]=f[1] with {Width=1920,Height=1080};
        Assert.Throws<FormatException>(()=>KnotPolicy.Parse(Points(),f));
    }
    [Fact] public void RestartCannotReplenishExplicitCarriedAllowance() {
        var b=new MetaCallSafetyBudget(1);
        Assert.True(b.TryReserve(true,true,true,true));
        Assert.False(b.TryReserve(true,true,true,true));
        Assert.Equal(0,b.Snapshot(true,true,true,true).RequestsRemaining);
        Assert.Throws<ArgumentOutOfRangeException>(()=>new MetaCallSafetyBudget(-1));
    }
    private sealed class Env(string root):IWebHostEnvironment {
        public string ApplicationName {get;set;}="Offline";
        public string EnvironmentName {get;set;}="Testing";
        public string ContentRootPath {get;set;}=root;
        public string WebRootPath {get;set;}=root;
        public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();
        public IFileProvider WebRootFileProvider {get;set;}=new NullFileProvider();
    }
    private sealed class Runtime:ISamVideoRuntime {
        public bool LiveCallsPermitted {get;set;}=true;
        public int Reservations;
        public Task<SamVideoSource> GetVerifiedSourceAsync(string s,string r,CancellationToken ct)=>
            Task.FromResult(new SamVideoSource(Sha,"video","synthetic.mp4","h264",1920,1080,10));
        public Task<double[]> ReadFramePtsAsync(string p,CancellationToken ct)=>
            Task.FromResult(Enumerable.Range(0,600).Select(i=>i/60d).ToArray());
        public Task CreateMp4Async(string a,string b,CancellationToken ct)=>throw new NotSupportedException();
        public void ReserveOrThrow(){if(!LiveCallsPermitted)throw new InvalidOperationException();Reservations++;}
    }
    private sealed class FramesStub:IKnotFrames {
        public int Calls;
        public async Task<KnotFrame> ExtractAsync(string p,string dest,int index,double time,CancellationToken ct) {
            Calls++;var bytes=Encoding.UTF8.GetBytes(new string('x',200));
            await File.WriteAllBytesAsync(dest,bytes,ct);
            return new(index,time,1080,1920,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
    }
    private sealed class Transport:IKnotTransport {
        public int Calls;public bool Fail;public Func<Task>? Wait;
        public async Task<KnotProviderResult> LocateAsync(byte[][] images,string key,CancellationToken ct) {
            Calls++;if(Wait is not null)await Wait();
            if(Fail)throw new HttpRequestException("Synthetic failure");
            Assert.Equal(Token,key);Assert.Equal(3,images.Length);
            return new(Envelope(Points()),"synthetic-not-provider",1000,100);
        }
    }
    private sealed class Fixture:IDisposable {
        public readonly string Root=Path.Combine(AppContext.BaseDirectory,"knot-test-data",Guid.NewGuid().ToString("N"));
        public readonly Runtime Runtime=new();public readonly FramesStub Frames=new();public readonly Transport Transport=new();
        public string Folder=>Path.Combine(Root,".private","physics","knot",KnotPolicy.Key(Sha));
        public PendulumKnotLocator Open()=>new(new Env(Root),Runtime,Frames,Transport);
        public KnotRunRequest Request()=>new(Sha,KnotPolicy.Key(Sha),Token,true,true,.05);
        public void Dispose(){if(Directory.Exists(Root))Directory.Delete(Root,true);}
    }
    [Fact] public async Task QuoteNeverDecodesUploadsOrReserves() {
        using var f=new Fixture();var q=await f.Open().QuoteAsync("study","baseline",Ct);
        Assert.False(q.CacheHit);Assert.True(q.CanRun);Assert.Equal(.05,q.MaximumEstimatedCostUsd);
        Assert.Equal(0,f.Frames.Calls);Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reservations);
    }
    [Fact] public async Task CacheSurvivesRestartAndNeedsNoTokenOrPayment() {
        using var f=new Fixture();var e=await f.Open().RunAsync("study","baseline",f.Request(),Ct);
        f.Runtime.LiveCallsPermitted=false;
        var again=await f.Open().RunAsync("study","baseline",f.Request() with {ModelApiToken="",ConfirmPaidCall=false},Ct);
        Assert.Equal(e.ResponseSha256,again.ResponseSha256);Assert.Equal(1,f.Transport.Calls);
        Assert.Equal(1,f.Runtime.Reservations);Assert.Equal(3,f.Frames.Calls);
        Assert.True((await f.Open().QuoteAsync("study","baseline",Ct)).CacheHit);
        var files=Directory.GetFiles(f.Folder,"*.json");
        foreach(var file in files)Assert.DoesNotContain(Token,await File.ReadAllTextAsync(file));
    }
    [Fact] public async Task WrongSourceConsentBudgetAndDisabledGatesStopBeforeWork() {
        using var f=new Fixture();
        foreach(var request in new[]{f.Request() with {VideoSha256=new string('b',64)},
            f.Request() with {ConfirmImageTransfer=false},f.Request() with {ConfirmPaidCall=false},
            f.Request() with {MaximumEstimatedCostUsd=.5},f.Request() with {ModelApiToken="bad"}})
            await Assert.ThrowsAsync<ArgumentException>(()=>f.Open().RunAsync("s","baseline",request,Ct));
        f.Runtime.LiveCallsPermitted=false;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Open().RunAsync("s","baseline",f.Request(),Ct));
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Frames.Calls);Assert.Equal(0,f.Runtime.Reservations);
    }
    [Fact] public async Task FailureLocksOutAllAutomaticRetriesAcrossInstances() {
        using var f=new Fixture();f.Transport.Fail=true;
        await Assert.ThrowsAsync<HttpRequestException>(()=>f.Open().RunAsync("s","baseline",f.Request(),Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Open().RunAsync("s","baseline",f.Request(),Ct));
        Assert.True((await f.Open().QuoteAsync("s","baseline",Ct)).PriorAttempt);
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reservations);
    }
    [Fact] public async Task ChangedImageBytesInvalidateCachedEvidence() {
        using var f=new Fixture();await f.Open().RunAsync("s","baseline",f.Request(),Ct);
        await File.WriteAllTextAsync(Path.Combine(f.Folder,"frame-0.jpg"),"tampered");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Open().QuoteAsync("s","baseline",Ct));
        Assert.Equal(1,f.Transport.Calls);
    }
    [Fact] public async Task ConcurrentInstancesCannotChargeTwice() {
        using var f=new Fixture();
        var entered=new TaskCompletionSource();var release=new TaskCompletionSource();
        f.Transport.Wait=async()=>{entered.SetResult();await release.Task;};
        var first=f.Open().RunAsync("s","baseline",f.Request(),Ct);
        try {await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<IOException>(()=>f.Open().RunAsync("s","baseline",f.Request(),Ct));
        }finally{release.SetResult();}
        await first;Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reservations);
    }
    private sealed class HttpStub(Func<HttpRequestMessage,HttpResponseMessage> reply):HttpMessageHandler,IHttpClientFactory {
        public int Calls;public HttpClient CreateClient(string name)=>new(this,false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c){Calls++;return Task.FromResult(reply(r));}
    }
    [Fact] public async Task ExcessiveTokenPreflightCannotReachInference() {
        using var http=new HttpStub(r=>{
            Assert.EndsWith("/responses/input_tokens",r.RequestUri!.AbsoluteUri);
            return new(System.Net.HttpStatusCode.OK){Content=new StringContent("{\"input_tokens\":40000}")};
        });
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new KnotMetaTransport(http)
            .LocateAsync(Enumerable.Range(0,3).Select(_=>new byte[200]).ToArray(),Token,Ct));
        Assert.Equal(1,http.Calls);
    }
    [Fact] public async Task RealTransportContractUsesOneBoundedInferenceWithoutTools() {
        using var http=new HttpStub(r=>{
            var body=r.Content!.ReadAsStringAsync().Result;
            using var doc=JsonDocument.Parse(body);var root=doc.RootElement;
            Assert.Equal(KnotPolicy.Model,root.GetProperty("model").GetString());
            Assert.False(root.TryGetProperty("tools",out _));
            Assert.Equal(2048,root.GetProperty("max_output_tokens").GetInt32());
            Assert.Equal("Bearer",r.Headers.Authorization!.Scheme);
            return new(System.Net.HttpStatusCode.OK){Content=new StringContent(
                r.RequestUri!.AbsolutePath.EndsWith("input_tokens")?"{\"input_tokens\":1200}":Envelope(Points()))};
        });
        var result=await new KnotMetaTransport(http).LocateAsync(
            Enumerable.Range(0,3).Select(_=>new byte[200]).ToArray(),Token,Ct);
        Assert.Equal(2,http.Calls);Assert.Equal(1000,result.InputTokens);
        Assert.Equal(Points(),KnotMetaTransport.OutputText(result.Raw));
    }
}
