using System.Security.Cryptography;
using System.Text.Json;
using ProofLab.Science;

namespace ProofLab.Api;

public sealed record PendulumVideoRecord(
    string VideoId,
    string PreregistrationId,
    string Condition,
    int Take,
    string OriginalFilename,
    DateTimeOffset UploadedAtUtc,
    long SizeBytes,
    string SourceSha256,
    bool PosterAvailable,
    VideoStreamMetadata Media,
    LocalMotionTrack Tracking,
    double? PreliminaryPeriodSeconds,
    string Outcome,
    string ProvenanceNote);

public sealed record PendulumVideoComparisonRequest(
    IReadOnlyList<string> BaselineVideoIds,
    IReadOnlyList<string> FollowUpVideoIds);

public sealed record PendulumVideoComparison(
    string PreregistrationId,
    DateTimeOffset ComparedAtUtc,
    double PredictedIncreasePercent,
    double? BaselineMeanPeriodSeconds,
    double? FollowUpMeanPeriodSeconds,
    double? ObservedIncreasePercent,
    double? DifferencePercentagePoints,
    double? BaselineSampleStandardDeviation,
    double? FollowUpSampleStandardDeviation,
    int BaselineVideos,
    int FollowUpVideos,
    string ScientificStatus,
    string Method,
    string Limitations);

/// <summary>
/// Strictly local ingestion: mobile-original video and derived data stay under
/// .private/; no external API, no paid model, no auto-scientific approval.
/// </summary>
public sealed class PendulumVideoWorkflow(
    IWebHostEnvironment environment,
    PendulumResearchStore studies,
    VideoMediaDecoder decoder)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { WriteIndented = true };
    private string Root => Path.Combine(environment.ContentRootPath,
        ".private", "physics", "pendulum", "video-evidence");

    public bool DecoderAvailable => decoder.Available;

    public async Task<PendulumVideoRecord> UploadAsync(
        string studyId, string condition, int take,
        string filename, Stream data, long length, CancellationToken ct)
    {
        var extension = VideoClipPolicy.CheckNameAndSize(filename, length);
        if (condition is not ("baseline" or "followup") || take is < 1 or > 3)
            throw new ArgumentException("Invalid video condition or take number.");

        var study = await studies.GetAsync(studyId, ct);
        if (!decoder.Available)
            throw new InvalidOperationException(
                "FFmpeg is missing on the server; configure Media:FfmpegDirectory.");

        await gate.WaitAsync(ct);
        string? filePath = null;
        string? posterPath = null;
        try
        {
            var folder = FolderFor(study.Id);
            Directory.CreateDirectory(folder);
            if (Directory.EnumerateFiles(folder, "*.json").Any(f =>
            {
                // Do not overwrite previous evidence submissions.
                try
                {
                    var saved = JsonSerializer.Deserialize<PendulumVideoRecord>(
                        File.ReadAllText(f), Json);
                    return saved?.Condition == condition && saved.Take == take;
                }
                catch { return true; } // corrupt evidence => fail closed.
            }))
                throw new ArgumentException("This trial number already has a video. Choose another take.");

            var videoId = Guid.NewGuid().ToString("N");
            filePath = Path.Combine(folder, videoId + extension);
            await using (var target = new FileStream(filePath,
                FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 128 * 1024, useAsync: true))
            {
                var buffer = new byte[128 * 1024];
                long copied = 0;
                while (true)
                {
                    var n = await data.ReadAsync(buffer, ct);
                    if (n == 0) break;
                    copied += n;
                    if (copied > VideoClipPolicy.MaximumBytes)
                        throw new ArgumentException("Video exceeds 240 MB limit.");
                    await target.WriteAsync(buffer.AsMemory(0, n), ct);
                }
                if (copied != length)
                    throw new ArgumentException("Incomplete video upload.");
            }

            string hash;
            await using (var original = File.OpenRead(filePath))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(original, ct))
                    .ToLowerInvariant();

            var metadata = await decoder.ProbeAsync(filePath, ct);
            LocalMotionTrack tracking;
            try { tracking = await decoder.TrackAsync(filePath, ct); }
            catch (ArgumentException)
            {
                tracking = new LocalMotionTrack([], 0, 0, 0,
                    "NEEDS_MANUAL_REVIEW", "LOCAL_DECODER_TRACKING_NOT_SAM",
                    "Video decodes but the local motion tracker could not produce reliable coordinates.");
            }

            posterPath = Path.Combine(folder, videoId + ".jpg");
            var posterAvailable = false;
            try
            {
                await decoder.CreatePosterAsync(filePath, posterPath, ct);
                posterAvailable = true;
            }
            catch (ArgumentException) { /* Still keep a decodable original without thumbnail. */ }

            double? detectedPeriod = null;
            var lengthMetres = condition == "baseline"
                ? study.Hypothesis.BaselineLengthMeters
                : study.Hypothesis.FollowUpLengthMeters;
            if (tracking.Outcome == "LOCAL_TRACKING_CANDIDATE_REQUIRES_VIDEO_REVIEW")
            {
                try
                {
                    var analysis = EvidenceAnalyzer.Analyze(new EvidenceRequest(
                        new ExperimentSetup(ExperimentKind.Pendulum,
                            LengthMeters: lengthMetres),
                        tracking.Observations, EvidenceOrigin.TrackingCoordinates,
                        "sha256:" + hash));
                    detectedPeriod = analysis.ObservedValue;
                }
                catch (ArgumentException) { }
            }

            var outcome = detectedPeriod.HasValue
                ? "PRELIMINARY_LOCAL_PERIOD_NOT_SAM"
                : "UPLOADED_NEEDS_MANUAL_TRACKING_REVIEW";
            var record = new PendulumVideoRecord(
                videoId, study.Id, condition, take, filename,
                DateTimeOffset.UtcNow, length, hash, posterAvailable, metadata,
                tracking, detectedPeriod, outcome,
                "Uploaded original from user; FFmpeg decoded locally. This is " +
                "NOT SAM segmentation or an independently authenticated physical measurement. " +
                "The preregistration timestamp does not prove video recording order.");

            var recordPath = Path.Combine(folder, videoId + ".json");
            await File.WriteAllTextAsync(recordPath,
                JsonSerializer.Serialize(record, Json), ct);
            return record;
        }
        catch
        {
            if (filePath != null && File.Exists(filePath))
                File.Delete(filePath);
            if (posterPath != null && File.Exists(posterPath))
                File.Delete(posterPath);
            throw;
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<PendulumVideoRecord>> ListAsync(
        string studyId, CancellationToken ct)
    {
        var study = await studies.GetAsync(studyId, ct);
        await gate.WaitAsync(ct);
        try
        {
            var folder = FolderFor(study.Id);
            if (!Directory.Exists(folder)) return [];
            var records = new List<PendulumVideoRecord>();
            foreach (var file in Directory.EnumerateFiles(folder, "*.json").Take(7))
            {
                var record = JsonSerializer.Deserialize<PendulumVideoRecord>(
                    await File.ReadAllTextAsync(file, ct), Json);
                if (record == null || record.PreregistrationId != study.Id)
                    throw new InvalidDataException("Video record integrity check failed.");
                records.Add(record);
            }
            return records.OrderBy(x => x.Condition).ThenBy(x => x.Take).ToArray();
        }
        finally { gate.Release(); }
    }

    public async Task<byte[]> ReadPosterAsync(string studyId, string videoId, CancellationToken ct)
    {
        if (videoId is null || videoId.Length != 32 || videoId.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid video identifier.");
        var records = await ListAsync(studyId, ct);
        if (!records.Any(x => x.VideoId == videoId && x.PosterAvailable))
            throw new FileNotFoundException("Thumbnail not available.");
        var path = Path.Combine(FolderFor(studyId), videoId + ".jpg");
        if (!File.Exists(path) || new FileInfo(path).Length is < 1000 or > 2_000_000)
            throw new FileNotFoundException("Thumbnail missing.");
        return await File.ReadAllBytesAsync(path, ct);
    }

    public async Task<PendulumVideoComparison> CompareAsync(
        string studyId, PendulumVideoComparisonRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var study = await studies.GetAsync(studyId, ct);
        var records = await ListAsync(study.Id, ct);
        var a = FindValid(request.BaselineVideoIds, "baseline", records);
        var b = FindValid(request.FollowUpVideoIds, "followup", records);
        if (a.Count == 0 || b.Count == 0)
            throw new ArgumentException("Upload at least one successfully tracked video for each length.");
        var baseline = a.Average(x => x.PreliminaryPeriodSeconds!.Value);
        var followup = b.Average(x => x.PreliminaryPeriodSeconds!.Value);
        var observed = (followup / baseline - 1) * 100;
        return new PendulumVideoComparison(
            study.Id, DateTimeOffset.UtcNow,
            study.Hypothesis.PredictedIncreasePercent,
            baseline, followup, observed,
            observed - study.Hypothesis.PredictedIncreasePercent,
            StandardDeviation(a.Select(x => x.PreliminaryPeriodSeconds!.Value)),
            StandardDeviation(b.Select(x => x.PreliminaryPeriodSeconds!.Value)),
            a.Count, b.Count,
            "PRELIMINARY_UNVERIFIED_LOCAL_VIDEO_TRACKING_NOT_SAM",
            "FFmpeg 12 fps decode + deterministic background segmentation; " +
            "independent position-midline period estimator.",
            "Not independently validated against visible pendulum frames, a manual " +
            "baseline, true camera timecodes, or a scientific uncertainty model. " +
            "Repeats with only one video cannot estimate sample variance. " +
            "No claim of an original discovery, Meta AI inference, or SAM video tracking.");
    }

    private static List<PendulumVideoRecord> FindValid(
        IReadOnlyList<string>? ids, string condition,
        IReadOnlyList<PendulumVideoRecord> records)
    {
        if (ids == null || ids.Count > 3 || ids.Count != ids.Distinct().Count())
            throw new ArgumentException("Choose one to three unique videos per condition.");
        var results = new List<PendulumVideoRecord>();
        foreach (var id in ids)
        {
            var record = records.FirstOrDefault(r => r.VideoId == id &&
                r.Condition == condition);
            if (record == null || record.PreliminaryPeriodSeconds is null)
                throw new ArgumentException("Selected video is not tracked or belongs to the wrong condition.");
            results.Add(record);
        }
        return results;
    }

    private static double? StandardDeviation(IEnumerable<double> values)
    {
        var a = values.ToArray();
        if (a.Length < 2) return null;
        var mean = a.Average();
        return Math.Sqrt(a.Sum(x => Math.Pow(x - mean, 2)) / (a.Length - 1));
    }

    private string FolderFor(string id)
    {
        if (id.Length != 32 || id.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid preregistration ID.");
        return Path.Combine(Root, id.ToLowerInvariant());
    }
}
