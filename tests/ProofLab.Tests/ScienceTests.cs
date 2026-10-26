using ProofLab.Science;

namespace ProofLab.Tests;

public sealed class ScienceTests
{
    [Fact]
    public void PendulumTheory_HalfMetrePeriodAround142Seconds()
    {
        var prediction = ScientificModels.Predict(
            new ExperimentSetup(ExperimentKind.Pendulum, LengthMeters: 0.5));
        Assert.InRange(prediction.ExpectedValue, 1.41, 1.43);
    }

    [Fact]
    public void RollingSolidSpherePrediction_UsesRotationalInertia()
    {
        var result = ScientificModels.Predict(
            new ExperimentSetup(ExperimentKind.RollingSphere, AngleDegrees: 20));
        Assert.InRange(result.ExpectedValue, 2.38, 2.41);
    }

    [Fact]
    public void PendulumAnalysis_InfersPeriodIndependentlyFromTheory()
    {
        const double actualPeriod = 1.8;
        var samples = Enumerable.Range(0, 1400).Select(i =>
        {
            var t = i / 60d;
            return new Observation(t, 100 * Math.Cos(2 * Math.PI * t / actualPeriod));
        }).ToArray();
        var result = EvidenceAnalyzer.Analyze(new EvidenceRequest(
            new ExperimentSetup(ExperimentKind.Pendulum, LengthMeters: 0.5),
            samples, EvidenceOrigin.Simulated));
        Assert.InRange(result.ObservedValue, 1.78, 1.82);
        Assert.True(result.DifferencePercent > 20);
        Assert.Contains("SYNTHETIC", result.EvidenceStatement);
    }

    [Fact]
    public void RollingSphereAnalysis_RecoversQuadraticAcceleration()
    {
        const double acceleration = 2.1;
        var samples = Enumerable.Range(0, 240).Select(i =>
        {
            var t = i / 120d;
            return new Observation(t, 0.04 + 0.16 * t + 0.5 * acceleration * t * t);
        }).ToArray();
        var result = EvidenceAnalyzer.Analyze(new EvidenceRequest(
            new ExperimentSetup(ExperimentKind.RollingSphere, AngleDegrees: 18),
            samples, EvidenceOrigin.ManuallyEntered));
        Assert.InRange(result.ObservedValue, 2.09999, 2.10001);
        Assert.Contains("USER-SUPPLIED", result.EvidenceStatement);
    }

    [Fact]
    public void Analysis_RejectsUnsortedTimestamps()
    {
        var samples = Enumerable.Range(0, 130).Select(i =>
            new Observation(i == 35 ? 0 : i / 60d, Math.Sin(i))).ToArray();
        Assert.Throws<ArgumentException>(() => EvidenceAnalyzer.Analyze(
            new EvidenceRequest(
                new ExperimentSetup(ExperimentKind.Pendulum, LengthMeters: 0.5),
                samples, EvidenceOrigin.Simulated)));
    }

    [Fact]
    public void Analysis_RejectsStationaryPendulum()
    {
        var samples = Enumerable.Range(0, 130).Select(i =>
            new Observation(i / 60d, 12)).ToArray();
        Assert.Throws<ArgumentException>(() => EvidenceAnalyzer.Analyze(
            new EvidenceRequest(
                new ExperimentSetup(ExperimentKind.Pendulum, LengthMeters: 0.5),
                samples, EvidenceOrigin.Simulated)));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    [InlineData(7.0)]
    public void Prediction_RejectsInvalidLengths(double length)
    {
        Assert.Throws<ArgumentException>(() => ScientificModels.Predict(
            new ExperimentSetup(ExperimentKind.Pendulum, LengthMeters: length)));
    }
}
