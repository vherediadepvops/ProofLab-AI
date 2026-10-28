using Microsoft.Data.Sqlite;

namespace ProofLab.Api;

/// <summary>
/// Stage 4 preparation: an OFFLINE index of previously verified SAM archives.
/// This component does not call Meta, approve billing, rewrite attempt markers,
/// or change the scientific player's observation source.
///
/// IMPORTANT: a SHA check proves file integrity, NOT provider authenticity.
/// Only a separately validated HTTPS provider receipt may be indexed.
/// </summary>
public sealed record SamIndexedEvidence(
    string AnalysisId, string VideoSha256, string CacheKey,
    string Model, string Protocol, string PromptSha256,
    string ResponseSha256, string ReceiptSha256,
    string ArchiveRelativePath, int SourceFrames,
    string CompletedAtUtc, string ProvenanceStatus);

public sealed class SamVideoEvidenceIndex
{
    public const string Provenance = "PROVIDER_RECEIPT_PREVIOUSLY_VERIFIED";
    private readonly string connectionString;
    private readonly SemaphoreSlim gate = new(1, 1);

    public SamVideoEvidenceIndex(string contentRoot)
    {
        var folder = Path.Combine(contentRoot, ".private", "physics");
        Directory.CreateDirectory(folder);
        connectionString = new SqliteConnectionStringBuilder {
            DataSource = Path.Combine(folder, "sam-video-evidence.sqlite"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
        using var db = new SqliteConnection(connectionString);
        db.Open();
        using var setup = db.CreateCommand();
        setup.CommandText = """
            CREATE TABLE IF NOT EXISTS sam_video_evidence (
              analysis_id TEXT PRIMARY KEY,
              video_sha256 TEXT NOT NULL,
              cache_key TEXT NOT NULL,
              model TEXT NOT NULL,
              protocol TEXT NOT NULL,
              prompt_sha256 TEXT NOT NULL,
              response_sha256 TEXT NOT NULL,
              receipt_sha256 TEXT NOT NULL,
              archive_relative_path TEXT NOT NULL,
              source_frames INTEGER NOT NULL,
              completed_at_utc TEXT NOT NULL,
              provenance_status TEXT NOT NULL
            );
            -- Each separately authorized analysis is immutable, even when
            -- the provider returns identical bytes. Preserve all existing rows.
            DROP INDEX IF EXISTS sam_video_evidence_unique;
            CREATE INDEX IF NOT EXISTS sam_video_evidence_response
                ON sam_video_evidence(cache_key, response_sha256);
            CREATE INDEX IF NOT EXISTS sam_video_evidence_lookup
                ON sam_video_evidence(video_sha256, model, protocol, prompt_sha256);
            """;
        setup.ExecuteNonQuery();
    }

    private static bool IsSha(string value) =>
        value.Length == 64 && value.All(c =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
    private static bool IsId(string value) =>
        value.Length == 32 && value.All(c =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

    public static void Validate(SamIndexedEvidence e)
    {
        if (!IsId(e.AnalysisId) || !IsSha(e.VideoSha256) ||
            !IsSha(e.CacheKey) || !IsSha(e.PromptSha256) ||
            !IsSha(e.ResponseSha256) || !IsSha(e.ReceiptSha256) ||
            e.Model != "sam-3.1" ||
            (e.Protocol != "PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V2" && e.Protocol != AssemblyPolicy.SamProtocol) ||
            e.ProvenanceStatus != Provenance ||
            e.SourceFrames is < 50 or > 2500 ||
            !DateTimeOffset.TryParse(e.CompletedAtUtc, out _) ||
            !ValidRelativePath(e.CacheKey, e.ArchiveRelativePath))
            throw new ArgumentException("Invalid or unverified SAM index metadata.");
    }

    /// <summary>Only a file path under the private SHA-key directory.</summary>
    public static bool ValidRelativePath(string cacheKey, string relative)
    {
        if (!IsSha(cacheKey)) return false;
        if (relative == cacheKey) return true; // existing legacy archive
        var prefix = cacheKey + "/runs/";
        return relative.StartsWith(prefix, StringComparison.Ordinal) &&
               IsId(relative[prefix.Length..]); // never allow ../ traversal
    }

    /// <summary>
    /// Insert only after the separate backend has verified the provider
    /// receipt and SHA-256 of the response archive. Never accept a pasted
    /// JSON from the offline inspector as trusted evidence.
    /// </summary>
    public async Task RegisterVerifiedAsync(SamIndexedEvidence item,
        CancellationToken ct)
    {
        Validate(item);
        await gate.WaitAsync(ct);
        try
        {
            await using var db = new SqliteConnection(connectionString);
            await db.OpenAsync(ct);
            await using var sql = db.CreateCommand();
            sql.CommandText = """
                INSERT OR IGNORE INTO sam_video_evidence
                (analysis_id,video_sha256,cache_key,model,protocol,prompt_sha256,
                 response_sha256,receipt_sha256,archive_relative_path,
                 source_frames,completed_at_utc,provenance_status)
                VALUES(@id,@video,@key,@model,@protocol,@prompt,
                       @response,@receipt,@path,@frames,@date,@status)
                """;
            sql.Parameters.AddWithValue("@id", item.AnalysisId);
            sql.Parameters.AddWithValue("@video", item.VideoSha256);
            sql.Parameters.AddWithValue("@key", item.CacheKey);
            sql.Parameters.AddWithValue("@model", item.Model);
            sql.Parameters.AddWithValue("@protocol", item.Protocol);
            sql.Parameters.AddWithValue("@prompt", item.PromptSha256);
            sql.Parameters.AddWithValue("@response", item.ResponseSha256);
            sql.Parameters.AddWithValue("@receipt", item.ReceiptSha256);
            sql.Parameters.AddWithValue("@path", item.ArchiveRelativePath);
            sql.Parameters.AddWithValue("@frames", item.SourceFrames);
            sql.Parameters.AddWithValue("@date", item.CompletedAtUtc);
            sql.Parameters.AddWithValue("@status", item.ProvenanceStatus);
            if(await sql.ExecuteNonQueryAsync(ct)==0)
            {
                // Idempotent duplicate MUST be identical; never silently
                // accept a conflicting scientific provenance row.
                await using var duplicate = db.CreateCommand();
                duplicate.CommandText = """
                    SELECT analysis_id,video_sha256,cache_key,model,protocol,
                           prompt_sha256,response_sha256,receipt_sha256,
                           archive_relative_path,source_frames,completed_at_utc,
                           provenance_status
                    FROM sam_video_evidence
                    WHERE analysis_id=@id
                    LIMIT 1
                    """;
                duplicate.Parameters.AddWithValue("@id",item.AnalysisId);
                duplicate.Parameters.AddWithValue("@key",item.CacheKey);
                duplicate.Parameters.AddWithValue("@response",item.ResponseSha256);
                await using var row=await duplicate.ExecuteReaderAsync(ct);
                if(!await row.ReadAsync(ct) ||
                   item!=ReadRow(row))
                    throw new InvalidDataException(
                        "Conflicting SAM scientific evidence metadata.");
            }
        }
        finally { gate.Release(); }
    }

    public async Task<SamIndexedEvidence?> FindArchiveAsync(string key, string path, CancellationToken ct)
    {
        if (!ValidRelativePath(key, path))
            throw new ArgumentException("Invalid SAM archive lookup.");
        await gate.WaitAsync(ct);
        try
        {
            await using var db = new SqliteConnection(connectionString);
            await db.OpenAsync(ct);
            await using var sql = db.CreateCommand();
            sql.CommandText = """
                SELECT analysis_id,video_sha256,cache_key,model,protocol,
                       prompt_sha256,response_sha256,receipt_sha256,
                       archive_relative_path,source_frames,completed_at_utc,provenance_status
                FROM sam_video_evidence WHERE cache_key=@key AND archive_relative_path=@path
                LIMIT 2
                """;
            sql.Parameters.AddWithValue("@key", key);
            sql.Parameters.AddWithValue("@path", path);
            await using var row = await sql.ExecuteReaderAsync(ct);
            if (!await row.ReadAsync(ct)) return null;
            var entry = ReadRow(row);
            Validate(entry);
            if (await row.ReadAsync(ct))
                throw new InvalidDataException("Multiple SAM analyses share an immutable archive path.");
            return entry;
        }
        finally { gate.Release(); }
    }

    private static SamIndexedEvidence ReadRow(SqliteDataReader row) => new(
        row.GetString(0),row.GetString(1),row.GetString(2),
        row.GetString(3),row.GetString(4),row.GetString(5),
        row.GetString(6),row.GetString(7),row.GetString(8),
        row.GetInt32(9),row.GetString(10),row.GetString(11));

    /// <summary>
    /// Read only: no provider calls, no inferred masks. Consumer MUST
    /// revalidate both response and receipt hashes on disk before replay.
    /// </summary>
    public async Task<SamIndexedEvidence?> FindLatestAsync(
        string videoSha, string cacheKey, string promptSha,
        CancellationToken ct)
    {
        if (!IsSha(videoSha) || !IsSha(cacheKey) || !IsSha(promptSha))
            throw new ArgumentException("Invalid SAM cache lookup SHA.");
        await gate.WaitAsync(ct);
        try
        {
            await using var db = new SqliteConnection(connectionString);
            await db.OpenAsync(ct);
            await using var sql = db.CreateCommand();
            sql.CommandText = """
                SELECT analysis_id,video_sha256,cache_key,model,protocol,
                       prompt_sha256,response_sha256,receipt_sha256,
                       archive_relative_path,source_frames,completed_at_utc,
                       provenance_status
                FROM sam_video_evidence
                WHERE video_sha256=@video AND cache_key=@key AND
                      prompt_sha256=@prompt AND model='sam-3.1' AND
                      protocol='PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V2'
                ORDER BY completed_at_utc DESC, analysis_id DESC LIMIT 1
                """;
            sql.Parameters.AddWithValue("@video", videoSha);
            sql.Parameters.AddWithValue("@key", cacheKey);
            sql.Parameters.AddWithValue("@prompt", promptSha);
            await using var row = await sql.ExecuteReaderAsync(ct);
            if (!await row.ReadAsync(ct)) return null;
            var entry = new SamIndexedEvidence(
                row.GetString(0), row.GetString(1), row.GetString(2),
                row.GetString(3), row.GetString(4), row.GetString(5),
                row.GetString(6), row.GetString(7), row.GetString(8),
                row.GetInt32(9), row.GetString(10), row.GetString(11));
            Validate(entry);
            return entry;
        }
        finally { gate.Release(); }
    }
}
