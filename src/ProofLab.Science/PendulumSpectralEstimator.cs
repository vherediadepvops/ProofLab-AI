namespace ProofLab.Science;

public sealed record PendulumSpectralAssessment(
    string Algorithm,
    double? PeriodSeconds,
    double FitRSquared,
    double OscillationAmplitudePixels,
    double? FirstHalfPeriodSeconds,
    double? SecondHalfPeriodSeconds,
    double? HalfDifferencePercent,
    int ObservationsUsed,
    double ExcludedInitialSeconds,
    string Status,
    string Limitations);

/// <summary>
/// Independent signal-based period estimate from previously measured (t,x) positions.
/// NEVER takes the theoretical length or predicted period as input.
/// A sinusoidal fit can still follow the wrong moving object; therefore
/// a candidate does not establish authenticated scientific observation.
/// </summary>
public static class PendulumSpectralEstimator
{
    public const string Version = "LOCAL_ROBUST_SINUSOID_V1_NOT_SAM";
    public const string Candidate = "PERIOD_CANDIDATE_REQUIRES_MANUAL_BOB_VERIFICATION";
    public const string Inconclusive = "INCONCLUSIVE_REQUIRES_MANUAL_REVIEW";
    private const double ExcludeFirstSeconds = 2.0;

    public static PendulumSpectralAssessment Estimate(
        IReadOnlyList<Observation> observations, double detectedFraction)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (!double.IsFinite(detectedFraction) || detectedFraction is < 0 or > 1)
            throw new ArgumentException("Invalid frame-detection fraction.");

        if (observations.Count > 30000)
            throw new ArgumentException("Too many samples.");
        for (var i = 0; i < observations.Count; i++)
        {
            if (!double.IsFinite(observations[i].TimeSeconds) ||
                !double.IsFinite(observations[i].Position) ||
                (i > 0 && observations[i].TimeSeconds <= observations[i - 1].TimeSeconds))
                throw new ArgumentException("Coordinates must be finite and time-ordered.");
        }

        var initial = observations.Count > 0 ? observations[0].TimeSeconds : 0;
        var selected = observations
            .Where(x => x.TimeSeconds >= initial + ExcludeFirstSeconds).ToArray();

        PendulumSpectralAssessment Incomplete(int n, double fit = 0, double amp = 0,
            double? first = null, double? second = null, double? difference = null) =>
            new(Version, null, fit, amp, first, second, difference, n,
                ExcludeFirstSeconds, Inconclusive,
                "Period cannot be validated from this local trace. Missing or incorrect " +
                "blob detections, camera movement and temporal interpolation are possible. " +
                "SAM 3.1 was NOT used; no manual verification of bob identity.");

        if (selected.Length < 120 || detectedFraction < .70 ||
            selected[^1].TimeSeconds - selected[0].TimeSeconds < 8)
            return Incomplete(selected.Length);

        // A fixed median filter (5 samples) suppresses brief erroneous object
        // swaps. It is applied to all studies, not adapted to their length.
        var smoothed = new SignalSample[selected.Length];
        Span<double> window = stackalloc double[5];
        for (var i = 0; i < selected.Length; i++)
        {
            var low = Math.Max(0, i - 2);
            var high = Math.Min(selected.Length - 1, i + 2);
            var count = high - low + 1;
            for (var j = 0; j < count; j++)
                window[j] = selected[low + j].Position;
            window[..count].Sort();
            smoothed[i] = new SignalSample(selected[i].TimeSeconds,
                window[count / 2]);
        }

        // A predetermined 8th–92nd percentile Winsorization handles occasional
        // tracking jumps without importing the theoretically expected period.
        var positions = smoothed.Select(x => x.Position).ToArray();
        Array.Sort(positions);
        var floor = positions[(int)Math.Floor((positions.Length - 1) * .08)];
        var ceiling = positions[(int)Math.Floor((positions.Length - 1) * .92)];
        if (ceiling - floor < 20) return Incomplete(smoothed.Length);
        for (var i = 0; i < smoothed.Length; i++)
            smoothed[i] = smoothed[i] with
            {
                Position = Math.Clamp(smoothed[i].Position, floor, ceiling)
            };

        var all = FindPeriod(smoothed, .65, 4.8);
        if (all is null) return Incomplete(smoothed.Length);
        var midpoint = (smoothed[0].Time + smoothed[^1].Time) / 2;
        var firstPart = smoothed.Where(x => x.Time <= midpoint).ToArray();
        var secondPart = smoothed.Where(x => x.Time > midpoint).ToArray();
        var firstFit = firstPart.Length > 50
            ? FindPeriod(firstPart, Math.Max(.65, all.Value.Period - .12),
                Math.Min(4.8, all.Value.Period + .12))
            : null;
        var secondFit = secondPart.Length > 50
            ? FindPeriod(secondPart, Math.Max(.65, all.Value.Period - .12),
                Math.Min(4.8, all.Value.Period + .12))
            : null;

        double? difference = firstFit.HasValue && secondFit.HasValue
            ? Math.Abs(firstFit.Value.Period - secondFit.Value.Period)
              / all.Value.Period * 100 : null;

        // Basic quality checks are source-blind. Strong periodicity in a
        // wrong object is possible: manual verification is always mandatory.
        var span = smoothed[^1].Time - smoothed[0].Time;
        var acceptable = all.Value.RSquared >= .82 &&
            all.Value.AmplitudePixels >= 12 &&
            span >= all.Value.Period * 5 &&
            firstFit is { RSquared: >= .65 } &&
            secondFit is { RSquared: >= .65 } &&
            difference is <= 4;
        return new(Version,
            acceptable ? all.Value.Period : null,
            all.Value.RSquared, all.Value.AmplitudePixels,
            firstFit?.Period, secondFit?.Period, difference,
            smoothed.Length, ExcludeFirstSeconds,
            acceptable ? Candidate : Inconclusive,
            "Local position data only; no SAM segmentation. The first two seconds " +
            "are excluded by a fixed rule. Five-point median smoothing, percentile " +
            "clipping, and sinusoidal fitting may conceal systematic tracking errors. " +
            "No pixel-level bob identity or actual source video timestamps were verified.");
    }

    private readonly record struct SignalSample(double Time, double Position);
    private readonly record struct Fit(double Period, double RSquared,
        double AmplitudePixels);

    private static Fit? FindPeriod(
        IReadOnlyList<SignalSample> samples, double minimum, double maximum)
    {
        var coarse = Search(samples, minimum, maximum, .004);
        if (coarse is null) return null;
        var refined = Search(samples,
            Math.Max(minimum, coarse.Value.Period - .012),
            Math.Min(maximum, coarse.Value.Period + .012), .0004);
        return refined ?? coarse;
    }

    private static Fit? Search(
        IReadOnlyList<SignalSample> data, double minimum, double maximum, double step)
    {
        if (data.Count < 50 || minimum >= maximum) return null;
        double mean = 0, totalVariance = 0;
        for (var i = 0; i < data.Count; i++) mean += data[i].Position;
        mean /= data.Count;
        for (var i = 0; i < data.Count; i++)
            totalVariance += Math.Pow(data[i].Position - mean, 2);
        if (totalVariance < 1e-8) return null;

        Fit? best = null;
        for (var period = minimum; period <= maximum + step / 10; period += step)
        {
            double sumS = 0, sumC = 0, ss = 0, cc = 0, sc = 0, sy = 0, cy = 0;
            foreach (var p in data)
            {
                var rad = 2 * Math.PI * p.Time / period;
                var sine = Math.Sin(rad);
                var cosine = Math.Cos(rad);
                var centered = p.Position - mean;
                sumS += sine; sumC += cosine;
                ss += sine * sine; cc += cosine * cosine; sc += sine * cosine;
                sy += centered * sine; cy += centered * cosine;
            }
            ss -= sumS * sumS / data.Count;
            cc -= sumC * sumC / data.Count;
            sc -= sumS * sumC / data.Count;
            var determinant = ss * cc - sc * sc;
            if (determinant < 1e-9) continue;
            var amplitudeSine = (sy * cc - cy * sc) / determinant;
            var amplitudeCosine = (cy * ss - sy * sc) / determinant;
            var explained = (amplitudeSine * sy + amplitudeCosine * cy)
                            / totalVariance;
            if (!double.IsFinite(explained)) continue;
            var rSquared = Math.Clamp(explained, 0, 1);
            if (best is null || rSquared > best.Value.RSquared)
                best = new Fit(period, rSquared,
                    Math.Sqrt(amplitudeSine * amplitudeSine +
                              amplitudeCosine * amplitudeCosine));
        }
        return best;
    }
}
