using ProofLab.Science;

namespace ProofLab.Tests;

public sealed class PendulumSpectralEstimatorTests
{
    [Theory]
    [InlineData(1.414, 2, 42)]
    [InlineData(1.800, 5, 16)]
    public void RecoversPeriodFromNoisyIrregularTrackedPositions_WithoutTheory(
        double actualPeriod, int frameSkipStride, int outlierStride)
    {
        const int count = 390;
        var samples = new List<Observation>();
        for (var i = 0; i < count; i++)
        {
            if (frameSkipStride > 1 && i % (frameSkipStride * 13) == 0) continue;
            var time = i / 12.0;
            var noise = 4 * Math.Sin(i * 13.3);
            var position = 220 + 95 * Math.Sin(time * Math.Tau / actualPeriod) + noise;
            if (time < 2) position = 50 + i; // The user released the bob.
            else if (i % outlierStride == 3) position += 125; // brief object swaps
            samples.Add(new Observation(time, position));
        }

        var outcome = PendulumSpectralEstimator.Estimate(samples, .85);
        Assert.Equal(PendulumSpectralEstimator.Candidate, outcome.Status);
        Assert.NotNull(outcome.PeriodSeconds);
        Assert.InRange(outcome.PeriodSeconds.Value,
            actualPeriod - .018, actualPeriod + .018);
        Assert.InRange(outcome.FitRSquared, .82, 1);
        Assert.True(outcome.HalfDifferencePercent < 4);
        Assert.Contains("NOT_SAM", outcome.Algorithm);
    }

    [Fact]
    public void StationaryInputCannotBeDeclaredPeriodic()
    {
        var samples = Enumerable.Range(0, 400)
            .Select(i => new Observation(i / 12.0, 120)).ToArray();
        var assessment = PendulumSpectralEstimator.Estimate(samples, .99);
        Assert.Null(assessment.PeriodSeconds);
        Assert.Equal(PendulumSpectralEstimator.Inconclusive, assessment.Status);
    }

    [Fact]
    public void InsufficientDetectionsCannotClaimObservedPeriod()
    {
        var samples = Enumerable.Range(0, 400)
            .Select(i => new Observation(i / 12.0,
                140 + 90 * Math.Sin(i / 12.0 * Math.Tau / 1.4))).ToArray();
        var result = PendulumSpectralEstimator.Estimate(samples, .40);
        Assert.Null(result.PeriodSeconds);
    }

    [Fact]
    public void NoiseCannotBeMistakenForPendulum()
    {
        var random = new Random(60421);
        var samples = Enumerable.Range(0, 400)
            .Select(i => new Observation(i / 12.0,
                140 + 110 * (random.NextDouble() - .5))).ToArray();
        var result = PendulumSpectralEstimator.Estimate(samples, .99);
        Assert.Null(result.PeriodSeconds);
        Assert.Equal(PendulumSpectralEstimator.Inconclusive, result.Status);
    }

    [Fact]
    public void UnsortedTimeMustFailClosed()
    {
        var samples = Enumerable.Range(0, 150)
            .Select(i => new Observation(i == 90 ? 0 : i / 12.0,
                Math.Sin(i))).ToArray();
        Assert.Throws<ArgumentException>(
            () => PendulumSpectralEstimator.Estimate(samples, .9));
    }
}
