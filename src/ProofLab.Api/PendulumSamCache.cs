using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProofLab.Science;

namespace ProofLab.Api;

/// <summary>
/// Provider-obtained SAM video observations are written here before any
/// successful HTTP response to a client. The response is never generated
/// from the local motion tracker or arbitrary user-supplied JSON.
/// SQLite indexing will be added in Stage 4; the archive already prevents
/// repeat inference of an identical video/prompt/model.
/// </summary>
public sealed record SamVideoReceipt(
    string CacheKey, string OriginalVideoSha256, string VideoId,
    string Model, string Concept, string RequestProtocol,
    DateTimeOffset CapturedAtUtc, string ResponseJsonSha256,
    string ProviderResponseId, int? BilledFrames,
    int SourceFrameCount, double[] FramePtsSeconds,
    string SubmittedMp4Sha256,
    string ProviderEvidenceStatus, double EstimatedCostUsd);

public sealed record SamVideoState(
    string Status, string Model, string Concept, string SourceSha256,
    string CacheKey, int SourceFrames, double EstimatedCostUsd,
    string? ResponseSha256, int? BilledFrames, bool CanRunLive,
    string Provenance);

public sealed record SamVideoPreflight(
    string SourceSha256, string VideoId, string Codec,
    int SourceWidth, int SourceHeight, int SourceFrames,
    double DurationSeconds, double EstimatedCostUsd,
    string FramePtsSha256, bool SupportsSingleCall,
    string TimingSource, string BillingStatus);

public sealed record SamVideoArtifact(
    SamVideoState State, SamVideoReceipt Receipt, string RawResponseJson);

public sealed record SamVideoRunRequest(
    string VideoSha256, bool ConfirmPrivateVideoTransfer,
    bool ConfirmOneBillableSamCall, double MaximumEstimatedCostUsd);

public sealed record SamApiCompletedResult(
    string FullResponseJson, string ProviderResponseId, int? BilledFrames);

public sealed class PendulumSamCache(
    IWebHostEnvironment environment, PendulumDirectorWorkflow director,
    VideoMediaDecoder decoder, MetaLiveAccess meta,
    SamVideoMetaTransport transport, ILogger<PendulumSamCache> logger)
{
    public const string SamModel = "sam-3.1";
    public const string Prompt = "metal pendulum bob";
    public const string Protocol = "PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V2";
    private const string ResponseFile = "response.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = true };
    // Locks requests from this backend process; the persistent attempt marker
    // prevents an automatic retry after process crash or network uncertainty.
    private readonly SemaphoreSlim gate = new(1,1);

    public static string CacheKey(string originalSha)
    {
        if (originalSha.Length != 64 || originalSha.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid original video SHA-256.");
        var identity = Protocol + "\n" + SamModel + "\n" + Prompt + "\n" +
                       originalSha.ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();
    }

    private string Folder(string key) => Path.Combine(environment.ContentRootPath,
        ".private", "physics", "sam-cache", key);

    private static string Hash(string contents) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contents)))
            .ToLowerInvariant();

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken ct)
    {
        await using var input = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(input, Json, ct) ??
            throw new InvalidDataException("Invalid scientific cache document.");
    }

    private static async Task WriteExclusiveAsync<T>(
        string filePath, T value, CancellationToken ct)
    {
        await using var stream = new FileStream(filePath, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, value, Json, ct);
        await stream.FlushAsync(ct);
    }

    private async Task<(DirectorVideoEvidence Video, string Path, string Key)> Source(
        string studyId, string role, CancellationToken ct)
    {
        var (record, path) = await director.GetVerifiedOriginalAsync(studyId, role, ct);
        if (record.Media.Codec is not ("h264" or "hevc" or "mpeg4") ||
            Path.GetExtension(path).ToLowerInvariant() is not (".mov" or ".mp4"))
            throw new ArgumentException(
                "SAM accepts this workflow only for source MOV/MP4 H.264/H.265/MPEG4.");
        return (record, path, CacheKey(record.Sha256));
    }

    private static void ValidateReceipt(SamVideoReceipt receipt,
        DirectorVideoEvidence video, string key, string raw)
    {
        if (receipt.CacheKey != key ||
            receipt.OriginalVideoSha256 != video.Sha256.ToLowerInvariant() ||
            receipt.Model != SamModel || receipt.Concept != Prompt ||
            receipt.RequestProtocol != Protocol ||
            receipt.SourceFrameCount is < 50 or > 2500 ||
            receipt.FramePtsSeconds.Length != receipt.SourceFrameCount ||
            receipt.SubmittedMp4Sha256.Length!=64 ||
            receipt.SubmittedMp4Sha256.Any(c=>!Uri.IsHexDigit(c)) ||
            receipt.ProviderEvidenceStatus != "COMPLETED_PROVIDER_RESPONSE_REQUIRES_VISUAL_REVIEW" ||
            !string.Equals(receipt.ResponseJsonSha256,Hash(raw),
                StringComparison.Ordinal))
            throw new InvalidDataException("Stored SAM video provenance mismatch.");
        using var response = JsonDocument.Parse(raw);
        var root = response.RootElement;
        if (!root.TryGetProperty("model", out var model) ||
            model.GetString() != SamModel ||
            !root.TryGetProperty("status", out var status) ||
            status.GetString() != "completed")
            throw new InvalidDataException("Incomplete or invalid SAM response cache.");
    }

    public async Task<SamVideoState> StatusAsync(
        string studyId, string role, CancellationToken ct)
    {
        var (video, _, key) = await Source(studyId, role, ct);
        var folder = Folder(key);
        if (File.Exists(Path.Combine(folder,"receipt.json")) &&
            File.Exists(Path.Combine(folder,ResponseFile)))
        {
            var cached = await ReadArchiveAsync(studyId,role,ct);
            return cached.State;
        }
        var attempted = File.Exists(Path.Combine(folder,"attempt-started.json"));
        var snapshot = meta.Status();
        return new SamVideoState(
            attempted ? "PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION" :
                        "NOT_ANALYZED_WITH_SAM",
            SamModel,Prompt,video.Sha256,key,0,0,null,null,
            !attempted && snapshot.LiveCallsPermitted,
            "NO_SAM_PROVIDER_EVIDENCE; LOCAL_FFMPEG_RESULTS_ARE_SEPARATE");
    }

    /// <summary>
    /// Non-billable read-only evaluation of exact source frame timestamps.
    /// Does not upload, reserve any Meta budget or write scientific evidence.
    /// </summary>
    public async Task<SamVideoPreflight> PreflightAsync(
        string studyId, string role, CancellationToken ct)
    {
        var (video,path,_) = await Source(studyId,role,ct);
        var timeline = await decoder.ReadVideoFramePtsAsync(path,ct);
        var cost = Math.Max(50,timeline.Length) * 0.0002;
        var serial = string.Join("\n",timeline.Select(t =>
            t.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
        var digest = Hash(serial);
        return new SamVideoPreflight(
            video.Sha256,video.VideoId,video.Media.Codec,
            video.Media.Width,video.Media.Height,timeline.Length,
            video.Media.DurationSeconds,cost,digest,
            timeline.Length <= 2500 && cost<=0.50,
            "FFPROBE_BEST_EFFORT_SOURCE_FRAME_PTS_NORMALIZED_TO_FIRST",
            "PROMOTIONAL_BALANCE_NOT_VERIFIED_BY_THIS_ENDPOINT");
    }

    public async Task<SamVideoArtifact> ReadArchiveAsync(
        string studyId, string role, CancellationToken ct)
    {
        var (video, _, key) = await Source(studyId,role,ct);
        var folder = Folder(key);
        var receiptPath = Path.Combine(folder,"receipt.json");
        var responsePath = Path.Combine(folder,ResponseFile);
        if (!File.Exists(receiptPath) || !File.Exists(responsePath))
            throw new FileNotFoundException("SAM provider evidence not cached.");
        var receipt = await ReadJsonAsync<SamVideoReceipt>(receiptPath, ct);
        var responseJson = await File.ReadAllTextAsync(responsePath,ct);
        ValidateReceipt(receipt,video,key,responseJson);
        var state = new SamVideoState(
            "CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW",
            SamModel,Prompt,video.Sha256,key,receipt.SourceFrameCount,
            receipt.EstimatedCostUsd,receipt.ResponseJsonSha256,
            receipt.BilledFrames,false,
            "META_SAM_3_1_CACHED_PROVIDER_OUTPUT; HUMAN_REVIEW_PENDING");
        return new SamVideoArtifact(state,receipt,responseJson);
    }

    /// <summary>
    /// One manual, bounded live action. Cache hit does NOT consume budget.
    /// A potentially billed attempt creates a durable marker before any
    /// outbound transfer; interrupted runs NEVER auto-retry.
    /// </summary>
    public async Task<SamVideoArtifact> RunOnceAsync(
        string studyId, string role, SamVideoRunRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        await gate.WaitAsync(ct);
        try
        {
            var (video,path,key) = await Source(studyId,role,ct);
            var folder=Folder(key);
            if (File.Exists(Path.Combine(folder,"receipt.json")) &&
                File.Exists(Path.Combine(folder,ResponseFile)))
                return await ReadArchiveAsync(studyId,role,ct); // zero cost

            if (!string.Equals(request.VideoSha256,video.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Video SHA-256 confirmation mismatch.");
            if (!request.ConfirmPrivateVideoTransfer ||
                !request.ConfirmOneBillableSamCall)
                throw new ArgumentException(
                    "Explicit third-party video transfer and billable-call confirmation required.");
            if (File.Exists(Path.Combine(folder,"attempt-started.json")))
                throw new InvalidOperationException(
                    "A prior SAM attempt may have been billed; reconcile it before manual retry.");
            if (!meta.Status().LiveCallsPermitted)
                throw new InvalidOperationException(
                    "Meta billing/key/owner consent gates have not been verified for this process.");

            // Read actual SOURCE PTS, not a theoretical 60 FPS approximation.
            // Restrict one run to <= 2500 frames and <= $0.50 at current Meta pricing.
            var timeline=await decoder.ReadVideoFramePtsAsync(path,ct);
            var estimate=Math.Max(50,timeline.Length)*0.0002;
            if (timeline.Length>2500 || estimate>0.500000001 ||
                !double.IsFinite(request.MaximumEstimatedCostUsd) ||
                request.MaximumEstimatedCostUsd < estimate ||
                request.MaximumEstimatedCostUsd>0.50)
                throw new ArgumentException(
                    "SAM request exceeds the explicit video-frame cost policy.");

            Directory.CreateDirectory(folder);
            var submittedPath=Path.Combine(folder,"sam-upload.mp4");
            // Files API expects video/mp4. Remux MOV/HEVC without re-encoding:
            // identical 1828 decoded source frames must be accounted for.
            await decoder.CreateSamCompatibleMp4Async(path,submittedPath,ct);
            var submittedPts=await decoder.ReadVideoFramePtsAsync(submittedPath,ct);
            if(submittedPts.Length!=timeline.Length)
                throw new InvalidDataException(
                    "MP4 remux changed the source frame count. NO upload allowed.");
            for(var i=0;i<timeline.Length;i++)
                if(Math.Abs(submittedPts[i]-timeline[i])>0.005)
                    throw new InvalidDataException(
                        "MP4 remux changed frame PTS. NO upload allowed.");
            string submittedSha;
            await using(var submittedStream=File.OpenRead(submittedPath))
                submittedSha=Convert.ToHexString(
                    await SHA256.HashDataAsync(submittedStream,ct)).ToLowerInvariant();

            // Must reserve BEFORE upload; not an exact monetary cap.
            meta.ReserveOrThrow();
            await WriteExclusiveAsync(Path.Combine(folder,"attempt-started.json"),
                new {protocol=Protocol,sourceSha256=video.Sha256,
                    submittedMp4Sha256=submittedSha,
                    frameCount=timeline.Length,estimatedUsd=estimate,
                    requestedAtUtc=DateTimeOffset.UtcNow,
                    status="MAY_BE_BILLABLE_NO_AUTOMATIC_RETRY"},ct);
            // Sanitized, durable per-attempt diagnostics. NO response text,
            // prompt, file ID, API token, video, mask bytes or identifiers.
            // The existing attempt lock is NEVER removed on failure.
            var stopwatch=Stopwatch.StartNew();
            var auditFile=Path.Combine(folder,"attempt-telemetry.jsonl");
            async ValueTask RecordProgress(SamVideoSseProgress progress)
            {
                var line=JsonSerializer.Serialize(new {
                    recordedAtUtc=DateTimeOffset.UtcNow,
                    progress.Stage,elapsedMs=stopwatch.ElapsedMilliseconds,
                    progress.Events,progress.DeltaEvents,progress.OutputChars
                },Json)+"\n";
                try { await File.AppendAllTextAsync(auditFile,line,CancellationToken.None); }
                catch(IOException)
                {
                    logger.LogWarning("SAM diagnostic journal write failed; no retry permitted.");
                }
            }

            await RecordProgress(new SamVideoSseProgress("REQUEST_BUDGET_RESERVED",0,0,0));
            SamApiCompletedResult result;
            try
            {
                // The ONLY external media transfer/inference path.
                result=await transport.UploadAndSegmentAsync(
                    submittedPath,Prompt,RecordProgress,ct);
            }
            catch(Exception ex)
            {
                var failure=ex switch {
                    SamVideoSseException sam => sam.Code,
                    OperationCanceledException => "SAM_REQUEST_CANCELLED_OR_TIMED_OUT",
                    HttpRequestException => "SAM_PROVIDER_HTTP_OR_TRANSPORT_FAILURE",
                    FormatException => "SAM_RESPONSE_FORMAT_INVALID",
                    _ => "SAM_REQUEST_UNEXPECTED_FAILURE"
                };
                await RecordProgress(new SamVideoSseProgress(failure,0,0,0));
                logger.LogWarning("SAM video attempt incomplete, reason={Reason}. Do not retry automatically.",failure);
                throw;
            }
            if (result.FullResponseJson.Length>85_000_000 ||
                result.FullResponseJson.Length==0)
                throw new InvalidDataException("Provider output exceeds cache safety limit.");
            using(var parsed=JsonDocument.Parse(result.FullResponseJson))
            {
                var p=parsed.RootElement;
                if (p.GetProperty("model").GetString()!=SamModel ||
                    p.GetProperty("status").GetString()!="completed")
                    throw new InvalidDataException("Provider did not complete SAM inference.");
            }
            var rawSha=Hash(result.FullResponseJson);
            var receipt=new SamVideoReceipt(
                key,video.Sha256.ToLowerInvariant(),video.VideoId,
                SamModel,Prompt,Protocol,DateTimeOffset.UtcNow,rawSha,
                result.ProviderResponseId,result.BilledFrames,timeline.Length,
                submittedPts,submittedSha,"COMPLETED_PROVIDER_RESPONSE_REQUIRES_VISUAL_REVIEW",
                estimate);
            // Do not expose "success" until both files exist; atomic creation
            // of receipt last makes incomplete archives unservable.
            var dataTemp=Path.Combine(folder,"response-writing.tmp");
            await File.WriteAllTextAsync(dataTemp,result.FullResponseJson,ct);
            File.Move(dataTemp,Path.Combine(folder,ResponseFile),overwrite:false);
            await WriteExclusiveAsync(Path.Combine(folder,"receipt.json"),receipt,ct);
            await RecordProgress(new SamVideoSseProgress(
                "PRIVATE_EVIDENCE_RECEIPT_COMMITTED",0,0,result.FullResponseJson.Length));
            return await ReadArchiveAsync(studyId,role,ct);
        }
        finally {gate.Release();}
    }
}
