using System.Text.Json;

namespace ProofLab.Api;

public sealed partial class SamAdaptiveLiveService
{
    private readonly SemaphoreSlim workflowGate=new(1,1);
    private sealed record RetryLink(string ParentBatchId,string ChildBatchId,string SourceSha256,
        string SceneKey,double CombinedSamEstimateUsd,DateTimeOffset CreatedAtUtc);
    private static SamAdaptiveSegmentPlan AutomaticContinuousPlan(int frames,int target)=>
        SamAdaptiveSegmentPlan.RestoreNative(frames,target,1,[new(0,0,frames,0,frames)]);
    private static string RetryId(string parent)=>Hash("ONE_CONTINUOUS_RETRY_V1:"+parent)[..32];
    private static bool CompletedEvidence(SamAdaptiveLiveReport r)=>
        r.Status=="PROVIDER_PARTS_COMPLETE_DERIVED_MASKS_PENDING_HUMAN_REVIEW" &&
        r.DerivedSeamsVerified==true && r.DerivedFramesWithMasks>0;

    private async Task<bool> MayRetry(SamAdaptiveLiveReport r,CancellationToken ct)
    {
        if(r.Parts is <2 or >3 || r.AutomaticRetryOfBatchId is not null)return false;
        if(r.Status=="PROVIDER_PARTS_COMPLETE_SEAM_OR_DETECTION_INCONCLUSIVE")return true;
        if(r.Status!="PARTIAL_PROVIDER_ATTEMPTS_REQUIRE_MANUAL_RECONCILIATION" || r.StartedParts==0)return false;
        // All requests have been drained before the final report exists. Auth/4xx,
        // source corruption and local validation errors do not trigger another charge.
        var failures=Directory.EnumerateFiles(BatchDir(r.BatchId),"failure.json",SearchOption.AllDirectories).ToArray();
        if(failures.Length==0)return false;
        foreach(var path in failures){
            using var d=JsonDocument.Parse(await File.ReadAllTextAsync(path,ct));
            var v=d.RootElement;var status=v.GetProperty("status").GetString();
            if(status is not ("SAM_PART_PROVIDER_HTTP_FAILURE" or "SAM_PART_PROVIDER_SSE_FAILURE" or "SAM_PART_DEADLINE_OR_CANCELLED"))return false;
            if(v.TryGetProperty("httpStatus",out var code) && code.ValueKind==JsonValueKind.Number && code.GetInt32() is >=400 and <500)return false;
        }
        return true;
    }
    private static SamAdaptiveLiveQuote AutomaticQuote(SamVideoSource source,SamSceneDefinition scene,
        double[] pts,SamAdaptiveSegmentPlan plan,string parent)=>new(
            Protocol,source.Sha256,scene.Key,plan.VerifySourcePts(pts),
            PlanKey(source.Sha256,scene.Key,plan.VerifySourcePts(pts),plan,null,parent),RetryId(parent),
            pts.Length,1,1,plan.SubmittedFrames,plan.BilledFrames,plan.EstimatedSamCostUsd,.50,
            null,true,true,false,"ONE_PREAUTHORIZED_CONTINUOUS_RETRY",1,scene.Seeds.Length,false);

    private async Task ValidateAutomaticParent(string parent,string sha,string scene,CancellationToken ct)
    {
        if(!ValidBatchId(parent))throw new InvalidDataException("Invalid retry parent.");
        var report=await ReadReportAsync(parent,ct);
        if(report is null || report.SourceSha256!=sha || report.SparkSceneKey!=scene || !await MayRetry(report,ct))
            throw new InvalidDataException("Retry requires a finalized failed batch for this exact source and scene.");
        var link=await SamVideoArchive.ReadJsonAsync<RetryLink>(Path.Combine(BatchDir(parent),"automatic-retry.json"),ct);
        using var authorization=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(BatchDir(parent),"batch-started.json"),ct));
        var a=authorization.RootElement;
        var expected=report.EstimatedSamUsd+Math.Max(50,report.TotalInputFrames)*SamAdaptiveSegmentPlan.SamUsdPerFrame;
        if(!double.IsFinite(link.CombinedSamEstimateUsd) || Math.Abs(link.CombinedSamEstimateUsd-expected)>1e-9 ||
           a.GetProperty("sourceSha256").GetString()!=sha || a.GetProperty("sceneKey").GetString()!=scene ||
           link.ParentBatchId!=parent || link.ChildBatchId!=RetryId(parent) || link.SourceSha256!=sha || link.SceneKey!=scene ||
           !a.TryGetProperty("confirmOneContinuousRetry",out var approved) || !approved.GetBoolean() ||
           !a.TryGetProperty("maximumCombinedSamCostUsd",out var budget) ||
           !double.IsFinite(budget.GetDouble()) || budget.GetDouble()+1e-9<link.CombinedSamEstimateUsd)
            throw new InvalidDataException("Retry consent or combined estimate does not match.");
    }

    public async Task<SamAdaptiveLiveReport> RunWithOneRetryAsync(string studyId,string role,
        SamAdaptiveLiveRunRequest request,CancellationToken ct)
    {
        await workflowGate.WaitAsync(ct);
        try{
            var first=await RunBatchCore(studyId,role,request,ct);
            if(!request.ConfirmOneContinuousRetry || CompletedEvidence(first) || !await MayRetry(first,CancellationToken.None))return first;
            // New action consent covers only one child. A durable deterministic claim
            // prevents duplicates across process restart or a lost HTTP response.
            var parent=first.BatchId;var child=RetryId(parent);
            var combined=first.EstimatedSamUsd+Math.Max(50,first.TotalInputFrames)*SamAdaptiveSegmentPlan.SamUsdPerFrame;
            if(combined>request.MaximumCombinedSamCostUsd+1e-9)throw new InvalidOperationException("Combined retry quote changed.");
            await WriteJson(Path.Combine(BatchDir(parent),"automatic-retry.json"),
                new RetryLink(parent,child,first.SourceSha256,first.SparkSceneKey,combined,DateTimeOffset.UtcNow),CancellationToken.None);
            try{
                var (source,scene,pts,plan,_)=await Context(studyId,role,request.TargetCoreFrames,1,CancellationToken.None,null,parent);
                var quote=AutomaticQuote(source,scene,pts,plan,parent);
                var retry=new SamAdaptiveLiveRunRequest{
                    ModelApiToken=request.ModelApiToken,VideoSha256=source.Sha256,SceneKey=scene.Key,
                    PlanSha256=quote.PlanSha256,ClientBatchId=child,MaximumSamCostUsd=quote.TotalSamEstimatedUsd,
                    TargetCoreFrames=request.TargetCoreFrames,MaxInFlight=1,
                    ConfirmPrivateVideoTransfer=true,ConfirmUpToManyBillableSamCalls=true,ConfirmNoAutomaticRetry=true
                };
                var result=await RunBatchCore(studyId,role,retry,CancellationToken.None,parent);
                await WriteJson(Path.Combine(BatchDir(parent),"automatic-retry-finished.json"),
                    new{childBatchId=child,status=result.Status,finishedAtUtc=DateTimeOffset.UtcNow},CancellationToken.None);
                return result;
            }catch(Exception e) when(e is InvalidDataException or InvalidOperationException or IOException or ArgumentException or OperationCanceledException){
                await WriteJson(Path.Combine(BatchDir(parent),"automatic-retry-stopped.json"),
                    new{childBatchId=child,status="RETRY_NOT_COMPLETED_NO_FURTHER_RETRY",finishedAtUtc=DateTimeOffset.UtcNow},CancellationToken.None);
                return first with{AutomaticRetryBatchId=child,AutomaticRetryStatus="RETRY_NOT_COMPLETED_NO_FURTHER_RETRY"};
            }
        }finally{workflowGate.Release();}
    }

    private async Task<(string? Id,SamAdaptiveProgress? Progress)> ReadAutomaticRetryProgress(string parent,CancellationToken ct)
    {
        var path=Path.Combine(BatchDir(parent),"automatic-retry.json");
        if(!File.Exists(path))return(null,null);
        var link=await SamVideoArchive.ReadJsonAsync<RetryLink>(path,ct);
        if(link.ParentBatchId!=parent || link.ChildBatchId!=RetryId(parent))throw new InvalidDataException("Retry link changed.");
        // Only the initial operation may own a retry; never recurse into a chain.
        if(File.Exists(Path.Combine(BatchDir(link.ChildBatchId),"automatic-retry.json")))throw new InvalidDataException("Unexpected retry chain.");
        SamAdaptiveProgress progress;
        try{progress=await ReadProgressAsync(link.ChildBatchId,ct);}
        catch(FileNotFoundException){progress=new(link.ChildBatchId,1,0,0,0,[],"PREPARING");}
        if(File.Exists(Path.Combine(BatchDir(parent),"automatic-retry-stopped.json")))progress=progress with{Phase="FAILED"};
        return(link.ChildBatchId,progress);
    }
}
