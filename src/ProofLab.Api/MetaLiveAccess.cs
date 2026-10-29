using Microsoft.Extensions.Configuration;
using ProofLab.Science;

namespace ProofLab.Api;

/// <summary>
/// Server-side, secret-free preflight for Meta Model API. Operator billing
/// acknowledgments are NOT automatic confirmation of account credits.
/// </summary>
public sealed class MetaLiveAccess(
    IConfiguration configuration, MetaCallSafetyBudget budget)
{
    private bool Flag(string name) =>
        string.Equals(configuration["Meta:" + name], "true",
            StringComparison.OrdinalIgnoreCase);

    public bool KeyConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MODEL_API_KEY"));

    public bool ManualRequestsUnlimited => Flag("UnlimitedManualRequests");

    // Capability only: admission still requires a valid token in the manual request.
    public bool ManualTokenCallsPermitted => budget.Snapshot(true,
        Flag("CreditsConfirmedByOwner"), Flag("OwnerApprovedPaidRequests"),
        Flag("AllowPaidRequests"), manualRequest:ManualRequestsUnlimited).LiveCallsPermitted;

    public MetaCallSafetySnapshot Status(string? oneShotMetaToken = null)
    {
        var manual = oneShotMetaToken is not null && SamManualApiTokenPolicy.Accepts(oneShotMetaToken);
        return budget.Snapshot(oneShotMetaToken is null ? KeyConfigured : manual,
            Flag("CreditsConfirmedByOwner"), Flag("OwnerApprovedPaidRequests"),
            Flag("AllowPaidRequests"), manualRequest:ManualRequestsUnlimited && manual);
    }

    public void ReserveOrThrow(string? oneShotMetaToken = null)
    {
        var manual = oneShotMetaToken is not null && SamManualApiTokenPolicy.Accepts(oneShotMetaToken);
        if (!budget.TryReserve(oneShotMetaToken is null ? KeyConfigured : manual,
                Flag("CreditsConfirmedByOwner"), Flag("OwnerApprovedPaidRequests"),
                Flag("AllowPaidRequests"), manualRequest:ManualRequestsUnlimited && manual))
            throw new InvalidOperationException(
                "Meta live request is blocked by credential, credit, consent or request-budget gate.");
    }
}