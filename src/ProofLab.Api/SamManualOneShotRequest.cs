namespace ProofLab.Api;

/// <summary>
/// Not a record: avoid auto-generated ToString printing the manually
/// pasted Meta bearer key. Never serialize this instance to disk/logs.
/// </summary>
public sealed class SamManualOneShotRequest
{
    public string ModelApiToken { get; init; } = "";
    public string VideoSha256 { get; init; } = "";
    public string? Mode { get; init; }
    public bool ConfirmPrivateVideoTransfer { get; init; }
    public bool ConfirmOneBillableSamCall { get; init; }
    public double MaximumEstimatedCostUsd { get; init; }
    public bool OwnerApprovesOneReconciledRetry { get; init; }
    public string? PreviousAttemptBillingEvidence { get; init; }

    public SamVideoRunRequest ToSafeRunRequest() =>
        new(VideoSha256, ConfirmPrivateVideoTransfer,
            ConfirmOneBillableSamCall, MaximumEstimatedCostUsd,
            OwnerApprovesOneReconciledRetry, PreviousAttemptBillingEvidence, Mode);
}
