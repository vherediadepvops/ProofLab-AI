using ProofLab.Science;

namespace ProofLab.Tests;

public sealed class OpenConjectureTests
{
    [Fact]
    public void GoldbachIsOpenWithDatedLiteratureAndNotAnExecutableLeanGoal()
    {
        var problem = OpenConjectureCatalog.Find("GOLDBACH-STRONG");
        Assert.Equal("OPEN_RESEARCH", problem.ResearchStatus);
        Assert.Equal("2026-10-27", problem.StatusCheckedOn);
        Assert.StartsWith("https://", problem.StatusSourceUrl);
        Assert.Contains("10.1090/", problem.LiteratureDoi);
        Assert.False(problem.IsExecutableLeanGoal);
        Assert.False(OpenConjectureCatalog.CanExecuteInHost(problem.Id));
        Assert.Contains("NOT_FORMALIZED", problem.FormalizationStatus);
    }

    [Fact]
    public void OpenConjecturesStaySeparateFromKnownLeanBenchmarks()
    {
        Assert.Equal(3, MathResearchCatalog.GetProblems().Count);
        Assert.Throws<ArgumentException>(() => MathResearchCatalog.Find("GOLDBACH-STRONG"));
        Assert.Throws<ArgumentException>(() => OpenConjectureCatalog.Find("MADE-UP-PROBLEM"));
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(20, 9)]
    [InlineData(100, 49)]
    [InlineData(2000, 999)]
    public void FiniteGoldbachCheckFindsWitnessesWithoutDeclaringFormalProof(
        int evenLimit, int expectedCases)
    {
        var result = GoldbachFiniteExperiment.Run(evenLimit);
        Assert.Equal(expectedCases, result.EvenCasesExamined);
        Assert.Equal(expectedCases, result.CasesWithPrimePair);
        Assert.Null(result.FirstCaseWithoutPair);
        Assert.Equal("NO_COUNTEREXAMPLE_IN_FINITE_RANGE", result.Outcome);
        Assert.False(result.IsNewDiscovery);
        Assert.False(result.FormalProofVerified);
        Assert.Equal(64, result.InputSha256.Length);
        Assert.Equal(64, result.ResultSha256.Length);
        Assert.All(result.WitnessSamples, w =>
        {
            Assert.Equal(w.EvenNumber, w.FirstPrime + w.SecondPrime);
            Assert.True(IsPrime(w.FirstPrime));
            Assert.True(IsPrime(w.SecondPrime));
        });
    }

    [Fact]
    public void RepeatSameRangeIsReproducibleWithoutSharingAttemptId()
    {
        var first = GoldbachFiniteExperiment.Run(100);
        var second = GoldbachFiniteExperiment.Run(100);
        Assert.Equal(first.InputSha256, second.InputSha256);
        Assert.Equal(first.ResultSha256, second.ResultSha256);
        Assert.NotEqual(first.ExperimentId, second.ExperimentId);
        Assert.NotEqual(GoldbachFiniteExperiment.Run(200).ResultSha256, second.ResultSha256);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(2002)]
    public void InvalidOrUnboundedRequestsAreRejected(int limit) =>
        Assert.Throws<ArgumentException>(() => GoldbachFiniteExperiment.Run(limit));

    private static bool IsPrime(int n)
    {
        if (n < 2) return false;
        for (var i = 2; i <= n / i; i++) if (n % i == 0) return false;
        return true;
    }
}
