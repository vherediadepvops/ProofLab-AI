namespace ProofLab.Api;

// Never make this a record: an accidental ToString must not expose the token.
public sealed class SamAdaptiveLiveRunRequest
{
    public string ModelApiToken { get; init; }="";
    public string VideoSha256 { get; init; }="";
    public string SceneKey { get; init; }="";
    public string PlanSha256 { get; init; }="";
    public string ClientBatchId { get; init; }="";
    public double MaximumSamCostUsd { get; init; }
    public int TargetCoreFrames { get; init; }=53;
    public int MaxInFlight { get; init; }=4;
    public bool ConfirmPrivateVideoTransfer { get; init; }
    public bool ConfirmUpToManyBillableSamCalls { get; init; }
    public bool ConfirmNoAutomaticRetry { get; init; }
}

public sealed record SamAdaptiveLiveQuote(
    string Protocol,string SourceSha256,string SparkSceneKey,
    string SourcePtsSha256,string PlanSha256,string SuggestedBatchId,int SourceFrames,
    int Parts,int SimultaneousRequests,int SubmittedFrames,int BillableFrames,
    double TotalSamEstimatedUsd,double MaximumAllowedSamUsd,
    int RemainingTrialSlots,bool CanRunWithManualToken,
    bool ExistingSparkSceneVerified,
    bool RequiresNewSparkCall,
    string Provenance,
    int IdealPartsWithoutSparkCoverage,
    int VerifiedSparkSeedCount,
    bool ReducedForSparseSparkSeeds);

public sealed record SamAdaptivePartReceipt(
    int Part,int InputStart,int InputEndExclusive,
    int CoreStart,int CoreEndExclusive,int InputFrameCount,
    int BilledFrameEstimate,string OriginalVideoSha256,string SparkSceneKey,
    string SourcePtsSha256,string ClipSha256,string PromptSha256,
    string ResponseSha256,string ProviderResponseId,int? ProviderBilledFrames,
    DateTimeOffset FinishedAtUtc,string Status);

public sealed record SamAdaptivePartStatus(int Part,string State,
    int Events=0,int Deltas=0,int OutputChars=0);

public sealed record SamAdaptiveLiveReport(
    string BatchId,string Status,string SourceSha256,string SparkSceneKey,
    string PlanSha256,int Parts,int StartedParts,int CompletedParts,
    int FailedParts,int SkippedParts,int TotalInputFrames,
    int EstimatedBillableFrames,double EstimatedSamUsd,
    string? DerivedResponseSha256,
    int? DerivedFramesWithMasks,
    bool? DerivedSeamsVerified,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset RecordedAtUtc,string Note);

public sealed record SamAdaptiveProgress(
    string BatchId,int Parts,int Started,int Completed,int Failed,
    IReadOnlyList<SamAdaptivePartStatus> PerPart);
