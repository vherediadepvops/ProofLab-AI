namespace ProofLab.Science;

/// <summary>
/// Deterministic geometric estimates derived ONLY from recorded 2D pixel
/// candidates and a separately chosen pivot. Neither predicted oscillation
/// periods nor model-generated trajectories enter this estimator.
/// </summary>
public sealed record PendulumPivotInput(
    double PivotX, double PivotY,
    double DeclaredLengthMeters, bool FrontalCameraAssumptionConfirmed);

public sealed record PendulumKinematicSample(
    double TimeSeconds,
    double BobXpx, double BobYpx,
    double AngleDegrees,
    double? AngularVelocityRadiansPerSecond,
    double? AngularAccelerationRadiansPerSecondSquared,
    double? ApparentTangentialSpeedPixelsPerSecond,
    double? TangentialSpeedMetersPerSecond,
    double? TangentialAccelerationMetersPerSecondSquared,
    double? CentripetalAccelerationMetersPerSecondSquared,
    bool IsValid);

public sealed record PendulumKinematicsResult(
    string Method,
    string Source,
    double PivotXpx, double PivotYpx,
    int SourceWidth, int SourceHeight,
    double MedianRadiusPixels,
    double RadiusMedianAbsoluteDeviationPercent,
    double? ObservedMaximumAngularSpeedRadPerSec,
    double? ObservedCenterAngularSpeedRadPerSec,
    double? ObservedExtremeAngularSpeedRadPerSec,
    double? TheoreticalMaxAngularSpeedRadPerSec,
    bool PhysicalUnitsEnabled,
    string QualityStatus,
    int SourcePoints, int DerivativePoints,
    string LimitationsEs, string LimitationsEn,
    IReadOnlyList<PendulumKinematicSample> Samples);

public static class PendulumVideoKinematics
{
    public const string Version = "LOCAL_2D_PIVOT_FINITE_DIFFERENCE_V1_NOT_SAM";
    public const int SourceWidth = 480;
    public const int SourceHeight = 270;
    private const double MaxGapSeconds = .22;
    private const double RadiusToleranceFraction = .14;

    public static PendulumKinematicsResult Analyze(
        IReadOnlyList<VideoPixelMotionSample> points,
        PendulumPivotInput calibration,
        double initialAngleDegrees = 15)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(calibration);
        if (!double.IsFinite(calibration.PivotX) ||
            !double.IsFinite(calibration.PivotY) ||
            calibration.PivotX < 0 || calibration.PivotX > SourceWidth ||
            calibration.PivotY < 0 || calibration.PivotY > SourceHeight)
            throw new ArgumentException("Pivot must lie inside the 480x270 tracked frame.");
        if (!double.IsFinite(calibration.DeclaredLengthMeters) ||
            calibration.DeclaredLengthMeters is < .1 or > 5)
            throw new ArgumentException("Declared length is outside safe model bounds.");
        if (!double.IsFinite(initialAngleDegrees) || initialAngleDegrees is < 1 or > 85)
            throw new ArgumentException("Initial angle must be 1 to 85 degrees.");
        if (points.Count > 2000)
            throw new ArgumentException("Too many source points.");
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (!double.IsFinite(p.TimeSeconds) || p.TimeSeconds < 0 ||
                !double.IsFinite(p.X) || !double.IsFinite(p.Y) ||
                p.X < 0 || p.X >= SourceWidth || p.Y < 0 || p.Y >= SourceHeight ||
                (i > 0 && p.TimeSeconds <= points[i - 1].TimeSeconds))
                throw new ArgumentException("Pixel samples must be finite, within frame and time ordered.");
        }

        static double Median(IEnumerable<double> values)
        {
            var array = values.OrderBy(x => x).ToArray();
            if (array.Length == 0) return 0;
            return array.Length % 2 == 1
                ? array[array.Length / 2]
                : (array[array.Length / 2 - 1] + array[array.Length / 2]) / 2;
        }
        var radius = points.Select(p => double.Hypot(
            p.X - calibration.PivotX, p.Y - calibration.PivotY)).ToArray();
        var rMedian = Median(radius);
        var radiusDeviation = rMedian > 1
            ? Median(radius.Select(x => Math.Abs(x - rMedian))) / rMedian * 100 : 100;
        var angles = points.Select(p => Math.Atan2(
            p.X - calibration.PivotX, p.Y - calibration.PivotY)).ToArray();
        // Circle-radius coherence is required for physical length calibration.
        // A user checkbox alone cannot make a poor track a physical measurement.
        var enableMetres = calibration.FrontalCameraAssumptionConfirmed &&
            rMedian >= 25 && radiusDeviation <= 7.5 &&
            points.Count >= 40;
        var theta = new double[points.Count];
        var pointValid = new bool[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            var n0 = Math.Max(0, i - 2);
            var n1 = Math.Min(points.Count - 1, i + 2);
            // Do not bias release angle by taking a one-sided median at the
            // temporal boundary. Full five-point smoothing starts at index 2.
            // Derivatives are deliberately unavailable at these edge samples.
            theta[i] = (i < 2 || i >= points.Count - 2)
                ? angles[i]
                : Median(angles.Skip(n0).Take(n1 - n0 + 1));
            // Reject obviously wrong blobs, not just mathematically invalid data.
            pointValid[i] = rMedian >= 20 &&
                Math.Abs(radius[i] - rMedian) <= Math.Max(12, rMedian * RadiusToleranceFraction) &&
                Math.Abs(angles[i]) <= Math.PI / 2;
        }

        var samples = new PendulumKinematicSample[points.Count];
        var validOmega = new List<double>();
        var validAtCenter = new List<double>();
        var validAtExtreme = new List<double>();
        var maxAngle = pointValid.Where(x => x).Any()
            ? theta.Where((_, i) => pointValid[i]).Select(Math.Abs).DefaultIfEmpty(0).Max()
            : 0;
        int validCount = 0;
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            double? omega = null, alpha = null, pxPerSec = null;
            double? metresPerSec = null, tangentAcc = null, radialAcc = null;
            // Quadratic least-squares fit within +/- 2 samples. Fail closed
            // across missing samples, tracking swaps or timestamp jumps.
            if (i >= 2 && i + 2 < points.Count &&
                Enumerable.Range(i - 2, 5).All(j => pointValid[j]) &&
                Enumerable.Range(i - 2, 4).All(j =>
                    points[j + 1].TimeSeconds - points[j].TimeSeconds <= MaxGapSeconds &&
                    Math.Abs(theta[j + 1] - theta[j]) < .48))
            {
                var fit = FitQuadratic(points, theta, i);
                if (fit is { } f &&
                    Math.Abs(f.Omega) <= 8 &&
                    Math.Abs(f.Alpha) <= 75 &&
                    double.IsFinite(f.Omega) && double.IsFinite(f.Alpha))
                {
                    omega = f.Omega;
                    alpha = f.Alpha;
                    pxPerSec = Math.Abs(f.Omega) * rMedian;
                    if (enableMetres)
                    {
                        metresPerSec = Math.Abs(f.Omega) * calibration.DeclaredLengthMeters;
                        tangentAcc = f.Alpha * calibration.DeclaredLengthMeters;
                        radialAcc = f.Omega * f.Omega * calibration.DeclaredLengthMeters;
                    }
                    validCount++;
                    var speed = Math.Abs(f.Omega);
                    validOmega.Add(speed);
                    if (Math.Abs(theta[i]) <= Math.Max(.025, maxAngle * .25))
                        validAtCenter.Add(speed);
                    if (maxAngle >= .04 && Math.Abs(theta[i]) >= maxAngle * .90)
                        validAtExtreme.Add(speed);
                }
            }
            samples[i] = new PendulumKinematicSample(
                p.TimeSeconds, p.X, p.Y, theta[i] * 180 / Math.PI,
                omega, alpha, pxPerSec, metresPerSec, tangentAcc, radialAcc,
                omega.HasValue && pointValid[i]);
        }
        var quality = validCount < 35 || rMedian < 20
            ? "INCONCLUSIVE_PIVOT_OR_TRACK_REVIEW_REQUIRED"
            : radiusDeviation > 10
            ? "LOCAL_ANGLE_CANDIDATE_RADIUS_INCONSISTENT"
            : "LOCAL_2D_CANDIDATE_REQUIRES_VISUAL_REVIEW";

        // This is from a separate ideal mathematical model and is explicitly
        // NOT used for fitting or correcting ANY of the observed values above.
        var theta0 = initialAngleDegrees * Math.PI / 180;
        var theoreticalOmegaAtBottom = Math.Sqrt(
            2 * ScientificModels.StandardGravity /
            calibration.DeclaredLengthMeters * (1 - Math.Cos(theta0)));
        return new PendulumKinematicsResult(
            Version,
            "FFMPEG_BACKGROUND_PIXEL_CANDIDATES_USER_PIVOT_NOT_INDEPENDENT",
            calibration.PivotX, calibration.PivotY,
            SourceWidth, SourceHeight, rMedian, radiusDeviation,
            validOmega.Count > 0 ? validOmega.Max() : null,
            validAtCenter.Count > 0 ? Median(validAtCenter) : null,
            validAtExtreme.Count > 0 ? Median(validAtExtreme) : null,
            theoreticalOmegaAtBottom,
            enableMetres, quality, points.Count, validCount,
            "Pivote elegido manualmente, centro X/Y de sustraccion local de fondo, NO SAM. " +
            "Omega y alfa provienen de ajuste polinomial local de angulos de imagen. " +
            "Valores m/s o m/s², si habilitados, son aproximaciones que usan la longitud declarada " +
            "y una suposicion humana de camara frontal; NO hay calibracion metrologica independiente. " +
            "El tiempo es una rejilla FFmpeg de 12 FPS, no PTS nativo. " +
            "No se fuerza velocidad cero en extremos ni maxima en el centro.",
            "Pivot manually selected; X/Y centers from local background subtraction, NOT SAM. " +
            "Omega/alpha are local polynomial estimates of image-space angles. " +
            "m/s or m/s², if enabled, use declared length and a user-assumed frontal camera; " +
            "no independent metric calibration. FFmpeg timestamps form a 12FPS grid, not native PTS. " +
            "Zero speed at endpoints and maximum at center are NOT forced on observations.",
            samples);
    }

    // Estimate theta(t) = a + omega*t + (alpha/2)*t² from five observed
    // points using least-squares and pivoted elimination.
    private static (double Omega, double Alpha)? FitQuadratic(
        IReadOnlyList<VideoPixelMotionSample> points, double[] theta, int center)
    {
        var s0 = 0d; var s1 = 0d; var s2 = 0d;
        var s3 = 0d; var s4 = 0d;
        var b0 = 0d; var b1 = 0d; var b2 = 0d;
        for (int j = center - 2; j <= center + 2; j++)
        {
            var t = points[j].TimeSeconds - points[center].TimeSeconds;
            var t2 = t * t;
            var value = theta[j];
            s0 += 1; s1 += t; s2 += t2; s3 += t2*t; s4 += t2*t2;
            b0 += value; b1 += value*t; b2 += value*t2;
        }
        double[,] a = { { s0, s1, s2, b0 }, { s1, s2, s3, b1 },
                        { s2, s3, s4, b2 } };
        for (int k = 0; k < 3; k++)
        {
            var pivot = k;
            for (int i = k + 1; i < 3; i++)
                if (Math.Abs(a[i, k]) > Math.Abs(a[pivot, k])) pivot = i;
            if (Math.Abs(a[pivot, k]) < 1e-12) return null;
            if (pivot != k)
                for (int c = k; c < 4; c++)
                    (a[k, c], a[pivot, c]) = (a[pivot, c], a[k, c]);
            var divisor = a[k, k];
            for (int c = k; c < 4; c++) a[k, c] /= divisor;
            for (int r = 0; r < 3; r++)
            {
                if (r == k) continue;
                var factor = a[r, k];
                for (int c = k; c < 4; c++) a[r, c] -= factor * a[k, c];
            }
        }
        return (a[1,3], 2*a[2,3]);
    }
}
