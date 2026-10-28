using ProofLab.Science;

namespace ProofLab.Tests;

public sealed class PendulumMathematicalModelTests
{
    [Fact]
    public void PredictsFiniteAmplitudeAndThreeConservedEnergyTerms()
    {
        var result = PendulumMathematicalModel.Predict(
            new PendulumMathematicalRequest(.5, 15, 300));
        Assert.Equal(PendulumMathematicalModel.Provenance, result.Source);
        Assert.InRange(result.Baseline.SmallAnglePeriodSeconds, 1.418, 1.420);
        Assert.True(result.Baseline.NonlinearPeriodSeconds >
                    result.Baseline.SmallAnglePeriodSeconds);
        Assert.InRange(result.Baseline.NonlinearPeriodSeconds, 1.423, 1.427);
        Assert.Equal(501, result.Baseline.Samples.Count);
        Assert.Null(result.FollowUp);
        Assert.Null(result.PredictedPeriodIncreasePercent);
        var first = result.Baseline.Samples[0];
        Assert.InRange(first.TimeSeconds, 0, 0);
        Assert.InRange(first.AngleDegrees, 14.9999999, 15.0000001);
        Assert.Equal(0, first.KineticJoules);
        Assert.InRange(first.HorizontalCentimeters, 12.93, 12.95);
        Assert.InRange(first.TotalJoules, .049, .051);
        Assert.True(result.Baseline.MaximumRelativeEnergyDriftPercent < .005);
        foreach (var pt in result.Baseline.Samples)
            Assert.InRange(Math.Abs(pt.TotalJoules - first.TotalJoules),
                0, .00001);
    }

    [Fact]
    public void BobMassChangesEnergyButDoesNotChangeIdealPeriod()
    {
        var a = PendulumMathematicalModel.Predict(
            new PendulumMathematicalRequest(.5, 20, 100));
        var b = PendulumMathematicalModel.Predict(
            new PendulumMathematicalRequest(.5, 20, 500));
        Assert.Equal(a.Baseline.NonlinearPeriodSeconds,
            b.Baseline.NonlinearPeriodSeconds);
        Assert.InRange(b.Baseline.InitialPotentialJoules /
                       a.Baseline.InitialPotentialJoules,4.99999,5.00001);
    }

    [Fact]
    public void LargerAngleLengthensPeriodRelativeToSmallAngle()
    {
        var small=PendulumMathematicalModel.Predict(
            new PendulumMathematicalRequest(.5, 5, 300));
        var large=PendulumMathematicalModel.Predict(
            new PendulumMathematicalRequest(.5, 70, 300));
        Assert.True(large.Baseline.NonlinearPeriodSeconds >
                    small.Baseline.NonlinearPeriodSeconds);
        Assert.True(large.Baseline.NonlinearPeriodSeconds >
                    large.Baseline.SmallAnglePeriodSeconds * 1.08);
    }

    [Fact]
    public void TwoLengthsRemainIndependentWithNonlinearAngles()
    {
        var both=PendulumMathematicalModel.Predict(
            new PendulumMathematicalRequest(.5,15,300,.8));
        Assert.NotNull(both.FollowUp);
        Assert.InRange(both.PredictedPeriodIncreasePercent!.Value,26.48,26.50);
        Assert.InRange(both.FollowUp.NonlinearPeriodSeconds,1.79,1.83);
        Assert.Equal(501,both.FollowUp.Samples.Count);
    }

    [Theory]
    [InlineData(.05,15,300)]
    [InlineData(.5,0,300)]
    [InlineData(.5,90,300)]
    [InlineData(.5,15,0)]
    [InlineData(double.NaN,15,300)]
    [InlineData(.5,15,double.PositiveInfinity)]
    public void InvalidPhysicalParametersAreRejected(double l,double theta,double mass) =>
        Assert.Throws<ArgumentException>(() => PendulumMathematicalModel.Predict(
            new PendulumMathematicalRequest(l,theta,mass)));

    [Fact]
    public void DuplicateFollowUpLengthIsRejected() =>
        Assert.Throws<ArgumentException>(() => PendulumMathematicalModel.Predict(
            new PendulumMathematicalRequest(.5,15,300,.5)));

    [Fact]
    public void ObservedDataDoesNotEnterTheModel()
    {
        var model=PendulumMathematicalModel.Predict(
            new PendulumMathematicalRequest(.5,15,300,.8));
        Assert.Contains("NOT_META",model.Source);
        Assert.DoesNotContain("Observed",model.Equation);
        Assert.Contains("no air resistance",model.Assumptions);
    }
}
