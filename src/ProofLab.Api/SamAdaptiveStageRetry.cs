using System.Security.Cryptography;
using System.Text.Json;

namespace ProofLab.Api;

// No token is ever included in a record or persisted to storage.
public sealed class SamStageRetryRequest
{
    public string FailedBatchId { get; init; }="";
    public string NewBatchId { get; init; }="";
    public string VideoSha256 { get; init; }="";
    public string PlanSha256 { get; init; }="";
    public string ModelApiToken { get; init; }="";
    public double MaximumEstimatedCostUsd { get; init; }
    public bool ConfirmPotentialSecondCharge { get; init; }
    public bool ConfirmVideoTransfer { get; init; }
}

public sealed record SamStageRetryQuote(
    string FailedBatchId,string NewBatchId,string VideoSha256,string SparkSceneKey,
    string SceneRunId,string PlanSha256,int Frames,double EstimatedSamUsd,
    int TargetCoreFrames,bool AlreadyRequested=false,bool PreviousMayHaveBeenBilled=true);

// A retry starts at the failed SAM *stage*, not at a frame in a partially received
// provider stream. Meta may charge again. Never imply resumable mask generation.
public sealed partial class SamAdaptiveLiveService
{
    private sealed record ManualStageRetryClaim(string FailedBatchId,string NewBatchId,
        string VideoSha256,string SceneKey,DateTimeOffset OwnerApprovedAtUtc,
        double MaximumEstimatedCostUsd,bool PreviousMayHaveBeenBilled);

    private static string StageRetryId(string parent)=>
        Hash("EXPLICIT_STAGE_RETRY_SINGLE_SAM_V1:"+parent)[..32];

    public async Task<SamStageRetryQuote?> StageRetryQuoteAsync(
        string studyId,string role,CancellationToken ct)
    {
        if(role is not ("baseline" or "followup"))throw new ArgumentException("Invalid video role.");
        var source=await runtime.GetVerifiedSourceAsync(studyId,role,ct);
        var directory=Path.Combine(StoreRoot,"batches");
        if(!Directory.Exists(directory))return null;
        // Latest finalized result for THIS exact original wins: never fall back
        // to an older failed run if a newer successful attempt already exists.
        var reports=new List<(SamAdaptiveLiveReport Report,SamAdaptiveBatchContext Context)>();
        foreach(var folder in Directory.EnumerateDirectories(directory)) {
            var batchId=Path.GetFileName(folder);
            var contextFile=Path.Combine(folder,"batch-context.json");
            if(!ValidBatchId(batchId) || !File.Exists(Path.Combine(folder,"batch-report.json")) ||
               !File.Exists(contextFile))continue;
            try {
                var report=await ReadReportAsync(batchId,ct);
                if(report is null || report.SourceSha256!=source.Sha256 ||
                   !await BatchOwnedAsync(batchId,studyId,role,source,ct))continue;
                var context=await SamVideoArchive.ReadJsonAsync<SamAdaptiveBatchContext>(contextFile,ct);
                if(context.Mode!=PendulumSamCache.FreshMode || !ValidBatchId(context.SceneRunId) ||
                   context.Scene.VideoSha256!=source.Sha256 || context.Scene.Key!=report.SparkSceneKey)continue;
                // The same video may be uploaded to two different investigations.
                // Only a Spark run owned by this exact study and role can be retried.
                var owned=await scenes.TrackingRunAsync(studyId,role,context.SceneRunId!,ct);
                if(owned is null || owned.Key!=context.Scene.Key)continue;
                reports.Add((report,context));
            }catch(IOException){}catch(JsonException){}catch(InvalidDataException){}
            catch(InvalidOperationException){}
        }
        var selected=reports.OrderByDescending(x=>x.Report.RecordedAtUtc).FirstOrDefault();
        var latest=selected.Report;
        if(latest is null)return null;
        // The manual retry itself may have completed while the browser was closed.
        // Recover that same claimed child without offering another paid call.
        var latestStarted=Path.Combine(BatchDir(latest.BatchId),"batch-started.json");
        if(File.Exists(latestStarted)) {
            using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(latestStarted,ct));
            if(doc.RootElement.TryGetProperty("manualRetryOfBatchId",out var previous) &&
               previous.ValueKind==JsonValueKind.String) {
                var parentId=previous.GetString();
                if(!ValidBatchId(parentId) ||
                   !await BatchOwnedAsync(parentId!,studyId,role,source,ct))return null;
                var parent=await ReadReportAsync(parentId!,ct);
                var claimFile=Path.Combine(BatchDir(parentId!),"manual-stage-retry.json");
                if(parent is null || parent.SourceSha256!=source.Sha256 ||
                   parent.SparkSceneKey!=latest.SparkSceneKey || !File.Exists(claimFile))return null;
                var completedClaim=await SamVideoArchive.ReadJsonAsync<ManualStageRetryClaim>(claimFile,ct);
                if(completedClaim.NewBatchId!=latest.BatchId || completedClaim.FailedBatchId!=parentId ||
                   completedClaim.VideoSha256!=source.Sha256 || completedClaim.SceneKey!=latest.SparkSceneKey)return null;
                var completedContext=selected.Context;
                return new(parentId!,latest.BatchId,source.Sha256,latest.SparkSceneKey,
                    completedContext.SceneRunId!,latest.PlanSha256,latest.TotalInputFrames,latest.EstimatedSamUsd,
                    completedContext.TargetCoreFrames,true);
            }
        }
        if(latest.Status!="PARTIAL_PROVIDER_ATTEMPTS_REQUIRE_MANUAL_RECONCILIATION" ||
           latest.Parts!=1 || latest.StartedParts!=1 || latest.CompletedParts!=0 || latest.FailedParts!=1 ||
           latest.AutomaticRetryOfBatchId is not null || !ValidBatchId(latest.BatchId))return null;
        var parentDir=BatchDir(latest.BatchId);
        // A child retry is final. Never offer a retry-of-retry, even if it fails.
        var started=Path.Combine(parentDir,"batch-started.json");
        if(!File.Exists(started))return null;
        using(var auth=JsonDocument.Parse(await File.ReadAllTextAsync(started,ct))) {
            if(auth.RootElement.TryGetProperty("manualRetryOfBatchId",out var retryParent) &&
               retryParent.ValueKind==JsonValueKind.String)return null;
        }
        var part=Path.Combine(parentDir,"part-00");
        if(!File.Exists(Path.Combine(part,"provider-dispatch.json")) ||
           !File.Exists(Path.Combine(part,"failure.json")) ||
           File.Exists(Path.Combine(part,"receipt.json")) ||
           File.Exists(Path.Combine(part,"response.json")))return null;
        var ctx=selected.Context;
        if(ctx.Mode!=PendulumSamCache.FreshMode ||
           !ValidBatchId(ctx.SceneRunId) || ctx.Scene.Key!=latest.SparkSceneKey ||
           ctx.Scene.VideoSha256!=source.Sha256 || ctx.MaxInFlight!=1 ||
           ctx.AutomaticRetryOfBatchId is not null || ctx.RecoveryFromBatchId is not null)return null;
        // Verify the exact original, original Spark run and full-video quote.
        var q=await PreflightAsync(studyId,role,ctx.TargetCoreFrames,1,ct,
            PendulumSamCache.FreshMode,ctx.SceneRunId);
        if(q.RequiresNewSparkCall || !q.ExistingSparkSceneVerified ||
           q.SourceSha256!=source.Sha256 || q.SparkSceneKey!=latest.SparkSceneKey ||
           q.SourceFrames!=latest.TotalInputFrames || q.Parts!=1 ||
           q.TotalSamEstimatedUsd>0.50)return null;
        var child=StageRetryId(latest.BatchId);
        var claim=Path.Combine(parentDir,"manual-stage-retry.json");
        var claimed=File.Exists(claim) || Directory.Exists(BatchDir(child));
        if(claimed && !File.Exists(claim))return null; // Never reauthorize an unlinked attempt.
        if(claimed){
            var link=await SamVideoArchive.ReadJsonAsync<ManualStageRetryClaim>(claim,ct);
            if(link.FailedBatchId!=latest.BatchId || link.NewBatchId!=child ||
               link.VideoSha256!=source.Sha256 || link.SceneKey!=latest.SparkSceneKey)return null;
        }
        return new(latest.BatchId,child,source.Sha256,latest.SparkSceneKey,
            ctx.SceneRunId!,q.PlanSha256,q.SourceFrames,q.TotalSamEstimatedUsd,
            ctx.TargetCoreFrames,claimed);
    }

    private async Task<SamAdaptiveClipManifest> ReusePreparedClipAsync(
        SamVideoSource source,double[] pts,SamAdaptiveSegmentPlan plan,
        string freshFolder,string parent,CancellationToken ct)
    {
        var previous=Path.Combine(BatchDir(parent),"clips");
        var manifest=await SamVideoArchive.ReadJsonAsync<SamAdaptiveClipManifest>(
            Path.Combine(previous,"clip-manifest.json"),ct);
        if(manifest.Clips.Count!=1 || manifest.OriginalVideoSha256!=source.Sha256 ||
           manifest.OriginalPtsSha256!=plan.VerifySourcePts(pts) ||
           manifest.OriginalFrames!=pts.Length || manifest.SubmittedFrames!=pts.Length ||
           manifest.BilledFrames!=plan.BilledFrames)
            throw new InvalidDataException("Original SAM preparation no longer matches the verified video.");
        var clip=manifest.Clips.Single();
        if(clip.Part!=0 || clip.Frames!=pts.Length || clip.RelativePath!="part-00.mp4" ||
           clip.InputStart!=0 || clip.CoreStart!=0 ||
           clip.InputEndExclusive!=pts.Length || clip.CoreEndExclusive!=pts.Length ||
           !ValidSha(clip.VideoSha256))
            throw new InvalidDataException("Saved SAM clip is not the complete original video.");
        var old=Path.Combine(previous,clip.RelativePath);
        if(!File.Exists(old) || new FileInfo(old).Length!=clip.Bytes)
            throw new InvalidDataException("Previous prepared MP4 is unavailable.");
        Directory.CreateDirectory(freshFolder);
        var dest=Path.Combine(freshFolder,"part-00.mp4");
        await using(var read=new FileStream(old,FileMode.Open,FileAccess.Read,FileShare.Read))
        await using(var write=new FileStream(dest,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            await read.CopyToAsync(write,ct);
        await using(var check=File.OpenRead(dest)){
            var sha=Convert.ToHexString(await SHA256.HashDataAsync(check,ct)).ToLowerInvariant();
            if(sha!=clip.VideoSha256 || new FileInfo(dest).Length!=clip.Bytes)
                throw new InvalidDataException("Saved MP4 changed during copy; no Meta request permitted.");
        }
        var reused=manifest with{CutProtocol="SAM_FULL_VIDEO_VERIFIED_REUSE_AFTER_FAILURE_V1",
            PrepareElapsedMs=0};
        await WriteJson(Path.Combine(freshFolder,"clip-manifest.json"),reused,ct);
        return reused;
    }

    public async Task<SamAdaptiveLiveReport> RunStageRetryAsync(
        string studyId,string role,SamStageRetryRequest request,CancellationToken ct)
    {
        if(!request.ConfirmPotentialSecondCharge || !request.ConfirmVideoTransfer ||
           !ValidBatchId(request.FailedBatchId) || !ValidBatchId(request.NewBatchId) ||
           !ValidSha(request.VideoSha256) || !ValidSha(request.PlanSha256) ||
           !SamManualApiTokenPolicy.Accepts(request.ModelApiToken) ||
           !access.Status(request.ModelApiToken).LiveCallsPermitted)
            throw new InvalidOperationException("Explicit authorization or backend paid gate missing.");
        await workflowGate.WaitAsync(ct);
        try {
            var quote=await StageRetryQuoteAsync(studyId,role,ct);
            if(quote is null || quote.AlreadyRequested ||
               request.FailedBatchId!=quote.FailedBatchId ||
               request.NewBatchId!=quote.NewBatchId ||
               request.VideoSha256!=quote.VideoSha256 ||
               request.PlanSha256!=quote.PlanSha256 ||
               !double.IsFinite(request.MaximumEstimatedCostUsd) ||
               request.MaximumEstimatedCostUsd+1e-9<quote.EstimatedSamUsd ||
               request.MaximumEstimatedCostUsd>.500000001)
                throw new InvalidOperationException("Another attempt exists or the owner-approved quote changed.");
            // Immutable claim BEFORE any potentially billable action. Even a lost
            // HTTP response must never start another request for the same parent.
            await WriteJson(Path.Combine(BatchDir(quote.FailedBatchId),"manual-stage-retry.json"),
                new ManualStageRetryClaim(quote.FailedBatchId,quote.NewBatchId,
                    quote.VideoSha256,quote.SparkSceneKey,DateTimeOffset.UtcNow,
                    request.MaximumEstimatedCostUsd,true),CancellationToken.None);
            var run=new SamAdaptiveLiveRunRequest{
                ModelApiToken=request.ModelApiToken,Mode=PendulumSamCache.FreshMode,
                SceneRunId=quote.SceneRunId,VideoSha256=quote.VideoSha256,
                SceneKey=quote.SparkSceneKey,PlanSha256=quote.PlanSha256,
                ClientBatchId=quote.NewBatchId,MaximumSamCostUsd=request.MaximumEstimatedCostUsd,
                TargetCoreFrames=quote.TargetCoreFrames,MaxInFlight=1,ConfirmPrivateVideoTransfer=true,
                ConfirmUpToManyBillableSamCalls=true,ConfirmNoAutomaticRetry=true
            };
            return await RunBatchCore(studyId,role,run,ct,
                manualPreparedParent:quote.FailedBatchId);
        }finally {workflowGate.Release();}
    }
}
