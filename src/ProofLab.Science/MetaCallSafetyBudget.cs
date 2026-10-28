namespace ProofLab.Science;

/// <summary>
/// Defense-in-depth for any billable Meta Model API call. This budget is an
/// upper bound per API process, not an account-wide hard spending limit.
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

    public MetaCallSafetyBudget(int alreadyUsedRequests = 0)
    {
        if(alreadyUsedRequests is <0 or >MaximumRequestsPerProcess)
            throw new ArgumentOutOfRangeException(nameof(alreadyUsedRequests));
        used=alreadyUsedRequests;
    }

    public int Used => Volatile.Read(ref used);

    public MetaCallSafetySnapshot Snapshot(
        bool apiKeyConfigured, bool creditsConfirmed, bool ownerApproved,
        bool paidRequestsEnabled)
    {
        var usedNow = Used;
        var remaining = Math.Max(0, MaximumRequestsPerProcess - usedNow);
        var allowed = apiKeyConfigured && creditsConfirmed && ownerApproved &&
            paidRequestsEnabled && remaining > 0;
        return new MetaCallSafetySnapshot(apiKeyConfigured, creditsConfirmed,
            ownerApproved, paidRequestsEnabled, allowed, usedNow, remaining,
            MaximumRequestsPerProcess);
    }

    public bool TryReserve(bool apiKeyConfigured, bool creditsConfirmed,
        bool ownerApproved, bool paidRequestsEnabled)
    {
        if (!(apiKeyConfigured && creditsConfirmed && ownerApproved && paidRequestsEnabled))
            return false;
        while (true)
        {
            var before = Used;
            if (before >= MaximumRequestsPerProcess) return false;
            if (Interlocked.CompareExchange(ref used, before + 1, before) == before)
                return true;
        }
    }
}
