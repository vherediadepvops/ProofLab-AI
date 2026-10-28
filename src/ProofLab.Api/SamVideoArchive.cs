using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProofLab.Api;

/// <summary>
/// SQLite locates private backend archives; every read verifies both files.
/// No HTTP endpoint accepts records for this store or imports inspector JSON.
/// </summary>
public sealed class SamVideoArchive(string contentRoot, SamVideoEvidenceIndex index)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = true };
    public sealed record Stored(SamIndexedEvidence Entry, SamVideoReceipt Receipt, string Raw);
    public sealed record CompletedBundle(SamVideoReceipt Receipt, string RawResponseJson);
    public sealed record Pending(bool LegacyOriginal, bool LegacyRetry, bool NewRun)
    {
        public bool Any => LegacyOriginal || LegacyRetry || NewRun;
        public string Status => NewRun ? "NEW_ANALYSIS_REQUIRES_MANUAL_RECONCILIATION" :
            LegacyRetry ? "SECOND_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION" :
            Any ? "PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION" : "NOT_ANALYZED_WITH_SAM";
    }
    private string Base => Path.Combine(contentRoot, ".private", "physics", "sam-cache");
    public string Folder(string key) => Path.Combine(Base, key);
    public string RunFolder(string key, string id) => Path.Combine(Folder(key), "runs", id);
    public static string Hash(string contents) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contents))).ToLowerInvariant();
    private static bool Sha(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    public static async Task<T> ReadJsonAsync<T>(string path, CancellationToken ct)
    {
        try
        {
            await using var input = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(input, Json, ct) ??
                throw new InvalidDataException("Invalid scientific cache document.");
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid scientific cache JSON.", ex); }
    }

    public static async Task WriteExclusiveAsync<T>(string path, T value, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, value, Json, ct);
        await stream.FlushAsync(ct);
        stream.Flush(flushToDisk: true);
    }

    public static void Validate(SamVideoReceipt receipt, SamVideoSource source,
        string key, string raw, string? analysisId)
    {
        var pts = receipt.FramePtsSeconds;
        if (string.IsNullOrEmpty(raw) || raw.Length > 85_000_000 ||
            receipt.CacheKey != key || receipt.OriginalVideoSha256 != source.Sha256.ToLowerInvariant() ||
            string.IsNullOrWhiteSpace(receipt.VideoId) ||
            receipt.Model != PendulumSamCache.SamModel || receipt.Concept != PendulumSamCache.Prompt ||
            receipt.RequestProtocol != PendulumSamCache.Protocol ||
            receipt.SourceFrameCount is < 50 or > 2500 || pts is null ||
            pts.Length != receipt.SourceFrameCount ||
            pts.Any(t => !double.IsFinite(t) || t < 0) || Math.Abs(pts[0]) > 0.005 ||
            pts.Zip(pts.Skip(1), (a,b) => b <= a).Any(x => x) ||
            !Sha(receipt.SubmittedMp4Sha256) ||
            string.IsNullOrWhiteSpace(receipt.ProviderResponseId) ||
            receipt.CapturedAtUtc == default || !double.IsFinite(receipt.EstimatedCostUsd) ||
            receipt.EstimatedCostUsd <= 0 || receipt.EstimatedCostUsd > 0.500000001 ||
            receipt.ProviderEvidenceStatus != "COMPLETED_PROVIDER_RESPONSE_REQUIRES_VISUAL_REVIEW" ||
            receipt.AnalysisId != analysisId || receipt.ResponseJsonSha256 != Hash(raw))
            throw new InvalidDataException("Stored SAM video provenance mismatch.");
        try
        {
            using var response = JsonDocument.Parse(raw);
            var root = response.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.String ||
                model.GetString() != PendulumSamCache.SamModel ||
                !root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String ||
                status.GetString() != "completed")
                throw new InvalidDataException("Incomplete SAM response archive.");
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid SAM response archive.", ex); }
    }

    private async Task<Stored> ReadFolderAsync(SamVideoSource source, string key,
        string relative, CancellationToken ct)
    {
        if (!SamVideoEvidenceIndex.ValidRelativePath(key, relative))
            throw new InvalidDataException("Invalid private SAM archive path.");
        var folder = Path.Combine(Base, relative.Replace('/', Path.DirectorySeparatorChar));
        var receiptPath = Path.Combine(folder, "receipt.json");
        var responsePath = Path.Combine(folder, "response.json");
        if (!File.Exists(receiptPath) || !File.Exists(responsePath))
            throw new InvalidDataException("Incomplete SAM archive. Reconciliation required; no new charge.");
        var receiptText = await File.ReadAllTextAsync(receiptPath, ct);
        var receipt = await ReadJsonAsync<SamVideoReceipt>(receiptPath, ct);
        var raw = await File.ReadAllTextAsync(responsePath, ct);
        var id = relative == key ? null : relative[(key.Length + 6)..];
        Validate(receipt, source, key, raw, id);
        var existing = relative == key ? await index.FindArchiveAsync(key, relative, ct) : null;
        var entry = new SamIndexedEvidence(id ?? existing?.AnalysisId ?? key[..32], source.Sha256.ToLowerInvariant(),
            key, PendulumSamCache.SamModel, PendulumSamCache.Protocol, Hash(PendulumSamCache.Prompt),
            Hash(raw), Hash(receiptText), relative, receipt.SourceFrameCount,
            receipt.CapturedAtUtc.ToUniversalTime().ToString("O"), SamVideoEvidenceIndex.Provenance);
        return new(entry, receipt, raw);
    }

    private async Task VerifyIndexedAsync(SamVideoSource source, string key,
        SamIndexedEvidence entry, CancellationToken ct)
    {
        var stored = await ReadFolderAsync(source, key, entry.ArchiveRelativePath, ct);
        if (stored.Entry != entry)
            throw new InvalidDataException("SQLite SAM receipt or response integrity mismatch.");
    }

    // SQLite is consulted first. Unindexed durable completions are recovered
    // from private files after a process interruption, never by calling Meta.
    public async Task<List<Stored>> LoadAsync(SamVideoSource source, string key, CancellationToken ct)
    {
        var latest = await index.FindLatestAsync(source.Sha256.ToLowerInvariant(),
            key, Hash(PendulumSamCache.Prompt), ct);
        if (latest is not null) await VerifyIndexedAsync(source, key, latest, ct);
        var result = new List<Stored>();
        var folder = Folder(key);
        if (!Directory.Exists(folder)) return result;
        async Task Read(string directory, string relative)
        {
            var journal = Path.Combine(directory, "completed-result.json");
            if (File.Exists(journal))
            {
                var bundle = await ReadJsonAsync<CompletedBundle>(journal, ct);
                var id = relative == key ? null : relative[(key.Length + 6)..];
                Validate(bundle.Receipt, source, key, bundle.RawResponseJson, id);
                await FinishBundleAsync(directory, bundle, ct);
            }
            if (File.Exists(Path.Combine(directory, "receipt.json")) ||
                File.Exists(Path.Combine(directory, "response.json")))
            {
                var stored = await ReadFolderAsync(source, key, relative, ct);
                await index.RegisterVerifiedAsync(stored.Entry, ct);
                result.Add(stored);
                if (File.Exists(journal)) File.Delete(journal);
            }
            else if (File.Exists(Path.Combine(directory, "response-writing.tmp")))
                throw new InvalidDataException("Incomplete legacy SAM archive needs reconciliation.");
        }
        await Read(folder, key);
        var runs = Path.Combine(folder, "runs");
        if (Directory.Exists(runs))
            foreach (var directory in Directory.EnumerateDirectories(runs))
                await Read(directory, key + "/runs/" + Path.GetFileName(directory));
        return result;
    }

    private static async Task EnsureFileAsync(string path, string contents, CancellationToken ct)
    {
        if (File.Exists(path))
        {
            if (await File.ReadAllTextAsync(path, ct) != contents)
                throw new InvalidDataException("Immutable SAM archive differs from completion journal.");
            return;
        }
        // Atomic rename prevents serving a truncated receipt or response.
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
        {
            var bytes = Encoding.UTF8.GetBytes(contents);
            await stream.WriteAsync(bytes, ct);
            await stream.FlushAsync(ct);
            stream.Flush(flushToDisk: true);
        }
        try { File.Move(temporary, path, overwrite: false); }
        catch (IOException) when (File.Exists(path))
        {
            if (await File.ReadAllTextAsync(path, ct) != contents) throw;
            File.Delete(temporary);
        }
    }

    private static async Task FinishBundleAsync(string folder, CompletedBundle bundle, CancellationToken ct)
    {
        await EnsureFileAsync(Path.Combine(folder, "response.json"), bundle.RawResponseJson, ct);
        await EnsureFileAsync(Path.Combine(folder, "receipt.json"),
            JsonSerializer.Serialize(bundle.Receipt, Json), ct);
    }

    public async Task<Stored> CommitAsync(SamVideoSource source, string key,
        SamVideoReceipt receipt, string raw)
    {
        var id = receipt.AnalysisId ?? throw new InvalidDataException("New analysis ID required.");
        Validate(receipt, source, key, raw, id);
        var folder = RunFolder(key, id);
        var bundle = new CompletedBundle(receipt, raw);
        // Once the paid provider has completed, a disconnected browser must
        // not discard its result. A crash after this journal is recoverable.
        await EnsureFileAsync(Path.Combine(folder, "completed-result.json"),
            JsonSerializer.Serialize(bundle, Json), CancellationToken.None);
        await FinishBundleAsync(folder, bundle, CancellationToken.None);
        var stored = await ReadFolderAsync(source, key, key + "/runs/" + id, CancellationToken.None);
        await index.RegisterVerifiedAsync(stored.Entry, CancellationToken.None);
        // Keep the small immutable receipt, response and attempt; the durable
        // completion journal is no longer needed after index registration.
        File.Delete(Path.Combine(folder, "completed-result.json"));
        return stored;
    }

    public async Task<Pending> OutstandingAsync(SamVideoSource source, string key,
        IReadOnlyList<Stored> stored, CancellationToken ct)
    {
        async Task<SamOriginalAttempt?> Attempt(string path, string expectedStatus)
        {
            if (!File.Exists(path)) return null;
            var a = await ReadJsonAsync<SamOriginalAttempt>(path, ct);
            if (a.Protocol != PendulumSamCache.Protocol ||
                !string.Equals(a.SourceSha256, source.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !Sha(a.SubmittedMp4Sha256) || a.FrameCount is < 50 or > 2500 ||
                a.RequestedAtUtc == default || a.Status != expectedStatus)
                throw new InvalidDataException("Prior SAM attempt provenance mismatch.");
            return a;
        }
        bool Resolved(SamOriginalAttempt a, Stored? completed) =>
            completed is not null && completed.Receipt.CapturedAtUtc >= a.RequestedAtUtc &&
            completed.Receipt.SourceFrameCount == a.FrameCount &&
            completed.Receipt.SubmittedMp4Sha256 == a.SubmittedMp4Sha256;
        var root = Folder(key);
        var original = await Attempt(Path.Combine(root, "attempt-started.json"),
            "MAY_BE_BILLABLE_NO_AUTOMATIC_RETRY");
        var retry = await Attempt(Path.Combine(root, "manual-retry-started.json"),
            "SECOND_ATTEMPT_MAY_BE_BILLABLE_NO_AUTOMATIC_RETRY");
        var legacyComplete = stored.FirstOrDefault(s => s.Entry.ArchiveRelativePath == key);
        var retryComplete = retry?.AnalysisId is { } retryId
            ? stored.FirstOrDefault(s => s.Receipt.AnalysisId == retryId) : legacyComplete;
        // Historical owner approval allowed exactly one reconciled retry.
        // Its marker permanently blocks a third charge for this cache key,
        // even if the second attempt completed successfully.
        var historicalRetryLock = retry is not null;
        var unresolvedOriginal = original is not null && !Resolved(original, legacyComplete) &&
            !(retry is not null && Resolved(retry, retryComplete));
        var runs = Path.Combine(root, "runs");
        var unresolvedRun = false;
        if (Directory.Exists(runs))
            foreach (var folder in Directory.EnumerateDirectories(runs))
            {
                var a = await Attempt(Path.Combine(folder, "attempt-started.json"),
                    "MAY_BE_BILLABLE_NO_AUTOMATIC_RETRY");
                if (a is null) continue;
                if (a.AnalysisId != Path.GetFileName(folder))
                    throw new InvalidDataException("SAM attempt analysis identity mismatch.");
                if (!Resolved(a, stored.FirstOrDefault(s => s.Receipt.AnalysisId == a.AnalysisId)))
                    unresolvedRun = true;
            }
        return new(unresolvedOriginal, historicalRetryLock, unresolvedRun);
    }
}
