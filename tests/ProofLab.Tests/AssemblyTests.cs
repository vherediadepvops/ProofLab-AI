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
    private static string GroundingPoints(string status="CLEAR_SINGLE_OBJECT",double confidence=.95) {
        var root=System.Text.Json.Nodes.JsonNode.Parse(Points(confidence:confidence))!;
        root["target"]=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new {
            status,justification=new {es="Un objeto suspendido visible en las imágenes.",en="One suspended object is visible in the images."}}));
        return root.ToJsonString();
    }
    private static AssemblyEvidence GroundingEvidence(string status="CLEAR_SINGLE_OBJECT",double confidence=.95) {
        var old=Evidence();var output=GroundingPoints(status,confidence);
        return old with {CacheKey=AssemblyPolicy.GroundingKey(Sha),Protocol=AssemblyPolicy.GroundingProtocol,
            Proposal=AssemblyPolicy.ParseGrounding(output,old.Frames)};
    }
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
        public string GroundingStatus="CLEAR_SINGLE_OBJECT";
        public double GroundingConfidence=.95;
        public async Task<KnotProviderResult> LocateGroundingAsync(byte[][] images,string key,CancellationToken ct) {
            var response=await LocateAsync(images,key,ct);
            return response with {Raw=Envelope(GroundingPoints(GroundingStatus,GroundingConfidence))};
        }
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
        public KnotRunRequest GroundRequest()=>new(Sha,AssemblyPolicy.GroundingKey(Sha),Token,true,true,.05);
        public string GroundFolder=>Path.Combine(Root,".private","physics","assembly",AssemblyPolicy.GroundingKey(Sha));
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
    [Fact] public async Task DirectSamRequiresSparkTargetBeforeProviderCall() {
        using var f=new Fixture();var locator=f.Open();var transport=new SamTransport();
        var sam=new PendulumSamCache(new Env(f.Root),f.Runtime,transport,new SamVideoEvidenceIndex(f.Root),
            NullLogger<PendulumSamCache>.Instance,locator);
        var quote=await sam.PreflightAsync("s","baseline",Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>sam.RunOnceAsync("s","baseline",
            new(Sha,true,true,quote.EstimatedCostUsd),Ct,Token));
        Assert.Equal(0,transport.Calls);Assert.Equal(0,f.Runtime.Reservations);Assert.Equal(0,f.Transport.Calls);
    }
    [Fact] public async Task ReviewedAssemblyFlowsIntoSamAndSqliteWithoutAnotherRecognition() {
        using var f=new Fixture();var locator=f.Open();var samTransport=new SamTransport();
        var sam=new PendulumSamCache(new Env(f.Root),f.Runtime,samTransport,new SamVideoEvidenceIndex(f.Root),
            NullLogger<PendulumSamCache>.Instance,locator);
        var request=new SamVideoRunRequest(Sha,true,true,.5);
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

    [Fact] public void AutomaticGroundingOnlyUsesClearObjectBoxesAndNeverApprovesPivot() {
        var evidence=GroundingEvidence();
        var scene=AssemblyPolicy.Ground(evidence)!;
        AssemblyPolicy.Validate(scene,Sha,60);
        Assert.Equal(AssemblyPolicy.GroundingOrigin,scene.Origin);
        Assert.Null(scene.PivotX);Assert.Null(scene.PivotY);
        Assert.Single(scene.Roles);Assert.Equal("object",scene.Roles[0].Role);
        Assert.Equal(new[]{6,30,54},scene.Seeds.Select(s=>s.Frame).ToArray());
        Assert.Contains("w=1080;h=1920",scene.Prompt);
        Assert.Contains("x1=432;y1=384",scene.Prompt);
        Assert.NotEqual(AssemblyPolicy.Key(Sha),evidence.CacheKey);
        var withPivot=scene with {Key="",PivotX=500,PivotY=133.3};
        withPivot=withPivot with {Key=AssemblyPolicy.DefinitionKey(withPivot)};
        Assert.Throws<InvalidDataException>(()=>AssemblyPolicy.Validate(withPivot,Sha));
    }
    [Theory]
    [InlineData("AMBIGUOUS",.95)]
    [InlineData("NOT_VISIBLE",.95)]
    [InlineData("CLEAR_SINGLE_OBJECT",.4)]
    public void AmbiguousInvisibleOrWeakTargetCannotBecomeSamScene(string status,double confidence) {
        Assert.Null(AssemblyPolicy.Ground(GroundingEvidence(status,confidence)));
    }
    [Fact] public void OneClearFrameDoesNotMeetAutomaticGroundingRequirement() {
        var e=GroundingEvidence();
        e=e with {Proposal=e.Proposal with {Observations=e.Proposal.Observations.Select(o=>
            o.Role=="object"&&o.Frame>0?o with {Confidence=.2}:o).ToArray()}};
        Assert.Null(AssemblyPolicy.Ground(e));
        Assert.Throws<KeyNotFoundException>(()=>AssemblyPolicy.ParseGrounding(Points(),e.Frames));
    }
    [Fact] public void HistoricalDefinitionHashRemainsByteCompatibleWhenOriginIsAbsent() {
        var d=AssemblyPolicy.Approve(Evidence(),["object"]);
        var legacy=JsonSerializer.Serialize(new {Key="",d.VideoSha256,d.EvidenceKey,d.ResponseSha256,
            d.Width,d.Height,d.Arrangement,d.Roles,d.Seeds,d.PivotX,d.PivotY,d.Prompt});
        Assert.Equal(KnotPolicy.Hash(legacy),d.Key);
        Assert.DoesNotContain("Origin",JsonSerializer.Serialize(d));
        Assert.DoesNotContain("Target",JsonSerializer.Serialize(Evidence().Proposal));
    }
    [Fact] public async Task GroundingQuoteIsReadOnlyAndCachedSceneNeedsNoToken() {
        using var f=new Fixture();var locator=f.Open();
        var q=await locator.GroundingQuoteAsync("s","baseline",Ct);
        Assert.Equal("NEEDS_LOCALIZATION",q.Status);Assert.True(q.CanRun);Assert.False(q.CacheHit);
        Assert.Equal(0,f.Frames.Calls);Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reservations);
        var result=await locator.GroundAsync("s","baseline",f.GroundRequest(),Ct);
        Assert.Equal("READY",result.Status);Assert.True(result.CacheHit);Assert.NotNull(result.Scene);
        Assert.False(File.Exists(Path.Combine(f.GroundFolder,"approved.json")));
        f.Runtime.LiveCallsPermitted=false;
        var cached=await f.Open().GroundAsync("s","baseline",f.GroundRequest() with {ModelApiToken="",ConfirmPaidCall=false},Ct);
        Assert.Equal(result.Scene!.Key,cached.Scene!.Key);
        Assert.Equal(result.Scene.Key,(await f.Open().TrackingAsync("s","baseline",Ct))!.Key);
        Assert.Null(await f.Open().ApprovedAsync("s","baseline",Ct));
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reservations);Assert.Equal(3,f.Frames.Calls);
        foreach(var file in Directory.GetFiles(f.GroundFolder,"*.json"))
            Assert.DoesNotContain(Token,await File.ReadAllTextAsync(file));
    }
    [Fact] public async Task AmbiguousGroundingIsPersistedAndNeverTriggersRetry() {
        using var f=new Fixture();f.Transport.GroundingStatus="AMBIGUOUS";
        var q=await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct);
        Assert.Equal("AMBIGUOUS",q.Status);Assert.True(q.CacheHit);Assert.False(q.CanRun);Assert.Null(q.Scene);
        var cached=await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct);
        Assert.Equal("AMBIGUOUS",cached.Status);Assert.Null(await f.Open().TrackingAsync("s","baseline",Ct));
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reservations);
    }
    [Fact] public async Task GroundingCannotBypassUncertainHistoricalAttempt() {
        using var f=new Fixture();f.Transport.Fail=true;
        await Assert.ThrowsAsync<HttpRequestException>(()=>f.Open().RunAsync("s","baseline",f.Request(),Ct));
        var q=await f.Open().GroundingQuoteAsync("s","baseline",Ct);
        Assert.Equal("PRIOR_ATTEMPT",q.Status);Assert.False(q.CanRun);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct));
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reservations);
    }
    [Fact] public async Task GroundingFailureLocksOutRetryAcrossInstances() {
        using var f=new Fixture();f.Transport.Fail=true;
        await Assert.ThrowsAsync<HttpRequestException>(()=>f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct));
        Assert.Equal("PRIOR_ATTEMPT",(await f.Open().GroundingQuoteAsync("s","baseline",Ct)).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct));
        Assert.Equal(1,f.Runtime.Reservations);Assert.Equal(1,f.Transport.Calls);
    }
    [Fact] public async Task GroundingFrameAndProviderReceiptAreAuthenticated() {
        using var f=new Fixture();await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct);
        var path=Path.Combine(f.GroundFolder,"evidence.json");
        var original=await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path,original.Replace("synthetic-not-provider","tampered-provider-id"));
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Open().GroundingQuoteAsync("s","baseline",Ct));
        await File.WriteAllTextAsync(path,original);
        await File.WriteAllTextAsync(Path.Combine(f.GroundFolder,"frame-0.jpg"),"tampered");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Open().TrackingAsync("s","baseline",Ct));
        Assert.Equal(1,f.Transport.Calls);
    }
    [Fact] public async Task HumanApprovedHistoricalSceneIsReusedWithoutAutomaticGrounding() {
        using var f=new Fixture();var locator=f.Open();
        var e=await locator.RunAsync("s","baseline",f.Request(),Ct);
        var reviewed=await locator.ConfirmAsync("s","baseline",new(Sha,e.CacheKey,true,["object","cord"]),Ct);
        var q=await locator.GroundingQuoteAsync("s","baseline",Ct);
        Assert.Equal("READY",q.Status);Assert.True(q.CacheHit);Assert.Equal(reviewed.Key,q.Scene!.Key);
        Assert.Null(q.Scene.Origin);Assert.NotNull(q.Scene.PivotX);
        var cached=await locator.GroundAsync("s","baseline",f.GroundRequest() with {ModelApiToken="",ConfirmPaidCall=false},Ct);
        Assert.Equal(reviewed.Key,cached.Scene!.Key);Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reservations);
    }
    [Fact] public async Task AutomaticBoxesFlowIntoSamWithoutHumanConfirmation() {
        using var f=new Fixture();var locator=f.Open();var samTransport=new SamTransport();
        var q=await locator.GroundAsync("s","baseline",f.GroundRequest(),Ct);
        var sam=new PendulumSamCache(new Env(f.Root),f.Runtime,samTransport,new SamVideoEvidenceIndex(f.Root),
            NullLogger<PendulumSamCache>.Instance,locator);
        var quote=await sam.PreflightAsync("s","baseline",Ct);
        var result=await sam.RunOnceAsync("s","baseline",new(Sha,true,true,quote.EstimatedCostUsd,SceneKey:q.Scene!.Key),Ct,Token);
        Assert.Equal(q.Scene.Prompt,samTransport.Prompt);Assert.Equal(q.Scene.Key,result.Receipt.Scene!.Key);
        Assert.Equal(AssemblyPolicy.GroundingOrigin,result.Receipt.Scene.Origin);Assert.Null(result.Receipt.Scene.PivotX);
        Assert.Equal(1,samTransport.Calls);Assert.Equal(1,f.Transport.Calls);Assert.Equal(2,f.Runtime.Reservations);
    }
    [Fact] public async Task AutomaticTransportRequiresAmbiguityDecisionInStrictSchema() {
        using var http=new HttpStub(r=>{
            using var doc=JsonDocument.Parse(r.Content!.ReadAsStringAsync().Result);var root=doc.RootElement;
            var format=root.GetProperty("text").GetProperty("format");
            Assert.True(format.GetProperty("strict").GetBoolean());
            Assert.Contains(format.GetProperty("schema").GetProperty("required").EnumerateArray(),p=>p.GetString()=="target");
            Assert.Equal(AssemblyPolicy.GroundingPrompt,root.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString());
            Assert.False(root.TryGetProperty("tools",out _));
            return new(System.Net.HttpStatusCode.OK){Content=new StringContent(
                r.RequestUri!.AbsolutePath.EndsWith("input_tokens")?"{\"input_tokens\":1200}":Envelope(GroundingPoints()))};
        });
        var result=await new AssemblyMetaTransport(http).LocateGroundingAsync(
            Enumerable.Range(0,3).Select(_=>new byte[200]).ToArray(),Token,Ct);
        AssemblyMetaTransport.ValidateReceipt(result.Raw,result.ProviderResponseId,result.InputTokens,result.OutputTokens);
        Assert.Equal(2,http.Calls);
    }

}
