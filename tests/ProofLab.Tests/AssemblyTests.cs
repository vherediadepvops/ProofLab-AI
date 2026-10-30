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
    [Fact] public void HistoricalOwnerGroundingKeySurvivesCheckoutLineEndings() {
        Assert.Equal("0b590dffdd6aede99cd45c45aa59c09f009cfb2cbfca5f13470d8b341be430fb",
            AssemblyPolicy.VideoGroundingKey("865ee836529e3a37de1a74f8d57d533558a2247abc71f8c5545a57f763b4d546"));
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
        public bool ShiftRemuxPts;
        public Task<SamVideoSource> GetVerifiedSourceAsync(string s,string r,CancellationToken ct)=>
            Task.FromResult(new SamVideoSource(Sha,"video","synthetic.mp4","h264",1920,1080,10));
        public Task<double[]> ReadFramePtsAsync(string p,CancellationToken ct)=>
            Task.FromResult(Enumerable.Range(0,600).Select(i=>i/60d+(ShiftRemuxPts&&p.EndsWith("full-video.mp4")?.01:0)).ToArray());
        public Task CreateMp4Async(string a,string b,CancellationToken ct)=>File.WriteAllTextAsync(b,"SYNTHETIC_REMUX_NOT_VIDEO",ct);
        public void ReserveOrThrow(){if(!LiveCallsPermitted)throw new InvalidOperationException();Reservations++;}
    }
    private sealed class FramesStub:IKnotFrames {
        public int Calls, BatchCalls;
        public async Task<byte[]> CropSuspensionAsync(string p,KnotFrame f,SuspensionCrop crop,string dest,CancellationToken ct) {
            var bytes=Encoding.UTF8.GetBytes(new string('c',200));await File.WriteAllBytesAsync(dest,bytes,ct);return bytes;
        }
        public async Task<byte[]> MarkObjectAsync(string p,KnotFrame f,AssemblyObservation box,string dest,CancellationToken ct) {
            var bytes=Encoding.UTF8.GetBytes(new string('m',200));await File.WriteAllBytesAsync(dest,bytes,ct);return bytes;
        }
        public async Task<KnotFrame[]> ExtractBatchAsync(string p,string root,(int Index,double Time)[] samples,CancellationToken ct) {
            BatchCalls++;var result=new KnotFrame[samples.Length];
            for(var i=0;i<samples.Length;i++)result[i]=await ExtractAsync(p,Path.Combine(root,$"frame-{i}.jpg"),samples[i].Index,samples[i].Time,ct);
            return result;
        }
        public async Task<KnotFrame> ExtractAsync(string p,string dest,int index,double time,CancellationToken ct) {
            Calls++;var bytes=Encoding.UTF8.GetBytes(new string('x',200));
            await File.WriteAllBytesAsync(dest,bytes,ct);
            return new(index,time,1080,1920,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
    }
    private sealed class Transport:IAssemblyTransport {
        public int Calls;public bool Fail;public Func<Task>? Wait;
        public string GroundingStatus="CLEAR_SINGLE_OBJECT";
        public bool NoInference;public int VideoCalls;
        public double GroundingConfidence=.95;
        public int ReviewCalls;public bool ReviewUncertain;public bool WrongReviewOrdinal;
        public Task<KnotProviderResult> ReviewObjectAsync(ObjectReviewImage[] refs,byte[][] images,string token,CancellationToken ct) {
            ReviewCalls++;
            var text=JsonSerializer.Serialize(new {observations=refs.Select(r=>new {frame=WrongReviewOrdinal?99:r.Ordinal,decision=ReviewUncertain?"UNCERTAIN":"CORRECTED",visible=!ReviewUncertain,
                x1=ReviewUncertain?0:400,y1=ReviewUncertain?0:700,x2=ReviewUncertain?0:450,y2=ReviewUncertain?0:800,confidence=ReviewUncertain?0:GroundingConfidence,visual_reason="The suspended body below the cord, not the cord."}),
                suspension=new {camera="STATIC",points=refs.Where(r=>r.SuspensionCrop is not null).Select(r=>new {frame=r.Ordinal,visible=true,x=500,y=333.25,confidence=.95,visual_reason="Visible fixed cord exit."})},
                target=new {status=GroundingStatus,justification=new {es="Cuerpo suspendido revisado.",en="Reviewed suspended body."}}});
            return Task.FromResult(new KnotProviderResult(Envelope(text),"synthetic-not-provider",1000,100));
        }
        public async Task<AssemblyVideoProviderResult> LocateVideoGroundingAsync(string path,AssemblyVideoInput input,
            KnotFrame[] anchors,byte[][] images,string token,double cost,Func<AssemblyVideoInput,Task> uploaded,CancellationToken ct) {
            VideoCalls++;
            if(NoInference)throw new AssemblyNoInferenceSentException(new InvalidOperationException("Synthetic admission failure"));
            var receipt=input with {ProviderFileId="file-synthetic-"+VideoCalls};
            await uploaded(receipt);
            var result=await LocateGroundingAsync(images,token,ct);
            if(anchors.Length==50)result=result with {Raw=Envelope(JsonSerializer.Serialize(new {
                arrangement="simple_pendulum",samples=Enumerable.Range(0,50).Select(i=>new double[]{i,1,400,200,450,800,GroundingConfidence}),
                points=new[]{new double[]{0,1,500,133.3,.95},new double[]{25,1,500,133.3,.95},new double[]{49,1,500,133.3,.95}},
                target=new {status=GroundingStatus,justification=new {es="Objeto visible.",en="Visible object."}}
            }))};
            return new(result,receipt);
        }
        public async Task<KnotProviderResult> LocateGroundingAsync(byte[][] images,string key,CancellationToken ct) {
            var response=await LocateAsync(images,key,ct);
            return response with {Raw=Envelope(GroundingPoints(GroundingStatus,GroundingConfidence))};
        }
        public async Task<KnotProviderResult> LocateAsync(byte[][] images,string key,CancellationToken ct) {
            Calls++;if(Wait is not null)await Wait();
            if(Fail)throw new HttpRequestException("Synthetic failure");
            Assert.Equal(Token,key);Assert.True(images.Length is 3 or 50);
            return new(Envelope(Points()),"synthetic-not-provider",1000,100);
        }
    }
    private sealed class Fixture:IDisposable {
        public readonly string Root=Path.Combine(AppContext.BaseDirectory,"assembly-test-data",Guid.NewGuid().ToString("N"));
        public readonly Runtime Runtime=new();public readonly FramesStub Frames=new();public readonly Transport Transport=new();
        public string Folder=>Path.Combine(Root,".private","physics","assembly",AssemblyPolicy.Key(Sha));
        public AssemblyLocator Open()=>new(new Env(Root),Runtime,Frames,Transport);
        public KnotRunRequest Request()=>new(Sha,AssemblyPolicy.Key(Sha),Token,true,true,.05);
        public AssemblyGroundingRunRequest GroundRequest()=>new(Sha,AssemblyPolicy.VideoGroundingKey(Sha),Token,true,true,.35,ConfirmVideoTransfer:true);
        public string GroundFolder=>Directory.GetDirectories(Path.Combine(Root,".private","physics","assembly",
            AssemblyPolicy.VideoGroundingKey(Sha),"runs")).OrderBy(Directory.GetCreationTimeUtc).Last();
        public string LegacyGroundFolder=>Path.Combine(Root,".private","physics","assembly",AssemblyPolicy.GroundingKey(Sha));
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
    [Fact] public async Task ImageTransportSkipsOptionalTokenCount() {
        using var http=new HttpStub(r=>{
            Assert.EndsWith("/responses",r.RequestUri!.AbsoluteUri);
            return new(System.Net.HttpStatusCode.OK){Content=new StringContent(Envelope(Points()))};
        });
        var result=await new AssemblyMetaTransport(http).LocateAsync(Enumerable.Range(0,3).Select(_=>new byte[200]).ToArray(),Token,Ct);
        Assert.Equal(1,http.Calls);Assert.Equal(1000,result.InputTokens);
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
        Assert.Equal(1,http.Calls);Assert.Equal(1000,result.InputTokens);
        Assert.Equal(Points(),AssemblyMetaTransport.OutputText(result.Raw));
    }
    private sealed class SamTransport:ISamVideoTransport {
        public int Calls;public string? Prompt;
        public Task<SamApiCompletedResult> UploadAndSegmentAsync(string path,string concept,
            Func<SamVideoSseProgress,ValueTask>? progress,CancellationToken ct,string? oneShotMetaToken=null) {
            Calls++;Prompt=concept;
            return Task.FromResult(new SamApiCompletedResult(JsonSerializer.Serialize(new {
                model="sam-3.1",status="completed",output=new[]{new {type="message",
                    content=new[]{new {type="output_text",text="<0f>1<|box;x1=211;y1=228;x2=270;y2=254;w=320;h=334|><|mask;x=0;y=0;data=27,60,~!!!!M!0c[0o91w?q1!pIH4pPRVp2B3'7`e.ioeAf6-k/#Xd8%dX9x(|>"}}}}
            }),"synthetic-sam",600));
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
        Assert.Equal("READY",result.Status);Assert.False(result.CacheHit);Assert.NotNull(result.Scene);
        Assert.False(File.Exists(Path.Combine(f.GroundFolder,"approved.json")));
        f.Runtime.LiveCallsPermitted=false;
        var cached=await f.Open().GroundAsync("s","baseline",f.GroundRequest() with {ModelApiToken="",ConfirmPaidCall=false},Ct);
        Assert.Equal(result.Scene!.Key,cached.Scene!.Key);
        Assert.Equal(result.Scene.Key,(await f.Open().TrackingAsync("s","baseline",Ct))!.Key);
        Assert.Null(await f.Open().ApprovedAsync("s","baseline",Ct));
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(2,f.Runtime.Reservations);Assert.Equal(50,f.Frames.Calls);
        foreach(var file in Directory.GetFiles(f.GroundFolder,"*.json"))
            Assert.DoesNotContain(Token,await File.ReadAllTextAsync(file));
    }
    [Fact] public async Task AmbiguousGroundingIsPersistedAndNeverTriggersRetry() {
        using var f=new Fixture();f.Transport.GroundingStatus="AMBIGUOUS";
        var q=await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct);
        Assert.Equal("AMBIGUOUS",q.Status);Assert.False(q.CacheHit);Assert.False(q.CanRun);Assert.Null(q.Scene);
        var cached=await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct);
        Assert.Equal("AMBIGUOUS",cached.Status);Assert.Null(await f.Open().TrackingAsync("s","baseline",Ct));
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(2,f.Runtime.Reservations);
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
        var archive=await sam.ReadArchiveAsync("s","baseline",Ct);
        Assert.NotNull(archive!.Suspension);Assert.Equal(q.Scene.EvidenceKey,archive.Suspension.EvidenceKey);
        Assert.Equal(ObjectReviewPolicy.Protocol,archive.Suspension.Refinement);
        Assert.Equal(AssemblyPolicy.GroundingOrigin,result.Receipt.Scene.Origin);Assert.Null(result.Receipt.Scene.PivotX);
        Assert.Equal(1,samTransport.Calls);Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Transport.ReviewCalls);Assert.Equal(3,f.Runtime.Reservations);
    }
    [Fact] public async Task AutomaticTransportRequiresAmbiguityDecisionInStrictSchema() {
        using var http=new HttpStub(r=>{
            using var doc=JsonDocument.Parse(r.Content!.ReadAsStringAsync().Result);var root=doc.RootElement;
            var format=root.GetProperty("text").GetProperty("format");
            Assert.True(format.GetProperty("strict").GetBoolean());
            Assert.Equal("medium",root.GetProperty("reasoning").GetProperty("effort").GetString());
            Assert.Contains(format.GetProperty("schema").GetProperty("required").EnumerateArray(),p=>p.GetString()=="target");
            Assert.Equal(AssemblyPolicy.GroundingPrompt,root.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString());
            Assert.False(root.TryGetProperty("tools",out _));
            return new(System.Net.HttpStatusCode.OK){Content=new StringContent(
                r.RequestUri!.AbsolutePath.EndsWith("input_tokens")?"{\"input_tokens\":1200}":Envelope(GroundingPoints()))};
        });
        var result=await new AssemblyMetaTransport(http).LocateGroundingAsync(
            Enumerable.Range(0,3).Select(_=>new byte[200]).ToArray(),Token,Ct);
        AssemblyMetaTransport.ValidateReceipt(result.Raw,result.ProviderResponseId,result.InputTokens,result.OutputTokens);
        Assert.Equal(1,http.Calls);
    }

    [Fact] public void ConfidenceInAnAbsentEntityNeverCreatesOrRejectsAnObjectBox() {
        var text=System.Text.Json.Nodes.JsonNode.Parse(GroundingPoints())!;
        foreach(var observation in text["observations"]!.AsArray())
            if(observation!["role"]!.GetValue<string>()=="pulley")observation["confidence"]=.98;
        var e=GroundingEvidence();
        e=e with {Proposal=AssemblyPolicy.ParseGrounding(text.ToJsonString(),e.Frames)};
        var scene=AssemblyPolicy.Ground(e)!;
        Assert.Single(scene.Roles);Assert.Equal("object",scene.Roles[0].Role);
        Assert.All(e.Proposal.Observations.Where(o=>o.Role=="pulley"),o=>{
            Assert.False(o.Visible);Assert.Equal(.98,o.Confidence);Assert.Equal(0,o.X1);});
    }
    [Fact] public async Task ApiModeCreatesNewImmutableSparkRunsAndTracksNewestScene() {
        using var f=new Fixture();var locator=f.Open();var request=f.GroundRequest() with {Mode="api-meta"};
        var first=await locator.GroundAsync("s","baseline",request,Ct);
        var q=await locator.GroundingQuoteAsync("s","baseline",Ct,"api-meta");
        Assert.Equal("NEEDS_LOCALIZATION",q.Status);Assert.True(q.FreshRequired);Assert.False(q.CacheHit);Assert.Null(q.Scene);
        var second=await locator.GroundAsync("s","baseline",request,Ct);
        Assert.False(first.CacheHit);Assert.False(second.CacheHit);Assert.False(second.FreshRequired);
        Assert.NotEqual(first.Scene!.Key,second.Scene!.Key);Assert.NotEqual(first.Evidence!.RunId,second.Evidence!.RunId);
        Assert.Equal(AssemblyPolicy.VideoGroundingProtocol,second.Evidence.Protocol);
        Assert.NotNull(second.Evidence.VideoInput);Assert.Equal(Sha,second.Evidence.VideoInput.SourceSha256);
        Assert.Equal(second.Scene.Key,(await locator.TrackingAsync("s","baseline",Ct))!.Key);
        var history=await locator.TrackingHistoryAsync("s","baseline",Ct);
        Assert.Equal(4,history.Length);Assert.Contains(history,s=>s.Key==first.Scene.Key);
        Assert.Equal(2,f.Transport.VideoCalls);Assert.Equal(2,f.Transport.ReviewCalls);Assert.Equal(4,f.Runtime.Reservations);
        var cached=await locator.GroundAsync("s","baseline",f.GroundRequest() with {ModelApiToken="",ConfirmPaidCall=false},Ct);
        Assert.True(cached.CacheHit);Assert.Equal(second.Scene.Key,cached.Scene!.Key);
        Assert.Equal(2,f.Transport.VideoCalls);
    }
    [Fact] public async Task FullVideoMustHaveSeparateConsentAndPreserveAllFrameTimes() {
        using var f=new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Open().GroundAsync("s","baseline",
            f.GroundRequest() with {ConfirmVideoTransfer=false},Ct));
        Assert.Equal(0,f.Runtime.Reservations);Assert.Equal(0,f.Transport.VideoCalls);
        f.Runtime.ShiftRemuxPts=true;
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct));
        Assert.Equal(0,f.Runtime.Reservations);Assert.Equal(0,f.Transport.VideoCalls);
    }
    [Fact] public async Task KnownNoInferenceOutcomeAllowsOnlyANewManualAttempt() {
        using var f=new Fixture();f.Transport.NoInference=true;
        await Assert.ThrowsAsync<AssemblyNoInferenceSentException>(()=>f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct));
        Assert.Contains("NO_INFERENCE_SENT",await File.ReadAllTextAsync(Path.Combine(f.GroundFolder,"outcome.json")));
        Assert.Equal(1,f.Runtime.Reservations);Assert.Equal(1,f.Transport.VideoCalls);
        var q=await f.Open().GroundingQuoteAsync("s","baseline",Ct);
        Assert.Equal("NEEDS_LOCALIZATION",q.Status);Assert.False(q.PriorAttempt);
        f.Transport.NoInference=false;
        await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct);
        Assert.Equal(3,f.Runtime.Reservations);Assert.Equal(2,f.Transport.VideoCalls);
    }
    [Fact] public async Task PreservedHistoricalResponseStaysReadableButCannotSeedANewSamCall() {
        using var f=new Fixture();Directory.CreateDirectory(f.LegacyGroundFolder);
        var anchors=new List<KnotFrame>();
        foreach(var (index,time) in new[]{(6,.1),(30,.5),(54,.9)})
            anchors.Add(await f.Frames.ExtractAsync("unused",Path.Combine(f.LegacyGroundFolder,$"frame-{anchors.Count}.jpg"),index,time,Ct));
        var payload=System.Text.Json.Nodes.JsonNode.Parse(GroundingPoints())!;
        foreach(var observation in payload["observations"]!.AsArray())
            if(observation!["role"]!.GetValue<string>()=="pulley")observation["confidence"]=.98;
        var raw=Envelope(payload.ToJsonString());
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await File.WriteAllTextAsync(Path.Combine(f.LegacyGroundFolder,"attempt.json"),JsonSerializer.Serialize(new {
            sha256=Sha,key=AssemblyPolicy.GroundingKey(Sha),model=AssemblyPolicy.Model,
            protocol=AssemblyPolicy.GroundingProtocol,createdAtUtc=DateTimeOffset.UtcNow,frames=anchors
        },options));
        await File.WriteAllTextAsync(Path.Combine(f.LegacyGroundFolder,"response.json"),raw);
        var q=await f.Open().GroundingQuoteAsync("s","baseline",Ct);
        Assert.Equal("NEEDS_LOCALIZATION",q.Status);Assert.False(q.CacheHit);Assert.False(q.PriorAttempt);
        Assert.Equal(2,q.RequiredSparkCalls);Assert.Null(await f.Open().TrackingAsync("s","baseline",Ct));
        var history=await f.Open().TrackingHistoryAsync("s","baseline",Ct);
        Assert.Single(history);Assert.Equal(AssemblyPolicy.GroundingKey(Sha),history[0].EvidenceKey);
        Assert.Equal(raw,await File.ReadAllTextAsync(Path.Combine(f.LegacyGroundFolder,"response.json")));
        Assert.Equal(0,f.Transport.VideoCalls);Assert.Equal(0,f.Runtime.Reservations);
        var freshQuote=await f.Open().GroundingQuoteAsync("s","baseline",Ct,"api-meta");
        Assert.Equal("NEEDS_LOCALIZATION",freshQuote.Status);Assert.True(freshQuote.FreshRequired);
    }


    [Fact] public async Task SuspensionUsesTheExactGroundingRunAndPreservesTheReceipt() {
        using var f=new Fixture();var locator=f.Open();var request=f.GroundRequest() with {Mode="api-meta"};
        var first=await locator.GroundAsync("s","baseline",request,Ct);
        var second=await locator.GroundAsync("s","baseline",request,Ct);
        var scene=first.Scene!;var before=JsonSerializer.Serialize(scene);
        var result=await locator.SuspensionAsync("s","baseline",scene,Ct);
        Assert.NotNull(result);Assert.Equal(first.Evidence!.CacheKey,result.EvidenceKey);
        Assert.NotEqual(second.Evidence!.CacheKey,result.EvidenceKey);
        Assert.Equal(scene.Key,result.SceneKey);Assert.Equal(first.Evidence.ResponseSha256,result.ResponseSha256);
        Assert.Equal(first.Evidence.Proposal.Suspension.Points,result.Points);
        Assert.Equal(before,JsonSerializer.Serialize(scene));Assert.Null(scene.PivotX);
        Assert.Equal(2,f.Transport.VideoCalls);
        await Assert.ThrowsAsync<InvalidDataException>(()=>locator.SuspensionAsync("s","baseline",
            scene with {ResponseSha256=new string('f',64)},Ct));
        Assert.Null(await locator.SuspensionAsync("s","baseline",scene with {Origin=null},Ct));
    }

    [Fact] public async Task FullVideoUsesOneNativeAnchorBatchAndRecordsLocalTiming() {
        using var f=new Fixture();
        var result=await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct);
        Assert.Equal(1,f.Frames.BatchCalls);Assert.Equal(50,f.Frames.Calls);
        Assert.Equal(Enumerable.Range(0,50).Select(i=>(int)Math.Round(599*i/49d)),result.Evidence!.Frames.Select(x=>x.Index));
        using var timing=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(f.GroundFolder,"preparation-timing.json")));
        Assert.Equal(50,timing.RootElement.GetProperty("referenceFrames").GetInt32());
        Assert.True(timing.RootElement.GetProperty("totalMs").GetInt64()>=0);
    }

    [Fact] public async Task UnreviewedDraftCannotSeedSamAndOnlyReviewIsCharged() {
        using var f=new Fixture();var locator=f.Open();
        await locator.GroundAsync("s","baseline",f.GroundRequest(),Ct);
        foreach(var name in new[]{"reviewed-evidence.json","object-review-attempt.json","object-review-response.json"})
            File.Delete(Path.Combine(f.GroundFolder,name));
        Assert.Null(await locator.TrackingAsync("s","baseline",Ct));
        var q=await locator.GroundingQuoteAsync("s","baseline",Ct);
        Assert.Equal("NEEDS_LOCALIZATION",q.Status);Assert.Equal(1,q.RequiredSparkCalls);Assert.Equal(.10,q.MaximumEstimatedCostUsd);
        var result=await locator.GroundAsync("s","baseline",f.GroundRequest() with {MaximumEstimatedCostUsd=q.MaximumEstimatedCostUsd},Ct);
        Assert.Equal("READY",result.Status);Assert.Equal(1,result.RequiredSparkCalls);
        Assert.Equal(1,f.Transport.VideoCalls);Assert.Equal(2,f.Transport.ReviewCalls);
        Assert.True(File.Exists(Directory.GetFiles(f.Root,"object-review-stages.json",SearchOption.AllDirectories).Single()));
        Assert.All(result.Scene!.Seeds,seed=>Assert.Equal(1344,seed.Y1));
    }
    [Fact] public async Task SuspensionCropReceiptSurvivesReloadButRejectsChangedPixels() {
        using var f=new Fixture();var locator=f.Open();
        var result=await locator.GroundAsync("s","baseline",f.GroundRequest(),Ct);
        var attempt=JsonSerializer.Deserialize<ObjectReviewAttempt>(await File.ReadAllTextAsync(
            Path.Combine(f.GroundFolder,"object-review-attempt.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var cropped=attempt.References.Where(r=>r.SuspensionCrop is not null).ToArray();
        Assert.Equal(3,cropped.Length);
        Assert.Equal(ObjectReviewPolicy.SuspensionOrdinals(attempt.References),cropped.Select(r=>r.Ordinal));
        var restored=await f.Open().TrackingAsync("s","baseline",Ct);
        Assert.Equal(result.Scene!.Key,restored!.Key);
        var suspension=await f.Open().SuspensionAsync("s","baseline",restored,Ct);
        Assert.Equal(ObjectReviewPolicy.Protocol,suspension!.Refinement);
        await File.WriteAllTextAsync(Path.Combine(f.GroundFolder,$"suspension-{cropped[0].Ordinal}.jpg"),"tampered");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Open().TrackingAsync("s","baseline",Ct));
        Assert.Equal(1,f.Transport.ReviewCalls);
    }
    [Fact] public async Task TamperedReviewedBoxIsRejectedBeforeSam() {
        using var f=new Fixture();var locator=f.Open();await locator.GroundAsync("s","baseline",f.GroundRequest(),Ct);
        var p=Path.Combine(f.GroundFolder,"reviewed-evidence.json");
        await File.WriteAllTextAsync(p,(await File.ReadAllTextAsync(p)).Replace("\"y1\":700","\"y1\":100"));
        await Assert.ThrowsAsync<InvalidDataException>(()=>locator.TrackingAsync("s","baseline",Ct));
    }
    [Fact] public async Task UncertainVisualReviewOverridesAConfidentDraft() {
        using var f=new Fixture();f.Transport.ReviewUncertain=true;
        var q=await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct);
        Assert.Equal("AMBIGUOUS",q.Status);Assert.Null(q.Scene);
        Assert.Null(await f.Open().TrackingAsync("s","baseline",Ct));
        Assert.All(q.Evidence!.Proposal.Observations,o=>Assert.False(o.Visible));
        Assert.Equal(2,f.Runtime.Reservations);
    }
    [Fact] public async Task WrongReviewFrameCannotSeedSamEvenWithHighConfidence() {
        using var f=new Fixture();f.Transport.WrongReviewOrdinal=true;
        await Assert.ThrowsAsync<FormatException>(()=>f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct));
        Assert.Null(await f.Open().TrackingAsync("s","baseline",Ct));
        Assert.True(File.Exists(Path.Combine(f.GroundFolder,"object-review-response.json")));
        Assert.False(File.Exists(Path.Combine(f.GroundFolder,"reviewed-evidence.json")));
        Assert.Equal(1,f.Transport.ReviewCalls);
    }
    [Fact] public async Task IncompleteVisualReviewNeverFallsBackToDraftBoxes() {
        using var f=new Fixture();var locator=f.Open();await locator.GroundAsync("s","baseline",f.GroundRequest(),Ct);
        File.Delete(Path.Combine(f.GroundFolder,"reviewed-evidence.json"));
        Assert.Null(await locator.TrackingAsync("s","baseline",Ct));
        var quote=await locator.GroundingQuoteAsync("s","baseline",Ct);
        Assert.True(quote.PriorAttempt);Assert.False(quote.CanRun);Assert.Equal(1,f.Transport.ReviewCalls);
    }

}
