using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProofLab.Science;

namespace ProofLab.Api;

public sealed record DirectorLocalEvidenceReceipt(
    string Protocol, string StudyId, string Role, string VideoId,
    string SourceSha256, DateTimeOffset ComputedAtUtc,
    LocalMotionTrack Tracking, PendulumSpectralAssessment Spectral,
    string EvidenceSha256);

public sealed class DirectorLocalEvidenceUnavailableException : InvalidOperationException
{
    public DirectorLocalEvidenceUnavailableException()
        : base("Local motion processing is unavailable; retry this step.") { }
}

public sealed partial class PendulumDirectorWorkflow
{
    private const string LocalEvidenceProtocol = "PROOFLAB_DEFERRED_LOCAL_EVIDENCE_V1";
    private const string LocalTrackingMethod =
        "LOCAL_MEDIAN_BACKGROUND_SUBTRACTION_12_FPS_NOT_SAM";

    // Callers invoke this workflow before taking their session gate. Ordinary
    // session reads must not wait for CPU-heavy local video decoding.
    private readonly SemaphoreSlim localEvidenceGate = new(1, 1);

    private string LocalEvidencePath(string id, string role) =>
        PathFor(id, role + "-local-evidence-v1.json");

    private static void ValidateLocalEvidenceState(DirectorVideoEvidence video)
    {
        var ready = video.LocalEvidenceStatus == "READY" &&
                    video.Tracking is not null && video.Spectral is not null;
        var deferred = video.LocalEvidenceStatus == "DEFERRED" &&
                       video.Tracking is null && video.Spectral is null;
        if (!ready && !deferred)
            throw new InvalidDataException("Local evidence state is inconsistent.");
    }

    private static string LocalEvidenceHash(DirectorLocalEvidenceReceipt value)
    {
        var payload = JsonSerializer.Serialize(new
        {
            value.Protocol, value.StudyId, value.Role, value.VideoId,
            value.SourceSha256, value.ComputedAtUtc, value.Tracking, value.Spectral
        }, Json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }

    private async Task<DirectorVideoEvidence?> ReadVideoWithLocalEvidenceAsync(
        string id, string role, CancellationToken ct)
    {
        var original = await ReadOptionalAsync<DirectorVideoEvidence>(
            PathFor(id, role + ".json"), ct);
        return original is null ? null :
            await AttachLocalEvidenceAsync(id, role, original, ct);
    }

    // Read-only enrichment never starts FFmpeg, rewrites original evidence,
    // or opens a cache/SQLite store.
    private async Task<DirectorVideoEvidence> AttachLocalEvidenceAsync(
        string id, string role, DirectorVideoEvidence original, CancellationToken ct)
    {
        if (original.Role != role)
            throw new InvalidDataException("Original video role mismatch.");
        ValidateLocalEvidenceState(original);
        if (original.LocalEvidenceStatus == "READY") return original;

        DirectorLocalEvidenceReceipt? saved;
        try
        {
            saved = await ReadOptionalAsync<DirectorLocalEvidenceReceipt>(
                LocalEvidencePath(id, role), ct);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("Stored local evidence is invalid.");
        }
        if (saved is null) return original;
        if (saved.Protocol != LocalEvidenceProtocol ||
            saved.StudyId != id.ToLowerInvariant() ||
            saved.Role != role || saved.VideoId != original.VideoId ||
            saved.SourceSha256 != original.Sha256 ||
            saved.ComputedAtUtc < original.UploadedAtUtc ||
            saved.Tracking is null || saved.Spectral is null ||
            saved.Tracking.Method != LocalTrackingMethod ||
            saved.Spectral.Algorithm != PendulumSpectralEstimator.Version ||
            !string.Equals(saved.EvidenceSha256, LocalEvidenceHash(saved),
                StringComparison.Ordinal))
            throw new InvalidDataException("Local evidence provenance mismatch.");

        return original with
        {
            Tracking = saved.Tracking, Spectral = saved.Spectral,
            LocalEvidenceStatus = "READY"
        };
    }

    /// <summary>
    /// Compute local observations only for a consumer that needs them.
    /// Does not rewrite original evidence, call Meta or access SQLite.
    /// </summary>
    public async Task<DirectorVideoEvidence> EnsureLocalEvidenceAsync(
        string id, string role, CancellationToken ct)
    {
        id = Path.GetFileName(Folder(id));
        if (role is not ("baseline" or "followup"))
            throw new ArgumentException("Invalid video role.");
        await RequirePreparedAnalysisAsync(id, ct);
        var (initial, _) = await GetVerifiedOriginalAsync(id, role, ct);
        var existing = await AttachLocalEvidenceAsync(id, role, initial, ct);
        if (existing.LocalEvidenceStatus == "READY") return existing;

        await localEvidenceGate.WaitAsync(ct);
        try
        {
            await RequirePreparedAnalysisAsync(id, ct);
            var (original, path) = await GetVerifiedOriginalAsync(id, role, ct);
            existing = await AttachLocalEvidenceAsync(id, role, original, ct);
            if (existing.LocalEvidenceStatus == "READY") return existing;
            if (!decoder.Available)
                throw new DirectorLocalEvidenceUnavailableException();

            LocalMotionTrack tracking;
            try
            {
                tracking = await decoder.TrackAsync(path, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new DirectorLocalEvidenceUnavailableException();
            }
            catch (InvalidOperationException)
            {
                throw new DirectorLocalEvidenceUnavailableException();
            }
            ct.ThrowIfCancellationRequested();
            var spectral = PendulumSpectralEstimator.Estimate(
                tracking.Observations, tracking.DetectionRate);
            ct.ThrowIfCancellationRequested();

            var (verifiedAfter, _) = await GetVerifiedOriginalAsync(id, role, ct);
            if (verifiedAfter.VideoId != original.VideoId ||
                verifiedAfter.Sha256 != original.Sha256 ||
                verifiedAfter.Bytes != original.Bytes ||
                verifiedAfter.UploadedAtUtc != original.UploadedAtUtc)
                throw new InvalidDataException(
                    "Original video changed during local processing.");

            var receipt = new DirectorLocalEvidenceReceipt(
                LocalEvidenceProtocol, id, role, original.VideoId,
                original.Sha256, DateTimeOffset.UtcNow, tracking, spectral, "");
            receipt = receipt with { EvidenceSha256 = LocalEvidenceHash(receipt) };
            var target = LocalEvidencePath(id, role);
            try
            {
                await WriteAtomicNewAsync(target, receipt, ct);
            }
            catch (IOException) when (File.Exists(target))
            {
                // Another process may have committed first. Validate its result;
                // never silently replace an existing evidence record.
            }
            var result = await AttachLocalEvidenceAsync(id, role, original, ct);
            if (result.LocalEvidenceStatus != "READY")
                throw new InvalidDataException("Local evidence was not committed.");
            return result;
        }
        finally { localEvidenceGate.Release(); }
    }
}
