using ProofLab.Science;

namespace ProofLab.Tests;

public sealed class ResearchAgentToolPolicyTests
{
    [Fact]
    public void ValidatedFiniteProposalOnlySelectsBoundedInstrument()
    {
        var p = ResearchAgentToolPolicy.ValidateProposal(
            "{\"tool\":\"CHECK_GOLDBACH_FINITE\",\"conjectureId\":\"GOLDBACH-STRONG\",\"evenLimit\":100}");
        Assert.Equal("CHECK_GOLDBACH_FINITE", p.Tool);
        Assert.Equal(100, p.EvenLimit);
        Assert.Null(p.Strategy);
        Assert.Equal(3, ResearchAgentToolPolicy.MaxToolCallsPerCycle);
    }

    [Fact]
    public void CuratedKnownBenchmarkMayUseOnlyCuratedProofStrategy()
    {
        var p = ResearchAgentToolPolicy.ValidateProposal(
            "{\"tool\":\"VERIFY_KNOWN_BENCHMARK\",\"problemId\":\"NAT-ADD-COMM\",\"strategy\":\"omega\"}");
        Assert.Equal("omega", p.Strategy);
        Assert.Equal("NAT-ADD-COMM", p.ProblemId);
    }

    [Theory]
    [InlineData("{\"tool\":\"SYSTEM_COMMAND\",\"command\":\"del C:\\\\private\"}")]
    [InlineData("{\"tool\":\"CHECK_GOLDBACH_FINITE\",\"conjectureId\":\"GOLDBACH-STRONG\",\"evenLimit\":3}")]
    [InlineData("{\"tool\":\"CHECK_GOLDBACH_FINITE\",\"conjectureId\":\"GOLDBACH-STRONG\",\"evenLimit\":2002}")]
    [InlineData("{\"tool\":\"CHECK_GOLDBACH_FINITE\",\"conjectureId\":\"GOLDBACH-STRONG\",\"evenLimit\":\"100\"}")]
    [InlineData("{\"tool\":\"CHECK_GOLDBACH_FINITE\",\"conjectureId\":\"GOLDBACH-STRONG\",\"evenLimit\":100,\"leanSource\":\"sorry\"}")]
    [InlineData("{\"tool\":\"VERIFY_KNOWN_BENCHMARK\",\"problemId\":\"GOLDBACH-STRONG\",\"strategy\":\"omega\"}")]
    [InlineData("{\"tool\":\"VERIFY_KNOWN_BENCHMARK\",\"problemId\":\"NAT-ADD-COMM\",\"strategy\":\"sorry\"}")]
    [InlineData("{\"tool\":\"VERIFY_KNOWN_BENCHMARK\",\"problemId\":\"NAT-ADD-COMM\",\"strategy\":\"omega\",\"code\":\"#eval\"}")]
    [InlineData("{\"tool\":\"CHECK_GOLDBACH_FINITE\",\"tool\":\"SYSTEM_COMMAND\",\"conjectureId\":\"GOLDBACH-STRONG\",\"evenLimit\":4}")]
    public void HostileOrMalformedProposalsAreRejected(string proposal)
    {
        Assert.ThrowsAny<Exception>(() => ResearchAgentToolPolicy.ValidateProposal(proposal));
    }
}
