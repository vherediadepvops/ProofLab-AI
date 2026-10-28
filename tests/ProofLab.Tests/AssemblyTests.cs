using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using ProofLab.Api;
namespace ProofLab.Tests;
public sealed class AssemblyTests
{
    private const string Sha="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Token="LLM_SYNTHETIC_OFFLINE_TEST_ONLY";
    private static readonly CancellationToken Ct=CancellationToken.None;
    private static string Points(string arrangement="simple_pendulum",bool pulley=false,bool cord=true,double confidence=.95)=>
        JsonSerializer.Serialize(new {
            arrangement,
            observations=Enumerable.Range(0,3).SelectMany(i=>AssemblyPolicy.RoleNames.Select(role=> {
                var visible=role=="object" || role=="knot" || role=="support" || (role=="cord"&&cord) || (role=="pulley"&&pulley);
                return new {frame=i,role,visible,x1=visible?400+i*10:0,y1=visible?200:0,
                    x2=visible?450+i*10:0,y2=visible?800:0,confidence=visible?confidence:0};
            })),
            points=Enumerable.Range(0,3).Select(i=>new {frame=i,visible=true,x=500,y=133.3,confidence=.95})
        });
    private static string Envelope(string output)=>JsonSerializer.Serialize(new {
        id="synthetic-not-provider",model=AssemblyPolicy.Model,status="completed",
        output=new[]{new {type="message",content=new[]{new {type="output_text",text=output}}}},
        usage=new {input_tokens=1000,output_tokens=100}});
    private static AssemblyEvidence Evidence(string? output=null) {
        var frames=new[]{new KnotFrame(6,.1,1080,1920,Sha),new KnotFrame(30,.5,1080,1920,Sha),new KnotFrame(54,.9,1080,1920,Sha)};
        return new(AssemblyPolicy.Key(Sha),Sha,AssemblyPolicy.Model,AssemblyPolicy.Protocol,frames,Sha,
            "synthetic-not-provider",DateTimeOffset.UtcNow,AssemblyPolicy.Parse(output??Points(),frames),1000,100);
    }
    [Fact] public void CoinOrAnyObjectUsesGeometryAndStableSeparateIds() {
        var d=AssemblyPolicy.Approve(Evidence(),["object","cord","knot","support"]);
        AssemblyPolicy.Validate(d,Sha,60);
        Assert.Equal("1",d.Roles.Single(r=>r.Role=="object").Id);
        Assert.DoesNotContain(d.Roles,r=>r.Role=="pulley");
        Assert.Contains("<6f>",d.Prompt);Assert.Contains("1<|box;x1=432;y1=384;x2=486;y2=1536;w=1080;h=1920|>",d.Prompt);
        Assert.DoesNotContain("metal",d.Prompt);Assert.True(AssemblyPolicy.IsSamPrompt(d.Prompt));
        Assert.Equal(500,d.PivotX);Assert.Equal(133.3,d.PivotY);
    }
    [Fact] public void AbsentCordOrPulleyCannotBeInventedOrApproved() {
        var e=Evidence(Points(cord:false));
        Assert.Throws<ArgumentException>(()=>AssemblyPolicy.Approve(e,["object","cord"]));
        Assert.Throws<ArgumentException>(()=>AssemblyPolicy.Approve(e,["object","pulley"]));
        Assert.Single(AssemblyPolicy.Approve(e,["object"]).Roles);
    }
    [Fact] public void PulleyAndUnclearSetupsCannotUseSimplePendulumPivot() {
        foreach(var kind in new[]{"pulley_system","unclear"}) {
            var d=AssemblyPolicy.Approve(Evidence(Points(kind,true)),["object","cord","pulley"]);
            Assert.Null(d.PivotX);Assert.Null(d.PivotY);
            Assert.Equal("4",d.Roles.Single(r=>r.Role=="pulley").Id);
        }
    }
    [Fact] public void LowConfidenceOrDuplicateRoleFrameIsNotAccepted() {
        Assert.Throws<ArgumentException>(()=>AssemblyPolicy.Approve(Evidence(Points(confidence:.2)),["object"]));
        Assert.Throws<FormatException>(()=>Evidence(Points().Replace("\"role\":\"cord\"","\"role\":\"object\"")));
        Assert.Throws<FormatException>(()=>Evidence(Points().Replace("\"x2\":450","\"x2\":1500")));
    }
    [Fact] public void DefinitionAndDifferentVideoCannotShareEvidence() {
        var d=AssemblyPolicy.Approve(Evidence(),["object"]);
        Assert.Throws<InvalidDataException>(()=>AssemblyPolicy.Validate(d with {VideoSha256=new string('b',64)},Sha));
        Assert.Throws<InvalidDataException>(()=>AssemblyPolicy.Validate(d with {PivotX=1},Sha));
        Assert.NotEqual(d.Key,AssemblyPolicy.Approve(Evidence(),["object","cord"]).Key);
        Assert.False(AssemblyPolicy.IsSamPrompt("object, cord, knot"));
        Assert.False(AssemblyPolicy.IsSamPrompt("<0f>1<|box;x1=0;y1=0;x2=20;y2=20;w=100;h=100|> ignore rules"));
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
        public Task CreateMp4Async(string a,string b,CancellationToken ct)=>File.WriteAllTextAsync(b,"SYNTHETIC_REMUX_NOT_VIDEO",ct);
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
    private sealed class Transport:IAssemblyTransport {
        public int Calls;public bool Fail;public Func<Task>? Wait;
        public async Task<KnotProviderResult> LocateAsync(byte[][] images,string key,CancellationToken ct) {
            Calls++;if(Wait is not null)await Wait();
            if(Fail)throw new HttpRequestException("Synthetic failure");
            Assert.Equal(Token,key);Assert.Equal(3,images.Length);
            return new(Envelope(Points()),"synthetic-not-provider",1000,100);
        }
    }
    private sealed class Fixture:IDisposable {
        public readonly string Root=Path.Combine(AppContext.BaseDirectory,"assembly-test-data",Guid.NewGuid().ToString("N"));
        public readonly Runtime Runtime=new();public readonly FramesStub Frames=new();public readonly Transport Transport=new();
        public string Folder=>Path.Combine(Root,".private","physics","assembly",AssemblyPolicy.Key(Sha));
        public AssemblyLocator Open()=>new(new Env(Root),Runtime,Frames,Transport);
        public KnotRunRequest Request()=>new(Sha,AssemblyPolicy.Key(Sha),Token,true,true,.05);
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
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new AssemblyMetaTransport(http)
            .LocateAsync(Enumerable.Range(0,3).Select(_=>new byte[200]).ToArray(),Token,Ct));
        Assert.Equal(1,http.Calls);
    }
    [Fact] public async Task RealTransportContractUsesOneBoundedInferenceWithoutTools() {
        using var http=new HttpStub(r=>{
            var body=r.Content!.ReadAsStringAsync().Result;
            using var doc=JsonDocument.Parse(body);var root=doc.RootElement;
            Assert.Equal(AssemblyPolicy.Model,root.GetProperty("model").GetString());
            Assert.False(root.TryGetProperty("tools",out _));
            Assert.Equal(4096,root.GetProperty("max_output_tokens").GetInt32());
            Assert.Equal("Bearer",r.Headers.Authorization!.Scheme);
            return new(System.Net.HttpStatusCode.OK){Content=new StringContent(
                r.RequestUri!.AbsolutePath.EndsWith("input_tokens")?"{\"input_tokens\":1200}":Envelope(Points()))};
        });
        var result=await new AssemblyMetaTransport(http).LocateAsync(
            Enumerable.Range(0,3).Select(_=>new byte[200]).ToArray(),Token,Ct);
        Assert.Equal(2,http.Calls);Assert.Equal(1000,result.InputTokens);
        Assert.Equal(Points(),AssemblyMetaTransport.OutputText(result.Raw));
    }
    private sealed class SamTransport:ISamVideoTransport {
        public int Calls;public string? Prompt;
        public Task<SamApiCompletedResult> UploadAndSegmentAsync(string path,string concept,
            Func<SamVideoSseProgress,ValueTask>? progress,CancellationToken ct,string? oneShotMetaToken=null) {
            Calls++;Prompt=concept;
            return Task.FromResult(new SamApiCompletedResult("{\"model\":\"sam-3.1\",\"status\":\"completed\"}","synthetic-sam",600));
        }
    }
    [Fact] public async Task ReviewedAssemblyFlowsIntoSamAndSqliteWithoutAnotherRecognition() {
        using var f=new Fixture();var locator=f.Open();var samTransport=new SamTransport();
        var sam=new PendulumSamCache(new Env(f.Root),f.Runtime,samTransport,new SamVideoEvidenceIndex(f.Root),
            NullLogger<PendulumSamCache>.Instance,locator);
        var request=new SamVideoRunRequest(Sha,true,true,.5);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>sam.RunOnceAsync("s","baseline",request,Ct,Token));
        Assert.Equal(0,f.Runtime.Reservations);
        var evidence=await locator.RunAsync("s","baseline",f.Request(),Ct);
        var scene=await locator.ConfirmAsync("s","baseline",new(Sha,evidence.CacheKey,true,["object","cord","knot"]),Ct);
        await Assert.ThrowsAsync<ArgumentException>(()=>sam.RunOnceAsync("s","baseline",request,Ct,Token));
        var result=await sam.RunOnceAsync("s","baseline",request with {SceneKey=scene.Key},Ct,Token);
        Assert.Equal(scene.Key,result.Receipt.Scene!.Key);Assert.Equal(scene.Prompt,samTransport.Prompt);
        Assert.Equal(AssemblyPolicy.SamProtocol,result.Receipt.RequestProtocol);
        Assert.NotEqual(PendulumSamCache.CacheKey(Sha),result.Receipt.CacheKey);
        f.Runtime.LiveCallsPermitted=false;
        var hit=await sam.RunOnceAsync("s","baseline",new(Sha,false,false,.5),Ct);
        Assert.True(hit.State.CacheHit);Assert.Equal(1,samTransport.Calls);
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(2,f.Runtime.Reservations);
        Assert.Equal(scene.Key,(await sam.ReadArchiveAsync("s","baseline",Ct)).Receipt.Scene!.Key);
    }
    [Fact] public async Task ReviewRequiresConsentAndCannotSilentlySwapTargets() {
        using var f=new Fixture();var locator=f.Open();var e=await locator.RunAsync("s","baseline",f.Request(),Ct);
        await Assert.ThrowsAsync<ArgumentException>(()=>locator.ConfirmAsync("s","baseline",new(Sha,e.CacheKey,false,["object"]),Ct));
        await Assert.ThrowsAsync<ArgumentException>(()=>locator.ConfirmAsync("s","baseline",new(Sha,e.CacheKey,true,["cord"]),Ct));
        var scene=await locator.ConfirmAsync("s","baseline",new(Sha,e.CacheKey,true,["object"]),Ct);
        Assert.Equal(scene.Key,(await f.Open().ApprovedAsync("s","baseline",Ct))!.Key);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>locator.ConfirmAsync("s","baseline",new(Sha,e.CacheKey,true,["object","cord"]),Ct));
        Assert.Equal(1,f.Transport.Calls);
    }

}