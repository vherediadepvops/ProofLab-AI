namespace ProofLab.Science;

public sealed record LocalMotionTrack(
    IReadOnlyList<Observation> Observations,
    int FramesAnalyzed,
    int DetectedFrames,
    double DetectionRate,
    string Outcome,
    string Method,
    string Limitations);

/// <summary>
/// Experimental, deterministic background-median tracker for a stable-camera,
/// mostly static laboratory scene. It is NOT SAM 3.1; all derived coordinates
/// require review against the source video before scientific conclusions.
/// </summary>
public static class PendulumMotionTracker
{
    public static LocalMotionTrack Track(
        IReadOnlyList<byte[]> frames, int width, int height, double framesPerSecond)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (width < 80 || height < 80 || !double.IsFinite(framesPerSecond) ||
            framesPerSecond < 1 || frames.Count is < 24 or > 1000 ||
            frames.Any(x => x.Length != width * height))
            throw new ArgumentException("Invalid decoded video frames.");

        var background = EstimateBackground(frames, width * height);
        var points = new List<Observation>(frames.Count);
        var visited = new int[width * height];
        var queue = new int[width * height];
        var mask = new bool[width * height];
        var epoch = 0;
        (double X, double Y)? prior = null;
        const int contrast = 28;

        for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
        {
            var image = frames[frameIndex];
            for (var y = 0; y < height; y++)
            {
                var offset = y * width;
                for (var x = 0; x < width; x++)
                {
                    var i = offset + x;
                    mask[i] = y > height * 0.18 && y < height * 0.98 &&
                              x > width * .02 && x < width * .98 &&
                              Math.Abs(image[i] - background[i]) >= contrast;
                }
            }

            epoch++;
            double bestScore = double.NegativeInfinity;
            (double X, double Y)? best = null;
            for (var i = 0; i < mask.Length; i++)
            {
                if (!mask[i] || visited[i] == epoch) continue;
                int head = 0, tail = 0;
                visited[i] = epoch;
                queue[tail++] = i;
                int count = 0, xmin = width, xmax = 0, ymin = height, ymax = 0;
                double sumx = 0, sumy = 0;

                while (head < tail)
                {
                    var pos = queue[head++];
                    var py = pos / width; var px = pos - py * width;
                    count++; sumx += px; sumy += py;
                    xmin = Math.Min(xmin, px); xmax = Math.Max(xmax, px);
                    ymin = Math.Min(ymin, py); ymax = Math.Max(ymax, py);

                    var left = px > 0 ? pos - 1 : -1;
                    var right = px + 1 < width ? pos + 1 : -1;
                    var up = py > 0 ? pos - width : -1;
                    var down = py + 1 < height ? pos + width : -1;
                    if (left >= 0 && mask[left] && visited[left] != epoch)
                    { visited[left] = epoch; queue[tail++] = left; }
                    if (right >= 0 && mask[right] && visited[right] != epoch)
                    { visited[right] = epoch; queue[tail++] = right; }
                    if (up >= 0 && mask[up] && visited[up] != epoch)
                    { visited[up] = epoch; queue[tail++] = up; }
                    if (down >= 0 && mask[down] && visited[down] != epoch)
                    { visited[down] = epoch; queue[tail++] = down; }
                }

                var w = xmax - xmin + 1;
                var h = ymax - ymin + 1;
                if (count < 24 || count > 9000 || w < 4 || h < 5 ||
                    w > width * .48 || h > height * .5) continue;

                var density = (double)count / (w * h);
                if (density < .18 || (double)w / h is < .15 or > 4.5) continue;
                var cx = sumx / count;
                var cy = sumy / count;
                if (cy < height * .28) continue;
                var continuity = prior is null ? 0 : Math.Max(-25,
                    16 - .28 * Math.Sqrt(Math.Pow(cx - prior.Value.X, 2) +
                                           Math.Pow(cy - prior.Value.Y, 2)));
                var score = Math.Log(count) * 12 + density * 18 +
                            18 * cy / height + continuity;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = (cx, cy);
                }
            }

            if (best is { } found)
            {
                points.Add(new Observation(frameIndex / framesPerSecond, found.X));
                prior = found;
            }
        }

        var rate = (double)points.Count / frames.Count;
        var amplitude = points.Count == 0 ? 0 :
            points.Max(x => x.Position) - points.Min(x => x.Position);
        var outcome = rate >= .72 && points.Count >= 120 && amplitude >= 8
            ? "LOCAL_TRACKING_CANDIDATE_REQUIRES_VIDEO_REVIEW"
            : "NEEDS_MANUAL_REVIEW";
        return new LocalMotionTrack(points, frames.Count, points.Count,
            rate, outcome,
            "LOCAL_MEDIAN_BACKGROUND_SUBTRACTION_12_FPS_NOT_SAM",
            "Experimental heuristic can confuse hands, shadows or background motion with " +
            "the pendulum. Source video and bob identity are not independently reviewed. " +
            "Times are normalized by FFmpeg to a 12 fps sample grid, not original frame PTS.");
    }

    private static byte[] EstimateBackground(IReadOnlyList<byte[]> frames, int pixels)
    {
        const int sampleCount = 21;
        var background = new byte[pixels];
        Span<byte> values = stackalloc byte[sampleCount];
        for (var pixel = 0; pixel < pixels; pixel++)
        {
            for (var k = 0; k < sampleCount; k++)
            {
                var index = k * (frames.Count - 1) / (sampleCount - 1);
                values[k] = frames[index][pixel];
            }
            // K=21: insertion sort has predictable bounded runtime and stack use.
            for (var k = 1; k < sampleCount; k++)
            {
                var v = values[k];
                var m = k - 1;
                while (m >= 0 && values[m] > v) { values[m + 1] = values[m]; m--; }
                values[m + 1] = v;
            }
            background[pixel] = values[sampleCount / 2];
        }
        return background;
    }
}
