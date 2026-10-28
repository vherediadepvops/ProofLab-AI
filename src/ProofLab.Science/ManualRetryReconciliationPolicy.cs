namespace ProofLab.Science;

/// <summary>
/// One-time, human-approved reconciliation after a single SAM provider run
/// returned HTTP 502 but Meta Billing credits visibly decreased. This
/// acknowledgment is not a receipt or exact provider usage report.
/// The actual artifact locks retain both historical attempts permanently.
/// </summary>
public static class ManualRetryReconciliationPolicy
{
    public const string OwnerAuthorization =
        "OWNER_APPROVED_ONE_MORE_SAM_ATTEMPT_2026_10_28_AFTER_CREDIT_DELTA_USD_0_37_USAGE_UNVERIFIED";

    public static bool IsAcceptable(string? value) =>
        string.Equals(value, OwnerAuthorization, StringComparison.Ordinal);

    // One manual second attempt is permitted only with a preserved previous
    // marker and matching owner acknowledgement. A second manual-retry
    // marker permanently blocks a third request, even after a restart.
    public static bool CanStartOneManualRetry(
        bool priorAttemptExists, bool manualRetryAlreadyStarted,
        bool newOwnerConsent, string? evidence) =>
        priorAttemptExists && !manualRetryAlreadyStarted &&
        newOwnerConsent && IsAcceptable(evidence);
}
