using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using ProofLab.Api;
using ProofLab.Science;

namespace ProofLab.Tests;

/// <summary>Real review, grounding, SAM orchestration and archive checks; synthetic transport only.</summary>
public sealed class SamInitialReferenceFlowTests
{
    private static readonly string Sha=new('a',64);
    private static readonly string RunId=new('b',32);
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private const string Token="LLM_SYNTHETIC_LOCAL_TEST_KEY_1234567890";

    [Theory]
    [InlineData("baseline")]
    [InlineData("followup")]
    public async Task ReviewedVisibleBodyPointUsesTheSameBoundPromptAndArchiveForEitherRole(string role)
    {
        using var f=new Fixture(role);
        Assert.Equal("READY",AssemblyPolicy.GroundingStatus(f.Scenes.Evidence));
        var scene=AssemblyPolicy.Ground(f.Scenes.Evidence)!;
        Assert.NotNull(scene.InitialBodyPoint);
        Assert.Equal(ObjectReviewPolicy.VisibleBodyPointProtocol,scene.InitialPointReviewProtocol);
        Assert.DoesNotContain(scene.Seeds,s=>s.Frame==0); // Initial outline is occluded.
        Assert.True(scene.BodyReferences!.Count(q=>q.WholeBodyVisible && !q.Occluded && !q.Clipped)>=2);
        var quote=await f.Quote();
        Assert.Equal(scene.InitialBodyPoint,quote.PrincipalReference!.InitialBodyPoint);
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);

        var report=await f.Service.RunOnceAsync("study",role,Request(quote),default);
        Assert.Equal("PROVIDER_PARTS_COMPLETE_DERIVED_MASKS_PENDING_HUMAN_REVIEW",report.Status);
        var point=scene.InitialBodyPoint!;
        Assert.Equal($"<0f>1<|point;x={point.X};y={point.Y};w=1080;h=1920|>",f.Transport.LastPrompt);
        Assert.DoesNotContain("<|box;",f.Transport.LastPrompt);
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reserved);
        var context=await f.Context(report.BatchId);
        Assert.Equal(role,context.VideoRole);Assert.Equal("source-"+role,context.SourceVideoId);
        Assert.Equal(scene.InitialBodyPoint,context.Scene.InitialBodyPoint);
        Assert.Equal(ObjectReviewPolicy.VisibleBodyPointProtocol,context.Scene.InitialPointReviewProtocol);
        Assert.Equal(scene.Seeds,context.Scene.Seeds);
        Assert.Equal(quote.PrincipalReference.InitialBodyPoint,context.PrincipalReference!.InitialBodyPoint);
        var before=await f.ArchiveBytes(report.BatchId);
        var playback=await f.Service.ReadPlaybackAsync("study",role,report.BatchId,default);
        Assert.True(playback.Derivation!.SeamsVerified);
        Assert.Equal(f.Runtime.Pts,playback.Receipt.FramePtsSeconds);
        Assert.Equal(before,await f.ArchiveBytes(report.BatchId));
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reserved);
    }

    public static IEnumerable<object[]> UnsafePoints()
    {
        foreach(var role in new[]{"baseline","followup"})
            foreach(var failure in new[]{"HAND","CORD","BACKGROUND","UNCERTAIN","OUTSIDE_REGION","NEAR_BOUNDARY","TINY_REGION","WRONG_CANDIDATE"})
                yield return new object[]{role,failure};
    }

    [Theory]
    [MemberData(nameof(UnsafePoints))]
    public async Task UnsafeInitialPointCannotUseAPriorQuoteOrReachTheProvider(string role,string failure)
    {
        using var f=new Fixture(role);
        var prior=await f.Quote();
        var payload=Payload(ObjectReviewPolicy.VisibleBodyPointProtocol);
        var point=payload["body_interior_point"]!;
        if(failure is "HAND" or "CORD" or "BACKGROUND" or "UNCERTAIN")point["material_point"]=failure;
        if(failure=="OUTSIDE_REGION")point["x"]=100;
        if(failure=="NEAR_BOUNDARY")point["x"]=301;
        if(failure=="TINY_REGION"){
            point["visible_body_region"]!["x1"]=499;point["visible_body_region"]!["x2"]=501;
        }
        if(failure=="WRONG_CANDIDATE"){
            point["x"]=100;point["y"]=100;
            point["visible_body_region"]!["x1"]=25;point["visible_body_region"]!["x2"]=175;
            point["visible_body_region"]!["y1"]=25;point["visible_body_region"]!["y2"]=175;
        }
        f.Scenes.Evidence=Review(ObjectReviewPolicy.VisibleBodyPointProtocol,payload);
        Assert.Equal("INITIAL_POINT_IDENTITY_UNRESOLVED",AssemblyPolicy.GroundingStatus(f.Scenes.Evidence));
        Assert.Null(AssemblyPolicy.Ground(f.Scenes.Evidence));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Quote());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.RunOnceAsync("study",role,Request(prior),default));
        Assert.Equal(0,f.Clips.Preparations);
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);
        Assert.False(Directory.Exists(Path.Combine(f.Store,"batches")));
    }

    [Theory]
    [InlineData("baseline")]
    [InlineData("followup")]
    public async Task ChangedSafePointInvalidatesTheApprovedQuoteBeforeAnyUpload(string role)
    {
        using var f=new Fixture(role);var quote=await f.Quote();
        var changed=Payload(ObjectReviewPolicy.VisibleBodyPointProtocol);
        changed["body_interior_point"]!["x"]=550;
        f.Scenes.Evidence=Review(ObjectReviewPolicy.VisibleBodyPointProtocol,changed);
        Assert.Equal("READY",AssemblyPolicy.GroundingStatus(f.Scenes.Evidence));
        var next=await f.Quote();
        Assert.NotEqual(quote.SparkSceneKey,next.SparkSceneKey);
        Assert.NotEqual(quote.PlanSha256,next.PlanSha256);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.RunOnceAsync("study",role,Request(quote),default));
        Assert.Equal(0,f.Clips.Preparations);
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);
    }

    [Theory]
    [InlineData("baseline")]
    [InlineData("followup")]
    public async Task HistoricalV3ReviewAndPlaybackRemainUnchangedAfterAV5Run(string role)
    {
        using var f=new Fixture(role);
        f.Scenes.Evidence=Review(SuspensionAuditPolicy.Protocol);
        var historicalJson=JsonSerializer.Serialize(f.Scenes.Evidence,Json);
        Assert.DoesNotContain("bodyInteriorPoint",historicalJson);
        Assert.DoesNotContain("visibleBodyRegion",historicalJson);
        Assert.DoesNotContain("safetyStatus",historicalJson);
        var oldEvidence=f.Scenes.Evidence;
        var historical=await f.Service.RunOnceAsync("study",role,Request(await f.Quote()),default);
        Assert.Contains("<|box;",f.Transport.LastPrompt);Assert.DoesNotContain("<|point;",f.Transport.LastPrompt);
        var before=await f.ArchiveBytes(historical.BatchId);
        var oldPlayback=await f.Service.ReadPlaybackAsync("study",role,historical.BatchId,default);

        f.Scenes.Evidence=Review(ObjectReviewPolicy.VisibleBodyPointProtocol);
        var current=await f.Service.RunOnceAsync("study",role,Request(await f.Quote()),default);
        Assert.Contains("<0f>1<|point;",f.Transport.LastPrompt);
        Assert.NotEqual(historical.PlanSha256,current.PlanSha256);
        var reread=await f.Service.ReadPlaybackAsync("study",role,historical.BatchId,default);
        Assert.Equal(oldPlayback.Receipt.CacheKey,reread.Receipt.CacheKey);
        Assert.Equal(oldPlayback.Receipt.ResponseJsonSha256,reread.Receipt.ResponseJsonSha256);
        Assert.Equal(before,await f.ArchiveBytes(historical.BatchId));
        Assert.Equal(historicalJson,JsonSerializer.Serialize(oldEvidence,Json));
        var replay=Review(SuspensionAuditPolicy.Protocol);
        Assert.Equal(oldEvidence.ResponseSha256,replay.ResponseSha256);
        Assert.Equal(JsonSerializer.Serialize(oldEvidence.Proposal,Json),JsonSerializer.Serialize(replay.Proposal,Json));
        Assert.Equal(2,f.Transport.Calls);Assert.Equal(2,f.Runtime.Reserved);
    }

    private static (AssemblyEvidence Draft,ObjectReviewImage[] Images) ReviewInputs(string protocol)
    {
        var frames=Enumerable.Range(0,50).Select(i=>new KnotFrame(i*20,i*20/30d,1080,1920,Sha)).ToArray();
        var points=Enumerable.Range(0,50).Select(i=>new KnotPoint(i,true,500,150,.95)).ToArray();
        var consensus=DenseLocalizationPolicy.Resolve(frames,points,"STATIC");
        var observations=Enumerable.Range(0,50).Select(i=>new AssemblyObservation(i,"object",true,400,700,450,800,.95)).ToArray();
        var draft=new AssemblyEvidence(AssemblyPolicy.GroundingKey(Sha),Sha,AssemblyPolicy.Model,
            AssemblyPolicy.GroundingProtocol,frames,Sha,"offline",DateTimeOffset.UnixEpoch,
            new("simple_pendulum",observations,DenseLocalizationPolicy.Suspension(consensus,points),
                new("CLEAR_SINGLE_OBJECT",new("Objeto visible","Visible object")),"STATIC",consensus),
            100,100,Derivation:DenseLocalizationPolicy.OriginalProtocol);
        var images=SuspensionAuditPolicy.RepresentativeOrdinals().Select(i=>new ObjectReviewImage(
            i,frames[i],observations[i],Sha,
            BodyCrop:ObjectReviewPolicy.BodyRegion(frames[i],observations[i],protocol) with {Sha256=Sha},
            SuspensionCandidate:points[i])).ToArray();
        return (draft,images);
    }

    private static JsonObject Payload(string protocol)
    {
        var rows=SuspensionAuditPolicy.RepresentativeOrdinals();
        var root=JsonSerializer.SerializeToNode(new {
            observations=rows.Select(i=>new {frame=i,decision=i==0?"UNCERTAIN":"CORRECTED",visible=i!=0,
                x1=i==0?0:300,y1=i==0?0:300,x2=i==0?0:700,y2=i==0?0:700,confidence=i==0?0:.95,
                visual_reason=i==0?"Hand hides part of the initial outline.":"Whole experimental body visible.",
                whole_body_visible=i!=0,occluded=i==0,clipped=false}),
            target=new {status="CLEAR_SINGLE_OBJECT",justification=new {es="Mismo objeto",en="Same body"}},
            suspension_audit=new {camera="STATIC",points=rows.Select(i=>new {frame=i,reference_kind="FIXED_CORD_CONTACT",
                visible=true,x=500,y=150,confidence=.95,visual_reason="Independent loaded cord contact."})}
        },Json)!.AsObject();
        if(protocol==ObjectReviewPolicy.VisibleBodyPointProtocol)
            root["body_interior_point"]=JsonSerializer.SerializeToNode(new {
                frame=0,visible=true,x=500,y=500,confidence=.95,
                visual_reason="Visible material of the experimental body, clear of the hand and cord.",
                material_point="BODY",visible_body_region=new {x1=300,y1=300,x2=700,y2=700}
            },Json);
        return root;
    }

    private static AssemblyEvidence Review(string protocol,JsonObject? payload=null)
    {
        var (draft,images)=ReviewInputs(protocol);
        var answer=(payload??Payload(protocol)).ToJsonString();
        var raw=JsonSerializer.Serialize(new {id="offline",model=AssemblyPolicy.Model,status="completed",
            usage=new {input_tokens=2,output_tokens=3},
            output=new[]{new {type="message",content=new[]{new {type="output_text",text=answer}}}}});
        return ObjectReviewPolicy.Derive(draft,new(raw,"offline",2,3),images,protocol);
    }

    private static SamAdaptiveLiveRunRequest Request(SamAdaptiveLiveQuote q)=>new(){
        ModelApiToken=Token,Mode=q.Mode,SceneRunId=q.SceneRunId,VideoSha256=q.SourceSha256,
        SceneKey=q.SparkSceneKey,PlanSha256=q.PlanSha256,ClientBatchId=q.SuggestedBatchId,
        MaximumSamCostUsd=q.TotalSamEstimatedUsd,TargetCoreFrames=53,MaxInFlight=1,
        ConfirmPrivateVideoTransfer=true,ConfirmUpToManyBillableSamCalls=true,ConfirmNoAutomaticRetry=true};

    private sealed class Fixture:IDisposable
    {
        private readonly string root=Path.Combine(Path.GetTempPath(),"prooflab-initial-reference-"+Guid.NewGuid().ToString("N"));
        private readonly string role;
        public string Store=>Path.Combine(root,".private","sam");
        public Runtime Runtime {get;}=new();public Scenes Scenes {get;}=new();
        public Transport Transport {get;}=new();public Clips Clips {get;}=new();
        public SamAdaptiveLiveService Service {get;}
        public Fixture(string role){
            this.role=role;Directory.CreateDirectory(root);
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
                ["SamAdaptive:PrivateRoot"]=Store,["Meta:AllowPaidRequests"]="true",
                ["Meta:CreditsConfirmedByOwner"]="true",["Meta:OwnerApprovedPaidRequests"]="true",
                ["Meta:UnlimitedManualRequests"]="true"}).Build();
            Service=new(Runtime,Scenes,Transport,Clips,new MetaLiveAccess(config,new MetaCallSafetyBudget(43)),new Env(root),config);
        }
        public Task<SamAdaptiveLiveQuote> Quote()=>Service.PreflightAsync("study",role,53,1,default,"api-meta",RunId);
        public Task<SamAdaptiveBatchContext> Context(string batch)=>SamVideoArchive.ReadJsonAsync<SamAdaptiveBatchContext>(Path.Combine(Store,"batches",batch,"batch-context.json"),default);
        public async Task<string> ArchiveBytes(string batch){
            var dir=Path.Combine(Store,"batches",batch);
            var entries=new List<string>();
            foreach(var path in Directory.GetFiles(dir,"*",SearchOption.AllDirectories).Order())
                entries.Add(Path.GetRelativePath(dir,path)+":"+Convert.ToBase64String(await File.ReadAllBytesAsync(path)));
            return string.Join("\n",entries);
        }
        public void Dispose()=>Directory.Delete(root,true);
    }
    private sealed class Scenes:IAssemblyScenes
    {
        public AssemblyEvidence Evidence=Review(ObjectReviewPolicy.VisibleBodyPointProtocol);
        public Task<SamSceneDefinition?> ApprovedAsync(string id,string role,CancellationToken ct)=>
            throw new InvalidOperationException("Fresh requests must use their exact Spark run.");
        public Task<SamSceneDefinition?> TrackingRunAsync(string id,string role,string runId,CancellationToken ct){
            Assert.Equal(RunId,runId);Assert.Contains(role,new[]{"baseline","followup"});
            return Task.FromResult(AssemblyPolicy.Ground(Evidence));
        }
    }
    private sealed class Runtime:ISamVideoRuntime
    {
        public double[] Pts=Enumerable.Range(0,1000).Select(i=>i/30d).ToArray();
        public int Reserved;public bool LiveCallsPermitted=>true;
        public void ReserveOrThrow()=>Reserved++;
        public Task<SamVideoSource> GetVerifiedSourceAsync(string id,string role,CancellationToken ct)=>
            Task.FromResult(new SamVideoSource(Sha,"source-"+role,"synthetic.mp4","h264",1080,1920,1000/30d));
        public Task<double[]> ReadFramePtsAsync(string path,CancellationToken ct)=>Task.FromResult(Pts);
        public Task CreateMp4Async(string source,string dest,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Clips:ISamAdaptiveClipBuilder
    {
        public int Preparations;
        public Task<SamAdaptiveSegmentPlan> PlanAsync(SamVideoSource source,IReadOnlyList<double> pts,
            SamSceneDefinition? scene,int target,int concurrency,CancellationToken ct)=>Task.FromResult(
                SamAdaptiveSegmentPlan.RestoreNative(pts.Count,target,1,[new(0,0,pts.Count,0,pts.Count)]));
        public async Task<SamAdaptiveClipManifest> PrepareAsync(SamVideoSource source,IReadOnlyList<double> pts,
            SamAdaptiveSegmentPlan plan,string folder,CancellationToken ct){
            Preparations++;Directory.CreateDirectory(folder);
            var text=JsonSerializer.Serialize(plan.Windows.Single());
            await File.WriteAllTextAsync(Path.Combine(folder,"part-00.mp4"),text,ct);
            var clip=new SamAdaptiveClip(0,0,pts.Count,0,pts.Count,pts.Count,"part-00.mp4",text.Length,
                SamVideoArchive.Hash(text),Sha,0,pts.Count);
            var manifest=new SamAdaptiveClipManifest(Sha,plan.VerifySourcePts(pts),pts.Count,
                plan.SubmittedFrames,plan.BilledFrames,plan.EstimatedSamCostUsd,"SYNTHETIC_LOCAL_TEST_ONLY",[clip],0);
            await File.WriteAllTextAsync(Path.Combine(folder,"clip-manifest.json"),JsonSerializer.Serialize(manifest),ct);
            return manifest;
        }
    }
    private sealed class Transport:ISamVideoTransport
    {
        public int Calls;public string LastPrompt="";
        public async Task<SamApiCompletedResult> UploadAndSegmentAsync(string path,string prompt,
            Func<SamVideoSseProgress,ValueTask>? progress,CancellationToken ct,string? token=null){
            Calls++;LastPrompt=prompt;
            var window=JsonSerializer.Deserialize<SamAdaptiveWindow>(await File.ReadAllTextAsync(path,ct))!;
            var wire=string.Concat(Enumerable.Range(0,window.InputFrames).Select(i=>
                $"<{i}f>1<|box;x1=440;y1=1400;x2=480;y2=1500;w=1080;h=1920|><|mask;x=440;y=1400;data=41,101,SYNTHETIC|>\n"));
            return new(JsonSerializer.Serialize(new{model="sam-3.1",status="completed",
                output=new[]{new{content=new[]{new{type="output_text",text=wire}}}}}),"synthetic-local-only",null);
        }
    }
    private sealed class Env(string root):IWebHostEnvironment
    {
        public string EnvironmentName{get;set;}="Development";public string ApplicationName{get;set;}="SyntheticTests";
        public string ContentRootPath{get;set;}=root;public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
        public string WebRootPath{get;set;}=root;public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
    }
}
