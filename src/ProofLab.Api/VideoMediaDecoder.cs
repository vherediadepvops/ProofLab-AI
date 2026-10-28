using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ProofLab.Science;

namespace ProofLab.Api;

public sealed record VideoStreamMetadata(
    string Codec, int Width, int Height, double DurationSeconds,
    double NominalFramesPerSecond, string ContainerFormat);

/// <summary>
/// Decodes local media without invoking the browser codec, without Meta API,
/// without a shell or allowing FFmpeg network protocols.
/// Only trusted binaries configured by the operator or found in local WinGet
/// are launched. Avoid passing untrusted filenames as executable arguments.
/// </summary>
public sealed class VideoMediaDecoder
{
    private readonly string? ffmpeg;
    private readonly string? ffprobe;
    private static readonly SemaphoreSlim DecoderLimit = new(1, 1);
    public bool Available => ffmpeg is not null && ffprobe is not null;

    public VideoMediaDecoder(IConfiguration configuration)
    {
        var configured = configuration["Media:FfmpegDirectory"];
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured)) paths.Add(configured);
        foreach (var item in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (!string.IsNullOrWhiteSpace(item)) paths.Add(item);
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            paths.Add(Path.Combine(appData, "Microsoft", "WinGet", "Links"));
        }
        foreach (var dir in paths.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var a = Path.Combine(dir, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
            var b = Path.Combine(dir, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            if (File.Exists(a) && File.Exists(b))
            {
                ffmpeg = a;
                ffprobe = b;
                break;
            }
        }
    }

    public async Task<VideoStreamMetadata> ProbeAsync(string localFile, CancellationToken ct)
    {
        if (ffprobe is null) throw new InvalidOperationException("Local FFmpeg/FFprobe is not installed.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        var proc = NewProcess(ffprobe,
            "-v", "error", "-protocol_whitelist", "file,pipe",
            "-select_streams", "v:0",
            "-show_entries", "stream=codec_name,width,height,avg_frame_rate:format=duration,format_name",
            "-of", "json", "-i", localFile);
        var output = await RunTextAsync(proc, timeout.Token);
        try
        {
            using var json = JsonDocument.Parse(output);
            var video = json.RootElement.GetProperty("streams").EnumerateArray().First();
            var format = json.RootElement.GetProperty("format");
            var codec = video.GetProperty("codec_name").GetString() ?? "";
            var width = video.GetProperty("width").GetInt32();
            var height = video.GetProperty("height").GetInt32();
            var duration = double.Parse(format.GetProperty("duration").GetString() ?? "0",
                CultureInfo.InvariantCulture);
            var fraction = video.TryGetProperty("avg_frame_rate", out var fpsValue)
                ? fpsValue.GetString() ?? "0" : "0";
            var parts = fraction.Split('/');
            var fps = parts.Length == 2 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numer) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denom) &&
                denom > 0 ? numer / denom : 0;
            VideoClipPolicy.CheckVideoStream(codec, width, height, duration);
            if (fps < 5 || fps > 240)
                throw new ArgumentException("Video frame rate outside 5–240 fps.");
            return new VideoStreamMetadata(codec, width, height, duration, fps,
                format.GetProperty("format_name").GetString() ?? "");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException
                                   or InvalidOperationException or FormatException)
        {
            throw new ArgumentException("Video metadata could not be decoded.");
        }
    }

    public async Task<LocalMotionTrack> TrackAsync(string localFile, CancellationToken ct)
    {
        if (ffmpeg is null) throw new InvalidOperationException("Local FFmpeg/FFprobe is not installed.");
        await DecoderLimit.WaitAsync(ct);
        try
        {
            var imageBytes = VideoClipPolicy.DecodedWidth * VideoClipPolicy.DecodedHeight;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(100));
            var proc = NewProcess(ffmpeg,
                "-hide_banner", "-loglevel", "error", "-nostdin",
                "-protocol_whitelist", "file,pipe", "-threads", "2",
                "-i", localFile, "-map", "0:v:0", "-an", "-sn", "-dn",
                "-t", VideoClipPolicy.MaximumDurationSeconds.ToString("F0", CultureInfo.InvariantCulture),
                "-vf", "fps=12,scale=480:270:flags=fast_bilinear,format=gray",
                "-frames:v", VideoClipPolicy.MaximumFrames.ToString(CultureInfo.InvariantCulture),
                "-f", "rawvideo", "-pix_fmt", "gray", "pipe:1");
            proc.Start();
            try
            {
                var errorTask = proc.StandardError.ReadToEndAsync(timeout.Token);
                using var output = new MemoryStream();
                var bytes = new byte[65536];
                var maxBytes = imageBytes * VideoClipPolicy.MaximumFrames;
                while (true)
                {
                    var n = await proc.StandardOutput.BaseStream.ReadAsync(bytes, timeout.Token);
                    if (n == 0) break;
                    if (output.Length + n > maxBytes)
                        throw new ArgumentException("Decoded video exceeds sample limits.");
                    output.Write(bytes, 0, n);
                }
                await proc.WaitForExitAsync(timeout.Token);
                await errorTask;
                if (proc.ExitCode != 0 || output.Length < imageBytes * 24 ||
                    output.Length % imageBytes != 0)
                    throw new ArgumentException("Video could not be decoded to complete frames.");

                var decoded = output.ToArray();
                var count = decoded.Length / imageBytes;
                var frames = new List<byte[]>(count);
                for (var i = 0; i < count; i++)
                {
                    var frame = new byte[imageBytes];
                    Buffer.BlockCopy(decoded, i * imageBytes, frame, 0, imageBytes);
                    frames.Add(frame);
                }
                return PendulumMotionTracker.Track(frames,
                    VideoClipPolicy.DecodedWidth, VideoClipPolicy.DecodedHeight,
                    VideoClipPolicy.SampleFramesPerSecond);
            }
            finally
            {
                if (!proc.HasExited)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                }
                proc.Dispose();
            }
        }
        finally { DecoderLimit.Release(); }
    }

    public async Task CreatePosterAsync(string originalFile, string posterFile, CancellationToken ct)
    {
        if (ffmpeg is null) throw new InvalidOperationException("FFmpeg is unavailable.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var proc = NewProcess(ffmpeg,
            "-hide_banner", "-loglevel", "error", "-nostdin",
            "-protocol_whitelist", "file,pipe",
            "-ss", "2", "-i", originalFile, "-map", "0:v:0",
            "-frames:v", "1", "-vf", "scale=640:-2:flags=fast_bilinear",
            "-q:v", "4", "-y", "-f", "image2", posterFile);
        await RunTextAsync(proc, timeout.Token);
        if (!File.Exists(posterFile) || new FileInfo(posterFile).Length is < 1000 or > 2_000_000)
            throw new ArgumentException("Could not produce safe video thumbnail.");
    }

    /// <summary>
    /// A bounded, server-side H.264 MP4 review proxy for browsers that cannot
    /// natively play iPhone HEVC/MOV. The original file and SHA never change.
    /// This is NOT the scientific input nor SAM inference output.
    /// </summary>
    public async Task CreatePlayablePreviewAsync(
        string originalFile, string proxyFile, CancellationToken ct)
    {
        if (ffmpeg is null) throw new InvalidOperationException("FFmpeg is unavailable.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(95));
        var proc = NewProcess(ffmpeg,
            "-hide_banner", "-loglevel", "error", "-nostdin",
            "-protocol_whitelist", "file,pipe", "-threads", "2",
            "-i", originalFile, "-map", "0:v:0", "-an", "-sn", "-dn",
            "-t", VideoClipPolicy.MaximumDurationSeconds.ToString(
                "F0", System.Globalization.CultureInfo.InvariantCulture),
            "-vf", "fps=12,scale=640:-2:flags=fast_bilinear",
            "-c:v", "libopenh264", "-b:v", "440k",
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart", "-y", "-f", "mp4", proxyFile);
        await RunTextAsync(proc, timeout.Token);
        if (!File.Exists(proxyFile) || new FileInfo(proxyFile).Length is
            < 10_000 or > 30_000_000)
            throw new ArgumentException("Could not produce playable browser proxy.");
    }

    private static Process NewProcess(string binary, params string[] args)
    {
        var info = new ProcessStartInfo(binary)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return new Process { StartInfo = info };
    }

    private static async Task<string> RunTextAsync(Process process, CancellationToken ct)
    {
        using (process)
        {
            process.Start();
            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync(ct);
                var stderr = process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);
                await stderr;
                var output = await stdout;
                if (process.ExitCode != 0 || output.Length > 32_000)
                    throw new ArgumentException("Unsupported, damaged or unreadable video.");
                return output;
            }
            finally
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                }
            }
        }
    }
}
