using Microsoft.Extensions.Configuration;
using ProofLab.Api;
using ProofLab.Science;

namespace ProofLab.Tests;

public sealed class MetaManualTokenAccessTests
{
    private const string Token = "LLM_FAKE_OFFLINE_TEST_ONLY_1234567890123456";
    private static MetaLiveAccess Access(MetaCallSafetyBudget budget,
        bool credits = true, bool owner = true, bool enabled = true, bool unlimited = false) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["Meta:CreditsConfirmedByOwner"] = credits.ToString(),
            ["Meta:OwnerApprovedPaidRequests"] = owner.ToString(),
            ["Meta:AllowPaidRequests"] = enabled.ToString(),
            ["Meta:UnlimitedManualRequests"] = unlimited.ToString()
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
        var access = Access(budget, credits, owner, enabled, unlimited:true);
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
    [Fact]
    public void UnlimitedManualRequestsKeepCountingAfterTheSessionBudgetIsSpent()
    {
        var budget = new MetaCallSafetyBudget(4,4);
        var access = Access(budget,unlimited:true);
        Assert.True(access.ManualRequestsUnlimited);
        Assert.True(access.ManualTokenCallsPermitted);
        Assert.True(access.Status(Token).LiveCallsPermitted);
        Parallel.For(0,100,_ => access.ReserveOrThrow(Token));
        Assert.Equal(104,budget.Used);
        Assert.Equal(0,access.Status().RequestsRemaining);
        Assert.True(access.ManualTokenCallsPermitted);
        Assert.False(access.Status().LiveCallsPermitted);
        Assert.False(budget.TryReserve(true,true,true,true));
        Assert.Throws<InvalidOperationException>(()=>access.ReserveOrThrow());
        Assert.Throws<InvalidOperationException>(()=>access.ReserveOrThrow("invalid"));
        Assert.False(access.Status("invalid").LiveCallsPermitted);
        Assert.Equal(104,budget.Used);
        var restarted = Access(new MetaCallSafetyBudget(budget.Used,4),unlimited:true);
        Assert.Equal(104,restarted.Status().RequestsUsed);
        Assert.True(restarted.Status(Token).LiveCallsPermitted);
        Assert.False(restarted.Status().LiveCallsPermitted);
    }
}