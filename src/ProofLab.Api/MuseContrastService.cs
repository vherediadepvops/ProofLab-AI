using System.Text.Json;
namespace ProofLab.Api;
public sealed class MuseContrastService(PendulumDirectorWorkflow director,PendulumSamCache sam,
    SamAdaptiveLiveService adaptive,ISamPeriodWorker worker,MuseExperimentStore research,
    PendulumDirectorKinematicsStore pivots,IWebHostEnvironment env,MetaLiveAccess access,MuseContrastTransport transport)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private string Folder(string id){MuseExperimentStore.StudyId(id);return Path.Combine(env.ContentRootPath,".private","physics","muse-contrasts",id);}
    private async Task<T?> Read<T>(string id,string file,CancellationToken ct){var p=Path.Combine(Folder(id),file);return File.Exists(p)?JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(p,ct),Json):default;}
    private async Task Save<T>(string id,string file,T value,CancellationToken ct){Directory.CreateDirectory(Folder(id));await using var s=new FileStream(Path.Combine(Folder(id),file),FileMode.CreateNew,FileAccess.Write,FileShare.None);await JsonSerializer.SerializeAsync(s,value,Json,ct);}
    public async Task<MuseContrastState> State(string id,CancellationToken ct)
    {
        var e=await Read<MuseContrastEvidence>(id,"evidence.json",ct);var a=await Read<MuseContrastEvaluation>(id,"evaluation.json",ct);
        if(e is not null&&(e.StudyId!=id||KnotPolicy.Hash(e.PayloadJson)!=e.Sha256))throw new InvalidDataException("Contrast evidence changed.");
        if(e is not null){using var p=JsonDocument.Parse(e.PayloadJson);if(research.Prediction(id)?.Sha256!=p.RootElement.GetProperty("predictionSha256").GetString())throw new InvalidDataException("Frozen source changed.");}
        if(a is not null){if(e is null||a.Model!=MuseExperimentPolicy.Model)throw new InvalidDataException("Contrast source or model missing.");var raw=await Read<string>(id,"response.json",ct)??throw new InvalidDataException("Missing contrast response.");
            var receipt=MuseExperimentTransport.Validate(raw);using var d=JsonDocument.Parse(e!.PayloadJson);
            var answer=MuseContrastPolicy.Parse(KnotMetaTransport.OutputText(raw),d.RootElement.GetProperty("numericalVerdict").GetString()!);
            if(a.EvidenceSha256!=e.Sha256||a.RawResponseSha256!=KnotPolicy.Hash(raw)||a.ResponseId!=receipt.ProviderResponseId||a.InputTokens!=receipt.InputTokens||a.OutputTokens!=receipt.OutputTokens||JsonSerializer.Serialize(a.Answer,Json)!=JsonSerializer.Serialize(answer,Json))throw new InvalidDataException("Contrast receipt changed.");}
        return new(e,a,a is not null?"COMPLETED":File.Exists(Path.Combine(Folder(id),"attempt.json"))?"ATTEMPT_REQUIRES_INSPECTION":null);
    }
    public async Task<MuseContrastEvidence> Prepare(string id,MuseContrastRequest request,CancellationToken ct)
    {
        using var held=research.LockStudy(id);
        var frozen=research.Prediction(id)??throw new ArgumentException("Freeze a prediction before comparison.");
        var s=await director.ReadAsync(id,ct);var video=s.Followup??throw new ArgumentException("Video two missing.");
        if(video.Sha256!=request.VideoSha256||video.UploadedAtUtc<=frozen.FrozenAtUtc||video.Sha256==s.Baseline?.Sha256)
            throw new InvalidDataException("Second video provenance invalid.");
        if(!double.IsFinite(request.FollowupLengthMeters)||request.FollowupLengthMeters is <.1 or >2)throw new ArgumentException("Declare measured second length.");
        var archive=request.AdaptiveBatchId is null?await sam.ReadArchiveAsync(id,"followup",ct):await adaptive.ReadPlaybackAsync(id,"followup",request.AdaptiveBatchId,ct);
        if(archive.Receipt.AnalysisId!=request.AnalysisId||archive.Receipt.OriginalVideoSha256!=video.Sha256)throw new ArgumentException("Second analysis changed.");
        object? pivot=null;var provenance="PIVOT_MISSING";
        if(request.PivotRevisionId is not null){var revision=await pivots.ReadLatestAsync(id,"followup",ct);if(revision.RevisionId!=request.PivotRevisionId)throw new ArgumentException("Pivot changed.");pivot=new{x=revision.Analysis.PivotXpx/480,y=revision.Analysis.PivotYpx/270};provenance=revision.PivotProvenance;}
        var measured=await worker.MeasureAsync(new{archive,studyId=id,role="followup",videoSha256=video.Sha256,pivot,
            sourceWidth=video.Media.Width,sourceHeight=video.Media.Height,request.PivotRevisionId,pivotProvenance=provenance},ct);
        using var m=JsonDocument.Parse(measured);using var p=JsonDocument.Parse(frozen.PayloadJson);var assessment=m.RootElement.GetProperty("assessment");
        var verdict=MuseContrastPolicy.NumericalVerdict(p.RootElement,assessment,request.FollowupLengthMeters);
        var period=assessment.GetProperty("periodSeconds");double? difference=period.ValueKind==JsonValueKind.Number?(period.GetDouble()/p.RootElement.GetProperty("prediction").GetProperty("periodSeconds").GetDouble()-1)*100:null;
        var payload=JsonSerializer.Serialize(new{protocol=MuseContrastPolicy.Version,studyId=id,request,
            predictionSha256=frozen.Sha256,frozenPrediction=frozen.PayloadJson,followupUploadedAtUtc=video.UploadedAtUtc,
            followupLengthMeters=request.FollowupLengthMeters,sourceSha256=video.Sha256,analysisId=request.AnalysisId,
            responseSha256=archive.Receipt.ResponseJsonSha256,derivation=archive.Derivation,
            measurement=m.RootElement,numericalVerdict=verdict,relativeDifferencePercent=difference,
            dataKind=video.OriginalFilename.StartsWith("synthetic-",StringComparison.OrdinalIgnoreCase)?"SYNTHETIC_SOFTWARE_VALIDATION":"OWNER_SUPPLIED_PREEXISTING_RECORDING"},Json);
        var evidence=new MuseContrastEvidence(id,payload,KnotPolicy.Hash(payload),DateTimeOffset.UtcNow);
        var existing=await State(id,ct);if(existing.Evidence is not null){if(existing.Evidence.Sha256!=evidence.Sha256)throw new InvalidOperationException("Comparison already bound to another source.");return existing.Evidence;}
        await Save(id,"evidence.json",evidence,ct);return evidence;
    }
    public async Task<MuseContrastState> Evaluate(string id,MuseContrastRunRequest request,CancellationToken ct)
    {
        using var held=research.LockStudy(id);var state=await State(id,ct);var e=state.Evidence??throw new ArgumentException("Prepare contrast first.");
        if(e.Sha256!=request.EvidenceSha256)throw new ArgumentException("Contrast changed.");if(state.Evaluation is not null)return state;
        if(state.AttemptStatus is not null)throw new InvalidOperationException("Inspect previous contrast attempt.");
        var token=request.ModelApiToken??"";
        if(!request.ConfirmMeasuredDataTransfer||!request.ConfirmPaidCall||request.MaximumEstimatedCostUsd!=.05||!access.Status(token).LiveCallsPermitted||!SamManualApiTokenPolicy.Accepts(token))throw new InvalidOperationException("Contrast not authorized.");
        var input=MuseContrastPolicy.Input(e);access.ReserveOrThrow(token);
        await Save(id,"attempt.json",new{e.Sha256,startedAtUtc=DateTimeOffset.UtcNow,inputSha256=KnotPolicy.Hash(input),maximumEstimatedCostUsd=.05},ct);
        using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(3));var raw=await transport.EvaluateAsync(input,token,deadline.Token);
        await Save(id,"response.json",raw,CancellationToken.None);var receipt=MuseExperimentTransport.Validate(raw);
        using var d=JsonDocument.Parse(e.PayloadJson);var answer=MuseContrastPolicy.Parse(KnotMetaTransport.OutputText(raw),d.RootElement.GetProperty("numericalVerdict").GetString()!);
        await Save(id,"evaluation.json",new MuseContrastEvaluation(e.Sha256,receipt.ProviderResponseId,MuseExperimentPolicy.Model,KnotPolicy.Hash(raw),answer,receipt.InputTokens,receipt.OutputTokens,DateTimeOffset.UtcNow),CancellationToken.None);
        return await State(id,CancellationToken.None);
    }
}
