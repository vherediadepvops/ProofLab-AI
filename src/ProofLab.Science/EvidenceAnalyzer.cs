namespace ProofLab.Science;

public static class EvidenceAnalyzer
{
    public static ScientificResult Analyze(EvidenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var predicted = ScientificModels.Predict(request.Setup);
        var points = request.Observations ??
            throw new ArgumentException("Observations are required.");
        var minimum = request.Setup.Kind == ExperimentKind.Pendulum ? 120 : 16;

        if (points.Count < minimum || points.Count > 30000)
            throw new ArgumentException($"Expect between {minimum} and 30000 observations.");

        for (var i = 0; i < points.Count; i++)
        {
            if (!double.IsFinite(points[i].TimeSeconds) ||
                !double.IsFinite(points[i].Position) ||
                (i > 0 && points[i].TimeSeconds <= points[i - 1].TimeSeconds))
                throw new ArgumentException("Observations must be finite and strictly time-ordered.");
        }

        var observed = request.Setup.Kind == ExperimentKind.Pendulum
            ? PendulumPeriod(points) : RampAcceleration(points);

        var error = Math.Abs(observed - predicted.ExpectedValue) /
                    Math.Abs(predicted.ExpectedValue) * 100;
        var method = request.Setup.Kind == ExperimentKind.Pendulum
            ? "Independent upward midline crossings with hysteresis; median cycle interval."
            : "Quadratic least-squares fit to calibrated position (metres) versus time.";
        var statement = request.Origin switch
        {
            EvidenceOrigin.Simulated => "SYNTHETIC: generated observations, not physical evidence.",
            EvidenceOrigin.ManuallyEntered => "USER-SUPPLIED: source not independently verified.",
            EvidenceOrigin.TrackingCoordinates => "TRACKED COORDINATES: original video and model provenance not independently verified.",
            _ => throw new ArgumentException("Invalid evidence origin.")
        };
        var caveat = request.Setup.Kind == ExperimentKind.Pendulum
            ? "Camera movement, depth motion and angle introduce error; horizontal position may be pixels."
            : "Coordinates must be in metres along the ramp; rolling must occur without slipping.";

        return new(predicted.Kind.ToString(), predicted.Metric, predicted.Unit,
            predicted.ExpectedValue, observed, error, points.Count,
            method, request.Origin, statement, caveat);
    }

    private static double PendulumPeriod(IReadOnlyList<Observation> data)
    {
        var smoothed = new double[data.Count];
        for (var i = 0; i < data.Count; i++)
        {
            var start = Math.Max(0, i - 2);
            var end = Math.Min(data.Count - 1, i + 2);
            var sum = 0.0;
            for (var j = start; j <= end; j++) sum += data[j].Position;
            smoothed[i] = sum / (end - start + 1);
        }

        var low = smoothed.Min();
        var high = smoothed.Max();
        if (high - low < 0.01)
            throw new ArgumentException("Insufficient movement.");

        var center = (low + high) / 2;
        var resetLine = low + (high - low) * 0.25;
        var armed = false;
        var crossingTimes = new List<double>();

        for (var i = 1; i < smoothed.Length; i++)
        {
            if (smoothed[i] <= resetLine) armed = true;
            if (!armed || smoothed[i - 1] >= center || smoothed[i] < center)
                continue;

            var proportion = (center - smoothed[i - 1]) /
                             (smoothed[i] - smoothed[i - 1]);
            var crossing = data[i - 1].TimeSeconds +
                          proportion * (data[i].TimeSeconds - data[i - 1].TimeSeconds);
            crossingTimes.Add(crossing);
            armed = false;
        }

        if (crossingTimes.Count < 4)
            throw new ArgumentException("At least four rising crossings are required.");

        var intervals = new List<double>();
        for (var i = 1; i < crossingTimes.Count; i++)
            intervals.Add(crossingTimes[i] - crossingTimes[i - 1]);
        intervals.Sort();
        var middle = intervals.Count / 2;
        return intervals.Count % 2 == 0
            ? (intervals[middle - 1] + intervals[middle]) / 2
            : intervals[middle];
    }

    private static double RampAcceleration(IReadOnlyList<Observation> data)
    {
        // Least-squares x = c0 + c1*t + c2*t², observed a = 2*c2.
        var a = new double[3, 4];
        var t0 = data[0].TimeSeconds;
        foreach (var sample in data)
        {
            var t = sample.TimeSeconds - t0;
            var v = new[] { 1.0, t, t * t };
            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                    a[row, column] += v[row] * v[column];
                a[row, 3] += v[row] * sample.Position;
            }
        }

        for (var pivot = 0; pivot < 3; pivot++)
        {
            var best = pivot;
            for (var row = pivot + 1; row < 3; row++)
                if (Math.Abs(a[row, pivot]) > Math.Abs(a[best, pivot])) best = row;
            if (Math.Abs(a[best, pivot]) < 1e-10)
                throw new ArgumentException("Insufficient time variation.");
            for (var column = pivot; column < 4; column++)
                (a[best, column], a[pivot, column]) =
                    (a[pivot, column], a[best, column]);

            var factor = a[pivot, pivot];
            for (var column = pivot; column < 4; column++)
                a[pivot, column] /= factor;
            for (var row = 0; row < 3; row++)
            {
                if (row == pivot) continue;
                var multiplier = a[row, pivot];
                for (var column = pivot; column < 4; column++)
                    a[row, column] -= multiplier * a[pivot, column];
            }
        }

        var result = 2 * a[2, 3];
        if (!double.IsFinite(result))
            throw new ArgumentException("Could not estimate acceleration.");
        return result;
    }
}
