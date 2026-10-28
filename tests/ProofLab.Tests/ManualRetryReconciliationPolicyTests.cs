using ProofLab.Science;

namespace ProofLab.Tests;

public class ManualRetryReconciliationPolicyTests
{
    [Fact]
    public void OwnerConsentRequiresExactExplicitReconciliation()
    {
        Assert.True(ManualRetryReconciliationPolicy.IsAcceptable(
            ManualRetryReconciliationPolicy.OwnerAuthorization));
        Assert.False(ManualRetryReconciliationPolicy.IsAcceptable(null));
        Assert.False(ManualRetryReconciliationPolicy.IsAcceptable(""));
        Assert.False(ManualRetryReconciliationPolicy.IsAcceptable(
            "RETRY_APPROVED"));
        Assert.False(ManualRetryReconciliationPolicy.IsAcceptable(
            ManualRetryReconciliationPolicy.OwnerAuthorization+"\n"));
        var evidence=ManualRetryReconciliationPolicy.OwnerAuthorization;
        Assert.True(ManualRetryReconciliationPolicy.CanStartOneManualRetry(
            true,false,true,evidence));
        Assert.False(ManualRetryReconciliationPolicy.CanStartOneManualRetry(
            true,true,true,evidence)); // blocks third attempt even if authorized
        Assert.False(ManualRetryReconciliationPolicy.CanStartOneManualRetry(
            true,false,false,evidence));
        Assert.False(ManualRetryReconciliationPolicy.CanStartOneManualRetry(
            false,false,true,evidence));
    }
}
