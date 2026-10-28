using ProofLab.Science;

namespace ProofLab.Tests;

public sealed class VideoClipPolicyTests
{
    [Theory]
    [InlineData("IMG_2925.MOV", 47_000_000L)]
    [InlineData("Galaxy_01.mp4", 85_000_000L)]
    [InlineData("browser.webm", 10_000L)]
    [InlineData("phone.3gp", 100_000L)]
    public void CommonPhoneContainers_Accepted(string file, long bytes) =>
        Assert.StartsWith(".", VideoClipPolicy.CheckNameAndSize(file, bytes));

    [Theory]
    [InlineData("bad.exe")]
    [InlineData("relative\\video.mov")]
    [InlineData("invalid.mp4/../x.mov")]
    public void UnsafeFilenames_AreRejected(string file) =>
        Assert.Throws<ArgumentException>(() => VideoClipPolicy.CheckNameAndSize(file, 50000));

    [Fact]
    public void OversizedVideo_IsRejected() =>
        Assert.Throws<ArgumentException>(() =>
            VideoClipPolicy.CheckNameAndSize("large.MOV", VideoClipPolicy.MaximumBytes + 1));

    [Theory]
    [InlineData("hevc")]
    [InlineData("h264")]
    [InlineData("av1")]
    [InlineData("vp9")]
    public void KnownVideoCodecs_Accepted(string codec) =>
        VideoClipPolicy.CheckVideoStream(codec, 1920, 1080, 31.2);

    [Fact]
    public void RejectsAudioOnlyAndUnsupportedCodecs() =>
        Assert.Throws<ArgumentException>(() =>
            VideoClipPolicy.CheckVideoStream("aac", 1920, 1080, 31.2));

    [Fact]
    public void RejectsExcessiveDuration() =>
        Assert.Throws<ArgumentException>(() =>
            VideoClipPolicy.CheckVideoStream("hevc", 1920, 1080, 120));
}

public sealed class PendulumMotionTrackerTests
{
    [Fact]
    public void LocalTracker_TracksSyntheticMovingBob_WithoutClaimingSam()
    {
        const int width = 160, height = 120, frames = 240;
        var data = new List<byte[]>(frames);
        for (var i = 0; i < frames; i++)
        {
            var image = new byte[width * height];
            var t = i / 12d;
            var centerX = 80 + (int)Math.Round(27 * Math.Cos(2 * Math.PI * t / 1.5));
            for (var y = 72; y < 120; y++)
                for (var x = 0; x < width; x++)
                    if (Math.Pow(x - centerX, 2) + Math.Pow(y - 86, 2) < 100)
                        image[y * width + x] = 235;
            data.Add(image);
        }

        var track = PendulumMotionTracker.Track(data, width, height, 12);
        Assert.Equal("LOCAL_TRACKING_CANDIDATE_REQUIRES_VIDEO_REVIEW", track.Outcome);
        Assert.True(track.DetectionRate > 0.85);
        Assert.DoesNotContain("SAM 3.1", track.Method);
        Assert.NotNull(track.PixelSamples);
        Assert.Equal(track.Observations.Count, track.PixelSamples.Count);
        Assert.All(track.PixelSamples, point =>
        {
            Assert.InRange(point.X, 40, 120);
            Assert.InRange(point.Y, 72, 102);
            Assert.InRange(point.ApproximateRadiusPixels, 2, 20);
        });
        Assert.Equal(track.Observations[0].TimeSeconds,
            track.PixelSamples[0].TimeSeconds);
        var measured = EvidenceAnalyzer.Analyze(new EvidenceRequest(
            new ExperimentSetup(ExperimentKind.Pendulum, LengthMeters: 0.5),
            track.Observations, EvidenceOrigin.TrackingCoordinates, "synthetic-test"));
        Assert.InRange(measured.ObservedValue, 1.45, 1.55);
    }

    [Fact]
    public void FlatBackground_CannotBeMistakenForReliablePendulumMovement()
    {
        const int width = 160, height = 120;
        var frames = Enumerable.Range(0, 140)
            .Select(_ => new byte[width * height]).ToArray();
        var result = PendulumMotionTracker.Track(frames, width, height, 12);
        Assert.Equal("NEEDS_MANUAL_REVIEW", result.Outcome);
        Assert.Empty(result.Observations);
        Assert.NotNull(result.PixelSamples);
        Assert.Empty(result.PixelSamples);
    }
}
