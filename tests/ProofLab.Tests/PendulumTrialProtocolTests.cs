using ProofLab.Science;

namespace ProofLab.Tests;

public sealed class PendulumTrialProtocolTests
{
    [Fact]
    public void Predict_FiftyToEightyCentimetres_PreregistersTwentySixPointFivePercentChange()
    {
        var prediction = PendulumTrialProtocol.Predict(0.5, 0.8);
        Assert.InRange(prediction.ExpectedBaselinePeriodSeconds, 1.418, 1.42);
        Assert.InRange(prediction.ExpectedFollowUpPeriodSeconds, 1.793, 1.795);
        Assert.InRange(prediction.PredictedIncreasePercent, 26.48, 26.50);
        Assert.Contains("T₂/T₁", prediction.Formula);
    }

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(0, 0.8)]
    [InlineData(0.5, double.NaN)]
    [InlineData(0.5, 6)]
    public void Predict_RejectsInvalidLengths(double baseline, double followUp)
    {
        Assert.Throws<ArgumentException>(() => PendulumTrialProtocol.Predict(baseline, followUp));
    }

    [Fact]
    public void Compare_NeverClaimsThatSyntheticInputIsPhysicalMeasurement()
    {
        var prediction = PendulumTrialProtocol.Predict(0.5, 0.8);
        var first = Series(0.5, 1.419, EvidenceOrigin.Simulated, "synthetic-baseline");
        var second = Series(0.8, 1.795, EvidenceOrigin.Simulated, "synthetic-follow-up");
        var result = PendulumTrialProtocol.Compare(prediction, first, second);

        Assert.InRange(result.ObservedIncreasePercent, 26.3, 26.7);
        Assert.Equal("SYNTHETIC_OR_MIXED_NOT_PHYSICAL_EVIDENCE", result.EvidenceClassification);
        Assert.Contains("NOT_A_CONFIRMED_DISCOVERY", result.ScientificConclusion);
        Assert.Contains("not necessarily data recording", result.Limitations);
    }

    [Fact]
    public void Compare_RejectsObservationsFromWrongLength()
    {
        var prediction = PendulumTrialProtocol.Predict(0.5, 0.8);
        Assert.Throws<ArgumentException>(() => PendulumTrialProtocol.Compare(
            prediction,
            Series(0.5, 1.42, EvidenceOrigin.ManuallyEntered, "a.csv"),
            Series(0.7, 1.8, EvidenceOrigin.ManuallyEntered, "b.csv")));
    }

    [Fact]
    public void Compare_RequiresTraceableSequenceReferences()
    {
        var prediction = PendulumTrialProtocol.Predict(0.5, 0.8);
        Assert.Throws<ArgumentException>(() => PendulumTrialProtocol.Compare(
            prediction,
            Series(0.5, 1.42, EvidenceOrigin.ManuallyEntered, ""),
            Series(0.8, 1.8, EvidenceOrigin.ManuallyEntered, "b.csv")));
    }

    [Fact]
    public void Compare_UserSuppliedTrackingStillRequiresIndependentVideoReview()
    {
        var prediction = PendulumTrialProtocol.Predict(0.5, 0.8);
        var result = PendulumTrialProtocol.Compare(
            prediction,
            Series(0.5, 1.42, EvidenceOrigin.TrackingCoordinates, "video1.csv"),
            Series(0.8, 1.8, EvidenceOrigin.TrackingCoordinates, "video2.csv"));
        Assert.Equal("USER_SUPPLIED_COORDINATES_NOT_INDEPENDENTLY_AUTHENTICATED",
            result.EvidenceClassification);
    }

    private static EvidenceRequest Series(
        double length, double period, EvidenceOrigin origin, string reference)
    {
        var points = Enumerable.Range(0, 1200).Select(i =>
        {
            var t = i / 60d;
            return new Observation(t, 50 * Math.Cos(2 * Math.PI * t / period));
        }).ToArray();
        return new EvidenceRequest(new ExperimentSetup(ExperimentKind.Pendulum, LengthMeters: length),
            points, origin, reference);
    }
}
