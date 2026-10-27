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

    public MetaCallSafetySnapshot Status() => budget.Snapshot(
        KeyConfigured,
        Flag("CreditsConfirmedByOwner"),
        Flag("OwnerApprovedPaidRequests"),
        Flag("AllowPaidRequests"));

    public void ReserveOrThrow()
    {
        if (!budget.TryReserve(
                KeyConfigured,
                Flag("CreditsConfirmedByOwner"),
                Flag("OwnerApprovedPaidRequests"),
                Flag("AllowPaidRequests")))
            throw new InvalidOperationException(
                "Meta live request is blocked by credential, credit, consent or request-budget gate.");
    }
}
