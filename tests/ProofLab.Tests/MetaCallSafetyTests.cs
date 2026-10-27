using ProofLab.Science;

namespace ProofLab.Tests;

public sealed class MetaCallSafetyTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, true)]
    public void MetaRequestsStayBlockedUntilAllFourGates(
        bool key, bool credits, bool consent, bool enabled)
    {
        var gate = new MetaCallSafetyBudget();
        var decision = gate.Snapshot(key, credits, consent, enabled);
        Assert.False(decision.LiveCallsPermitted);
        Assert.False(gate.TryReserve(key, credits, consent, enabled));
        Assert.Equal(0, gate.Used);
    }

    [Fact]
    public void ApprovedMetaCallsHaveStrictPerProcessBudget()
    {
        var gate = new MetaCallSafetyBudget();
        Assert.True(gate.Snapshot(true, true, true, true).LiveCallsPermitted);
        Assert.True(gate.TryReserve(true, true, true, true));
        Assert.True(gate.TryReserve(true, true, true, true));
        Assert.False(gate.TryReserve(true, true, true, true));
        var state = gate.Snapshot(true, true, true, true);
        Assert.False(state.LiveCallsPermitted);
        Assert.Equal(2, state.RequestsUsed);
        Assert.Equal(0, state.RequestsRemaining);
    }

    [Fact]
    public void ConcurrentRequestsCannotExceedCostBudget()
    {
        var gate = new MetaCallSafetyBudget();
        var admitted = 0;
        Parallel.For(0, 50, _ =>
        {
            if (gate.TryReserve(true, true, true, true))
                Interlocked.Increment(ref admitted);
        });
        Assert.Equal(MetaCallSafetyBudget.MaximumRequestsPerProcess, admitted);
        Assert.Equal(MetaCallSafetyBudget.MaximumRequestsPerProcess, gate.Used);
    }

    [Fact]
    public void NoChargesArePossibleThroughMissingCredentials()
    {
        var gate = new MetaCallSafetyBudget();
        for (var i = 0; i < 100; i++)
            Assert.False(gate.TryReserve(false, true, true, true));
        Assert.Equal(0, gate.Used);
    }
}
