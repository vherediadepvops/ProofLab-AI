using ProofLab.Science;

namespace ProofLab.Tests;

public sealed class PendulumDirectorProtocolTests
{
    [Fact]
    public void LocalFallbackIsExplicitlyNotMeta()
    {
        Assert.Contains("NOT_META_AI", PendulumDirectorProtocol.Source);
        Assert.Contains("NOT_SAM", PendulumDirectorProtocol.Source);
    }

    [Fact]
    public void FiftyCentimetresRecommendsEightyCentimetresAsKnownControl()
    {
        var predicted = PendulumDirectorProtocol.RecommendLocalControl(.5);
        Assert.Equal(.5, predicted.BaselineLengthMeters);
        Assert.Equal(.8, predicted.FollowUpLengthMeters, precision: 8);
        Assert.InRange(predicted.PredictedIncreasePercent,26.48,26.5);
    }

    [Fact]
    public void HigherLengthUsesSafeBoundedFallback()
    {
        var proposed = PendulumDirectorProtocol.RecommendLocalControl(4.9);
        Assert.InRange(proposed.FollowUpLengthMeters,3.4,3.45);
        Assert.NotEqual(proposed.BaselineLengthMeters,proposed.FollowUpLengthMeters);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(6)]
    public void InvalidBaselineCannotBeUsed(double length) =>
        Assert.Throws<ArgumentException>(() =>
            PendulumDirectorProtocol.RecommendLocalControl(length));

    [Fact]
    public void AFixedLocalProposalDoesNotUseMeasuredPeriodToChooseLength()
    {
        var first=PendulumDirectorProtocol.RecommendLocalControl(.5);
        var compare=PendulumDirectorProtocol.Compare(first,1.41693,1.79853);
        Assert.InRange(compare.ObservedChangePercent,26.92,26.95);
        Assert.InRange(compare.GapPercentagePoints,.42,.46);
    }

    [Theory]
    [InlineData(-1,1.5)]
    [InlineData(double.PositiveInfinity,1.5)]
    [InlineData(1.5,double.NaN)]
    public void InvalidObservationCannotBePresentedAsComparison(double a,double b) =>
        Assert.Throws<ArgumentException>(() =>
            PendulumDirectorProtocol.Compare(
                PendulumDirectorProtocol.RecommendLocalControl(.5),a,b));
}
