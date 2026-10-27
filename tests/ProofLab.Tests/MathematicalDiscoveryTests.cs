using ProofLab.Science;

namespace ProofLab.Tests;

public sealed class MathematicalDiscoveryTests
{
    [Fact]
    public void CatalogContainsOnlyClearlyLabeledKnownTheorems()
    {
        Assert.Equal(3, MathResearchCatalog.GetProblems().Count);
        Assert.All(MathResearchCatalog.GetProblems(), problem =>
            Assert.Equal("KNOWN_THEOREM", problem.ResearchStatus));
    }

    [Theory]
    [InlineData("NAT-ADD-ZERO", "rfl")]
    [InlineData("NAT-ADD-COMM", "omega")]
    [InlineData("NAT-ADD-BOUND", "omega")]
    public void FixedProblemAndWhitelistedTacticCanFormSource(
        string problemId, string strategy)
    {
        var problem = MathResearchCatalog.Find(problemId);
        var source = MathResearchCatalog.BuildLeanSource(problem, strategy);
        Assert.StartsWith("import Lean", source);
        Assert.Contains(problem.LeanDeclaration, source);
        Assert.EndsWith("  " + strategy + "\n", source);
        Assert.DoesNotContain("sorry", source);
        Assert.DoesNotContain("admit", source);
    }

    [Theory]
    [InlineData("omega\n#eval IO.FS.removeFile \"secret\"")]
    [InlineData("sorry")]
    [InlineData("rfl; admit")]
    [InlineData("axiom")]
    public void UnapprovedScriptIsNeverExecuted(string input)
    {
        var p = MathResearchCatalog.Find("NAT-ADD-ZERO");
        Assert.Throws<ArgumentException>(() =>
            MathResearchCatalog.BuildLeanSource(p, input));
    }

    [Fact]
    public void UnknownProblemRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            MathResearchCatalog.Find("MY-FAKE-THEOREM"));
    }

    [Fact]
    public void ProofSourceHasStableDigest()
    {
        var p = MathResearchCatalog.Find("NAT-ADD-COMM");
        var a = MathResearchCatalog.BuildLeanSource(p, "omega");
        var b = MathResearchCatalog.BuildLeanSource(p, "omega");
        Assert.Equal(MathResearchCatalog.Sha256(a), MathResearchCatalog.Sha256(b));
        Assert.Equal(64, MathResearchCatalog.Sha256(a).Length);
        Assert.NotEqual(MathResearchCatalog.Sha256(a),
            MathResearchCatalog.Sha256(MathResearchCatalog.BuildLeanSource(p, "rfl")));
    }
}
