using ProofLab.Science;

namespace ProofLab.Tests;

public sealed class LeanLocalizationTests
{
    private static MathVerificationResult Result(string outcome, string tactic) =>
        new("test-id", "NAT-ADD-COMM", tactic, outcome,
            "Lean 4 kernel", new string('a', 64),
            DateTimeOffset.UtcNow, "Tactic 'rfl' failed: output from compiler.",
            IsNewDiscovery: false);

    [Fact]
    public void StatusCodesRemainStableWhileOutputLabelsChange()
    {
        var result = Result("VERIFIED", "omega");
        Assert.Equal("VERIFIED", result.Outcome);
        Assert.Equal("VERIFICADO", LeanResultLanguage.Outcome(result.Outcome, true));
        Assert.Equal("VERIFIED", LeanResultLanguage.Outcome(result.Outcome, false));
        Assert.Equal(new string('a', 64), result.SourceSha256);
    }

    [Fact]
    public void RejectedRflIsExplainedWithoutClaimingTheoremDisproof()
    {
        var result = Result("REJECTED", "rfl");
        var spanish = LeanResultLanguage.Explain(result, true);
        var english = LeanResultLanguage.Explain(result, false);
        Assert.Contains("no son iguales por definición", spanish);
        Assert.Contains("tampoco lo refuta", spanish);
        Assert.Contains("not equal by definition", english);
        Assert.DoesNotContain("Tactic 'rfl' failed", spanish);
        Assert.Equal("Tactic 'rfl' failed: output from compiler.", result.Diagnostic);
    }

    [Fact]
    public void VerifiedKnownTheoremIsNeverPresentedAsNovelDiscovery()
    {
        var result = Result("VERIFIED", "omega");
        Assert.Contains("teorema ya conocido", LeanResultLanguage.Explain(result, true));
        Assert.Contains("previously known theorem", LeanResultLanguage.Explain(result, false));
    }

    [Fact]
    public void SimpFailureAndToolTimeoutAreLocalized()
    {
        Assert.Contains("simplificación",
            LeanResultLanguage.Explain(Result("REJECTED", "simp"), true));
        Assert.Contains("Se agotó el tiempo",
            LeanResultLanguage.Explain(Result("TIMED_OUT", "omega"), true));
        Assert.Equal("NO DISPONIBLE", LeanResultLanguage.Outcome("UNAVAILABLE", true));
    }
}
