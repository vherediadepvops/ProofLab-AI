namespace ProofLab.Science;

/// <summary>
/// Defense-in-depth for any billable Meta Model API call. This budget is an
/// upper bound for non-manual calls, not an account-wide hard spending limit.
/// Explicit manual admission may continue without a session count limit.
/// All gates default to false, and each request requires explicit admission.
/// </summary>
public sealed record MetaCallSafetySnapshot(
    bool ApiKeyConfigured, bool CreditsConfirmedByOwner,
    bool OwnerApprovedPaidRequests, bool PaidRequestsSwitchEnabled,
    bool LiveCallsPermitted, int RequestsUsed, int RequestsRemaining,
    int MaxRequestsPerProcess);

public sealed class MetaCallSafetyBudget
{
    public const int MaximumRequestsPerProcess = 2;
    private int used;
    private readonly int maximum;

    public MetaCallSafetyBudget(int alreadyUsedRequests = 0, int maximumRequests = MaximumRequestsPerProcess)
    {
        if(maximumRequests is <1 or >5)
            throw new ArgumentOutOfRangeException(nameof(maximumRequests));
        maximum=maximumRequests;
        if(alreadyUsedRequests<0)
            throw new ArgumentOutOfRangeException(nameof(alreadyUsedRequests));
        used=alreadyUsedRequests;
    }

    public int Used => Volatile.Read(ref used);

    public MetaCallSafetySnapshot Snapshot(
        bool apiKeyConfigured, bool creditsConfirmed, bool ownerApproved,
        bool paidRequestsEnabled, bool manualRequest = false)
    {
        var usedNow = Used;
        var remaining = Math.Max(0, maximum - usedNow);
        var allowed = apiKeyConfigured && creditsConfirmed && ownerApproved &&
            paidRequestsEnabled && (manualRequest || remaining > 0);
        return new MetaCallSafetySnapshot(apiKeyConfigured, creditsConfirmed,
            ownerApproved, paidRequestsEnabled, allowed, usedNow, remaining,
            maximum);
    }

    public bool TryReserve(bool apiKeyConfigured, bool creditsConfirmed,
        bool ownerApproved, bool paidRequestsEnabled, bool manualRequest = false)
    {
        if (!(apiKeyConfigured && creditsConfirmed && ownerApproved && paidRequestsEnabled))
            return false;
        while (true)
        {
            var before = Used;
            if (!manualRequest && before >= maximum) return false;
            if (Interlocked.CompareExchange(ref used, before + 1, before) == before)
                return true;
        }
    }
}