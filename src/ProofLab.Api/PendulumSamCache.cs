using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using ProofLab.Science;

namespace ProofLab.Api;

/// <summary>
/// Explicit fresh analysis or verified SQLite reuse. Only the production
/// runtime can reserve billing; a cache hit never reserves or calls transport.
/// </summary>
public sealed class PendulumSamCache(
    IWebHostEnvironment environment, ISamVideoRuntime runtime,
    ISamVideoTransport transport, SamVideoEvidenceIndex index,
    ILogger<PendulumSamCache> logger, IAssemblyScenes? scenes = null)
{
    public const string SamModel = "sam-3.1";
    public const string Prompt = "metal pendulum bob";
    public const string Protocol = "PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V2";
    public const string CacheMode = "api-meta-cache";
    public const string FreshMode = "api-meta";
    private readonly SemaphoreSlim gate = new(1, 1);

    public static string NormalizeMode(string? mode) => mode switch {
        null or "" or CacheMode => CacheMode,
        FreshMode => FreshMode,
        _ => throw new ArgumentException("SAM mode must be api-meta or api-meta-cache.")
    };

    public static string CacheKey(string originalSha)
    {
        if (originalSha.Length != 64 || originalSha.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid original video SHA-256.");
        return SamVideoArchive.Hash(Protocol + "\n" + SamModel + "\n" + Prompt +
            "\n" + originalSha.ToLowerInvariant());
    }

    private async Task<(SamVideoSource Video, string Key, SamVideoArchive Archive, SamSceneDefinition? Scene)> Source(
        string studyId, string role, CancellationToken ct)
    {
        var video = await runtime.GetVerifiedSourceAsync(studyId, role, ct);
        if (video.Codec is not ("h264" or "hevc" or "mpeg4") ||
            Path.GetExtension(video.Path).ToLowerInvariant() is not (".mov" or ".mp4"))
            throw new ArgumentException("SAM requires a source MOV/MP4 H.264/H.265/MPEG4.");
        var scene=scenes is null?null:await scenes.ApprovedAsync(studyId,role,ct);
        if(scene is not null)AssemblyPolicy.Validate(scene,video.Sha256);
        var key=scene is null?CacheKey(video.Sha256):
            SamVideoArchive.Hash(AssemblyPolicy.SamProtocol+"\n"+SamModel+"\n"+scene.Key+"\n"+video.Sha256);
        if(scene is not null) {
            var legacyArchive=new SamVideoArchive(environment.ContentRootPath,index);
            var legacyKey=CacheKey(video.Sha256);
            var old=await legacyArchive.LoadAsync(video,legacyKey,ct);
            var pending=await legacyArchive.OutstandingAsync(video,legacyKey,old,ct);
            if(pending.LegacyOriginal || pending.NewRun || (pending.LegacyRetry && old.Count==0))
                throw new InvalidOperationException("Reconcile earlier video attempt before a new target.");
        }
        return (video,key,new SamVideoArchive(environment.ContentRootPath,index,scene),scene);
    }

    private static SamVideoArchive.Stored? Latest(IEnumerable<SamVideoArchive.Stored> all) =>
        all.OrderByDescending(a => a.Receipt.CapturedAtUtc)
            .ThenByDescending(a => a.Entry.AnalysisId).FirstOrDefault();

    private static SamVideoArtifact Artifact(SamVideoArchive.Stored stored,
        string mode = CacheMode, bool hit = true)
    {
        var r = stored.Receipt;
        return new(new SamVideoState(
            "CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW",
            SamModel, r.Concept, r.OriginalVideoSha256, r.CacheKey,
            r.SourceFrameCount, r.EstimatedCostUsd, r.ResponseJsonSha256,
            r.BilledFrames, false,
            "META_SAM_3_1_CACHED_PROVIDER_OUTPUT; HUMAN_REVIEW_PENDING",
            stored.Entry.AnalysisId, mode, hit, SceneKey:r.Scene?.Key), r, stored.Raw);
    }

    public async Task<SamVideoState> StatusAsync(
        string studyId, string role, CancellationToken ct, string? mode = null)
    {
        mode = NormalizeMode(mode);
        var (video, key, archive, scene) = await Source(studyId, role, ct);
        var all = await archive.LoadAsync(video, key, ct);
        var latest = Latest(all);
        if (mode == CacheMode && latest is not null) return Artifact(latest).State;
        var pending = await archive.OutstandingAsync(video, key, all, ct);
        var state = latest is null
            ? new SamVideoState("NOT_ANALYZED_WITH_SAM", SamModel, scene?.Prompt ?? Prompt,
                video.Sha256, key, 0, 0, null, null, false,
                "NO_SAM_PROVIDER_EVIDENCE; LOCAL_FFMPEG_RESULTS_ARE_SEPARATE")
            : Artifact(latest).State;
        return state with {
            Status = pending.Any ? pending.Status :
                latest is not null ? "READY_FOR_FRESH_META_ANALYSIS" : "NOT_ANALYZED_WITH_SAM",
            CanRunLive = !pending.Any && (scenes is null || scene is not null) && runtime.LiveCallsPermitted,
            RecognitionRequired = scenes is not null && scene is null, SceneKey=scene?.Key,
            Mode = mode, CacheHit = false
        };
    }

    public async Task<SamVideoPreflight> PreflightAsync(
        string studyId, string role, CancellationToken ct)
    {
        var (video, _, _, _) = await Source(studyId, role, ct);
        var pts = await runtime.ReadFramePtsAsync(video.Path, ct);
        var cost = Math.Max(50, pts.Length) * 0.0002;
        var serial = string.Join("\n", pts.Select(t =>
            t.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        return new(video.Sha256, video.VideoId, video.Codec, video.Width, video.Height,
            pts.Length, video.DurationSeconds, cost, SamVideoArchive.Hash(serial),
            pts.Length is >= 50 and <= 2500 && cost <= 0.50,
            "FFPROBE_BEST_EFFORT_SOURCE_FRAME_PTS_NORMALIZED_TO_FIRST",
            "PROMOTIONAL_BALANCE_NOT_VERIFIED_BY_THIS_ENDPOINT");
    }

    public async Task<SamVideoArtifact> ReadArchiveAsync(
        string studyId, string role, CancellationToken ct)
    {
        var (video, key, archive, scene) = await Source(studyId, role, ct);
        var all = await archive.LoadAsync(video, key, ct);
        return Artifact(Latest(all) ??
            throw new FileNotFoundException("SAM provider evidence not cached."));
    }

    public async Task<SamVideoArtifact> RunOnceAsync(
        string studyId, string role, SamVideoRunRequest request,
        CancellationToken ct, string? oneShotMetaToken = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var mode = NormalizeMode(request.Mode);
        if (oneShotMetaToken is not null && !SamManualApiTokenPolicy.Accepts(oneShotMetaToken))
            throw new ArgumentException("Invalid one-shot Meta API token.");
        await gate.WaitAsync(ct);
        try
        {
            var (video, key, archive, scene) = await Source(studyId, role, ct);
            var root = archive.Folder(key);
            Directory.CreateDirectory(root);
            // A persistent path with an OS-exclusive handle serializes all
            // backend processes, including a second localhost API instance.
            // The file remains; only the handle is released (also on crash).
            FileStream processLock;
            try { processLock = new FileStream(Path.Combine(root, ".run.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new InvalidOperationException("Another SAM operation is already active."); }
            await using var heldLock = processLock;
            var all = await archive.LoadAsync(video, key, ct);
            var latest = Latest(all);
            if (!string.Equals(request.VideoSha256, video.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Video SHA-256 confirmation mismatch.");
            if (mode == CacheMode && latest is not null) return Artifact(latest);
            if(scenes is not null && scene is null)
                throw new InvalidOperationException("Review assembly before a new SAM call.");
            if(request.SceneKey!=scene?.Key)
                throw new ArgumentException("Assembly changed; refresh the quote.");
            if (!request.ConfirmPrivateVideoTransfer || !request.ConfirmOneBillableSamCall)
                throw new ArgumentException("Explicit third-party video transfer and billable-call confirmation required.");

            var pending = await archive.OutstandingAsync(video, key, all, ct);
            if (pending.NewRun || pending.LegacyRetry)
                throw new InvalidOperationException("Prior paid attempt requires manual reconciliation; no additional charge allowed.");
            var reconciledRetry = pending.LegacyOriginal;
            if (reconciledRetry)
            {
                if (!ManualRetryReconciliationPolicy.CanStartOneManualRetry(true, false,
                    request.OwnerApprovesOneReconciledRetry, request.PreviousAttemptBillingEvidence))
                    throw new InvalidOperationException("Prior paid attempt requires explicit owner-approved reconciliation.");
            }
            else if (request.OwnerApprovesOneReconciledRetry)
                throw new ArgumentException("Do not use manual reconciliation on a new SAM analysis.");
            if (!runtime.CanRunWithToken(oneShotMetaToken))
                throw new InvalidOperationException("Meta billing/key/owner consent gates have not been verified for this process.");

            var timeline = await runtime.ReadFramePtsAsync(video.Path, ct);
            if(scene is not null)AssemblyPolicy.Validate(scene,video.Sha256,timeline.Length);
            var estimate = Math.Max(50, timeline.Length) * 0.0002;
            if (timeline.Length is < 50 or > 2500 || estimate > 0.500000001 ||
                !double.IsFinite(request.MaximumEstimatedCostUsd) ||
                request.MaximumEstimatedCostUsd < estimate || request.MaximumEstimatedCostUsd > 0.50)
                throw new ArgumentException("SAM request exceeds the explicit video-frame cost policy.");
            var id = Guid.NewGuid().ToString("N");
            var folder = archive.RunFolder(key, id);
            Directory.CreateDirectory(folder);
            var submittedPath = reconciledRetry ? Path.Combine(root, "sam-upload.mp4") :
                Path.Combine(folder, "sam-upload.mp4");
            if (!reconciledRetry) await runtime.CreateMp4Async(video.Path, submittedPath, ct);
            var submittedPts = await runtime.ReadFramePtsAsync(submittedPath, ct);
            if (submittedPts.Length != timeline.Length ||
                timeline.Where((t, i) => !double.IsFinite(t) || t < 0 ||
                    !double.IsFinite(submittedPts[i]) || submittedPts[i] < 0 ||
                    Math.Abs(submittedPts[i] - t) > 0.005).Any() ||
                Math.Abs(timeline[0]) > 0.005 ||
                timeline.Zip(timeline.Skip(1), (a,b) => b <= a).Any(x => x) ||
                submittedPts.Zip(submittedPts.Skip(1), (a,b) => b <= a).Any(x => x))
                throw new InvalidDataException("MP4 remux changed source frame PTS. NO upload allowed.");
            string submittedSha;
            await using (var stream = File.OpenRead(submittedPath))
                submittedSha = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
            if (reconciledRetry)
            {
                var prior = await SamVideoArchive.ReadJsonAsync<SamOriginalAttempt>(
                    Path.Combine(root, "attempt-started.json"), ct);
                if (prior.SubmittedMp4Sha256 != submittedSha)
                    throw new InvalidDataException("Previous submitted MP4 SHA mismatch.");
            }
            ct.ThrowIfCancellationRequested();
            runtime.ReserveOrThrow(oneShotMetaToken);
            var attempt = new SamOriginalAttempt(scene is null?Protocol:AssemblyPolicy.SamProtocol, video.Sha256, submittedSha,
                timeline.Length, estimate, DateTimeOffset.UtcNow,
                "MAY_BE_BILLABLE_NO_AUTOMATIC_RETRY", id);
            await SamVideoArchive.WriteExclusiveAsync(Path.Combine(folder, "attempt-started.json"),
                attempt, CancellationToken.None);
            if (reconciledRetry)
                await SamVideoArchive.WriteExclusiveAsync(Path.Combine(root, "manual-retry-started.json"),
                    new {
                        attempt.Protocol, attempt.SourceSha256, attempt.SubmittedMp4Sha256,
                        attempt.FrameCount, attempt.EstimatedUsd, attempt.RequestedAtUtc,
                        attempt.AnalysisId, reconciliation = request.PreviousAttemptBillingEvidence,
                        ownerApprovedOneRetry = true,
                        status = "SECOND_ATTEMPT_MAY_BE_BILLABLE_NO_AUTOMATIC_RETRY"
                    }, CancellationToken.None);

            var stopwatch = Stopwatch.StartNew();
            async ValueTask RecordProgress(SamVideoSseProgress progress)
            {
                var line = JsonSerializer.Serialize(new {
                    recordedAtUtc = DateTimeOffset.UtcNow, progress.Stage,
                    elapsedMs = stopwatch.ElapsedMilliseconds,
                    progress.Events, progress.DeltaEvents, progress.OutputChars
                }) + "\n";
                try { await File.AppendAllTextAsync(Path.Combine(folder, "attempt-telemetry.jsonl"),
                    line, CancellationToken.None); }
                catch (IOException) { logger.LogWarning("SAM journal write failed; no automatic retry permitted."); }
            }
            await RecordProgress(new("REQUEST_BUDGET_RESERVED", 0, 0, 0));
            SamApiCompletedResult result;
            try
            {
                // Browser cancellation is honored until reservation. Thereafter
                // the authorized operation has its own bounded lifetime so a
                // disconnected tab cannot discard a potentially billed result.
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(30));
                result = await transport.UploadAndSegmentAsync(submittedPath, scene?.Prompt ?? Prompt,
                    RecordProgress, deadline.Token, oneShotMetaToken);
            }
            catch (Exception ex)
            {
                var failure = ex switch {
                    SamVideoSseException sam => sam.Code,
                    OperationCanceledException => "SAM_REQUEST_CANCELLED_OR_TIMED_OUT",
                    HttpRequestException => "SAM_PROVIDER_HTTP_OR_TRANSPORT_FAILURE",
                    _ => "SAM_REQUEST_UNEXPECTED_FAILURE"
                };
                await RecordProgress(new(failure, 0, 0, 0));
                logger.LogWarning("SAM attempt incomplete, reason={Reason}; no automatic retry.", failure);
                throw;
            }
            if (result.FullResponseJson.Length is 0 or > 85_000_000)
                throw new InvalidDataException("Provider output exceeds cache safety limit.");
            var receipt = new SamVideoReceipt(key, video.Sha256.ToLowerInvariant(), video.VideoId,
                SamModel, scene?.Prompt ?? Prompt, scene is null?Protocol:AssemblyPolicy.SamProtocol, DateTimeOffset.UtcNow, SamVideoArchive.Hash(result.FullResponseJson),
                result.ProviderResponseId, result.BilledFrames, timeline.Length, submittedPts, submittedSha,
                "COMPLETED_PROVIDER_RESPONSE_REQUIRES_VISUAL_REVIEW", estimate, id, scene);
            var committed = await archive.CommitAsync(video, key, receipt, result.FullResponseJson);
            await RecordProgress(new("PRIVATE_EVIDENCE_RECEIPT_COMMITTED", 0, 0, result.FullResponseJson.Length));
            return Artifact(committed, mode, hit: false);
        }
        finally { gate.Release(); }
    }
}