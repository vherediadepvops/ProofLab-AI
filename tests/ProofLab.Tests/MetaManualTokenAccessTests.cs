using Microsoft.Extensions.Configuration;
using ProofLab.Api;
using ProofLab.Science;

namespace ProofLab.Tests;

public sealed class MetaManualTokenAccessTests
{
    private const string Token = "LLM_FAKE_OFFLINE_TEST_ONLY_1234567890123456";
    private static MetaLiveAccess Access(MetaCallSafetyBudget budget,
        bool credits = true, bool owner = true, bool enabled = true) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["Meta:CreditsConfirmedByOwner"] = credits.ToString(),
            ["Meta:OwnerApprovedPaidRequests"] = owner.ToString(),
            ["Meta:AllowPaidRequests"] = enabled.ToString()
        }).Build(), budget);

    [Fact]
    public void BrowserTokenSuppliesCredentialWithoutChangingGlobalConfiguration()
    {
        var budget = new MetaCallSafetyBudget();
        var access = Access(budget);
        var before = access.Status();
        Assert.True(access.ManualTokenCallsPermitted);
        Assert.True(access.Status(Token).LiveCallsPermitted);
        Assert.Equal(before.ApiKeyConfigured, access.Status().ApiKeyConfigured);
        Assert.Equal(0, budget.Used);
        access.ReserveOrThrow(Token);
        Assert.Equal(1, budget.Used);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void ValidBrowserTokenCannotOpenClosedBillingGates(bool credits, bool owner, bool enabled)
    {
        var budget = new MetaCallSafetyBudget();
        var access = Access(budget, credits, owner, enabled);
        Assert.False(access.ManualTokenCallsPermitted);
        Assert.False(access.Status(Token).LiveCallsPermitted);
        Assert.Throws<InvalidOperationException>(() => access.ReserveOrThrow(Token));
        Assert.Equal(0, budget.Used);
    }

    [Fact]
    public void BrowserAndBackendPathsShareTheSameProcessBudget()
    {
        var budget = new MetaCallSafetyBudget();
        var access = Access(budget);
        access.ReserveOrThrow(Token);
        access.ReserveOrThrow(Token);
        Assert.False(access.ManualTokenCallsPermitted);
        Assert.False(access.Status(Token).LiveCallsPermitted);
        Assert.Throws<InvalidOperationException>(() => access.ReserveOrThrow(Token));
        Assert.Equal(2, budget.Used);
    }
}
