namespace ProofLab.Api;

public sealed record SamVideoReceipt(
    string CacheKey, string OriginalVideoSha256, string VideoId,
    string Model, string Concept, string RequestProtocol,
    DateTimeOffset CapturedAtUtc, string ResponseJsonSha256,
    string ProviderResponseId, int? BilledFrames,
    int SourceFrameCount, double[] FramePtsSeconds,
    string SubmittedMp4Sha256,
    string ProviderEvidenceStatus, double EstimatedCostUsd,
    string? AnalysisId = null, SamSceneDefinition? Scene = null);

public sealed record SamVideoState(
    string Status, string Model, string Concept, string SourceSha256,
    string CacheKey, int SourceFrames, double EstimatedCostUsd,
    string? ResponseSha256, int? BilledFrames, bool CanRunLive,
    string Provenance, string? AnalysisId = null,
    string Mode = "api-meta-cache", bool CacheHit = false, string Storage = "sqlite", string? SceneKey = null, bool RecognitionRequired = false,
    string? PreviousAttemptStatus = null);

public sealed record SamVideoPreflight(
    string SourceSha256, string VideoId, string Codec,
    int SourceWidth, int SourceHeight, int SourceFrames,
    double DurationSeconds, double EstimatedCostUsd,
    string FramePtsSha256, bool SupportsSingleCall,
    string TimingSource, string BillingStatus, double SuspensionReviewMaximumCostUsd=0.05);

// Derived metadata beside the immutable receipt; never labels an AI estimate as human review.
public sealed record SamSuspensionEvidence(string SourceSha256,string SceneKey,string EvidenceKey,
    string ResponseSha256,string Model,int Width,int Height,KnotPoint[] Points, string? Refinement=null);
public sealed record SamVideoArtifact(
    SamVideoState State, SamVideoReceipt Receipt, string RawResponseJson,
    SamSuspensionEvidence? Suspension = null, SuspensionReviewEvidence? SuspensionReview=null,
    SamAdaptiveDerivation? Derivation=null);

public sealed record SamVideoRunRequest(
    string VideoSha256, bool ConfirmPrivateVideoTransfer,
    bool ConfirmOneBillableSamCall, double MaximumEstimatedCostUsd,
    bool OwnerApprovesOneReconciledRetry = false,
    string? PreviousAttemptBillingEvidence = null,
    string? Mode = null, string? SceneKey = null);

public sealed record SamApiCompletedResult(
    string FullResponseJson, string ProviderResponseId, int? BilledFrames);

public sealed record SamOriginalAttempt(
    string Protocol, string SourceSha256, string SubmittedMp4Sha256,
    int FrameCount, double EstimatedUsd, DateTimeOffset RequestedAtUtc,
    string Status, string? AnalysisId = null, string? PreviousAttemptStatus = null);

public sealed record SamVideoSource(
    string Sha256, string VideoId, string Path, string Codec,
    int Width, int Height, double DurationSeconds);

// Narrow dependency boundaries permit offline orchestration tests. Production
// adapters keep source verification, media validation and billing gates intact.
public interface ISamVideoRuntime
{
    Task<SamVideoSource> GetVerifiedSourceAsync(string studyId, string role, CancellationToken ct);
    Task<double[]> ReadFramePtsAsync(string path, CancellationToken ct);
    Task<SamVideoSource> GetQuoteSourceAsync(string studyId,string role,CancellationToken ct)=>
        GetVerifiedSourceAsync(studyId,role,ct);
    Task<double[]> ReadSourceFramePtsAsync(SamVideoSource source,CancellationToken ct)=>ReadFramePtsAsync(source.Path,ct);
    async Task<VideoFrameCount> GetFrameCountAsync(SamVideoSource source,CancellationToken ct) {
        var pts=await ReadFramePtsAsync(source.Path,ct);
        return new(pts.Length,"FFPROBE_BEST_EFFORT_SOURCE_FRAME_PTS_NORMALIZED_TO_FIRST",
            SamVideoArchive.Hash(string.Join("\n",pts.Select(t=>t.ToString("R",System.Globalization.CultureInfo.InvariantCulture)))));
    }
    Task CreateMp4Async(string source, string destination, CancellationToken ct);
    bool LiveCallsPermitted { get; }
    bool ManualTokenCallsPermitted => LiveCallsPermitted;
    void ReserveOrThrow();
    bool CanRunWithToken(string? oneShotMetaToken) => LiveCallsPermitted;
    void ReserveOrThrow(string? oneShotMetaToken) => ReserveOrThrow();
}

public interface ISamVideoTransport
{
    Task<SamApiCompletedResult> UploadAndSegmentAsync(
        string originalPath, string concept,
        Func<SamVideoSseProgress, ValueTask>? progress, CancellationToken ct,
        string? oneShotMetaToken = null);
}
