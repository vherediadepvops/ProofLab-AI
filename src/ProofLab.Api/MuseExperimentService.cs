using System.Security.Cryptography;
using System.Text.Json;
namespace ProofLab.Api;

public sealed class MuseExperimentService(
    PendulumDirectorWorkflow director,PendulumSamCache sam,
    PendulumDirectorKinematicsStore pivots,ISamPeriodWorker worker,
    MuseExperimentStore store,MetaLiveAccess access,IMuseExperimentTransport transport)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public MuseExperimentState State(string id)
    {
        MuseExperimentStore.StudyId(id);
        var prediction=store.Prediction(id);
        var observation=prediction is null?store.LatestObservation(id):store.Observation(prediction.ObservationId);
        return new(observation,observation is null?null:store.Evaluation(observation.Id),prediction,
            observation is null?null:store.AttemptStatus(observation.Id),access.ManualTokenCallsPermitted);
    }

    public async Task<MuseObservation> PrepareAsync(string id,MusePrepareRequest request,CancellationToken ct)
    {
        MuseExperimentStore.StudyId(id);
        var study=await director.ReadAsync(id,ct);
        if(study.Baseline is null || study.Followup is not null || store.Prediction(id) is not null)
            throw new InvalidOperationException("Research requires baseline evidence before video two.");
        var archive=await sam.ReadArchiveAsync(id,"baseline",ct);
        if(archive.Receipt.OriginalVideoSha256!=request.VideoSha256 ||
           archive.Receipt.AnalysisId!=request.AnalysisId || study.Baseline.Sha256!=request.VideoSha256)
            throw new ArgumentException("Source analysis changed.");
        object? pivot=null;var pivotProvenance="PIVOT_MISSING";
        if(request.PivotRevisionId is not null) {
            var revision=await pivots.ReadLatestAsync(id,"baseline",ct);
            if(revision.RevisionId!=request.PivotRevisionId)throw new ArgumentException("Pivot changed.");
            pivot=new {x=revision.Analysis.PivotXpx/480,y=revision.Analysis.PivotYpx/270};
            pivotProvenance=revision.PivotProvenance;
        } else if(archive.Receipt.Scene is {PivotX: not null,PivotY: not null} scene) {
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
            request.PivotRevisionId,pivot,pivotProvenance,sceneKey=archive.Receipt.Scene?.Key,
            baselineLengthMeters=study.BaselineLengthMeters,study.AngleDegrees,study.MassGrams,
            objectReview=study.HumanReview?.BobConfirmed??false
        },Json);
        var key=KnotPolicy.Hash(identity.GetRawText());
        var existing=store.Observation(key);if(existing is not null)return existing;
        var measured=await worker.MeasureAsync(new {
            archive,studyId=id,videoSha256=request.VideoSha256,pivot,
            request.PivotRevisionId,pivotProvenance
        },ct);
        using var measurement=JsonDocument.Parse(measured);
        if(measurement.RootElement.GetProperty("source").GetProperty("analysisId").GetString()!=request.AnalysisId)
            throw new InvalidDataException("Worker evidence mismatch.");
        var payload=JsonSerializer.Serialize(new {
            identity,baselineLengthMeters=study.BaselineLengthMeters,
            lengthProvenance="USER_CONFIGURED_NOT_CALIBRATED_SUSPENSION_TO_VISIBLE_CENTER",
            nominalReleaseAngleDegrees=study.AngleDegrees,massGrams=study.MassGrams,
            humanObjectReviewed=study.HumanReview?.BobConfirmed??false,
            pivotIndependentlyVerified=false,totalUncertaintyAvailable=false,
            measurement=measurement.RootElement
        },Json);
        return store.SaveObservation(new(key,id,request.VideoSha256,request.AnalysisId,
            DateTimeOffset.UtcNow,payload,KnotPolicy.Hash(payload)));
    }
    public async Task<MuseExperimentState> EvaluateAsync(string id,MuseExperimentRunRequest request,CancellationToken ct)
    {
        using var held=store.LockStudy(id);
        var frozen=store.Prediction(id);
        if(frozen is not null) {
            if(frozen.ObservationId!=request.ObservationId)throw new ArgumentException("Different frozen observation.");
            return State(id);
        }
        var observation=store.Observation(request.ObservationId)??throw new FileNotFoundException("Prepare evidence first.");
        if(observation.StudyId!=id)throw new ArgumentException("Observation belongs to another study.");
        var evaluation=store.Evaluation(observation.Id);if(evaluation is not null)return State(id);
        if(store.AttemptStatus(observation.Id) is not null)
            throw new InvalidOperationException("Previous attempt requires reconciliation; no automatic retry.");
        using var doc=JsonDocument.Parse(observation.PayloadJson);
        var root=doc.RootElement;var identity=root.GetProperty("identity");
        var current=await PrepareAsync(id,new(observation.VideoSha256,observation.AnalysisId,
            identity.GetProperty("pivotRevisionId").GetString()),ct);
        if(current.Id!=observation.Id)throw new InvalidOperationException("Measurement changed; prepare again.");
        var token=request.ModelApiToken??Environment.GetEnvironmentVariable("MODEL_API_KEY")??"";
        if(!request.ConfirmMeasuredDataTransfer || !request.ConfirmPaidCall ||
           request.MaximumEstimatedCostUsd!=MuseExperimentPolicy.MaximumEstimatedCostUsd ||
           !SamManualApiTokenPolicy.Accepts(token)||!access.Status(token).LiveCallsPermitted)
            throw new InvalidOperationException("Credential, consent, credits or cost gate blocked.");
        var summary=root.GetProperty("measurement").GetProperty("assessment").EnumerateObject()
            .Where(p=>p.Name!="cycles").ToDictionary(p=>p.Name,p=>p.Value.Clone());
        var input=JsonSerializer.Serialize(new {
            observationId=observation.Id,videoSha256=observation.VideoSha256,samAnalysisId=observation.AnalysisId,
            baselineLengthMeters=root.GetProperty("baselineLengthMeters"),
            lengthProvenance=root.GetProperty("lengthProvenance"),
            nominalReleaseAngleDegrees=root.GetProperty("nominalReleaseAngleDegrees"),
            humanObjectReviewed=root.GetProperty("humanObjectReviewed"),
            pivotIndependentlyVerified=false,totalUncertaintyAvailable=false,assessment=summary,
            limitations=root.GetProperty("measurement").GetProperty("limitations"),
            experimentOptions=MuseExperimentPolicy.ExperimentOptions(root),
            nextVideoSeen=false,interpretation="Masked image centroids are not independently measured centers of mass."
        },Json);
        if(input.Length>16000)throw new ArgumentException("Evidence summary exceeds the input limit.");
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
            var finalSource=await PrepareAsync(id,new(observation.VideoSha256,observation.AnalysisId,
                identity.GetProperty("pivotRevisionId").GetString()),CancellationToken.None);
            if(finalSource.Id!=observation.Id)
                throw new InvalidOperationException("Source changed during evaluation; prediction not frozen.");
            store.Complete(observation,result);
            return State(id);
        } catch {
            store.Fail(observation.Id,"FAILED_REQUIRES_RECONCILIATION");throw;
        }
    }
}
