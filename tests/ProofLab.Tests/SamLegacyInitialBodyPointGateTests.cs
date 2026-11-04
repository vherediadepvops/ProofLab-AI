using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using ProofLab.Api;
namespace ProofLab.Tests;

/// <summary>Legacy paid entry points cannot bypass the new human-review gate. All media and transport are fake.</summary>
public sealed class SamLegacyInitialBodyPointGateTests
{
    private const string Token="LLM_FAKE_OFFLINE_TEST_ONLY_1234567890123456";
    private static readonly CancellationToken Ct=CancellationToken.None;
    private sealed class Runtime : ISamVideoRuntime
    {
        public required SamVideoSource Source {get;init;}
        public bool LiveCallsPermitted=>true;
        public int Reservations {get;private set;}
        public int Preparations {get;private set;}
        public Task<SamVideoSource> GetVerifiedSourceAsync(string id,string role,CancellationToken ct)=>Task.FromResult(Source);
        public Task<double[]> ReadFramePtsAsync(string path,CancellationToken ct)=>Task.FromResult(Enumerable.Range(0,60).Select(i=>i/30d).ToArray());
        public Task CreateMp4Async(string source,string destination,CancellationToken ct){Preparations++;throw new InvalidOperationException("Unexpected test preparation.");}
        public void ReserveOrThrow(){Reservations++;throw new InvalidOperationException("Unexpected test reservation.");}
    }
    private sealed class Transport : ISamVideoTransport
    {
        public int Calls {get;private set;}
        public Task<SamApiCompletedResult> UploadAndSegmentAsync(string original,string concept,Func<SamVideoSseProgress,ValueTask>? progress,CancellationToken ct,string? token=null)
        {Calls++;throw new InvalidOperationException("Unexpected test transport.");}
    }
    private sealed class Scenes(SamSceneDefinition scene):IAssemblyScenes
    {
        public Task<SamSceneDefinition?> ApprovedAsync(string id,string role,CancellationToken ct)=>Task.FromResult<SamSceneDefinition?>(scene);
    }
    private sealed class Environment(string root):IWebHostEnvironment
    {
        public string ApplicationName {get;set;}="Offline tests";
        public string EnvironmentName {get;set;}="Testing";
        public string ContentRootPath {get;set;}=root;
        public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();
        public string WebRootPath {get;set;}=root;
        public IFileProvider WebRootFileProvider {get;set;}=new NullFileProvider();
    }
    private sealed class Fixture:IDisposable
    {
        public string Root {get;}=Path.Combine(AppContext.BaseDirectory,"sam-initial-gate-test-data",Guid.NewGuid().ToString("N"));
        public Runtime Runtime {get;}
        public Transport Transport {get;}=new();
        public SamSceneDefinition Scene {get;}
        public Fixture(bool missingFirstFrame)
        {
            Directory.CreateDirectory(Root);
            Runtime=new(){Source=new(new('a',64),"offline-video",Path.Combine(Root,"original.mp4"),"h264",640,480,2)};
            var scene=new SamSceneDefinition("",Runtime.Source.Sha256,new('b',64),new('c',64),640,480,"simple_pendulum",
                [new("1","object")],[new(6,"object",200,300,250,370),new(30,"object",300,300,350,370)],null,null,
                Origin:AssemblyPolicy.GroundingOrigin,BodyReferences:[new(6,true,false,false),new(30,true,false,false)],
                InitialBodyPoint:new(0,220,340),InitialPointReviewProtocol:ObjectReviewPolicy.VisibleBodyPointProtocol);
            if(!missingFirstFrame)scene=scene with{Seeds=[new(0,"object",200,300,250,370),..scene.Seeds],
                BodyReferences=[new(0,false,true,false),..scene.BodyReferences!]};
            Scene=scene with{Key=AssemblyPolicy.DefinitionKey(scene)};
        }
        public PendulumSamCache Open()=>new(new Environment(Root),Runtime,Transport,new SamVideoEvidenceIndex(Root),NullLogger<PendulumSamCache>.Instance,new Scenes(Scene));
        public SamVideoRunRequest Request(string mode)=>new(Runtime.Source.Sha256,true,true,.5,Mode:mode,SceneKey:Scene.Key);
        public void Dispose(){if(Directory.Exists(Root))Directory.Delete(Root,true);}
    }

    [Theory]
    [InlineData(true,"api-meta",false)] [InlineData(true,"api-meta",true)]
    [InlineData(false,"api-meta",false)] [InlineData(false,"api-meta",true)]
    [InlineData(true,"api-meta-cache",false)] [InlineData(false,"api-meta-cache",true)]
    public async Task NewLegacySubmissionStopsBeforePreparationReservationOrTransport(bool missingFirstFrame,string mode,bool manualToken)
    {
        using var f=new Fixture(missingFirstFrame);
        await Assert.ThrowsAsync<SamInitialReferenceReviewRequiredException>(()=>f.Open().RunOnceAsync("study","baseline",f.Request(mode),Ct,manualToken?Token:null));
        Assert.Equal(0,f.Runtime.Preparations);Assert.Equal(0,f.Runtime.Reservations);Assert.Equal(0,f.Transport.Calls);
        Assert.Empty(Directory.GetFiles(f.Root,"attempt-started.json",SearchOption.AllDirectories));
    }

    [Fact]
    public async Task HistoricAmbiguousSceneStillLoadsAndReusesItsExactReceiptWithoutPaidWork()
    {
        using var f=new Fixture(true);
        var key=SamVideoArchive.Hash(AssemblyPolicy.SamProtocol+"\n"+PendulumSamCache.SamModel+"\n"+f.Scene.Key+"\n"+f.Runtime.Source.Sha256);
        var archive=new SamVideoArchive(f.Root,new SamVideoEvidenceIndex(f.Root),f.Scene);
        var id=Guid.NewGuid().ToString("N");Directory.CreateDirectory(archive.RunFolder(key,id));
        const string wire="<0f>0<|box;x1=211;y1=228;x2=270;y2=254;w=320;h=334|>"+
            "<|mask;x=0;y=0;data=27,60,~!!!!M!0c[0o91w?q1!pIH4pPRVp2B3'7`e.ioeAf6-k/#Xd8%dX9x(|>";
        var raw=JsonSerializer.Serialize(new{model="sam-3.1",status="completed",output=new[]{new{type="message",content=new[]{new{type="output_text",text=wire}}}}});
        var receipt=new SamVideoReceipt(key,f.Runtime.Source.Sha256,"offline-video",PendulumSamCache.SamModel,f.Scene.Prompt,AssemblyPolicy.SamProtocol,
            DateTimeOffset.UtcNow,SamVideoArchive.Hash(raw),"SYNTHETIC-HISTORICAL-NOT-PROVIDER",60,60,
            Enumerable.Range(0,60).Select(i=>i/30d).ToArray(),new('d',64),"COMPLETED_PROVIDER_RESPONSE_REQUIRES_VISUAL_REVIEW",.012,id,f.Scene);
        await archive.CommitAsync(f.Runtime.Source,key,receipt,raw);
        var receiptPath=Path.Combine(archive.RunFolder(key,id),"receipt.json");var before=await File.ReadAllTextAsync(receiptPath);
        var api=f.Open();
        var hit=await api.RunOnceAsync("study","baseline",f.Request("api-meta-cache") with{ConfirmPrivateVideoTransfer=false,ConfirmOneBillableSamCall=false,MaximumEstimatedCostUsd=0},Ct);
        Assert.True(hit.State.CacheHit);Assert.Equal(id,hit.State.AnalysisId);Assert.Equal(raw,hit.RawResponseJson);
        var replay=await api.ReadArchiveAsync("study","baseline",Ct,id);
        Assert.Equal(id,replay.State.AnalysisId);Assert.Equal(before,await File.ReadAllTextAsync(receiptPath));
        await Assert.ThrowsAsync<SamInitialReferenceReviewRequiredException>(()=>api.RunOnceAsync("study","baseline",f.Request("api-meta"),Ct,Token));
        Assert.Equal(0,f.Runtime.Preparations);Assert.Equal(0,f.Runtime.Reservations);Assert.Equal(0,f.Transport.Calls);
    }
}
