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
        Assert.Null(upper.RemainingTrialSlots);Assert.Equal(1,upper.Parts);Assert.Equal(1,upper.SimultaneousRequests);
        Assert.Equal(667,upper.SubmittedFrames);Assert.Equal(667,upper.BillableFrames);
        Assert.Equal(.1334,upper.TotalSamEstimatedUsd,6);Assert.Equal(0,upper.ContinuousRetryEstimatedUsd);
        Assert.Equal(upper.TotalSamEstimatedUsd,upper.MaximumSamWithRetryEstimatedUsd);
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);
        f.Scenes.Scene=Scene();
        var exact=await f.Quote();
        Assert.False(exact.RequiresNewSparkCall);Assert.True(exact.ExistingSparkSceneVerified);
        Assert.Equal(1,exact.Parts);Assert.Equal(1,exact.SimultaneousRequests);
        Assert.Equal(upper.TotalSamEstimatedUsd,exact.TotalSamEstimatedUsd);
    }
    [Fact]
    public async Task MoreThanThreeManualBatchesWorkAndDuplicateIdsNeverResend()
    {
        using var f=new Fixture();SamAdaptiveLiveRunRequest? last=null;
        for(var i=0;i<4;i++){
            last=Request(await f.Quote());
            var report=await f.Service.RunOnceAsync("study","baseline",last,default);
            Assert.Equal("PROVIDER_PARTS_COMPLETE_DERIVED_MASKS_PENDING_HUMAN_REVIEW",report.Status);
            Assert.True(report.DerivedSeamsVerified);Assert.Equal(667,report.DerivedFramesWithMasks);
            Assert.Equal(1,report.Parts);Assert.Null(report.AutomaticRetryOfBatchId);
        }
        Assert.Equal(4,f.Transport.Calls);Assert.Equal(4,f.Runtime.Reserved);Assert.Equal(1,f.Transport.MaximumActive);
        Assert.All(f.Transport.Windows,w=>{
            Assert.Equal(0,w.InputStart);Assert.Equal(667,w.InputEndExclusive);
            Assert.Equal(0,w.CoreStart);Assert.Equal(667,w.CoreEndExclusive);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.RunOnceAsync("study","baseline",last!,default));
        Assert.Equal(4,f.Transport.Calls);
    }
    [Fact]
    public async Task ExplicitManualSamStageRetryReusesSparkAndPreparedClipWithoutDoubleSubmission()
    {
        using var f=new Fixture();
        var quote=await f.Quote("api-meta",f.Scenes.ExactRunId);
        var firstRequest=Request(quote);
        f.Transport.Fail=true;
        var failed=await f.Service.RunWithOneRetryAsync("study","baseline",firstRequest,default);
        Assert.Equal(1,failed.FailedParts);
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reserved);
        Assert.Null(await f.Service.StageRetryQuoteAsync("another-study","baseline",default));
        var retry=await f.Service.StageRetryQuoteAsync("study","baseline",default);
        Assert.NotNull(retry);Assert.Equal(firstRequest.ClientBatchId,retry.FailedBatchId);
        Assert.Equal(quote.SceneRunId,retry.SceneRunId);
        Assert.Equal(quote.TotalSamEstimatedUsd,retry.EstimatedSamUsd,6);
        Assert.False(retry.AlreadyRequested);Assert.True(retry.PreviousMayHaveBeenBilled);
        var run=new SamStageRetryRequest{
            FailedBatchId=retry.FailedBatchId,NewBatchId=retry.NewBatchId,VideoSha256=retry.VideoSha256,
            PlanSha256=retry.PlanSha256,ModelApiToken=Token,
            MaximumEstimatedCostUsd=retry.EstimatedSamUsd,
            ConfirmPotentialSecondCharge=true,ConfirmVideoTransfer=true
        };
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.RunStageRetryAsync(
            "study","baseline",new SamStageRetryRequest{
                FailedBatchId=run.FailedBatchId,NewBatchId=run.NewBatchId,
                VideoSha256=run.VideoSha256,PlanSha256=run.PlanSha256,ModelApiToken=Token,
                MaximumEstimatedCostUsd=run.MaximumEstimatedCostUsd,
                ConfirmPotentialSecondCharge=false,ConfirmVideoTransfer=true
            },default));
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reserved);
        f.Transport.Fail=false;
        var completed=await f.Service.RunStageRetryAsync("study","baseline",run,default);
        Assert.Equal(retry.NewBatchId,completed.BatchId);
        Assert.Equal("PROVIDER_PARTS_COMPLETE_DERIVED_MASKS_PENDING_HUMAN_REVIEW",completed.Status);
        Assert.Equal(667,completed.DerivedFramesWithMasks);
        Assert.Equal(2,f.Transport.Calls);Assert.Equal(2,f.Runtime.Reserved);
        var source=Path.Combine(f.Store,"batches",failed.BatchId,"clips","part-00.mp4");
        var reused=Path.Combine(f.Store,"batches",completed.BatchId,"clips","part-00.mp4");
        Assert.Equal(await File.ReadAllBytesAsync(source),await File.ReadAllBytesAsync(reused));
        var archive=await f.Service.ReadPlaybackAsync("study","baseline",completed.BatchId,default);
        Assert.True(archive.Derivation!.SeamsVerified);
        Assert.Null(await f.Service.StageRetryQuoteAsync("another-study","baseline",default));
        Assert.Null(await f.Service.LatestOwnedResultAsync("another-study","baseline",default));
        await Assert.ThrowsAsync<InvalidDataException>(()=>
            f.Service.ReadPlaybackAsync("another-study","baseline",completed.BatchId,default));
        Assert.Equal(failed.Status,(await f.Service.ReadReportAsync(failed.BatchId,default))!.Status);
        var saved=await f.Service.StageRetryQuoteAsync("study","baseline",default);
        Assert.True(saved?.AlreadyRequested);Assert.Equal(completed.BatchId,saved?.NewBatchId);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.RunStageRetryAsync(
            "study","baseline",run,default));
        Assert.Equal(2,f.Transport.Calls);
        Assert.Equal(0,f.Scenes.TrackingReads);
        Assert.Equal(0,f.Scenes.QuoteReads);
    }

    [Fact]
    public async Task SameVideoBytesInNewResearchMustNotOfferOldSamRetryOrRecoverOldMasks()
    {
        using var f=new Fixture();
        var quote=await f.Quote("api-meta",f.Scenes.ExactRunId);
        f.Transport.Fail=true;
        var old=await f.Service.RunOnceAsync("study","baseline",Request(quote),default);
        Assert.Equal(1,old.FailedParts);
        // Both studies share the same SHA and exact Spark run. Their registered
        // video IDs and scientific research IDs are different.
        var current=await f.Runtime.GetVerifiedSourceAsync("study","baseline",default);
        var uploaded=await f.Runtime.GetVerifiedSourceAsync("another-study","baseline",default);
        Assert.Equal(current.Sha256,uploaded.Sha256);
        Assert.NotEqual(current.VideoId,uploaded.VideoId);
        Assert.NotNull(await f.Scenes.TrackingRunAsync("another-study","baseline",f.Scenes.ExactRunId,default));
        Assert.Null(await f.Service.StageRetryQuoteAsync("another-study","baseline",default));
        Assert.Null(await f.Service.LatestOwnedResultAsync("another-study","baseline",default));
        Assert.NotNull(await f.Service.StageRetryQuoteAsync("study","baseline",default));
        f.Transport.Fail=false;
        var next=await f.Service.RunOnceAsync("study","baseline",Request(await f.Quote("api-meta",f.Scenes.ExactRunId)),default);
        Assert.Equal("PROVIDER_PARTS_COMPLETE_DERIVED_MASKS_PENDING_HUMAN_REVIEW",next.Status);
        Assert.Equal(next.BatchId,(await f.Service.LatestOwnedResultAsync("study","baseline",default))?.BatchId);
        Assert.Null(await f.Service.LatestOwnedResultAsync("another-study","baseline",default));
        await Assert.ThrowsAsync<InvalidDataException>(()=>
            f.Service.ReadPlaybackAsync("another-study","baseline",next.BatchId,default));
        f.Runtime.OverrideVideoId="synthetic-reuploaded";
        Assert.Null(await f.Service.LatestOwnedResultAsync("study","baseline",default));
        await Assert.ThrowsAsync<InvalidDataException>(()=>
            f.Service.ReadPlaybackAsync("study","baseline",next.BatchId,default));
        f.Runtime.OverrideVideoId=null;
        await Assert.ThrowsAsync<InvalidDataException>(()=>
            f.Service.ReadPlaybackAsync("study","followup",next.BatchId,default));
        Assert.Equal(2,f.Transport.Calls);
        Assert.Equal(2,f.Runtime.Reserved); // No additional Meta calls for another study.
    }

    [Fact]
    public async Task HistoricUnboundFailedBatchCannotAuthorizeASecondPaidSamRequest()
    {
        using var f=new Fixture();f.Transport.Fail=true;
        var q=await f.Quote("api-meta",f.Scenes.ExactRunId);
        var failed=await f.Service.RunOnceAsync("study","baseline",Request(q),default);
        var folder=Path.Combine(f.Store,"batches",failed.BatchId);
        // Simulate old immutable archive metadata with no study/video ownership.
        var contextPath=Path.Combine(folder,"batch-context.json");
        var context=await SamVideoArchive.ReadJsonAsync<SamAdaptiveBatchContext>(contextPath,default);
        await File.WriteAllTextAsync(contextPath,JsonSerializer.Serialize(context with{
            StudyId=null,VideoRole=null,SourceVideoId=null}));
        var startedPath=Path.Combine(folder,"batch-started.json");
        var started=System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(startedPath))!;
        started.AsObject().Remove("studyId");
        started.AsObject().Remove("videoRole");
        started.AsObject().Remove("sourceVideoId");
        await File.WriteAllTextAsync(startedPath,started.ToJsonString());
        Assert.NotNull(await f.Service.ReadReportAsync(failed.BatchId,default)); // Evidence remains intact.
        Assert.Null(await f.Service.StageRetryQuoteAsync("study","baseline",default));
        Assert.Null(await f.Service.StageRetryQuoteAsync("another-study","baseline",default));
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reserved);
    }

    [Fact]
    public async Task TamperedPreparedClipNeverBillsManualStageRetry()
    {
        using var f=new Fixture();f.Transport.Fail=true;
        var q=await f.Quote("api-meta",f.Scenes.ExactRunId);
        var parent=await f.Service.RunWithOneRetryAsync("study","baseline",Request(q),default);
        var retry=await f.Service.StageRetryQuoteAsync("study","baseline",default);
        Assert.NotNull(retry);
        var original=Path.Combine(f.Store,"batches",parent.BatchId,"clips","part-00.mp4");
        await File.AppendAllTextAsync(original,"TAMPER");
        var run=new SamStageRetryRequest{
            FailedBatchId=retry.FailedBatchId,NewBatchId=retry.NewBatchId,
            VideoSha256=retry.VideoSha256,PlanSha256=retry.PlanSha256,
            ModelApiToken=Token,MaximumEstimatedCostUsd=retry.EstimatedSamUsd,
            ConfirmPotentialSecondCharge=true,ConfirmVideoTransfer=true
        };
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service.RunStageRetryAsync(
            "study","baseline",run,default));
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reserved);
        var claimed=await f.Service.StageRetryQuoteAsync("study","baseline",default);
        Assert.True(claimed?.AlreadyRequested);
    }

    [Fact]
    public async Task PlaybackVerifiesPartReceiptsAndPreservesOriginalTiming()
    {
        using var f=new Fixture();var request=Request(await f.Quote());
        await f.Service.RunOnceAsync("study","baseline",request,default);
        var artifact=await f.Service.ReadPlaybackAsync("study","baseline",request.ClientBatchId,default);
        Assert.False(artifact.Derivation!.NativeProviderResult);Assert.True(artifact.Derivation.SeamsVerified);
        Assert.Single(artifact.Derivation.PartResponseSha256);Assert.Equal(f.Runtime.Pts,artifact.Receipt.FramePtsSeconds);
        var context=await SamVideoArchive.ReadJsonAsync<SamAdaptiveBatchContext>(
            Path.Combine(f.Store,"batches",request.ClientBatchId,"batch-context.json"),default);
        var window=Assert.Single(context.NativeWindows!);
        Assert.Equal(0,window.InputStart);Assert.Equal(667,window.InputEndExclusive);
        var progress=await f.Service.ReadProgressAsync(request.ClientBatchId,default);
        Assert.Equal("COMPLETED",progress.Phase);Assert.Equal(1,progress.Parts);Assert.Equal(1,progress.Completed);
        Assert.Null(progress.ContinuousRetry);Assert.Null(progress.ContinuousRetryBatchId);
        Assert.True(progress.TotalElapsedMs>=progress.PreparationElapsedMs);
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
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,report.StartedParts);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service.ReadPlaybackAsync("study","baseline",request.ClientBatchId,default));
    }
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public async Task DenseAuthenticSeedsUseOneFullRequestEvenWithHigherRequestedConcurrency(int width)
    {
        using var f=new Fixture();
        var planned=SamAdaptiveSegmentPlan.Create(667,alignToTwoFrameIntraKeyframes:true);
        var draft=Scene() with {Seeds=planned.Windows.Select(w=>
            new SamSceneSeed((w.CoreStart+w.CoreEndExclusive)/2,"object",20,30,40,50)).ToArray()};
        f.Scenes.Scene=draft with{Key=AssemblyPolicy.DefinitionKey(draft)};
        var quote=await f.Service.PreflightAsync("study","baseline",53,width,default);
        Assert.Equal(1,quote.Parts);Assert.Equal(1,quote.SimultaneousRequests);
        var report=await f.Service.RunOnceAsync("study","baseline",Request(quote),default);
        Assert.True(report.DerivedSeamsVerified);Assert.Equal(1,f.Transport.Calls);
        Assert.Equal(1,f.Transport.MaximumActive);
        var automatic=await f.Service.PreflightAsync("study","baseline",53,0,default);
        Assert.Equal(1,automatic.SimultaneousRequests);
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.PreflightAsync("study","baseline",53,13,default));
    }

    private static async Task<(SamAdaptiveLiveReport Report,SamAdaptiveLiveQuote Quote)> WriteHistoricalBatch(
        Fixture f,int parts,bool inconclusive=false,string? automaticParent=null)
    {
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var inv=System.Globalization.CultureInfo.InvariantCulture;
        var scene=f.Scenes.Scene??throw new InvalidOperationException("Synthetic historical scene missing.");
        var frames=f.Runtime.Pts.Length;
        var plan=SamAdaptiveSegmentPlan.Native(frames,
            Enumerable.Range(0,(frames+1)/2).Select(i=>i*2).ToArray(),
            Enumerable.Range(1,frames).ToArray(),scene.Seeds.Select(s=>s.Frame).ToArray(),
            53,Math.Min(parts,8),maximumParts:parts);
        Assert.Equal(parts,plan.PartCount);
        if(automaticParent is not null)Assert.Equal(1,parts);
        var ptsSha=plan.VerifySourcePts(f.Runtime.Pts);
        var id=automaticParent is null?Guid.NewGuid().ToString("N"):
            SamVideoArchive.Hash("ONE_CONTINUOUS_RETRY_V1:"+automaticParent)[..32];
        var key=SamVideoArchive.Hash(SamAdaptiveLiveService.Protocol+"\n"+Sha+"\n"+scene.Key+"\n"+ptsSha+"\n"+
            plan.RequestedTargetCoreFrames.ToString(inv)+"\n"+plan.MaxInFlight.ToString(inv)+"\n"+
            plan.PartCount.ToString(inv)+"\n"+plan.BilledFrames.ToString(inv)+"\n"+
            plan.EstimatedSamCostUsd.ToString("R",inv)+"\nNATIVE_PACKET_COPY_V1:"+JsonSerializer.Serialize(plan.Windows)+
            (automaticParent is null?"":"\nONE_AUTHORIZED_CONTINUOUS_RETRY_V1:"+automaticParent));
        var folder=Path.Combine(f.Store,"batches",id);Directory.CreateDirectory(folder);
        var now=DateTimeOffset.UtcNow;
        Task Write(string path,object value)=>File.WriteAllTextAsync(path,JsonSerializer.Serialize(value,json));
        await Write(Path.Combine(folder,"batch-context.json"),new SamAdaptiveBatchContext(
            scene,53,plan.MaxInFlight,null,plan.Windows.ToArray(),automaticParent));
        await Write(Path.Combine(folder,"batch-started.json"),new{
            batchId=id,createdAtUtc=now,expectedParts=parts,sourceSha256=Sha,sceneKey=scene.Key,
            planSha256=key,protocol=SamAdaptiveLiveService.Protocol,quotedMaximumUsd=plan.EstimatedSamCostUsd,
            approvedByOwner=true,model="sam-3.1",confirmOneContinuousRetry=false,
            maximumCombinedSamCostUsd=plan.EstimatedSamCostUsd,noAutomaticRetry=true,
            syntheticHistoricalFixture=true
        });
        await Write(Path.Combine(folder,"preparation-completed.json"),new{finishedAtUtc=now,elapsedMs=0});
        var outputs=new List<SamAdaptivePartOutput>();
        foreach(var w in plan.Windows){
            var directory=Path.Combine(folder,"part-"+w.Part.ToString("D2",inv));Directory.CreateDirectory(directory);
            var indices=inconclusive?Enumerable.Range(w.CoreStart-w.InputStart,w.CoreFrames):Enumerable.Range(0,w.InputFrames);
            var wire=string.Concat(indices.Select(i=>"<"+i.ToString(inv)+"f>1<|box;x1=20;y1=30;x2=40;y2=50;w=1080;h=1920|><|mask;x=20;y=30;data=21,21,SYNTHETIC|>\n"));
            var raw=JsonSerializer.Serialize(new{model="sam-3.1",status="completed",
                output=new[]{new{content=new[]{new{type="output_text",text=wire}}}}});
            var responseSha=SamVideoArchive.Hash(raw);
            var receipt=new SamAdaptivePartReceipt(w.Part,w.InputStart,w.InputEndExclusive,w.CoreStart,w.CoreEndExclusive,
                w.InputFrames,w.BilledFrames,Sha,scene.Key,ptsSha,Sha,
                SamVideoArchive.Hash(SamAdaptiveGroundedPlan.BuildPrompt(scene,w)),responseSha,"synthetic-"+w.Part,null,now,
                "COMPLETED_PROVIDER_STREAM_PENDING_HUMAN_VISUAL_REVIEW");
            await File.WriteAllTextAsync(Path.Combine(directory,"response.json"),raw);
            await Write(Path.Combine(directory,"receipt.json"),receipt);
            outputs.Add(new(w.Part,w.InputStart,w.InputEndExclusive,w.CoreStart,w.CoreEndExclusive,responseSha,raw));
        }
        var derived=SamAdaptiveMaskStitcher.Stitch(plan,outputs);
        Assert.Equal(!inconclusive,derived.SeamsVerified);
        await Write(Path.Combine(folder,"derived-report.json"),new{
            derived.Provenance,derived.SourceFrames,derived.FramesWithMasks,derived.MissingCoreFrames,
            derived.SeamsVerified,derived.Seams,derived.DerivedResponseSha256,nativeProviderResult=false
        });
        if(derived.SeamsVerified)await File.WriteAllTextAsync(Path.Combine(folder,"derived-response.json"),derived.DerivedResponseJson);
        var report=new SamAdaptiveLiveReport(id,
            inconclusive?"PROVIDER_PARTS_COMPLETE_SEAM_OR_DETECTION_INCONCLUSIVE":"PROVIDER_PARTS_COMPLETE_DERIVED_MASKS_PENDING_HUMAN_REVIEW",
            Sha,scene.Key,key,parts,parts,parts,0,0,frames,plan.BilledFrames,plan.EstimatedSamCostUsd,
            derived.DerivedResponseSha256,derived.FramesWithMasks,derived.SeamsVerified,now,now,
            "SYNTHETIC_HISTORICAL_ARCHIVE_NO_PROVIDER_DISPATCH",automaticParent);
        await Write(Path.Combine(folder,"batch-report.json"),report);
        if(automaticParent is not null){
            var parent=await f.Service.ReadReportAsync(automaticParent,default)??throw new InvalidOperationException("Synthetic parent missing.");
            Assert.Equal(scene.Key,parent.SparkSceneKey);Assert.Equal(Sha,parent.SourceSha256);
            var combined=parent.EstimatedSamUsd+Math.Max(50,parent.TotalInputFrames)*SamAdaptiveSegmentPlan.SamUsdPerFrame;
            var parentFolder=Path.Combine(f.Store,"batches",automaticParent);
            var startedPath=Path.Combine(parentFolder,"batch-started.json");
            var started=System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(startedPath))!;
            started["confirmOneContinuousRetry"]=true;started["maximumCombinedSamCostUsd"]=combined;started["noAutomaticRetry"]=false;
            await File.WriteAllTextAsync(startedPath,started.ToJsonString());
            await Write(Path.Combine(parentFolder,"automatic-retry.json"),new{
                parentBatchId=automaticParent,childBatchId=id,sourceSha256=Sha,sceneKey=scene.Key,
                combinedSamEstimateUsd=combined,createdAtUtc=now
            });
        }
        var retry=automaticParent is null && parts is >=2 and <=3?plan.UnsplitSamCostUsd:0;
        var quote=new SamAdaptiveLiveQuote(SamAdaptiveLiveService.Protocol,Sha,scene.Key,ptsSha,key,id,
            frames,parts,plan.MaxInFlight,plan.SubmittedFrames,plan.BilledFrames,plan.EstimatedSamCostUsd,.50,
            null,true,true,false,"SYNTHETIC_HISTORICAL_QUOTE",parts,scene.Seeds.Length,false,null,retry,plan.EstimatedSamCostUsd+retry);
        return(report,quote);
    }

    private static async Task<string> SaveInconclusive(Fixture f,string? source=null,bool partial=false){
        var saved=await WriteHistoricalBatch(f,3,inconclusive:true);
        var completed=saved.Report;
        var r=completed with {
            SourceSha256=source??Sha,
            CompletedParts=partial?completed.Parts-1:completed.Parts,
            FailedParts=partial?1:0,
            Note="Synthetic historical seam disagreement"
        };
        await File.WriteAllTextAsync(Path.Combine(f.Store,"batches",completed.BatchId,"batch-report.json"),
            JsonSerializer.Serialize(r,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);
        return completed.BatchId;
    }
    [Fact]
    public async Task InconsistentSeamsQuoteOneContinuousCallAndPreserveThePriorEvidence(){
        using var f=new Fixture();var prior=await SaveInconclusive(f);
        var path=Path.Combine(f.Store,"batches",prior,"batch-report.json");
        var original=await File.ReadAllTextAsync(path);
        var q=await f.Quote();
        Assert.Equal(prior,q.RecoveryFromBatchId);Assert.Equal(1,q.Parts);Assert.Equal(1,q.SimultaneousRequests);
        Assert.False(q.RequiresNewSparkCall);Assert.Equal(0,f.Transport.Calls);
        Assert.Equal(667,q.SubmittedFrames);Assert.Equal(.1334,q.TotalSamEstimatedUsd,6);
        var request=Request(q);var result=await f.Service.RunOnceAsync("study","baseline",request,default);
        Assert.True(result.DerivedSeamsVerified);Assert.Equal(667,result.DerivedFramesWithMasks);
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reserved);
        var playback=await f.Service.ReadPlaybackAsync("study","baseline",request.ClientBatchId,default);
        Assert.Single(playback.Derivation!.PartResponseSha256);
        Assert.StartsWith("<0f>1<|box;",f.Transport.LastPrompt);
        Assert.Contains("<67f>1<|box;",f.Transport.LastPrompt);
        Assert.Equal(f.Runtime.Pts,playback.Receipt.FramePtsSeconds);
        Assert.Equal(original,await File.ReadAllTextAsync(path));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.RunOnceAsync("study","baseline",request,default));
        Assert.Equal(1,f.Transport.Calls);
    }
    [Theory]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public async Task OtherVideoOrIncompleteProviderWorkDoesNotOfferSeamRecovery(bool otherVideo,bool partial){
        using var f=new Fixture();await SaveInconclusive(f,otherVideo?new string('f',64):null,partial);
        var q=await f.Quote();Assert.Null(q.RecoveryFromBatchId);Assert.Equal(1,q.Parts);
        Assert.Equal(0,f.Transport.Calls);
    }
    [Fact]
    public async Task StaleQuoteStopsBeforeAnyRecoveryCall(){
        using var f=new Fixture();var stale=Request(await f.Quote());await SaveInconclusive(f);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.RunOnceAsync("study","baseline",stale,default));
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);
    }
    [Fact]
    public async Task ContinuousProviderFailureIsNotRetriedAutomatically(){
        using var f=new Fixture();await SaveInconclusive(f);f.Transport.Fail=true;
        var request=Request(await f.Quote());
        var report=await f.Service.RunOnceAsync("study","baseline",request,default);
        Assert.Equal(1,report.FailedParts);Assert.Equal(1,f.Transport.Calls);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service.ReadPlaybackAsync("study","baseline",request.ClientBatchId,default));
        Assert.Equal(1,f.Transport.Calls);
    }


    [Fact]
    public async Task ChangedRecoveryReferenceNeverConsumesAnotherProviderCall(){
        using var f=new Fixture();var prior=await SaveInconclusive(f);
        var path=Path.Combine(f.Store,"batches",prior,"part-00","response.json");
        await File.AppendAllTextAsync(path," ");
        var request=Request(await f.Quote());
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service.RunOnceAsync("study","baseline",request,default));
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);
    }

    [Fact]
    public async Task ArchivedEightPartPlaybackRemainsReadableAfterThePolicyChange(){
        using var f=new Fixture();
        var draft=Scene() with {Seeds=Enumerable.Range(0,667).Where(i=>i%15==0)
            .Select(i=>new SamSceneSeed(i,"object",20,30,40,50)).ToArray()};
        f.Scenes.Scene=draft with {Key=AssemblyPolicy.DefinitionKey(draft)};
        var archived=await WriteHistoricalBatch(f,8);
        var current=await f.Quote();Assert.Equal(1,current.Parts);Assert.Equal(1,current.SimultaneousRequests);
        var playback=await f.Service.ReadPlaybackAsync("study","baseline",archived.Report.BatchId,default);
        Assert.Equal(8,playback.Derivation!.PartResponseSha256.Length);
        Assert.True(playback.Derivation.SeamsVerified);Assert.Equal(f.Runtime.Pts,playback.Receipt.FramePtsSeconds);
        var stale=Request(archived.Quote,batchId:Guid.NewGuid().ToString("N"),concurrency:1);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.RunOnceAsync("study","baseline",stale,default));
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);
    }
    [Fact]
    public async Task OldEightPartSeamFailureQuotesOneWithoutSendingOrRewritingEvidence(){
        using var f=new Fixture();
        var draft=Scene() with {Seeds=Enumerable.Range(0,667).Where(i=>i%15==0)
            .Select(i=>new SamSceneSeed(i,"object",20,30,40,50)).ToArray()};
        f.Scenes.Scene=draft with {Key=AssemblyPolicy.DefinitionKey(draft)};
        var archived=await WriteHistoricalBatch(f,8,inconclusive:true);
        var path=Path.Combine(f.Store,"batches",archived.Report.BatchId,"batch-report.json");
        var original=await File.ReadAllTextAsync(path);var quote=await f.Quote();
        Assert.Equal(1,quote.Parts);Assert.Null(quote.RecoveryFromBatchId);
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);
        Assert.Equal(original,await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task AuthorizedFullVideoHasNoFallbackBudgetOrSecondCall(){
        using var f=new Fixture();var q=await f.Quote();
        Assert.Equal(0,q.ContinuousRetryEstimatedUsd);Assert.Equal(q.TotalSamEstimatedUsd,q.MaximumSamWithRetryEstimatedUsd);
        var request=Request(q);var r=await f.Service.RunWithOneRetryAsync("study","baseline",request,default);
        Assert.True(r.DerivedSeamsVerified);Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reserved);
        Assert.Null(r.AutomaticRetryOfBatchId);Assert.Null(r.AutomaticRetryBatchId);
        Assert.False(File.Exists(Path.Combine(f.Store,"batches",r.BatchId,"automatic-retry.json")));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExplicitWholeVideoCanFollowHistoricalPartialEmptyOrInconclusiveSegments(int failure){
        using var f=new Fixture();var archived=await WriteHistoricalBatch(f,3,inconclusive:true);
        var report=archived.Report;
        if(failure==0)report=report with {
            Status="PARTIAL_PROVIDER_ATTEMPTS_REQUIRE_MANUAL_RECONCILIATION",
            CompletedParts=2,FailedParts=1,DerivedFramesWithMasks=null,DerivedSeamsVerified=null};
        if(failure==1)report=report with {DerivedFramesWithMasks=0,DerivedSeamsVerified=true};
        var path=Path.Combine(f.Store,"batches",report.BatchId,"batch-report.json");
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(report,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var original=await File.ReadAllTextAsync(path);var quote=await f.Quote();
        Assert.Equal(1,quote.Parts);Assert.Equal(0,quote.ContinuousRetryEstimatedUsd);
        Assert.Equal(failure==2?report.BatchId:null,quote.RecoveryFromBatchId);
        var request=Request(quote);
        var result=await f.Service.RunWithOneRetryAsync("study","baseline",request,default);
        Assert.True(result.DerivedSeamsVerified);Assert.Equal(667,result.DerivedFramesWithMasks);Assert.Equal(1,result.Parts);
        Assert.Null(result.AutomaticRetryOfBatchId);Assert.Null(result.AutomaticRetryBatchId);
        Assert.Equal(request.ClientBatchId,result.BatchId);Assert.NotEqual(report.BatchId,result.BatchId);
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reserved);Assert.Equal(0,f.Transport.ActiveAtFullDispatch);
        var full=Assert.Single(f.Transport.Windows);
        Assert.Equal(0,full.InputStart);Assert.Equal(667,full.InputEndExclusive);
        Assert.Equal(0,full.CoreStart);Assert.Equal(667,full.CoreEndExclusive);
        var playback=await f.Service.ReadPlaybackAsync("study","baseline",result.BatchId,default);
        Assert.Equal(f.Runtime.Pts,playback.Receipt.FramePtsSeconds);Assert.Single(playback.Derivation!.PartResponseSha256);
        Assert.Null((await f.Service.ReadProgressAsync(result.BatchId,default)).ContinuousRetry);
        Assert.Equal(original,await File.ReadAllTextAsync(path));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.RunWithOneRetryAsync("study","baseline",request,default));
        Assert.Equal(1,f.Transport.Calls);
    }
    [Fact]
    public async Task EmptyWholeVideoCompletesTransportButFailsValidationAndCannotDriveOverlays(){
        using var f=new Fixture();f.Transport.EmptyWhole=true;
        var request=Request(await f.Quote());
        var result=await f.Service.RunWithOneRetryAsync("study","baseline",request,default);
        Assert.Equal(1,result.Parts);Assert.Equal(1,result.CompletedParts);Assert.Equal(0,result.FailedParts);
        Assert.True(result.DerivedSeamsVerified);Assert.Equal(0,result.DerivedFramesWithMasks);
        Assert.Equal("PROVIDER_PARTS_COMPLETE_SEAM_OR_DETECTION_INCONCLUSIVE",result.Status);
        var progress=await f.Service.ReadProgressAsync(request.ClientBatchId,default);
        Assert.Null(progress.ContinuousRetryBatchId);Assert.Null(progress.ContinuousRetry);
        Assert.Equal("FAILED",progress.Phase);Assert.Equal(1,progress.Completed);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service.ReadPlaybackAsync("study","baseline",result.BatchId,default));
        Assert.Equal(1,f.Transport.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.RunWithOneRetryAsync("study","baseline",request,default));
        Assert.Equal(1,f.Transport.Calls);
    }

    [Fact]
    public async Task FailedWholeVideoHasNoSecondAttemptAndCannotDriveOverlays(){
        using var f=new Fixture();f.Transport.Fail=true;var request=Request(await f.Quote());
        var r=await f.Service.RunWithOneRetryAsync("study","baseline",request,default);
        Assert.Equal(1,r.Parts);Assert.Equal(1,r.FailedParts);Assert.Equal(1,f.Transport.Calls);
        Assert.Null(r.AutomaticRetryOfBatchId);Assert.Null(r.AutomaticRetryBatchId);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service.ReadPlaybackAsync("study","baseline",r.BatchId,default));
        var progress=await f.Service.ReadProgressAsync(request.ClientBatchId,default);
        Assert.Equal("FAILED",progress.Phase);Assert.Null(progress.ContinuousRetry);Assert.Equal(1,f.Transport.Calls);
    }
    [Fact]
    public async Task NewAutomaticRetryConsentIsRejectedBeforeAnyProviderPreparation(){
        using var f=new Fixture();var q=await f.Quote();
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.RunWithOneRetryAsync("study","baseline",
            Request(q,retry:true,combined:q.TotalSamEstimatedUsd),default));
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);
        Assert.Empty(Directory.Exists(f.Store)?Directory.GetFiles(f.Store,"batch-preparing.json",SearchOption.AllDirectories):[]);
    }
    [Theory]
    [InlineData(401)]
    [InlineData(429)]
    public async Task CredentialOrRateLimitErrorsDoNotTriggerTheFallback(int status){
        using var f=new Fixture();f.Transport.Fail=true;f.Transport.FailureStatus=(System.Net.HttpStatusCode)status;
        var r=await f.Service.RunWithOneRetryAsync("study","baseline",Request(await f.Quote()),default);
        Assert.Equal(1,r.Parts);Assert.Equal(1,f.Transport.Calls);
        Assert.Null((await f.Service.ReadProgressAsync(r.BatchId,default)).ContinuousRetry);
    }
    [Fact]
    public async Task NoRetryConsentKeepsTheOriginalSingleBatchBehavior(){
        using var f=new Fixture();f.Transport.EmptyWhole=true;
        var r=await f.Service.RunWithOneRetryAsync("study","baseline",Request(await f.Quote()),default);
        Assert.Equal(1,r.Parts);Assert.Equal(1,f.Transport.Calls);Assert.Null(r.AutomaticRetryOfBatchId);
    }
    [Fact]
    public async Task RetryPlaybackRejectsChangedParentConsent(){
        using var f=new Fixture();
        var parent=await WriteHistoricalBatch(f,3,inconclusive:true);
        var child=await WriteHistoricalBatch(f,1,automaticParent:parent.Report.BatchId);
        var playback=await f.Service.ReadPlaybackAsync("study","baseline",child.Report.BatchId,default);
        Assert.Single(playback.Derivation!.PartResponseSha256);Assert.Equal(f.Runtime.Pts,playback.Receipt.FramePtsSeconds);
        var path=Path.Combine(f.Store,"batches",parent.Report.BatchId,"batch-started.json");
        var node=System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        node["confirmOneContinuousRetry"]=false;await File.WriteAllTextAsync(path,node.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service.ReadPlaybackAsync("study","baseline",child.Report.BatchId,default));
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);
    }


    [Fact] public async Task FreshQuotesIgnoreHistoricalScenesAndRecoveryReports(){
        using var f=new Fixture();f.Scenes.RejectHistoricalReads=true;
        var folder=Path.Combine(f.Store,"batches",new string('c',32));Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,"batch-report.json");const string corrupt="{synthetic invalid historical JSON";
        await File.WriteAllTextAsync(path,corrupt);
        var upper=await f.Quote("api-meta");
        Assert.Equal("api-meta",upper.Mode);Assert.Null(upper.SceneRunId);
        Assert.True(upper.RequiresNewSparkCall);Assert.False(upper.ExistingSparkSceneVerified);
        Assert.Equal("",upper.SparkSceneKey);Assert.Null(upper.RecoveryFromBatchId);
        Assert.Equal(0,f.Scenes.TrackingReads);Assert.Equal(0,f.Scenes.QuoteReads);Assert.Empty(f.Scenes.ExactRuns);
        var exact=await f.Quote("api-meta",f.Scenes.ExactRunId);
        Assert.Equal("api-meta",exact.Mode);Assert.Equal(f.Scenes.ExactRunId,exact.SceneRunId);
        Assert.False(exact.RequiresNewSparkCall);Assert.True(exact.ExistingSparkSceneVerified);
        Assert.Equal(f.Scenes.Runs[f.Scenes.ExactRunId].Key,exact.SparkSceneKey);Assert.Null(exact.RecoveryFromBatchId);
        Assert.Equal(0,f.Scenes.TrackingReads);Assert.Equal(0,f.Scenes.QuoteReads);
        Assert.All(f.Scenes.ExactRuns,run=>Assert.Equal(f.Scenes.ExactRunId,run));
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);
        Assert.Equal(corrupt,await File.ReadAllTextAsync(path));
    }

    [Fact] public async Task FreshWholeVideoKeepsExactSparkRunAndModeWithoutHistoricalReads(){
        using var f=new Fixture();f.Scenes.RejectHistoricalReads=true;
        var quote=await f.Quote("api-meta",f.Scenes.ExactRunId);var request=Request(quote);
        var result=await f.Service.RunWithOneRetryAsync("study","baseline",request,default);
        Assert.Equal("PROVIDER_PARTS_COMPLETE_DERIVED_MASKS_PENDING_HUMAN_REVIEW",result.Status);
        Assert.True(result.DerivedSeamsVerified);Assert.Equal(667,result.DerivedFramesWithMasks);Assert.Equal(1,result.Parts);
        Assert.Null(result.AutomaticRetryOfBatchId);Assert.Null(result.AutomaticRetryBatchId);Assert.Equal(request.ClientBatchId,result.BatchId);
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reserved);Assert.Equal(0,f.Transport.ActiveAtFullDispatch);
        var context=await SamVideoArchive.ReadJsonAsync<SamAdaptiveBatchContext>(Path.Combine(f.Store,"batches",result.BatchId,"batch-context.json"),default);
        Assert.Equal("api-meta",context.Mode);Assert.Equal(f.Scenes.ExactRunId,context.SceneRunId);
        Assert.Equal(quote.SparkSceneKey,context.Scene.Key);Assert.Null(context.RecoveryFromBatchId);
        Assert.Equal(1,context.MaxInFlight);Assert.Single(context.NativeWindows!);
        Assert.Equal(0,f.Scenes.TrackingReads);Assert.Equal(0,f.Scenes.QuoteReads);
        Assert.All(f.Scenes.ExactRuns,run=>Assert.Equal(f.Scenes.ExactRunId,run));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.RunWithOneRetryAsync("study","baseline",request,default));
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reserved);
    }

    [Fact] public async Task ChangedExactSparkRunStopsBeforeAnySamReservation(){
        using var f=new Fixture();f.Scenes.RejectHistoricalReads=true;
        var quote=await f.Quote("api-meta",f.Scenes.ExactRunId);var otherRun=new string('d',32);
        var otherScene=Scene() with{EvidenceKey=new string('e',64)};
        otherScene=otherScene with{Key=AssemblyPolicy.DefinitionKey(otherScene)};f.Scenes.Runs.Add(otherRun,otherScene);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.RunWithOneRetryAsync("study","baseline",Request(quote with{SceneRunId=otherRun}),default));
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);Assert.Equal(0,f.Scenes.TrackingReads);Assert.Equal(0,f.Scenes.QuoteReads);
        Assert.Empty(Directory.Exists(f.Store)?Directory.GetFiles(f.Store,"batch-started.json",SearchOption.AllDirectories):[]);
    }

    [Fact] public async Task FreshWithoutAnExactSparkRunOrWithHistoricalRecoveryCannotStartSam(){
        using var f=new Fixture();var quote=await f.Quote("api-meta",f.Scenes.ExactRunId);f.Scenes.RejectHistoricalReads=true;
        foreach(var invalid in new[]{quote with{SceneRunId=null},quote with{RecoveryFromBatchId=new string('f',32)}}){
            await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.RunWithOneRetryAsync("study","baseline",Request(invalid),default));
        }
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);Assert.Equal(0,f.Scenes.TrackingReads);Assert.Equal(0,f.Scenes.QuoteReads);
        Assert.Empty(Directory.Exists(f.Store)?Directory.GetFiles(f.Store,"batch-started.json",SearchOption.AllDirectories):[]);
    }

    [Theory] [InlineData("cache-only")] [InlineData("none")] [InlineData("unknown")]
    public async Task NonApiSelectionsCannotQuoteOrStartPaidBatches(string mode){
        using var f=new Fixture();var quote=await f.Quote();var request=Request(quote with{Mode=mode},retry:true);
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Quote(mode));
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.RunWithOneRetryAsync("study","baseline",request,default));
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);
        Assert.Empty(Directory.Exists(f.Store)?Directory.GetFiles(f.Store,"batch-started.json",SearchOption.AllDirectories):[]);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    public async Task HistoricalConcurrencyNormalizesInQuoteButCannotBeSubmitted(int width){
        using var f=new Fixture();
        var quote=await f.Service.PreflightAsync("study","baseline",53,width,default);
        Assert.Equal(1,quote.Parts);Assert.Equal(1,quote.SimultaneousRequests);
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.RunWithOneRetryAsync(
            "study","baseline",Request(quote,concurrency:width),default));
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);
        Assert.Empty(Directory.Exists(f.Store)?Directory.GetFiles(f.Store,"batch-preparing.json",SearchOption.AllDirectories):[]);
    }

    [Theory]
    [InlineData(49)]
    [InlineData(2501)]
    public async Task UnsupportedWholeVideoFrameCountNeverFallsBackToSplitting(int frames){
        using var f=new Fixture();var prior=Request(await f.Quote());
        f.Runtime.Pts=Enumerable.Range(0,frames).Select(i=>i/30d).ToArray();
        await Assert.ThrowsAsync<SamSingleVideoFrameLimitException>(()=>f.Quote());
        await Assert.ThrowsAsync<SamSingleVideoFrameLimitException>(()=>f.Service.RunWithOneRetryAsync(
            "study","baseline",prior,default));
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);
        Assert.Empty(Directory.Exists(f.Store)?Directory.GetFiles(f.Store,"batch-preparing.json",SearchOption.AllDirectories):[]);
    }


    [Fact]
    public async Task NewRequestsUseOneBoundReferenceWhileHistoricalFullVideoArchivesKeepEveryPrompt()
    {
        using var f=new Fixture();
        var historical=await WriteHistoricalBatch(f,1);
        var oldPlayback=await f.Service.ReadPlaybackAsync("study","baseline",historical.Report.BatchId,default);
        Assert.Equal(historical.Report.PlanSha256,oldPlayback.Receipt.CacheKey);
        var quote=await f.Quote();Assert.NotNull(quote.PrincipalReference);
        Assert.Equal(67,quote.PrincipalReference.Seed.Frame);
        Assert.NotEqual(historical.Report.PlanSha256,quote.PlanSha256);
        var result=await f.Service.RunOnceAsync("study","baseline",Request(quote),default);
        Assert.Equal(1,f.Transport.LastPrompt.Split("<|box;").Length-1);
        Assert.Contains("<67f>",f.Transport.LastPrompt);
        Assert.DoesNotContain("<333f>",f.Transport.LastPrompt);
        var context=await SamVideoArchive.ReadJsonAsync<SamAdaptiveBatchContext>(
            Path.Combine(f.Store,"batches",result.BatchId,"batch-context.json"),default);
        Assert.Equal(3,context.Scene.Seeds.Length);Assert.NotNull(context.PrincipalReference);
        Assert.Equal(quote.PrincipalReference.Seed,context.PrincipalReference.Seed);
        Assert.NotNull(await f.Service.ReadPlaybackAsync("study","baseline",result.BatchId,default));
        // New writes cannot change the interpretation of immutable old receipts.
        Assert.Equal(oldPlayback.Receipt.ResponseJsonSha256,
            (await f.Service.ReadPlaybackAsync("study","baseline",historical.Report.BatchId,default)).Receipt.ResponseJsonSha256);
        Assert.Equal(1,f.Transport.Calls);
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("moved")]
    [InlineData("policy")]
    public async Task PrincipalReferenceTamperingCannotReinterpretCompletedEvidence(string tamper)
    {
        using var f=new Fixture();
        var run=await f.Service.RunOnceAsync("study","baseline",Request(await f.Quote()),default);
        var path=Path.Combine(f.Store,"batches",run.BatchId,"batch-context.json");
        var context=await SamVideoArchive.ReadJsonAsync<SamAdaptiveBatchContext>(path,default);
        var principal=context.PrincipalReference!;
        context=context with{PrincipalReference=tamper=="removed"?null:tamper=="moved"
            ?principal with{Seed=context.Scene.Seeds[1]}:principal with{Protocol="UNRECOGNIZED"}};
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(context));
        await Assert.ThrowsAsync<InvalidDataException>(()=>
            f.Service.ReadPlaybackAsync("study","baseline",run.BatchId,default));
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reserved);
    }

    [Fact]
    public async Task ManualStageRetryQuotesAndRecordsThePrincipalReferenceWithoutRepeatingSpark()
    {
        using var f=new Fixture();f.Transport.Fail=true;
        var failed=await f.Service.RunOnceAsync("study","baseline",
            Request(await f.Quote("api-meta",f.Scenes.ExactRunId)),default);
        var quote=await f.Service.StageRetryQuoteAsync("study","baseline",default);
        Assert.NotNull(quote);f.Transport.Fail=false;
        var retried=await f.Service.RunStageRetryAsync("study","baseline",new(){
            FailedBatchId=quote.FailedBatchId,NewBatchId=quote.NewBatchId,
            VideoSha256=quote.VideoSha256,PlanSha256=quote.PlanSha256,
            ModelApiToken=Token,MaximumEstimatedCostUsd=quote.EstimatedSamUsd,
            ConfirmPotentialSecondCharge=true,ConfirmVideoTransfer=true},default);
        var context=await SamVideoArchive.ReadJsonAsync<SamAdaptiveBatchContext>(
            Path.Combine(f.Store,"batches",retried.BatchId,"batch-context.json"),default);
        Assert.NotNull(context.PrincipalReference);
        Assert.Equal(1,f.Transport.LastPrompt.Split("<|box;").Length-1);
        Assert.Equal(3,context.Scene.Seeds.Length);Assert.Equal(2,f.Transport.Calls);
        Assert.NotNull(await f.Service.ReadPlaybackAsync("study","baseline",retried.BatchId,default));
        Assert.True((await f.Service.StageRetryQuoteAsync("study","baseline",default))!.AlreadyRequested);
        Assert.Equal(failed.Status,(await f.Service.ReadReportAsync(failed.BatchId,default))!.Status);
    }


    [Fact]
    public async Task HistoricalFailedBatchWithoutReferenceCanBeRetriedWithoutRewritingItsEvidence()
    {
        using var f=new Fixture();f.Transport.Fail=true;
        var failed=await f.Service.RunOnceAsync("study","baseline",
            Request(await f.Quote("api-meta",f.Scenes.ExactRunId)),default);
        var folder=Path.Combine(f.Store,"batches",failed.BatchId);
        var contextPath=Path.Combine(folder,"batch-context.json");
        var context=await SamVideoArchive.ReadJsonAsync<SamAdaptiveBatchContext>(contextPath,default);
        var scene=context.Scene;var windows=context.NativeWindows!;
        var plan=SamAdaptiveSegmentPlan.RestoreNative(f.Runtime.Pts.Length,context.TargetCoreFrames,1,windows);
        var inv=System.Globalization.CultureInfo.InvariantCulture;
        var legacyKey=SamVideoArchive.Hash(SamAdaptiveLiveService.Protocol+"\n"+Sha+"\n"+scene.Key+"\n"+
            plan.VerifySourcePts(f.Runtime.Pts)+"\n"+plan.RequestedTargetCoreFrames.ToString(inv)+"\n"+
            plan.MaxInFlight.ToString(inv)+"\n"+plan.PartCount.ToString(inv)+"\n"+
            plan.BilledFrames.ToString(inv)+"\n"+plan.EstimatedSamCostUsd.ToString("R",inv)+
            "\nNATIVE_PACKET_COPY_V1:"+JsonSerializer.Serialize(plan.Windows));
        // Recreate an archived pre-upgrade failure entirely inside the fake store.
        await File.WriteAllTextAsync(contextPath,JsonSerializer.Serialize(context with{PrincipalReference=null}));
        await File.WriteAllTextAsync(Path.Combine(folder,"batch-report.json"),
            JsonSerializer.Serialize(failed with{PlanSha256=legacyKey}));
        async Task Set(string name,string field,string value){
            var path=Path.Combine(folder,name);var d=System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            d[field]=value;await File.WriteAllTextAsync(path,d.ToJsonString());
        }
        await Set("batch-started.json","planSha256",legacyKey);
        await Set(Path.Combine("part-00","attempt-started.json"),"promptSha256",
            SamVideoArchive.Hash(SamAdaptiveGroundedPlan.BuildPrompt(scene,windows.Single())));
        var paths=new[]{"batch-context.json","batch-report.json","batch-started.json",Path.Combine("part-00","attempt-started.json")};
        var before=new Dictionary<string,string>();
        foreach(var name in paths)before[name]=await File.ReadAllTextAsync(Path.Combine(folder,name));
        var retry=await f.Service.StageRetryQuoteAsync("study","baseline",default);
        Assert.NotNull(retry);Assert.NotEqual(legacyKey,retry.PlanSha256);f.Transport.Fail=false;
        var child=await f.Service.RunStageRetryAsync("study","baseline",new(){
            FailedBatchId=retry.FailedBatchId,NewBatchId=retry.NewBatchId,
            VideoSha256=retry.VideoSha256,PlanSha256=retry.PlanSha256,ModelApiToken=Token,
            MaximumEstimatedCostUsd=retry.EstimatedSamUsd,ConfirmPotentialSecondCharge=true,
            ConfirmVideoTransfer=true},default);
        var upgraded=await SamVideoArchive.ReadJsonAsync<SamAdaptiveBatchContext>(
            Path.Combine(f.Store,"batches",child.BatchId,"batch-context.json"),default);
        Assert.NotNull(upgraded.PrincipalReference);Assert.Equal(1,f.Transport.LastPrompt.Split("<|box;").Length-1);
        Assert.Equal(2,f.Transport.Calls);
        foreach(var name in paths)Assert.Equal(before[name],await File.ReadAllTextAsync(Path.Combine(folder,name)));
        Assert.NotNull(await f.Service.ReadPlaybackAsync("study","baseline",child.BatchId,default));
    }

    private static SamAdaptiveLiveRunRequest Request(SamAdaptiveLiveQuote q,double? maximum=null,string? batchId=null,bool retry=false,double? combined=null,int? concurrency=null)=>new(){
        MaxInFlight=concurrency??q.SimultaneousRequests,ModelApiToken=Token,VideoSha256=q.SourceSha256,SceneKey=q.SparkSceneKey,PlanSha256=q.PlanSha256,
        ClientBatchId=batchId??q.SuggestedBatchId,MaximumSamCostUsd=maximum??q.TotalSamEstimatedUsd,RecoveryFromBatchId=q.RecoveryFromBatchId,Mode=q.Mode,SceneRunId=q.SceneRunId,
        ConfirmPrivateVideoTransfer=true,ConfirmUpToManyBillableSamCalls=true,ConfirmNoAutomaticRetry=true,
        ConfirmOneContinuousRetry=retry,MaximumCombinedSamCostUsd=combined??q.MaximumSamWithRetryEstimatedUsd
    };
    private static SamSceneDefinition Scene(){
        var draft=new SamSceneDefinition("",Sha,Sha,Sha,1080,1920,"simple_pendulum",
            [new("1","object")],[new(67,"object",20,30,40,50),new(333,"object",20,30,40,50),
            new(599,"object",20,30,40,50)],null,null,AssemblyPolicy.GroundingOrigin);
        return draft with{Key=AssemblyPolicy.DefinitionKey(draft)};
    }
    [Fact]
    public async Task IncompleteProviderOutput_IsArchivedPrivatelyButNeverAcceptedOrRetried()
    {
        using var f=new Fixture();
        const string wire="PRIVATE_SYNTHETIC_INCOMPLETE_MASK";
        using var reader=new StringReader("data: "+JsonSerializer.Serialize(new {
            type="response.output_text.delta",delta=wire})+"\n\n");
        f.Transport.InjectedFailure=await Assert.ThrowsAsync<SamVideoSseException>(
            ()=>SamVideoSseParser.ReadAsync(reader,null,default));
        var result=await f.Service.RunWithOneRetryAsync("study","baseline",Request(await f.Quote()),default);
        Assert.Equal(1,result.FailedParts);Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reserved);
        var folder=Path.Combine(f.Store,"batches",result.BatchId,"part-00");
        var failure=await File.ReadAllTextAsync(Path.Combine(folder,"failure.json"));
        Assert.DoesNotContain(wire,failure);
        using var json=JsonDocument.Parse(failure);
        Assert.Equal("SAM_SSE_MISSING_COMPLETION",json.RootElement.GetProperty("protocolCode").GetString());
        Assert.Equal("SAVED_UNVERIFIED",json.RootElement.GetProperty("pendingCapture").GetString());
        Assert.Equal(wire.Length,json.RootElement.GetProperty("lastProgress").GetProperty("outputChars").GetInt32());
        Assert.Equal(wire,await File.ReadAllTextAsync(Path.Combine(folder,"incomplete-output.txt")));
        Assert.Contains("UNVERIFIED_INCOMPLETE_NOT_FOR_PLAYBACK",await File.ReadAllTextAsync(Path.Combine(folder,"incomplete-output.json")));
        Assert.False(File.Exists(Path.Combine(folder,"response.json")));
        Assert.False(File.Exists(Path.Combine(folder,"receipt.json")));
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service.ReadPlaybackAsync("study","baseline",result.BatchId,default));
    }

    [Fact]
    public async Task FreshSamUsesPreparedLocalMediaBeforeItsOnlyProviderCall()
    {
        using var f=new Fixture(useLocalPreparation:true);
        var q=await f.Quote();
        var result=await f.Service.RunWithOneRetryAsync("study","baseline",Request(q),default);
        Assert.Equal(1,f.LocalPreparation!.SamCopies);
        Assert.Equal(1,f.Clips.Preparations);
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reserved);
        Assert.True(result.DerivedSeamsVerified);
    }
    [Fact]
    public async Task FailedLocalPreparationDoesNotFallbackOrReserveProviderRequest()
    {
        using var f=new Fixture(useLocalPreparation:true);f.LocalPreparation!.Fail=true;
        var q=await f.Quote();
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service.RunWithOneRetryAsync(
            "study","baseline",Request(q),default));
        Assert.Equal(1,f.LocalPreparation.SamCopies);Assert.Equal(0,f.Clips.Preparations);
        Assert.Equal(0,f.Transport.Calls);Assert.Equal(0,f.Runtime.Reserved);
    }
    private sealed class LocalPreparation(Clips clips):ILocalVideoPreparation
    {
        public int SamCopies;public bool Fail;
        public void Queue(SamVideoSource source)=>throw new NotSupportedException();
        public Task<LocalPreparedMedia> PrepareAsync(SamVideoSource source,CancellationToken ct)=>throw new NotSupportedException();
        public Task<LocalPreparationStatus?> StatusAsync(SamVideoSource source,CancellationToken ct)=>throw new NotSupportedException();
        public Task<SamAdaptiveClipManifest> PrepareSamAsync(SamVideoSource source,IReadOnlyList<double> pts,
            SamAdaptiveSegmentPlan plan,string folder,CancellationToken ct)
        {
            SamCopies++;
            Assert.Equal(Sha,source.Sha256);Assert.Equal(667,pts.Count);Assert.Equal(1,plan.PartCount);
            if(Fail)throw new InvalidDataException("Synthetic prepared media integrity failure.");
            return clips.PrepareAsync(source,pts,plan,folder,ct);
        }
    }

    private sealed class Fixture:IDisposable{
        private readonly string root=Path.Combine(Path.GetTempPath(),"prooflab-segments-"+Guid.NewGuid().ToString("N"));
        public string Store=>Path.Combine(root,".private","segments");
        public Runtime Runtime {get;}=new();public Scenes Scenes {get;}=new();public Transport Transport {get;}=new();
        public Clips Clips {get;}=new();
        public SamAdaptiveLiveService Service {get;}
        public LocalPreparation? LocalPreparation {get;}
        public Fixture(bool useLocalPreparation=false){
            Directory.CreateDirectory(root);
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
                ["SamAdaptive:PrivateRoot"]=Store,["Meta:AllowPaidRequests"]="true",
                ["Meta:CreditsConfirmedByOwner"]="true",["Meta:OwnerApprovedPaidRequests"]="true",
                ["Meta:UnlimitedManualRequests"]="true"}).Build();
            LocalPreparation=useLocalPreparation?new(Clips):null;
            Service=new(Runtime,Scenes,Transport,Clips,new MetaLiveAccess(config,new MetaCallSafetyBudget(43)),new Env(root),config,localPreparation:LocalPreparation);
        }
        public Task<SamAdaptiveLiveQuote> Quote(string? mode=null,string? sceneRunId=null)=>Service.PreflightAsync("study","baseline",53,4,default,mode,sceneRunId);
        public void Dispose()=>Directory.Delete(root,true);
    }
    private sealed class Scenes:IAssemblyScenes{
        public SamSceneDefinition? Scene=SamAdaptiveLiveIntegrationTests.Scene();
        public readonly string ExactRunId=new('b',32);
        public readonly Dictionary<string,SamSceneDefinition> Runs=new();
        public readonly List<string> ExactRuns=new();
        public bool RejectHistoricalReads;public int TrackingReads,QuoteReads;
        public Scenes(){Runs.Add(ExactRunId,SamAdaptiveLiveIntegrationTests.Scene());}
        public Task<SamSceneDefinition?> ApprovedAsync(string id,string role,CancellationToken ct){
            if(RejectHistoricalReads)throw new InvalidOperationException("Synthetic test forbids historical scene lookup.");
            return Task.FromResult(Scene);
        }
        public Task<SamSceneDefinition?> TrackingAsync(string id,string role,CancellationToken ct){TrackingReads++;return ApprovedAsync(id,role,ct);}
        public Task<SamSceneDefinition?> TrackingForQuoteAsync(string id,string role,CancellationToken ct){QuoteReads++;return ApprovedAsync(id,role,ct);}
        public Task<SamSceneDefinition?> TrackingRunAsync(string id,string role,string runId,CancellationToken ct){
            ExactRuns.Add(runId);
            // Deliberately use a SHA-keyed shared Spark run across studies:
            // Spark identity alone must NEVER authorize a SAM stage retry.
            return Task.FromResult(Runs.GetValueOrDefault(runId));
        }
    }
    private sealed class Runtime:ISamVideoRuntime{
        public double[] Pts=Enumerable.Range(0,667).Select(i=>i/30d).ToArray();
        public int Reserved;public string? OverrideVideoId;
        public bool LiveCallsPermitted=>true;
        public void ReserveOrThrow()=>Interlocked.Increment(ref Reserved);
        public Task<SamVideoSource> GetVerifiedSourceAsync(string id,string role,CancellationToken ct)=>
            Task.FromResult(new SamVideoSource(Sha,OverrideVideoId ?? (id=="study" && role=="baseline"?"synthetic":
                id=="another-study"?"synthetic-other":"synthetic-followup"),
                "synthetic.mp4","h264",1080,1920,667/30d));
        public Task<double[]> ReadFramePtsAsync(string path,CancellationToken ct)=>Task.FromResult(Pts);
        public Task CreateMp4Async(string source,string dest,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Clips:ISamAdaptiveClipBuilder{
        public int Preparations;
        public int MaximumParts=SamAdaptiveSegmentPlan.MaximumNewNativeParts;
        public Task<SamAdaptiveSegmentPlan> PlanAsync(SamVideoSource source,IReadOnlyList<double> pts,SamSceneDefinition? scene,int target,int concurrency,CancellationToken ct)=>
            Task.FromResult(SamAdaptiveSegmentPlan.Native(pts.Count,Enumerable.Range(0,(pts.Count+1)/2).Select(i=>i*2).ToArray(),
                Enumerable.Range(1,pts.Count).ToArray(),scene?.Seeds.Select(s=>s.Frame).ToArray(),target,Math.Min(concurrency,8),MaximumParts));
        public async Task<SamAdaptiveClipManifest> PrepareAsync(SamVideoSource source,IReadOnlyList<double> pts,
            SamAdaptiveSegmentPlan plan,string folder,CancellationToken ct){
            Preparations++;Directory.CreateDirectory(folder);var clips=new List<SamAdaptiveClip>();
            foreach(var w in plan.Windows){
                var name=plan.PartCount==1?"part-00.mp4":w.Part+".json";
                var text=JsonSerializer.Serialize(w);
                await File.WriteAllTextAsync(Path.Combine(folder,name),text,ct);
                var clipSha=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
                clips.Add(new(w.Part,w.InputStart,w.InputEndExclusive,w.CoreStart,w.CoreEndExclusive,w.InputFrames,
                    name,text.Length,clipSha,Sha,0,Math.Max(50,w.InputFrames)));
            }
            var manifest=new SamAdaptiveClipManifest(Sha,plan.VerifySourcePts(pts),pts.Count,
                plan.SubmittedFrames,plan.BilledFrames,plan.EstimatedSamCostUsd,
                "SYNTHETIC_LOCAL_TEST_ONLY",clips,0);
            await File.WriteAllTextAsync(Path.Combine(folder,"clip-manifest.json"),
                JsonSerializer.Serialize(manifest),ct);
            return manifest;
        }
    }
    private sealed class Transport:ISamVideoTransport{
        public int Calls,Active,MaximumActive;public bool Fail,FailSegmentsOnly,EmptySegments,EmptyWhole,BrokenSeams;
        public System.Net.HttpStatusCode? FailureStatus;
        public Exception? InjectedFailure;
        public string LastPrompt="";public int ActiveAtFullDispatch=-1;
        public readonly System.Collections.Concurrent.ConcurrentBag<SamAdaptiveWindow> Windows=new();
        public async Task<SamApiCompletedResult> UploadAndSegmentAsync(string path,string prompt,
            Func<SamVideoSseProgress,ValueTask>? progress,CancellationToken ct,string? token=null){
            var w=JsonSerializer.Deserialize<SamAdaptiveWindow>(await File.ReadAllTextAsync(path,ct))!;
            Windows.Add(w);if(w.InputFrames==667)ActiveAtFullDispatch=Active;
            LastPrompt=prompt;Interlocked.Increment(ref Calls);var active=Interlocked.Increment(ref Active);
            int before;do{before=MaximumActive;if(before>=active)break;}
                while(Interlocked.CompareExchange(ref MaximumActive,active,before)!=before);
            try{
                await Task.Delay(20,ct);
                if(InjectedFailure is not null)throw InjectedFailure;
                if((Fail || (FailSegmentsOnly && w.InputFrames<667)) && w.Part==0)
                    throw new HttpRequestException("Synthetic part failure",null,FailureStatus);
                var indices=BrokenSeams && w.InputFrames<667?Enumerable.Range(w.CoreStart-w.InputStart,w.CoreEndExclusive-w.CoreStart):
                    Enumerable.Range(0,(EmptySegments && w.InputFrames<667) || (EmptyWhole && w.InputFrames==667)?0:w.InputFrames);
                var wire=string.Concat(indices.Select(i=>
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
