namespace ProofLab.Science;

/// <summary>
/// Conservative allowlist. The actual compressed stream must also pass ffprobe;
/// an extension or browser-provided MIME type alone never establishes support.
/// </summary>
public static class VideoClipPolicy
{
    public const long MaximumBytes = 240_000_000;
    public const double MinimumDurationSeconds = 5;
    public const double MaximumDurationSeconds = 80;
    public const int SampleFramesPerSecond = 12;
    public const int DecodedWidth = 480;
    public const int DecodedHeight = 270;
    public const int MaximumFrames = 960;

    public static readonly IReadOnlySet<string> AllowedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".mov", ".mp4", ".m4v", ".webm", ".mkv", ".avi", ".3gp", ".3g2" };

    public static readonly IReadOnlySet<string> AllowedCodecs =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "h264", "hevc", "av1", "vp8", "vp9", "mpeg4", "mjpeg", "prores", "h263" };

    public static string CheckNameAndSize(string filename, long bytes)
    {
        if (string.IsNullOrWhiteSpace(filename) || filename.Length > 200 ||
            filename.IndexOfAny(new[] { '/', '\\', '\0', '\r', '\n' }) >= 0 ||
            filename is "." or "..")
            throw new ArgumentException("Invalid video filename.");

        var extension = Path.GetExtension(filename);
        if (!AllowedExtensions.Contains(extension))
            throw new ArgumentException("Unsupported video container. Try MOV, MP4, M4V, WebM, MKV, AVI or 3GP.");
        if (bytes < 10_000 || bytes > MaximumBytes)
            throw new ArgumentException("Video must be between 10 KB and 240 MB.");
        return extension.ToLowerInvariant();
    }

    public static void CheckVideoStream(string codec, int width, int height, double duration)
    {
        if (string.IsNullOrWhiteSpace(codec) || !AllowedCodecs.Contains(codec))
            throw new ArgumentException("Unsupported video codec. Try H.264, H.265/HEVC, AV1, VP8 or VP9.");
        if (width < 160 || height < 120 || width > 8192 || height > 8192 ||
            (long)width * height > 35_400_000)
            throw new ArgumentException("Video resolution outside supported limits.");
        if (!double.IsFinite(duration) || duration < MinimumDurationSeconds ||
            duration > MaximumDurationSeconds)
            throw new ArgumentException("Video duration must be between 5 and 80 seconds.");
    }
}
