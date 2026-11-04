using System.Security.Cryptography;
using System.Text.Json;
namespace ProofLab.Api;

public sealed class MuseExperimentService(
    PendulumDirectorWorkflow director,PendulumSamCache sam,
    PendulumDirectorKinematicsStore pivots,ISamPeriodWorker worker,
    MuseExperimentStore store,MetaLiveAccess access,IMuseExperimentTransport transport,SamAdaptiveLiveService adaptive)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public MuseExperimentState State(string id,string purpose=MuseExperimentPurpose.Interpretation)
    {
        MuseExperimentStore.StudyId(id);MuseExperimentPurpose.Validate(purpose);
        var prediction=store.Prediction(id);
        var frozenObservation=prediction is null?null:store.Observation(prediction.ObservationId);
        var observation=purpose==MuseExperimentPurpose.Prediction
            ?frozenObservation??store.PredictionDraft(id)
            :frozenObservation is not null && MuseExperimentPurpose.Of(frozenObservation)==MuseExperimentPurpose.Interpretation
                ?frozenObservation:store.LatestObservation(id,MuseExperimentPurpose.Interpretation);
        // A previous freeze validation could append an unevaluated review-only
        // snapshot. Recover its original completed answer without rewriting history.
        if(observation is not null && store.AttemptStatus(observation.Id) is null)
            observation=store.CompletedBeforeHumanReview(observation)??observation;
        return new(observation,observation is null?null:store.Evaluation(observation.Id),prediction,
            observation is null?null:store.AttemptStatus(observation.Id),access.ManualTokenCallsPermitted);
    }

    public async Task<DirectorSession> ConfirmTrackingReviewAsync(string id,
        DirectorHumanReviewRequest request,CancellationToken ct)
    {
        DirectorBaselineReviewPolicy.RequireHuman(request.BobConfirmed);
        if(request.SamEvidence is null)
            return await director.ConfirmBaselineAsync(id,request.BobConfirmed,ct);
        using var held=store.LockStudy(id);
        // Reuse the authenticated archive and native SAM measurement. This path
        // never invokes a provider and does not overwrite the prior Muse answer.
        var frozen=store.Prediction(id);
        var saved=frozen is null?null:store.Observation(frozen.ObservationId)
            ??throw new FileNotFoundException("Frozen Muse evidence missing.");
        var observation=saved is null?await PrepareAsync(id,request.SamEvidence,ct):
            await PrepareAsync(id,RequestFor(saved) with {
                VideoSha256=request.SamEvidence.VideoSha256,AnalysisId=request.SamEvidence.AnalysisId,
                PivotRevisionId=request.SamEvidence.PivotRevisionId,AdaptiveBatchId=request.SamEvidence.AdaptiveBatchId
            },ct,saved,forFrozenBaselineReview:true);
        var evidence=DirectorBaselineReviewPolicy.FromSamObservation(id,request.SamEvidence,observation);
        return await director.ConfirmSamBaselineAsync(id,request.BobConfirmed,evidence,ct);
    }

    public async Task<MuseObservation> PrepareAsync(string id,MusePrepareRequest request,CancellationToken ct)
    {
        using var held=request.Purpose==MuseExperimentPurpose.Prediction?store.LockStudy(id):null;
        var observation=await PrepareAsync(id,request,ct,null);
        if(request.Purpose==MuseExperimentPurpose.Prediction)store.SelectPredictionDraft(observation);
        return observation;
    }
    private static MusePrepareRequest RequestFor(MuseObservation observation)
    {
        using var payload=JsonDocument.Parse(observation.PayloadJson);
        var root=payload.RootElement;var identity=root.GetProperty("identity");
        var purpose=MuseExperimentPurpose.Of(root);
        return new(observation.VideoSha256,observation.AnalysisId,identity.GetProperty("pivotRevisionId").GetString(),
            identity.TryGetProperty("adaptiveBatchId",out var batch)?batch.GetString():null,purpose,
            purpose==MuseExperimentPurpose.Prediction?root.GetProperty("plannedFollowupLengthMeters").GetDouble():null);
    }

    private async Task<MuseObservation> PrepareAsync(string id,MusePrepareRequest request,CancellationToken ct,
        MuseObservation? expected,bool forFrozenBaselineReview=false)
    {
        MuseExperimentStore.StudyId(id);
        var study=await director.ReadAsync(id,ct);
        MuseExperimentPurpose.Validate(request.Purpose);
        if(request.Purpose==MuseExperimentPurpose.Prediction)
            MuseExperimentPurpose.ValidateChosenLength(request.PlannedFollowupLengthMeters,study.BaselineLengthMeters);
        else if(request.PlannedFollowupLengthMeters is not null)
            throw new ArgumentException("A chosen length requires prediction purpose.");
        var plannedLength=request.Purpose==MuseExperimentPurpose.Prediction?request.PlannedFollowupLengthMeters:
            study.FollowUpMode=="manual"?study.SelectedFollowUpLengthMeters:null;
        var frozen=store.Prediction(id);
        if(study.Baseline is null ||
           !forFrozenBaselineReview && (study.Followup is not null || frozen is not null))
            throw new InvalidOperationException("Research requires baseline evidence before video two.");
        // Older sessions may have a frozen prediction without the separate
        // human acknowledgement. Validate that exact saved baseline, preserving
        // its answer and prediction even if video two is already present.
        if(forFrozenBaselineReview && (expected is null || frozen?.ObservationId!=expected.Id || expected.StudyId!=id))
            throw new InvalidOperationException("Review requires the original frozen baseline evidence.");
        var archive=request.AdaptiveBatchId is null ? await sam.ReadArchiveAsync(id,"baseline",ct)
            : await adaptive.ReadPlaybackAsync(id,"baseline",request.AdaptiveBatchId,ct);
        if(archive.Receipt.OriginalVideoSha256!=request.VideoSha256 ||
           archive.Receipt.AnalysisId!=request.AnalysisId || study.Baseline.Sha256!=request.VideoSha256)
            throw new ArgumentException("Source analysis changed.");
        object? pivot=null;var pivotProvenance="PIVOT_MISSING";
        if(request.PivotRevisionId is not null) {
            var revision=await pivots.ReadLatestAsync(id,"baseline",ct);
            if(revision.RevisionId!=request.PivotRevisionId)throw new ArgumentException("Pivot changed.");
            pivot=new {x=revision.Analysis.PivotXpx/480,y=revision.Analysis.PivotYpx/270};
            pivotProvenance=revision.PivotProvenance;
        } else if(archive.Receipt.Scene is {PivotX: not null,PivotY: not null} scene && scene.Origin!=AssemblyPolicy.GroundingOrigin) {
            pivot=new {x=scene.PivotX/1000,y=scene.PivotY/1000};
            pivotProvenance="HUMAN_CONFIRMED_SCENE_PIVOT";
        }
        var workerFile=Path.Combine(AppContext.BaseDirectory,"Workers","sam-period-worker.cjs");
        var workerHash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(workerFile,ct))).ToLowerInvariant();
        var identity=JsonSerializer.SerializeToElement(new {
            protocol=MuseExperimentPolicy.Version,workerHash,studyId=id,
            videoSha256=request.VideoSha256,analysisId=request.AnalysisId,
            responseSha256=archive.Receipt.ResponseJsonSha256,
            ptsSha256=KnotPolicy.Hash(JsonSerializer.Serialize(archive.Receipt.FramePtsSeconds,Json)),
            request.PivotRevisionId,request.AdaptiveBatchId,derivation=archive.Derivation,pivot,pivotProvenance,automaticSuspension=archive.Suspension,suspensionReview=archive.SuspensionReview,sceneKey=archive.Receipt.Scene?.Key,
            baselineLengthMeters=study.BaselineLengthMeters,study.AngleDegrees,study.MassGrams,
            plannedFollowupLengthMeters=plannedLength,
            objectReview=study.HumanReview?.BobConfirmed??false
        },Json);
        // Legacy interpretation identities stay byte-for-byte compatible. Only
        // explicit prediction requests add an intent and bind the user's length.
        if(request.Purpose==MuseExperimentPurpose.Prediction) {
            var node=System.Text.Json.Nodes.JsonNode.Parse(identity.GetRawText())!;
            node["purpose"]=MuseExperimentPurpose.Prediction;
            identity=JsonSerializer.SerializeToElement(node,Json);
        }
        var key=KnotPolicy.Hash(identity.GetRawText());
        // Validate before measuring or persisting anything. Human review happens
        // after automatic interpretation; it does not change the measured source.
        // Its original false value stays in the immutable AI input and limitations.
        if(expected is not null) {
            if(!MuseObservationIdentity.MatchesCurrent(expected,identity))
                throw new InvalidOperationException("Source changed; do not reuse an old interpretation.");
            return expected;
        }
        var existing=store.Observation(key);
        if(existing is not null) {
            if(store.AttemptStatus(existing.Id) is null)
                return store.CompletedBeforeHumanReview(existing)??existing;
            return existing;
        }
        var completed=store.CompletedBeforeHumanReview(id,identity);
        if(completed is not null)return completed;
        var measured=await worker.MeasureAsync(new {
            archive,studyId=id,videoSha256=request.VideoSha256,pivot,
            sourceWidth=study.Baseline.Media.Width,sourceHeight=study.Baseline.Media.Height,
            request.PivotRevisionId,pivotProvenance
        },ct);
        using var measurement=JsonDocument.Parse(measured);
        if(measurement.RootElement.GetProperty("source").GetProperty("analysisId").GetString()!=request.AnalysisId)
            throw new InvalidDataException("Worker evidence mismatch.");
        var payload=JsonSerializer.Serialize(new {
            identity,baselineLengthMeters=study.BaselineLengthMeters,
            dataKind=study.Baseline.OriginalFilename.StartsWith("synthetic-",StringComparison.OrdinalIgnoreCase)?"SYNTHETIC_SOFTWARE_VALIDATION":"OWNER_SUPPLIED_PREEXISTING_RECORDING",
            plannedFollowupLengthMeters=plannedLength,
            lengthProvenance=study.BaselineLengthMeters is null ? "NOT_PROVIDED" : "USER_CONFIGURED_NOT_CALIBRATED_SUSPENSION_TO_VISIBLE_CENTER",
            nominalReleaseAngleDegrees=study.AngleDegrees,massGrams=study.MassGrams,
            humanObjectReviewed=study.HumanReview?.BobConfirmed??false,
            pivotIndependentlyVerified=false,totalUncertaintyAvailable=false,
            measurement=measurement.RootElement
        },Json);
        return store.SaveObservation(new(key,id,request.VideoSha256,request.AnalysisId,
            DateTimeOffset.UtcNow,payload,KnotPolicy.Hash(payload)));
    }
    public async Task<MuseExperimentState> FreezeAsync(string id,MuseFreezeRequest request,CancellationToken ct)
    {
        using var held=store.LockStudy(id);
        var study=await director.ReadAsync(id,ct);
        if(study.Baseline is null || study.Followup is not null ||
           study.HumanReview?.BobConfirmed!=true)
            throw new InvalidOperationException("Review the tracked object before freezing a prediction.");
        var observation=store.Observation(request.ObservationId)
            ??throw new FileNotFoundException("Muse evidence missing.");
        if(observation.StudyId!=id || observation.VideoSha256!=study.Baseline.Sha256)
            throw new ArgumentException("Muse result does not match this study.");
        var evaluation=store.Evaluation(observation.Id)
            ??throw new InvalidOperationException("Muse interpretation not complete.");
        if(evaluation.ResponseId!=request.ResponseId || evaluation.Answer.Decision!="PROPOSE_EXPERIMENT")
            throw new InvalidOperationException("No eligible Muse proposal to freeze.");
        // Idempotent recovery for a lost HTTP reply. An existing frozen decision
        // is immutable; do not re-run PrepareAsync once it is frozen.
        var frozen=store.Prediction(id);
        if(frozen is not null){
            if(frozen.ObservationId!=observation.Id || frozen.ResponseId!=evaluation.ResponseId)
                throw new InvalidOperationException("This study already has a different frozen prediction.");
            return State(id,MuseExperimentPurpose.Of(observation));
        }
        var purpose=MuseExperimentPurpose.Of(observation);
        if(purpose!=MuseExperimentPurpose.Prediction)
            throw new InvalidOperationException("Choose a second length and request its prediction before freezing.");
        if(store.PredictionDraft(id)?.Id!=observation.Id)
            throw new InvalidOperationException("The selected prediction length changed.");
        await PrepareAsync(id,RequestFor(observation),ct,observation);
        store.Freeze(observation,evaluation);
        return State(id,purpose);
    }

    public async Task<MuseExperimentState> EvaluateAsync(string id,MuseExperimentRunRequest request,CancellationToken ct)
    {
        using var held=store.LockStudy(id);
        var frozen=store.Prediction(id);
        if(frozen is not null) {
            if(frozen.ObservationId!=request.ObservationId)throw new ArgumentException("Different frozen observation.");
            return State(id,MuseExperimentPurpose.Of(store.Observation(frozen.ObservationId)!));
        }
        var observation=store.Observation(request.ObservationId)??throw new FileNotFoundException("Prepare evidence first.");
        if(observation.StudyId!=id)throw new ArgumentException("Observation belongs to another study.");
        var purpose=MuseExperimentPurpose.Of(observation);
        if(purpose==MuseExperimentPurpose.Prediction && store.PredictionDraft(id)?.Id!=observation.Id)
            throw new InvalidOperationException("The selected prediction length changed.");
        var evaluation=store.Evaluation(observation.Id);if(evaluation is not null)return State(id,purpose);
        if(store.AttemptStatus(observation.Id) is not null)
            throw new InvalidOperationException("Previous attempt requires reconciliation; no automatic retry.");
        using var doc=JsonDocument.Parse(observation.PayloadJson);
        var root=doc.RootElement;
        await PrepareAsync(id,RequestFor(observation),ct,observation);
        var token=request.ModelApiToken??Environment.GetEnvironmentVariable("MODEL_API_KEY")??"";
        if(!request.ConfirmMeasuredDataTransfer || !request.ConfirmPaidCall ||
           request.MaximumEstimatedCostUsd!=MuseExperimentPolicy.MaximumEstimatedCostUsd ||
           !SamManualApiTokenPolicy.Accepts(token)||!access.Status(token).LiveCallsPermitted)
            throw new InvalidOperationException("Credential, consent, credits or cost gate blocked.");
        var input=MuseExperimentPolicy.BuildInput(observation);
        ct.ThrowIfCancellationRequested();
        access.ReserveOrThrow(token);
        store.BeginAttempt(observation);
        try {
            // Continue evidence capture even if the browser disconnects after admission.
            using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var response=await transport.EvaluateAsync(input,token,deadline.Token);
            store.SaveRaw(observation.Id,response.Raw);
            var receipt=MuseExperimentTransport.Validate(response.Raw);
            var answer=MuseExperimentPolicy.Parse(KnotMetaTransport.OutputText(response.Raw),root);
            var result=new MuseEvaluation(id,observation.Id,receipt.ProviderResponseId,MuseExperimentPolicy.Model,
                KnotPolicy.Hash(response.Raw),answer,receipt.InputTokens,receipt.OutputTokens,DateTimeOffset.UtcNow,
                receipt.InputTokens*1.25/1_000_000+receipt.OutputTokens*4.25/1_000_000);
            // The study lock is also held by uploads; video two cannot arrive during inference.
            await PrepareAsync(id,RequestFor(observation),CancellationToken.None,observation);
            store.Complete(observation,result);
            return State(id,purpose);
        } catch {
            store.Fail(observation.Id,"FAILED_REQUIRES_RECONCILIATION");throw;
        }
    }
}
