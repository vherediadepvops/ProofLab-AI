using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using ProofLab.Api;
using ProofLab.Science;

namespace ProofLab.Tests;

/// <summary>Real orchestration and archive validation; synthetic transport only, no network.</summary>
public sealed class SamAdaptiveLiveIntegrationTests
{
    private const string Token="LLM_SYNTHETIC_LOCAL_TEST_KEY_1234567890";
    private static readonly string Sha=new('a',64);
    [Fact]
    public async Task MissingSceneHasReadOnlyUpperBoundAndThenAnExactGroundedQuote()
    {
        using var f=new Fixture();f.Scenes.Scene=null;
        var upper=await f.Quote();
        Assert.True(upper.RequiresNewSparkCall);Assert.True(upper.CanRunWithManualToken);
        Assert.Null(upper.RemainingTrialSlots);Assert.Equal(13,upper.Parts);
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);
        f.Scenes.Scene=Scene();
        var exact=await f.Quote();
        Assert.False(exact.RequiresNewSparkCall);Assert.True(exact.ExistingSparkSceneVerified);
        Assert.Equal(4,exact.Parts);Assert.True(exact.TotalSamEstimatedUsd<=upper.TotalSamEstimatedUsd);
    }
    [Fact]
    public async Task MoreThanThreeManualBatchesWorkAndDuplicateIdsNeverResend()
    {
        using var f=new Fixture();
        SamAdaptiveLiveRunRequest? last=null;
        for(var i=0;i<4;i++){
            last=Request(await f.Quote());
            var report=await f.Service.RunOnceAsync("study","baseline",last,default);
            Assert.Equal("PROVIDER_PARTS_COMPLETE_DERIVED_MASKS_PENDING_HUMAN_REVIEW",report.Status);
            Assert.True(report.DerivedSeamsVerified);Assert.Equal(667,report.DerivedFramesWithMasks);
        }
        Assert.Equal(16,f.Transport.Calls);Assert.InRange(f.Transport.MaximumActive,2,4);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.RunOnceAsync("study","baseline",last!,default));
        Assert.Equal(16,f.Transport.Calls);
    }
    [Fact]
    public async Task PlaybackVerifiesPartReceiptsAndPreservesOriginalTiming()
    {
        using var f=new Fixture();var request=Request(await f.Quote());
        await f.Service.RunOnceAsync("study","baseline",request,default);
        var artifact=await f.Service.ReadPlaybackAsync("study","baseline",request.ClientBatchId,default);
        Assert.False(artifact.Derivation!.NativeProviderResult);Assert.True(artifact.Derivation.SeamsVerified);
        Assert.Equal(4,artifact.Derivation.PartResponseSha256.Length);
        Assert.Equal(f.Runtime.Pts,artifact.Receipt.FramePtsSeconds);
        var progress=await f.Service.ReadProgressAsync(request.ClientBatchId,default);
        Assert.Equal("COMPLETED",progress.Phase);
        Assert.Equal(4,progress.Completed);Assert.True(progress.TotalElapsedMs>=progress.PreparationElapsedMs);
        Assert.All(progress.PerPart,x=>Assert.True(x.ElapsedMs>=0));
        Assert.Contains("f>1<|box;",SamFourWayMaskAssembler.OutputWire(artifact.RawResponseJson));
        Assert.Equal(SamAdaptiveLiveService.Protocol,artifact.Receipt.RequestProtocol);
        Assert.Equal("VERIFIED_META_SAM_SEGMENTS_REQUIRES_VISUAL_REVIEW",artifact.State.Status);
        var path=Path.Combine(f.Store,"batches",request.ClientBatchId,"part-00","response.json");
        await File.AppendAllTextAsync(path," ");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service.ReadPlaybackAsync("study","baseline",request.ClientBatchId,default));
    }
    [Fact]
    public async Task QuoteMismatchStopsBeforeAnyProviderCall()
    {
        using var f=new Fixture();var q=await f.Quote();
        var request=Request(q,maximum:.01);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.RunOnceAsync("study","baseline",request,default));
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);
    }
    [Fact]
    public async Task FailedPartDrainsStartedWorkAndCannotBecomePlaybackEvidence()
    {
        using var f=new Fixture();f.Transport.Fail=true;var request=Request(await f.Quote());
        var report=await f.Service.RunOnceAsync("study","baseline",request,default);
        Assert.Contains("PARTIAL",report.Status);Assert.Equal(0,f.Transport.Active);
        Assert.InRange(f.Transport.Calls,1,4);Assert.Equal(f.Transport.Calls,report.StartedParts);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service.ReadPlaybackAsync("study","baseline",request.ClientBatchId,default));
    }
    private static SamAdaptiveLiveRunRequest Request(SamAdaptiveLiveQuote q,double? maximum=null)=>new(){
        ModelApiToken=Token,VideoSha256=q.SourceSha256,SceneKey=q.SparkSceneKey,PlanSha256=q.PlanSha256,
        ClientBatchId=q.SuggestedBatchId,MaximumSamCostUsd=maximum??q.TotalSamEstimatedUsd,
        ConfirmPrivateVideoTransfer=true,ConfirmUpToManyBillableSamCalls=true,ConfirmNoAutomaticRetry=true
    };
    private static SamSceneDefinition Scene(){
        var draft=new SamSceneDefinition("",Sha,Sha,Sha,1080,1920,"simple_pendulum",
            [new("1","object")],[new(67,"object",20,30,40,50),new(333,"object",20,30,40,50),
            new(599,"object",20,30,40,50)],null,null,AssemblyPolicy.GroundingOrigin);
        return draft with{Key=AssemblyPolicy.DefinitionKey(draft)};
    }
    private sealed class Fixture:IDisposable{
        private readonly string root=Path.Combine(Path.GetTempPath(),"prooflab-segments-"+Guid.NewGuid().ToString("N"));
        public string Store=>Path.Combine(root,".private","segments");
        public Runtime Runtime {get;}=new();public Scenes Scenes {get;}=new();public Transport Transport {get;}=new();
        public SamAdaptiveLiveService Service {get;}
        public Fixture(){
            Directory.CreateDirectory(root);
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
                ["SamAdaptive:PrivateRoot"]=Store,["Meta:AllowPaidRequests"]="true",
                ["Meta:CreditsConfirmedByOwner"]="true",["Meta:OwnerApprovedPaidRequests"]="true",
                ["Meta:UnlimitedManualRequests"]="true"}).Build();
            Service=new(Runtime,Scenes,Transport,new Clips(),new MetaLiveAccess(config,new MetaCallSafetyBudget(43)),new Env(root),config);
        }
        public Task<SamAdaptiveLiveQuote> Quote()=>Service.PreflightAsync("study","baseline",53,4,default);
        public void Dispose()=>Directory.Delete(root,true);
    }
    private sealed class Scenes:IAssemblyScenes{
        public SamSceneDefinition? Scene=SamAdaptiveLiveIntegrationTests.Scene();
        public Task<SamSceneDefinition?> ApprovedAsync(string id,string role,CancellationToken ct)=>Task.FromResult(Scene);
    }
    private sealed class Runtime:ISamVideoRuntime{
        public double[] Pts=Enumerable.Range(0,667).Select(i=>i/30d).ToArray();
        public int Reserved;
        public bool LiveCallsPermitted=>true;
        public void ReserveOrThrow()=>Interlocked.Increment(ref Reserved);
        public Task<SamVideoSource> GetVerifiedSourceAsync(string id,string role,CancellationToken ct)=>
            Task.FromResult(new SamVideoSource(Sha,"synthetic","synthetic.mp4","h264",1080,1920,667/30d));
        public Task<double[]> ReadFramePtsAsync(string path,CancellationToken ct)=>Task.FromResult(Pts);
        public Task CreateMp4Async(string source,string dest,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Clips:ISamAdaptiveClipBuilder{
        public async Task<SamAdaptiveClipManifest> PrepareAsync(SamVideoSource source,IReadOnlyList<double> pts,
            SamAdaptiveSegmentPlan plan,string folder,CancellationToken ct){
            Directory.CreateDirectory(folder);var clips=new List<SamAdaptiveClip>();
            foreach(var w in plan.Windows){
                var name=w.Part+".json";var text=JsonSerializer.Serialize(w);
                await File.WriteAllTextAsync(Path.Combine(folder,name),text,ct);
                clips.Add(new(w.Part,w.InputStart,w.InputEndExclusive,w.CoreStart,w.CoreEndExclusive,w.InputFrames,
                    name,text.Length,Sha,Sha,0,Math.Max(50,w.InputFrames)));
            }
            return new(Sha,plan.VerifySourcePts(pts),pts.Count,plan.SubmittedFrames,plan.BilledFrames,
                plan.EstimatedSamCostUsd,"SYNTHETIC_LOCAL_TEST_ONLY",clips,0);
        }
    }
    private sealed class Transport:ISamVideoTransport{
        public int Calls,Active,MaximumActive;public bool Fail;
        public async Task<SamApiCompletedResult> UploadAndSegmentAsync(string path,string prompt,
            Func<SamVideoSseProgress,ValueTask>? progress,CancellationToken ct,string? token=null){
            Interlocked.Increment(ref Calls);var active=Interlocked.Increment(ref Active);
            int before;do{before=MaximumActive;if(before>=active)break;}
                while(Interlocked.CompareExchange(ref MaximumActive,active,before)!=before);
            try{
                await Task.Delay(20,ct);
                var w=JsonSerializer.Deserialize<SamAdaptiveWindow>(await File.ReadAllTextAsync(path,ct))!;
                if(Fail && w.Part==0)throw new HttpRequestException("Synthetic part failure");
                var wire=string.Concat(Enumerable.Range(0,w.InputFrames).Select(i=>
                    "<"+i+"f>1<|box;x1=20;y1=30;x2=40;y2=50;w=1080;h=1920|><|mask;x=20;y=30;data=21,21,SYNTHETIC|>\n"));
                return new(JsonSerializer.Serialize(new{model="sam-3.1",status="completed",
                    output=new[]{new{content=new[]{new{type="output_text",text=wire}}}}}),"synthetic-"+w.Part,null);
            }finally{Interlocked.Decrement(ref Active);}
        }
    }
    private sealed class Env(string root):IWebHostEnvironment{
        public string EnvironmentName{get;set;}="Development";
        public string ApplicationName{get;set;}="SyntheticTests";
        public string ContentRootPath{get;set;}=root;
        public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
        public string WebRootPath{get;set;}=root;
        public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
    }
}
