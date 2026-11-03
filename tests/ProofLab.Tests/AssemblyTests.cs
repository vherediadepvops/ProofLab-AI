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
        public bool ExtraRemuxFrame;
        public Task<SamVideoSource> GetVerifiedSourceAsync(string s,string r,CancellationToken ct)=>
            Task.FromResult(new SamVideoSource(Sha,"video","synthetic.mp4","h264",1920,1080,10));
        public Task<double[]> ReadFramePtsAsync(string p,CancellationToken ct)=>
            Task.FromResult(Enumerable.Range(0,ExtraRemuxFrame&&p.EndsWith("full-video.mp4")?601:600).Select(i=>i/60d+(ShiftRemuxPts&&p.EndsWith("full-video.mp4")?.01:0)).ToArray());
        public Task CreateMp4Async(string a,string b,CancellationToken ct)=>File.WriteAllTextAsync(b,"SYNTHETIC_REMUX_NOT_VIDEO",ct);
        public void ReserveOrThrow(){if(!LiveCallsPermitted)throw new InvalidOperationException();Reservations++;}
    }
    private sealed class FramesStub:IKnotFrames {
        public int Calls, BatchCalls;
        public async Task<byte[]> CropSuspensionAsync(string p,KnotFrame f,SuspensionCrop crop,string dest,CancellationToken ct) {
            Assert.Equal(f.Sha256,Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(p,ct))).ToLowerInvariant());
            var bytes=Encoding.UTF8.GetBytes(new string('c',200));await File.WriteAllBytesAsync(dest,bytes,ct);return bytes;
        }
        public async Task<byte[]> MarkObjectAsync(string p,KnotFrame f,AssemblyObservation box,string dest,CancellationToken ct) {
            Assert.Equal(f.Sha256,Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(p,ct))).ToLowerInvariant());
            var bytes=Encoding.UTF8.GetBytes(new string('m',200));await File.WriteAllBytesAsync(dest,bytes,ct);return bytes;
        }
        public async Task<KnotFrame[]> ExtractBatchAsync(string p,string root,(int Index,double Time)[] samples,CancellationToken ct) {
            BatchCalls++;var result=new KnotFrame[samples.Length];
            for(var i=0;i<samples.Length;i++)result[i]=await ExtractAsync(p,Path.Combine(root,$"frame-{i}.jpg"),samples[i].Index,samples[i].Time,ct);
            return result;
        }
        public async Task<KnotFrame> ExtractAsync(string p,string dest,int index,double time,CancellationToken ct) {
            Calls++;var bytes=Encoding.UTF8.GetBytes(new string('x',200)+index.ToString()+"/"+time.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await File.WriteAllBytesAsync(dest,bytes,ct);
            return new(index,time,1080,1920,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
    }
    private sealed class Transport:IAssemblyTransport {
        public int Calls;public bool Fail;public Func<Task>? Wait;
        public System.Net.HttpStatusCode? RejectStatus;
        public string GroundingStatus="CLEAR_SINGLE_OBJECT";
        public bool NoInference;public int VideoCalls;public int InsufficientRounds;public int FailOnRound;public readonly List<string> FileIds=new();
        public double GroundingConfidence=.95;
        public int ZoomCalls;public readonly List<double> ZoomScales=new();public int FailOnZoom;
        public bool ZoomDisagree,ZoomHidden;
        public Task<KnotProviderResult> ReviewZoomSuspensionAsync(SuspensionZoomImage[] refs,byte[][] images,double zoom,string token,double budget,CancellationToken ct) {
            ZoomCalls++;ZoomScales.Add(zoom);Assert.Equal(50,refs.Length);Assert.Equal(50,images.Length);
            if(FailOnZoom==ZoomCalls)throw new HttpRequestException("Synthetic zoom transport failure");
            double y=ZoomDisagree&&zoom==3?200:133.3;
            var text=JsonSerializer.Serialize(new {camera="STATIC",reference_kind="FIXED_CORD_CONTACT",loop_motion="MOVING",contact_reason="Visible stationary cord contact; the lower loop swings independently.",points=refs.Select(r=>new {frame=r.Ordinal,visible=!ZoomHidden,
                x=ZoomHidden?0:(500*r.Frame.Width/1000-r.Crop.Left)*1000/r.Crop.Width,
                y=ZoomHidden?0:(y*r.Frame.Height/1000-r.Crop.Top)*1000/r.Crop.Height,
                confidence=ZoomHidden?0:.95,visual_reason="Visible free cord exit from its fixed upper support."})});
            return Task.FromResult(new KnotProviderResult(Envelope(text),"synthetic-not-provider",1000,100));
        }
        public int ReviewCalls;public bool ReviewUncertain;public bool WrongReviewOrdinal;public bool CoarseCord;public int ObjectInsufficientRounds;
        public Task<KnotProviderResult> ReviewObjectAsync(ObjectReviewImage[] refs,byte[][] images,string token,CancellationToken ct) {
            ReviewCalls++;
            var text=JsonSerializer.Serialize(new {observations=refs.Select(r=>new {frame=WrongReviewOrdinal?99:r.Ordinal,decision=ReviewUncertain?"UNCERTAIN":"CORRECTED",visible=!ReviewUncertain,
                x1=ReviewUncertain?0:(400*r.Frame.Width/1000-r.BodyCrop!.Left)*1000/r.BodyCrop.Width,y1=ReviewUncertain?0:(700*r.Frame.Height/1000-r.BodyCrop!.Top)*1000/r.BodyCrop.Height,x2=ReviewUncertain?0:(450*r.Frame.Width/1000-r.BodyCrop!.Left)*1000/r.BodyCrop.Width,y2=ReviewUncertain?0:(800*r.Frame.Height/1000-r.BodyCrop!.Top)*1000/r.BodyCrop.Height,confidence=ReviewUncertain?0:GroundingConfidence,visual_reason="The suspended body below the cord, not the cord."}),
                suspension=new {camera="STATIC",points=refs.Where(r=>r.SuspensionCrop is not null).Select(r=>new {frame=r.Ordinal,visible=true,x=500,y=333.25,confidence=.95,visual_reason="Visible fixed cord exit."})},
                target=new {status=GroundingStatus,justification=new {es="Cuerpo suspendido revisado.",en="Reviewed suspended body."}}});
            return Task.FromResult(new KnotProviderResult(Envelope(text),"synthetic-not-provider",1000,100));
        }
        public async Task<AssemblyVideoProviderResult> LocateVideoGroundingAsync(string path,AssemblyVideoInput input,
            KnotFrame[] anchors,byte[][] images,string token,double cost,Func<AssemblyVideoInput,Task> uploaded,CancellationToken ct) {
            VideoCalls++;
            if(NoInference)throw new AssemblyNoInferenceSentException(new InvalidOperationException("Synthetic admission failure"));
            if(FailOnRound==VideoCalls)throw new HttpRequestException("Synthetic round failure");
            var receipt=input with {ProviderFileId=string.IsNullOrEmpty(input.ProviderFileId)?"file-synthetic-"+VideoCalls:input.ProviderFileId};
            FileIds.Add(receipt.ProviderFileId);
            await uploaded(receipt);
            if(RejectStatus is { } status)throw new HttpRequestException("Synthetic provider rejection",null,status);
            var result=await LocateGroundingAsync(images,token,ct);
            if(anchors.Length==50)result=result with {Raw=Envelope(JsonSerializer.Serialize(new {
                arrangement="simple_pendulum",camera="STATIC",samples=Enumerable.Range(0,50).Select(i=>new double[]{i,1,400+i,CoarseCord?578:700,450+i,CoarseCord?681:800,VideoCalls<=ObjectInsufficientRounds?.2:GroundingConfidence}),
                points=Enumerable.Range(0,50).Select(i=>VideoCalls<=InsufficientRounds?new double[]{i,0,0,0,0}:new double[]{i,1,500,133.3,.95}),
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
        public AssemblyLocator Open(ILocalVideoPreparation? local=null)=>new(new Env(Root),Runtime,Frames,Transport,localPreparation:local);
        public KnotRunRequest Request()=>new(Sha,AssemblyPolicy.Key(Sha),Token,true,true,.05);
        public AssemblyGroundingRunRequest GroundRequest()=>new(Sha,AssemblyPolicy.VideoGroundingKey(Sha),Token,true,true,.35,ConfirmVideoTransfer:true);
        public string GroundFolder=>Directory.GetDirectories(Path.Combine(Root,".private","physics","assembly",
            AssemblyPolicy.VideoGroundingKey(Sha),"runs")).OrderBy(Directory.GetCreationTimeUtc).Last();
        public string LegacyGroundFolder=>Path.Combine(Root,".private","physics","assembly",AssemblyPolicy.GroundingKey(Sha));
        public async Task<AssemblyGroundingQuote> LegacyGroundAsync() {
            var uncertain=Transport.ReviewUncertain;var wrongOrdinal=Transport.WrongReviewOrdinal;
            Transport.ReviewUncertain=false;Transport.WrongReviewOrdinal=false;
            await Open().GroundAsync("s","baseline",GroundRequest(),Ct);
            Transport.ReviewUncertain=uncertain;Transport.WrongReviewOrdinal=wrongOrdinal;
            Transport.ReviewCalls=0;Transport.ZoomCalls=0;Runtime.Reservations-=1;
            var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var folder=GroundFolder;var saved=JsonSerializer.Deserialize<AssemblyEvidence>(await File.ReadAllTextAsync(Path.Combine(folder,"evidence.json")),options)!;
            var payload=System.Text.Json.Nodes.JsonNode.Parse(AssemblyMetaTransport.OutputText(await File.ReadAllTextAsync(Path.Combine(folder,"response.json"))))!.AsObject();
            payload.Remove("camera");payload["points"]=System.Text.Json.Nodes.JsonNode.Parse("[[0,1,500,133.3,0.95],[25,1,500,133.3,0.95],[49,1,500,133.3,0.95]]");
            var raw=Envelope(payload.ToJsonString());
            saved=saved with {ResponseSha256=KnotPolicy.Hash(raw),Proposal=AssemblyPolicy.ParseGrounding(payload.ToJsonString(),saved.Frames)};
            await File.WriteAllTextAsync(Path.Combine(folder,"response.json"),raw);
            await File.WriteAllTextAsync(Path.Combine(folder,"evidence.json"),JsonSerializer.Serialize(saved,options));
            foreach(var name in new[]{"dense-localization-attempt.json","dense-localization.json","reviewed-evidence.json","body-reviewed-evidence.json","object-review-attempt.json","object-review-response.json","object-review-stages.json"})File.Delete(Path.Combine(folder,name));
            return await Open().GroundAsync("s","baseline",GroundRequest() with {MaximumEstimatedCostUsd=.10},Ct);
        }
        // Build a synthetic preserved historical receipt, independently of the new
        // application path. No production writer or provider is called here.
        public async Task<AssemblyGroundingQuote> ProgressiveGroundAsync() {
            var initial=await Open().GroundAsync("s","baseline",GroundRequest(),Ct);
            var root=GroundFolder;var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
            async Task Write(string name,object value)=>await File.WriteAllTextAsync(Path.Combine(root,name),JsonSerializer.Serialize(value,options));
            var first=JsonSerializer.Deserialize<AssemblyEvidence>(await File.ReadAllTextAsync(Path.Combine(root,"evidence.json")),options)!;
            var points=first.Proposal.Suspension.Points;var inputHash=first.ResponseSha256;
            var answers=new List<SuspensionZoomAnswer>();var receipts=new List<KnotProviderResult>();
            for(var stage=0;stage<2;stage++) {
                var zoom=stage==0?1.5:3d;var folder=Path.Combine(root,$"zoom-{stage+2}");Directory.CreateDirectory(folder);
                var refs=new List<SuspensionZoomImage>();var images=new List<byte[]>();
                for(var i=0;i<50;i++) {
                    var frame=first.Frames[i];var crop=ProgressiveSuspensionPolicy.Crop(frame,points[i],zoom);
                    var path=Path.Combine(folder,$"crop-{i}.jpg");
                    var bytes=await ((IKnotFrames)Frames).CropZoomSuspensionAsync(Path.Combine(root,$"frame-{i}.jpg"),frame,crop,path,Ct);
                    refs.Add(new(i,frame,crop with {Sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}));images.Add(bytes);
                }
                await Write($"zoom-{stage+2}/attempt.json",new SuspensionZoomAttempt(ProgressiveSuspensionPolicy.Protocol,Sha,first.CacheKey,inputHash,zoom,refs.ToArray(),.25));
                var receipt=await Transport.ReviewZoomSuspensionAsync(refs.ToArray(),images.ToArray(),zoom,Token,.25,Ct);Runtime.Reservations++;
                await File.WriteAllTextAsync(Path.Combine(folder,"response.json"),receipt.Raw);
                var answer=ProgressiveSuspensionPolicy.Parse(AssemblyMetaTransport.OutputText(receipt.Raw),refs.ToArray());
                answers.Add(answer);receipts.Add(receipt);points=answer.Points;
                inputHash=KnotPolicy.Hash(inputHash+"\n"+KnotPolicy.Hash(receipt.Raw));
            }
            var combined=ProgressiveSuspensionPolicy.Combine(first,answers[0],answers[1],receipts[0],receipts[1]);
            await Write("dense-localization-attempt.json",new {protocol=ProgressiveSuspensionPolicy.Protocol,maximumAttempts=3});
            await Write("dense-localization.json",new {protocol=ProgressiveSuspensionPolicy.Protocol,rounds=3,stopReason="ZOOM_REVIEWS_COMPLETE",responseHashes=new[]{first.ResponseSha256}.Concat(receipts.Select(x=>KnotPolicy.Hash(x.Raw))).ToArray()});
            await Write("reviewed-evidence.json",combined);
            var attempt=JsonSerializer.Deserialize<ObjectReviewAttempt>(await File.ReadAllTextAsync(Path.Combine(root,"object-review-attempt.json")),options)!;
            attempt=attempt with {Protocol=ProgressiveSuspensionPolicy.BodyProtocol,DraftResponseSha256=combined.ResponseSha256};
            await Write("object-review-attempt.json",attempt);
            var raw=await File.ReadAllTextAsync(Path.Combine(root,"object-review-response.json"));
            var reviewed=ObjectReviewPolicy.Derive(combined,new(raw,"synthetic-not-provider",1000,100),attempt.References);
            await Write("body-reviewed-evidence.json",reviewed);
            return initial with {Evidence=reviewed,Scene=AssemblyPolicy.Ground(reviewed),Status=AssemblyPolicy.GroundingStatus(reviewed)};
        }
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
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(0,f.Transport.ZoomCalls);Assert.Equal(2,f.Runtime.Reservations);
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
    [Theory]
    [InlineData("api-meta")]
    [InlineData("api-meta-cache")]
    public async Task Confirmed413PreservesHistoryAndAllowsOnlyANewManualAttempt(string mode) {
        using var f=new Fixture();f.Transport.RejectStatus=System.Net.HttpStatusCode.RequestEntityTooLarge;
        var request=f.GroundRequest() with {Mode=mode};
        await Assert.ThrowsAsync<HttpRequestException>(()=>f.Open().GroundAsync("s","baseline",request,Ct));
        var failed=f.GroundFolder;
        var before=await File.ReadAllTextAsync(Path.Combine(failed,"attempt.json"));
        var outcome=await File.ReadAllTextAsync(Path.Combine(failed,"outcome.json"));
        Assert.Contains("PROVIDER_REJECTED_REQUEST",outcome);Assert.Contains("413",outcome);
        Assert.False(File.Exists(Path.Combine(failed,"response.json")));
        Assert.Equal(1,f.Transport.VideoCalls);Assert.Equal(1,f.Runtime.Reservations);
        var quote=await f.Open().GroundingQuoteAsync("s","baseline",Ct,mode);
        Assert.Equal("NEEDS_LOCALIZATION",quote.Status);Assert.True(quote.CanRun);Assert.False(quote.PriorAttempt);
        Assert.Equal(1,f.Transport.VideoCalls);Assert.Equal(1,f.Runtime.Reservations);
        f.Transport.RejectStatus=null;
        var result=await f.Open().GroundAsync("s","baseline",request,Ct);
        Assert.NotNull(result.Scene);Assert.NotEqual(failed,f.GroundFolder);
        Assert.Equal(2,f.Transport.VideoCalls);Assert.Equal(3,f.Runtime.Reservations);
        Assert.Equal(before,await File.ReadAllTextAsync(Path.Combine(failed,"attempt.json")));
        Assert.Equal(outcome,await File.ReadAllTextAsync(Path.Combine(failed,"outcome.json")));
    }
    [Fact] public async Task RejectedAttemptCannotHaveConflictingEvidenceOrWrongProvenance() {
        using var f=new Fixture();f.Transport.RejectStatus=System.Net.HttpStatusCode.RequestEntityTooLarge;
        await Assert.ThrowsAsync<HttpRequestException>(()=>f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct));
        var path=Path.Combine(f.GroundFolder,"outcome.json");var original=await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path,original.Replace(Sha,new string('b',64)));
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Open().GroundingQuoteAsync("s","baseline",Ct));
        await File.WriteAllTextAsync(path,original);
        await File.WriteAllTextAsync(Path.Combine(f.GroundFolder,"response.json"),Envelope(GroundingPoints()));
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Open().GroundingQuoteAsync("s","baseline",Ct));
        Assert.Equal(1,f.Transport.VideoCalls);Assert.Equal(1,f.Runtime.Reservations);
    }
    [Theory]
    [InlineData(System.Net.HttpStatusCode.InternalServerError)]
    [InlineData(System.Net.HttpStatusCode.RequestTimeout)]
    public async Task UnknownProviderOutcomeStillRequiresReview(System.Net.HttpStatusCode status) {
        using var f=new Fixture();f.Transport.RejectStatus=status;
        await Assert.ThrowsAsync<HttpRequestException>(()=>f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct));
        var quote=await f.Open().GroundingQuoteAsync("s","baseline",Ct);
        Assert.Equal("PRIOR_ATTEMPT",quote.Status);Assert.False(quote.CanRun);
        Assert.False(File.Exists(Path.Combine(f.GroundFolder,"outcome.json")));
        Assert.Equal(1,f.Transport.VideoCalls);Assert.Equal(1,f.Runtime.Reservations);
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
        Assert.Equal(DenseLocalizationPolicy.OriginalProtocol,archive.Suspension.Refinement);
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
        Assert.Equal(2,history.Length);Assert.Contains(history,s=>s.Key==first.Scene.Key);
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
    [Fact] public async Task ExtraRemuxFrameIsRejectedBeforeAnyProviderReservation() {
        using var f=new Fixture();f.Runtime.ExtraRemuxFrame=true;
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
        foreach(var name in new[]{"body-reviewed-evidence.json","object-review-attempt.json","object-review-response.json","object-review-stages.json"})
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
        var result=await f.LegacyGroundAsync();
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

    [Fact] public async Task BodyCropPixelsAreBoundToTheReviewedScene() {
        using var f=new Fixture();var locator=f.Open();var result=await f.LegacyGroundAsync();
        var attempt=JsonSerializer.Deserialize<ObjectReviewAttempt>(await File.ReadAllTextAsync(Path.Combine(f.GroundFolder,"object-review-attempt.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.All(attempt.References,r=>Assert.NotNull(r.BodyCrop));
        Assert.Equal(result.Scene!.Key,(await f.Open().TrackingAsync("s","baseline",Ct))!.Key);
        await File.WriteAllTextAsync(Path.Combine(f.GroundFolder,$"body-{attempt.References[0].Ordinal}.jpg"),"changed pixels");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Open().TrackingAsync("s","baseline",Ct));
        Assert.Equal(1,f.Transport.ReviewCalls);
    }
    [Fact] public async Task TamperedReviewedBoxIsRejectedBeforeSam() {
        using var f=new Fixture();var locator=f.Open();await f.LegacyGroundAsync();
        var p=Path.Combine(f.GroundFolder,"reviewed-evidence.json");
        await File.WriteAllTextAsync(p,(await File.ReadAllTextAsync(p)).Replace("\"y1\":700","\"y1\":100"));
        await Assert.ThrowsAsync<InvalidDataException>(()=>locator.TrackingAsync("s","baseline",Ct));
    }
    [Fact] public async Task UncertainVisualReviewOverridesAConfidentDraft() {
        using var f=new Fixture();f.Transport.ReviewUncertain=true;
        var q=await f.LegacyGroundAsync();
        Assert.Equal("AMBIGUOUS",q.Status);Assert.Null(q.Scene);
        Assert.Null(await f.Open().TrackingAsync("s","baseline",Ct));
        Assert.All(q.Evidence!.Proposal.Observations,o=>Assert.False(o.Visible));
        Assert.Equal(2,f.Runtime.Reservations);
    }
    [Fact] public async Task WrongReviewFrameCannotSeedSamEvenWithHighConfidence() {
        using var f=new Fixture();f.Transport.WrongReviewOrdinal=true;
        await Assert.ThrowsAsync<FormatException>(()=>f.LegacyGroundAsync());
        Assert.Null(await f.Open().TrackingAsync("s","baseline",Ct));
        Assert.True(File.Exists(Path.Combine(f.GroundFolder,"object-review-response.json")));
        Assert.False(File.Exists(Path.Combine(f.GroundFolder,"reviewed-evidence.json")));
        Assert.Equal(1,f.Transport.ReviewCalls);
    }
    [Fact] public async Task IncompleteVisualReviewNeverFallsBackToDraftBoxes() {
        using var f=new Fixture();var locator=f.Open();await f.LegacyGroundAsync();
        File.Delete(Path.Combine(f.GroundFolder,"reviewed-evidence.json"));
        Assert.Null(await locator.TrackingAsync("s","baseline",Ct));
        var quote=await locator.GroundingQuoteAsync("s","baseline",Ct);
        Assert.True(quote.PriorAttempt);Assert.False(quote.CanRun);Assert.Equal(1,f.Transport.ReviewCalls);
    }


    [Theory] [InlineData("api-meta")] [InlineData("api-meta-cache")]
    public async Task OriginalReferencesAndBodyReviewUseTwoCallsWithoutAnyZoom(string mode) {
        using var f=new Fixture();f.Transport.FailOnZoom=1;
        var q=await f.Open().GroundingQuoteAsync("s","baseline",Ct,mode);
        Assert.Equal(2,q.RequiredSparkCalls);Assert.Equal(.35,q.MaximumEstimatedCostUsd);Assert.Equal(0,f.Transport.VideoCalls);
        Assert.Equal(DenseLocalizationPolicy.OriginalProtocol,q.LocalizationPlan);
        var result=await f.Open().GroundAsync("s","baseline",f.GroundRequest() with {Mode=mode},Ct);
        Assert.Equal(50,result.Evidence!.Frames.Length);Assert.Equal(50,result.Evidence.Frames.Select(x=>x.Index).Distinct().Count());
        Assert.Equal(DenseLocalizationPolicy.OriginalBodyProtocol,result.Evidence.Derivation);
        Assert.Equal(1,f.Transport.VideoCalls);Assert.Equal(1,f.Transport.ReviewCalls);Assert.Equal(2,f.Runtime.Reservations);
        Assert.Equal(0,f.Transport.ZoomCalls);Assert.Empty(f.Transport.ZoomScales);Assert.Empty(Directory.GetDirectories(f.GroundFolder,"zoom-*"));
        Assert.Equal("CONSENSUS_ESTIMATE",result.Evidence.Proposal.Consensus!.Status);Assert.Null(result.Evidence.Proposal.ContactReviews);
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var first=JsonSerializer.Deserialize<AssemblyEvidence>(await File.ReadAllTextAsync(Path.Combine(f.GroundFolder,"evidence.json")),options)!;
        Assert.Equal(first.Proposal.Suspension.Points,result.Evidence.Proposal.Suspension.Points);
        var restored=await f.Open().SuspensionAsync("s","baseline",result.Scene!,Ct);
        Assert.Equal(DenseLocalizationPolicy.OriginalProtocol,restored!.Refinement);Assert.Null(restored.ContactReviews);
        var cached=await f.Open().GroundAsync("s","baseline",f.GroundRequest() with {ModelApiToken="",ConfirmPaidCall=false},Ct);
        Assert.True(cached.CacheHit);Assert.Equal(result.Scene!.Key,cached.Scene!.Key);
        Assert.Equal(2,f.Runtime.Reservations);Assert.Equal(0,f.Transport.ZoomCalls);
    }

    [Fact] public async Task MissingOriginalSuspensionDoesNotTriggerZoomOrInventContact() {
        using var f=new Fixture();f.Transport.InsufficientRounds=1;
        var result=await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct);
        Assert.Equal(50,result.Evidence!.Frames.Length);Assert.Equal(0,f.Transport.ZoomCalls);
        Assert.Null(result.Evidence.Proposal.Suspension.X);Assert.Equal("INSUFFICIENT_OBSERVATIONS",result.Evidence.Proposal.Consensus!.Status);
        Assert.Null(result.Evidence.Proposal.ContactReviews);Assert.All(result.Evidence.Proposal.Suspension.Points,p=>Assert.False(p.Visible));
        Assert.Equal(1,f.Transport.VideoCalls);Assert.Equal(1,f.Transport.ReviewCalls);Assert.Equal(2,f.Runtime.Reservations);
        Assert.Contains("ORIGINAL_REFERENCES_COMPLETE",await File.ReadAllTextAsync(Path.Combine(f.GroundFolder,"dense-localization.json")));
        Assert.Equal(result.Scene!.Key,(await f.Open().TrackingAsync("s","baseline",Ct))!.Key);
    }

    [Fact] public async Task HistoricalIncompleteZoomRemainsPendingWithoutFabricatedCompletionOrRetry() {
        using var f=new Fixture();await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct);
        var old=f.GroundFolder;
        await File.WriteAllTextAsync(Path.Combine(old,"dense-localization-attempt.json"),JsonSerializer.Serialize(new {protocol=ProgressiveSuspensionPolicy.Protocol,maximumAttempts=3}));
        foreach(var name in new[]{"reviewed-evidence.json","dense-localization.json","body-reviewed-evidence.json","object-review-attempt.json","object-review-response.json"})File.Delete(Path.Combine(old,name));
        Directory.CreateDirectory(Path.Combine(old,"zoom-2"));await File.WriteAllTextAsync(Path.Combine(old,"zoom-2","attempt.json"),"preserved interrupted zoom request");
        var oldResponse=await File.ReadAllBytesAsync(Path.Combine(old,"response.json"));
        Assert.Null(await f.Open().TrackingAsync("s","baseline",Ct));
        var pending=await f.Open().GroundingQuoteAsync("s","baseline",Ct);Assert.True(pending.PriorAttempt);Assert.False(pending.CanRun);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct));
        Assert.Equal(2,f.Runtime.Reservations);Assert.Equal(0,f.Transport.ZoomCalls);
        var fresh=await f.Open().GroundAsync("s","baseline",f.GroundRequest() with {Mode="api-meta"},Ct);Assert.NotNull(fresh.Scene);
        Assert.Equal(4,f.Runtime.Reservations);Assert.Equal(0,f.Transport.ZoomCalls);
        Assert.False(File.Exists(Path.Combine(old,"dense-localization.json")));Assert.Equal(oldResponse,await File.ReadAllBytesAsync(Path.Combine(old,"response.json")));
        Assert.Equal("preserved interrupted zoom request",await File.ReadAllTextAsync(Path.Combine(old,"zoom-2","attempt.json")));
    }

    [Fact] public async Task FreshGroundingBypassesHistoricalResponsesAndPreparedMediaWithoutRewritingThem(){
        using var f=new Fixture();var locator=f.Open();
        var first=await locator.GroundAsync("s","baseline",f.GroundRequest(),Ct);
        var root=Path.Combine(f.Root,".private","physics","assembly",AssemblyPolicy.VideoGroundingKey(Sha));
        var oldResponse=Path.Combine(root,"runs",first.Evidence!.RunId!,"response.json");
        await File.AppendAllTextAsync(oldResponse," ");var oldBytes=await File.ReadAllTextAsync(oldResponse);
        var oldPrepared=Path.Combine(root,"prepared-fifty-boundaries-v2.json");
        await File.WriteAllTextAsync(oldPrepared,"invalid old prepared cache");
        var quote=await locator.GroundingQuoteAsync("s","baseline",Ct,"api-meta");
        Assert.True(quote.FreshRequired);Assert.False(quote.CacheHit);Assert.False(quote.PriorAttempt);Assert.Equal(1,f.Transport.VideoCalls);
        var fresh=await locator.GroundAsync("s","baseline",f.GroundRequest() with{Mode="api-meta"},Ct);
        Assert.NotEqual(first.Evidence.RunId,fresh.Evidence!.RunId);Assert.Equal(2,f.Transport.VideoCalls);Assert.Equal(4,f.Runtime.Reservations);
        Assert.Equal(100,f.Frames.Calls);Assert.Equal(2,f.Frames.BatchCalls);
        Assert.Equal(fresh.Scene!.Key,(await locator.TrackingRunAsync("s","baseline",fresh.Evidence.RunId!,Ct))!.Key);
        Assert.Equal(oldBytes,await File.ReadAllTextAsync(oldResponse));Assert.Equal("invalid old prepared cache",await File.ReadAllTextAsync(oldPrepared));
        await Assert.ThrowsAsync<InvalidDataException>(()=>locator.TrackingRunAsync("s","baseline",first.Evidence.RunId!,Ct));
    }

    [Fact] public async Task ExactSparkRunPinsSceneAndSuspensionAndRejectsWrongOrChangedEvidence(){
        using var f=new Fixture();var locator=f.Open();var request=f.GroundRequest() with{Mode="api-meta"};
        var first=await locator.GroundAsync("s","baseline",request,Ct);var second=await locator.GroundAsync("s","baseline",request,Ct);
        var firstId=first.Evidence!.RunId!;var secondId=second.Evidence!.RunId!;
        Assert.Equal(first.Scene!.Key,(await locator.TrackingRunAsync("s","baseline",firstId,Ct))!.Key);
        var suspension=await locator.SuspensionRunAsync("s","baseline",firstId,first.Scene,Ct);
        Assert.Equal(first.Scene.Key,suspension!.SceneKey);Assert.Equal(first.Evidence.ResponseSha256,suspension.ResponseSha256);
        await Assert.ThrowsAsync<InvalidDataException>(()=>locator.SuspensionRunAsync("s","baseline",secondId,first.Scene,Ct));
        var response=Path.Combine(f.Root,".private","physics","assembly",AssemblyPolicy.VideoGroundingKey(Sha),"runs",secondId,"response.json");
        await File.AppendAllTextAsync(response," ");
        Assert.Equal(first.Scene.Key,(await locator.TrackingRunAsync("s","baseline",firstId,Ct))!.Key);
        Assert.Equal(first.Scene.Key,(await locator.SuspensionRunAsync("s","baseline",firstId,first.Scene,Ct))!.SceneKey);
        await Assert.ThrowsAsync<InvalidDataException>(()=>locator.TrackingRunAsync("s","baseline",secondId,Ct));
        await Assert.ThrowsAsync<ArgumentException>(()=>locator.TrackingRunAsync("s","baseline","../untrusted",Ct));
        await Assert.ThrowsAsync<FileNotFoundException>(()=>locator.TrackingRunAsync("s","baseline",new string('f',32),Ct));
        Assert.Equal(2,f.Transport.VideoCalls);Assert.Equal(4,f.Runtime.Reservations);
    }

    [Theory] [InlineData("cache-only")] [InlineData("none")] [InlineData("unknown")]
    public async Task NonApiSelectionsCannotStartSpark(string mode){
        using var f=new Fixture();var locator=f.Open();
        await Assert.ThrowsAsync<ArgumentException>(()=>locator.GroundingQuoteAsync("s","baseline",Ct,mode));
        await Assert.ThrowsAsync<ArgumentException>(()=>locator.GroundAsync("s","baseline",f.GroundRequest() with{Mode=mode},Ct));
        Assert.Equal(0,f.Transport.VideoCalls);Assert.Equal(0,f.Runtime.Reservations);Assert.Equal(0,f.Frames.Calls);
    }

    [Fact] public async Task DenseCordCandidateIsCorrectedBeforeSamWithoutLosingSuspensionConsensus() {
        using var f=new Fixture();f.Transport.CoarseCord=true;
        var result=await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct);
        Assert.Equal("READY",result.Status);Assert.Equal(DenseLocalizationPolicy.OriginalBodyProtocol,result.Evidence!.Derivation);
        Assert.Equal(50,result.Evidence.Proposal.Suspension.Points.Length);
        Assert.Equal("CONSENSUS_ESTIMATE",result.Evidence.Proposal.Consensus!.Status);
        Assert.All(result.Scene!.Seeds,seed=>Assert.Equal(1344,seed.Y1));
        Assert.Equal(1,f.Transport.VideoCalls);Assert.Equal(1,f.Transport.ReviewCalls);
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var draft=JsonSerializer.Deserialize<AssemblyEvidence>(await File.ReadAllTextAsync(Path.Combine(f.GroundFolder,"evidence.json")),options)!;
        Assert.All(draft.Proposal.Observations,o=>Assert.Equal(578,o.Y1));
        var attempt=JsonSerializer.Deserialize<ObjectReviewAttempt>(await File.ReadAllTextAsync(Path.Combine(f.GroundFolder,"object-review-attempt.json")),options)!;
        Assert.Equal(9,attempt.References.Length);Assert.All(attempt.References,r=>Assert.Null(r.SuspensionCrop));
        Assert.Equal(result.Scene.Key,(await f.Open().TrackingAsync("s","baseline",Ct))!.Key);
        await File.WriteAllTextAsync(Path.Combine(f.GroundFolder,$"body-{attempt.References[0].Ordinal}.jpg"),"tampered");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Open().TrackingAsync("s","baseline",Ct));
    }
    [Fact] public async Task DenseUncertainBodyReviewDoesNotReuseConfidentCordCandidate() {
        using var f=new Fixture();f.Transport.CoarseCord=true;f.Transport.ReviewUncertain=true;
        var result=await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct);
        Assert.Equal("AMBIGUOUS",result.Status);Assert.Null(result.Scene);
        Assert.Null(await f.Open().TrackingAsync("s","baseline",Ct));
        Assert.Empty(await f.Open().TrackingHistoryAsync("s","baseline",Ct));
        Assert.All(result.Evidence!.Proposal.Observations,o=>Assert.False(o.Visible));
    }
    [Fact] public async Task BodyReviewUsesOriginalJpegsWithoutSuspensionCrops() {
        using var f=new Fixture();var result=await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct);
        Assert.Equal(50,result.Evidence!.Frames.Length);Assert.NotNull(result.Scene);
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var attempt=JsonSerializer.Deserialize<ObjectReviewAttempt>(await File.ReadAllTextAsync(Path.Combine(f.GroundFolder,"object-review-attempt.json")),options)!;
        var first=JsonSerializer.Deserialize<AssemblyVideoAttempt>(await File.ReadAllTextAsync(Path.Combine(f.GroundFolder,"attempt.json")),options)!;
        Assert.Equal(DenseLocalizationPolicy.OriginalBodyProtocol,attempt.Protocol);Assert.Equal(9,attempt.References.Length);
        Assert.All(attempt.References,r=> {Assert.Contains(r.Frame,first.Frames);Assert.Equal(r.Frame,result.Evidence.Frames[r.Ordinal]);Assert.NotNull(r.BodyCrop);Assert.Null(r.SuspensionCrop);});
        Assert.Equal(result.Scene!.Key,(await f.Open().TrackingAsync("s","baseline",Ct))!.Key);
        Assert.Equal(1,f.Transport.VideoCalls);Assert.Equal(0,f.Transport.ZoomCalls);Assert.Equal(1,f.Transport.ReviewCalls);
    }

    [Fact] public async Task DisagreeingZoomsNeverFallBackToTheConfidentOriginalPoint() {
        using var f=new Fixture();f.Transport.ZoomDisagree=true;
        var result=await f.ProgressiveGroundAsync();
        Assert.Equal("ZOOM_REVIEW_INCONCLUSIVE",result.Evidence!.Proposal.Consensus!.Status);
        Assert.Null(result.Evidence.Proposal.Suspension.X);Assert.Empty(result.Evidence.Proposal.Consensus.InlierOrdinals);
        Assert.Equal(2,f.Transport.ZoomCalls);Assert.Equal(4,f.Runtime.Reservations);
        var restored=await f.Open().SuspensionAsync("s","baseline",result.Scene!,Ct);
        Assert.Equal("ZOOM_REVIEW_INCONCLUSIVE",restored!.Consensus!.Status);
    }
    [Theory] [InlineData("response.json")] [InlineData("attempt.json")]
    public async Task AlteredZoomResponseOrGeometryCannotBeReused(string filename) {
        using var f=new Fixture();await f.ProgressiveGroundAsync();
        var path=Path.Combine(f.GroundFolder,"zoom-3",filename);
        var original=await File.ReadAllTextAsync(path);
        var changed=filename=="response.json"?original+" ":original.Replace("\"zoom\":3","\"zoom\":1.5");
        Assert.NotEqual(original,changed);await File.WriteAllTextAsync(path,changed);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Open().TrackingAsync("s","baseline",Ct));
        Assert.Equal(4,f.Runtime.Reservations);
    }
    [Fact] public async Task HistoricalDenseReviewRemainsReadableWithoutNewRequests() {
        using var f=new Fixture();await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct);
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);var root=f.GroundFolder;
        async Task Write(string name,object value)=>await File.WriteAllTextAsync(Path.Combine(root,name),JsonSerializer.Serialize(value,options));
        var first=JsonSerializer.Deserialize<AssemblyEvidence>(await File.ReadAllTextAsync(Path.Combine(root,"evidence.json")),options)!;
        var combined=DenseLocalizationPolicy.Combine([first]);
        await Write("dense-localization-attempt.json",new {protocol=DenseLocalizationPolicy.Protocol,maximumAttempts=3});
        await Write("dense-localization.json",new {protocol=DenseLocalizationPolicy.Protocol,rounds=1,stopReason="CONSENSUS_REACHED",responseHashes=new[]{first.ResponseSha256}});
        await Write("reviewed-evidence.json",combined);
        var attempt=JsonSerializer.Deserialize<ObjectReviewAttempt>(await File.ReadAllTextAsync(Path.Combine(root,"object-review-attempt.json")),options)!;
        attempt=attempt with {Protocol=ObjectReviewPolicy.DenseBodyProtocol,DraftResponseSha256=combined.ResponseSha256};
        await Write("object-review-attempt.json",attempt);
        var raw=await File.ReadAllTextAsync(Path.Combine(root,"object-review-response.json"));
        var derived=ObjectReviewPolicy.Derive(combined,new KnotProviderResult(raw,"synthetic-not-provider",1000,100),attempt.References);
        await Write("body-reviewed-evidence.json",derived);
        var scene=await f.Open().TrackingAsync("s","baseline",Ct);Assert.NotNull(scene);
        Assert.Equal(AssemblyPolicy.Ground(derived)!.Key,scene.Key);
        Assert.Equal(DenseLocalizationPolicy.Protocol,(await f.Open().SuspensionAsync("s","baseline",scene,Ct))!.Refinement);
        Assert.Equal(2,f.Runtime.Reservations);
    }
    [Theory]
    [InlineData("FIXED_CORD_CONTACT","CONSENSUS_ESTIMATE")]
    [InlineData("MOVING_CONTACT","MOVING_CORD_CONTACT")]
    [InlineData("UNRESOLVED","CORD_CONTACT_UNRESOLVED")]
    public async Task MovingLoopDoesNotProveMovingOrFixedEffectiveContact(string kind,string status) {
        using var f=new Fixture();await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct);
        var first=JsonSerializer.Deserialize<AssemblyEvidence>(await File.ReadAllTextAsync(Path.Combine(f.GroundFolder,"evidence.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var medium=ProgressiveSuspensionPolicy.Parse(ZoomAnswer(ZoomReferences()),ZoomReferences());
        var close=medium with {Contact=new(kind,"MOVING","Inspect the actual cord/support contact, independently of the moving loop.")};
        var receipt=new KnotProviderResult("synthetic", "offline",100,50);
        var result=ProgressiveSuspensionPolicy.Combine(first,medium,close,receipt,receipt);
        Assert.Equal(status,result.Proposal.Consensus!.Status);
        Assert.Equal(2,result.Proposal.ContactReviews!.Length);
        if(kind!="FIXED_CORD_CONTACT")Assert.Null(result.Proposal.Suspension.X);
        else Assert.NotNull(result.Proposal.Suspension.X);
        var saved=(await f.Open().SuspensionAsync("s","baseline",AssemblyPolicy.Ground((await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct)).Evidence!)!,Ct))!;
        Assert.Null(saved.ContactReviews);
    }
    [Fact] public void LegacyCoordinatesRemainReadableButCannotMasqueradeAsContactReview() {
        var refs=ZoomReferences();var json=System.Text.Json.Nodes.JsonNode.Parse(ZoomAnswer(refs))!.AsObject();
        json.Remove("reference_kind");json.Remove("loop_motion");json.Remove("contact_reason");
        var legacy=ProgressiveSuspensionPolicy.Parse(json.ToJsonString(),refs,ProgressiveSuspensionPolicy.LegacyProtocol);
        Assert.Null(legacy.Contact);Assert.Equal(50,legacy.Points.Length);
        Assert.Throws<KeyNotFoundException>(()=>ProgressiveSuspensionPolicy.Parse(json.ToJsonString(),refs));
    }
    private static SuspensionZoomImage[] ZoomReferences(double zoom=1.5) =>Enumerable.Range(0,50).Select(i=> {
        var frame=new KnotFrame(i,i*.2,1080,1920,Sha);
        return new SuspensionZoomImage(i,frame,ProgressiveSuspensionPolicy.Crop(frame,new(i,true,458,134,.95),zoom));
    }).ToArray();
    private static string ZoomAnswer(SuspensionZoomImage[] refs,double x=440,double y=155)=>JsonSerializer.Serialize(new {
        camera="STATIC",reference_kind="FIXED_CORD_CONTACT",loop_motion="MOVING",contact_reason="Visible stationary cord contact; the lower loop swings independently.",points=refs.Select(r=>new {frame=r.Ordinal,visible=true,
            x=(x*r.Frame.Width/1000-r.Crop.Left)*1000/r.Crop.Width,
            y=(y*r.Frame.Height/1000-r.Crop.Top)*1000/r.Crop.Height,
            confidence=.95,visual_reason="Free cord leaves the fixed support."})});
    [Theory] [InlineData(1.5)] [InlineData(3d)]
    public void ZoomCoordinatesRoundTripIntoOriginalPortraitPixels(double zoom) {
        var refs=ZoomReferences(zoom);var result=ProgressiveSuspensionPolicy.Parse(ZoomAnswer(refs),refs);
        Assert.All(result.Points,p=>{Assert.True(p.Visible);Assert.Equal(440,p.X,8);Assert.Equal(155,p.Y,8);});
        Assert.All(refs,r=>{Assert.Equal((int)Math.Round(1080/zoom),r.Crop.Width);Assert.Equal((int)Math.Round(1920/zoom),r.Crop.Height);});
        var edge=ProgressiveSuspensionPolicy.Crop(refs[0].Frame,new(0,true,999,999,.95),zoom);
        Assert.Equal(1080,edge.Left+edge.Width);Assert.Equal(1920,edge.Top+edge.Height);
    }
    [Theory] [InlineData("ordinal")] [InlineData("coordinate")] [InlineData("reason")] [InlineData("missing")]
    public void InvalidZoomAnswersCannotAcquireNativeCoordinates(string invalid) {
        var refs=ZoomReferences();var json=System.Text.Json.Nodes.JsonNode.Parse(ZoomAnswer(refs))!;
        var points=json["points"]!.AsArray();
        switch(invalid){case "ordinal":points[1]!["frame"]=0;break;case "coordinate":points[0]!["x"]=1000;break;
            case "reason":points[0]!["visual_reason"]="";break;default:points.RemoveAt(49);break;}
        Assert.Throws<FormatException>(()=>ProgressiveSuspensionPolicy.Parse(json.ToJsonString(),refs));
    }
    [Fact] public void ExactNativeIndicesAreMappedWithoutChangingCropCoordinates() {
        var refs=ZoomReferences().Select((r,i)=>r with {Frame=r.Frame with {Index=i*29}}).ToArray();
        var ordinal=ProgressiveSuspensionPolicy.Parse(ZoomAnswer(refs),refs);
        var json=System.Text.Json.Nodes.JsonNode.Parse(ZoomAnswer(refs))!;
        foreach(var (point,i) in json["points"]!.AsArray().Select((p,i)=>(p,i)))point!["frame"]=refs[i].Frame.Index;
        var native=ProgressiveSuspensionPolicy.Parse(json.ToJsonString(),refs);
        Assert.Equal(ordinal.Points,native.Points);
        Assert.Equal(Enumerable.Range(0,50),native.Points.Select(p=>p.Frame));
        json["points"]![1]!["frame"]=1;
        Assert.Throws<FormatException>(()=>ProgressiveSuspensionPolicy.Parse(json.ToJsonString(),refs));
        json["points"]![1]!["frame"]=99999;
        Assert.Throws<FormatException>(()=>ProgressiveSuspensionPolicy.Parse(json.ToJsonString(),refs));
    }
    [Fact] public void ZoomBoundaryIsNotMistakenForAVisibleCordExit() {
        var refs=ZoomReferences();var json=System.Text.Json.Nodes.JsonNode.Parse(ZoomAnswer(refs))!;
        foreach(var point in json["points"]!.AsArray())point!["x"]=1;
        Assert.All(ProgressiveSuspensionPolicy.Parse(json.ToJsonString(),refs).Points,p=>{Assert.False(p.Visible);Assert.Equal(0,p.Confidence);});
    }
    [Theory] [InlineData("STATIC",true)] [InlineData("MOVING",false)] [InlineData("UNCLEAR",false)]
    public async Task EnlargedReviewsCanCorrectTheCoarsePointButCannotResolveCameraMotion(string camera,bool accepted) {
        using var f=new Fixture();await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct);
        var first=JsonSerializer.Deserialize<AssemblyEvidence>(await File.ReadAllTextAsync(Path.Combine(f.GroundFolder,"evidence.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var medium=ProgressiveSuspensionPolicy.Parse(ZoomAnswer(ZoomReferences()),ZoomReferences());
        var close=medium with {Camera=camera};
        var receipt=new KnotProviderResult("synthetic", "offline",100,50);
        var result=ProgressiveSuspensionPolicy.Combine(first,medium,close,receipt,receipt);
        Assert.Equal(accepted?"CONSENSUS_ESTIMATE":"ZOOM_REVIEW_INCONCLUSIVE",result.Proposal.Consensus!.Status);
        if(accepted){Assert.Equal(440,result.Proposal.Suspension.X!.Value,8);Assert.Equal(155,result.Proposal.Suspension.Y!.Value,8);}
        else Assert.Null(result.Proposal.Suspension.X);
        Assert.Equal(500,first.Proposal.Suspension.Points[0].X);
        Assert.NotEqual(first.ResponseSha256,result.ResponseSha256);
    }
    [Fact] public async Task ZoomTransportSendsExactlyFiftyImagesWithoutUploadingAnotherVideo() {
        var images=Enumerable.Range(0,50).Select(_=>new byte[200]).ToArray();
        var hash=Convert.ToHexString(SHA256.HashData(images[0])).ToLowerInvariant();
        var refs=ZoomReferences().Select(r=>r with {Crop=r.Crop with {Sha256=hash}}).ToArray();
        using var http=new HttpStub(r=> {
            Assert.EndsWith("/responses",r.RequestUri!.AbsolutePath);
            var body=r.Content!.ReadAsStringAsync().Result;using var doc=JsonDocument.Parse(body);
            var content=doc.RootElement.GetProperty("input")[0].GetProperty("content").EnumerateArray().ToArray();
            Assert.Equal(50,content.Count(c=>c.GetProperty("type").GetString()=="input_image"));
            Assert.DoesNotContain("input_video",body);Assert.DoesNotContain("input_file",body);
            Assert.Contains("WITHIN THIS CROP",body);Assert.Contains("visual_reason",body);
            return new(System.Net.HttpStatusCode.OK){Content=new StringContent(Envelope(ZoomAnswer(refs)))};
        });
        var result=await new AssemblyMetaTransport(http).ReviewZoomSuspensionAsync(refs,images,1.5,Token,.25,Ct);
        Assert.Equal(50,ProgressiveSuspensionPolicy.Parse(AssemblyMetaTransport.OutputText(result.Raw),refs).Points.Length);
        Assert.Equal(1,http.Calls);
    }

    private sealed class SharedLocalMedia(Fixture fixture):ILocalVideoPreparation
    {
        private LocalPreparedMedia? ready;
        public void Queue(SamVideoSource source)=>throw new NotSupportedException();
        public Task<LocalPreparationStatus?> StatusAsync(SamVideoSource source,CancellationToken ct)=>throw new NotSupportedException();
        public Task<SamAdaptiveClipManifest> PrepareSamAsync(SamVideoSource source,IReadOnlyList<double> pts,SamAdaptiveSegmentPlan plan,string directory,CancellationToken ct)=>throw new NotSupportedException();
        public async Task<LocalPreparedMedia> PrepareAsync(SamVideoSource source,CancellationToken ct)
        {
            if(ready is not null)return ready with {Reused=true};
            var root=Path.Combine(fixture.Root,".private","shared-local-media");Directory.CreateDirectory(root);
            var path=Path.Combine(root,"full-video.mp4");await fixture.Runtime.CreateMp4Async(source.Path,path,ct);
            var pts=await ((ISamVideoRuntime)fixture.Runtime).ReadSourceFramePtsAsync(source,ct);
            var plan=SamAdaptiveSegmentPlan.RestoreNative(pts.Length,53,1,[new(0,0,pts.Length,0,pts.Length)]);
            var indices=AssemblyPolicy.ReferenceIndices(pts.Length,plan);
            var refs=await fixture.Frames.ExtractBatchAsync(source.Path,root,indices.Select(i=>(i,pts[i])).ToArray(),ct);
            var bytes=await File.ReadAllBytesAsync(path,ct);var sha=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var clip=new SamAdaptiveClip(0,0,pts.Length,0,pts.Length,pts.Length,"part-00.mp4",bytes.Length,sha,null,0,plan.BilledFrames);
            var manifest=new SamAdaptiveClipManifest(source.Sha256,plan.VerifySourcePts(pts),pts.Length,pts.Length,plan.BilledFrames,
                plan.EstimatedSamCostUsd,"SAM_CONTINUOUS_ORIGINAL_REMUX_FILE_SHA_PTS_V2",[clip],1);
            return ready=new(path,root,sha,bytes.Length,refs,pts,false,manifest);
        }
    }
    [Fact] public async Task FreshMetaRunsReuseOnlyLocalMediaAndPreserveHistoricalEvidence()
    {
        using var f=new Fixture();var locator=f.Open(new SharedLocalMedia(f));
        var request=f.GroundRequest() with {Mode="api-meta"};
        var first=await locator.GroundAsync("s","baseline",request,Ct);
        var root=Path.Combine(f.Root,".private","physics","assembly",AssemblyPolicy.VideoGroundingKey(Sha));
        var oldResponse=Path.Combine(root,"runs",first.Evidence!.RunId!,"response.json");
        await File.AppendAllTextAsync(oldResponse," ");var oldBytes=await File.ReadAllBytesAsync(oldResponse);
        var legacyCache=Path.Combine(root,"prepared-fifty-boundaries-v2.json");
        await File.WriteAllTextAsync(legacyCache,"preserved legacy data");
        var second=await locator.GroundAsync("s","baseline",request,Ct);
        Assert.NotEqual(first.Evidence.RunId,second.Evidence!.RunId);
        Assert.Equal(2,f.Transport.VideoCalls);Assert.Equal(4,f.Runtime.Reservations);
        Assert.Equal(50,f.Frames.Calls);Assert.Equal(1,f.Frames.BatchCalls);
        Assert.Equal(oldBytes,await File.ReadAllBytesAsync(oldResponse));
        Assert.Equal("preserved legacy data",await File.ReadAllTextAsync(legacyCache));
        Assert.Equal(first.Evidence.Frames,second.Evidence.Frames);
        var runRoot=Path.Combine(root,"runs",second.Evidence.RunId!);
        Assert.All(Enumerable.Range(0,50),i=>Assert.True(File.Exists(Path.Combine(runRoot,$"frame-{i}.jpg"))));
    }

    [Fact] public async Task HistoricalProgressiveReceiptsRetainTheirSceneContactReviewsAndCacheIdentity() {
        using var f=new Fixture();var old=await f.ProgressiveGroundAsync();var root=f.GroundFolder;
        var preserved=Directory.GetFiles(root,"*.json",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
        var cached=await f.Open().GroundAsync("s","baseline",f.GroundRequest() with {ModelApiToken="",ConfirmPaidCall=false},Ct);
        Assert.True(cached.CacheHit);Assert.Equal(old.Evidence!.ResponseSha256,cached.Evidence!.ResponseSha256);Assert.Equal(old.Scene!.Key,cached.Scene!.Key);
        var suspension=await f.Open().SuspensionRunAsync("s","baseline",old.Evidence.RunId!,old.Scene,Ct);
        Assert.Equal(ProgressiveSuspensionPolicy.Protocol,suspension!.Refinement);Assert.Equal(2,suspension.ContactReviews!.Length);
        Assert.Equal(4,f.Runtime.Reservations);Assert.Equal(2,f.Transport.ZoomCalls);Assert.Equal(1,f.Transport.ReviewCalls);
        foreach(var pair in preserved)Assert.Equal(pair.Value,await File.ReadAllBytesAsync(pair.Key));
    }
    [Theory] [InlineData("dense-localization-attempt.json")] [InlineData("dense-localization.json")] [InlineData("reviewed-evidence.json")] [InlineData("object-review-attempt.json")]
    public async Task OriginalOnlyProtocolRejectsAlteredDerivationMarkers(string name) {
        using var f=new Fixture();await f.Open().GroundAsync("s","baseline",f.GroundRequest(),Ct);
        var path=Path.Combine(f.GroundFolder,name);var text=await File.ReadAllTextAsync(path);
        var changed=name=="dense-localization-attempt.json"?text.Replace("\"maximumAttempts\":1","\"maximumAttempts\":3"):
            name=="dense-localization.json"?text.Replace("ORIGINAL_REFERENCES_COMPLETE","ZOOM_REVIEWS_COMPLETE"):
            name=="object-review-attempt.json"?text.Replace(DenseLocalizationPolicy.OriginalBodyProtocol,ProgressiveSuspensionPolicy.BodyProtocol):text.Replace(DenseLocalizationPolicy.OriginalProtocol,ProgressiveSuspensionPolicy.Protocol);
        Assert.NotEqual(text,changed);await File.WriteAllTextAsync(path,changed);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Open().TrackingAsync("s","baseline",Ct));
        Assert.Equal(2,f.Runtime.Reservations);Assert.Equal(0,f.Transport.ZoomCalls);
    }

}
