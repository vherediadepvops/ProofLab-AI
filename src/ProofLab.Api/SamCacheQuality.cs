using System.Text.Json;
using Microsoft.Data.Sqlite;
namespace ProofLab.Api;

// A usability decision is derived metadata, never a replacement for an immutable provider receipt.
public sealed record SamCacheQuality(bool Usable,string Reason,double? PeriodSeconds,
    int UsedCycles,int DetectedFrames,int TotalFrames);
public sealed record SamReusableResult(string SourceSha256,bool CacheHit,string? AnalysisId,
    string? AdaptiveBatchId,int ExcludedResults,SamCacheQuality? Quality,string Storage="sqlite");

public sealed partial class SamVideoEvidenceIndex
{
    private const string QualitySchema="""
        CREATE TABLE IF NOT EXISTS sam_cache_quality (
          fingerprint TEXT PRIMARY KEY, video_sha256 TEXT NOT NULL, analysis_id TEXT NOT NULL,
          policy TEXT NOT NULL, usable INTEGER NOT NULL, quality_json TEXT NOT NULL,
          checked_at_utc TEXT NOT NULL
        );
        """;
    public async Task<SamCacheQuality?> ReadQualityAsync(string fingerprint,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try {
            await using var db=new SqliteConnection(connectionString);await db.OpenAsync(ct);
            await using var sql=db.CreateCommand();sql.CommandText=QualitySchema;await sql.ExecuteNonQueryAsync(ct);
            sql.CommandText="SELECT quality_json FROM sam_cache_quality WHERE fingerprint=@key";
            sql.Parameters.AddWithValue("@key",fingerprint);
            var json=await sql.ExecuteScalarAsync(ct) as string;
            return json is null?null:JsonSerializer.Deserialize<SamCacheQuality>(json);
        } finally {gate.Release();}
    }
    public async Task WriteQualityAsync(string fingerprint,string video,string analysis,string policy,
        SamCacheQuality quality,CancellationToken ct)
    {
        if(!IsSha(fingerprint)||!IsSha(video)||!IsId(analysis))throw new ArgumentException("Invalid quality identity.");
        await gate.WaitAsync(ct);
        try {
            await using var db=new SqliteConnection(connectionString);await db.OpenAsync(ct);
            await using var sql=db.CreateCommand();sql.CommandText=QualitySchema;await sql.ExecuteNonQueryAsync(ct);
            sql.CommandText="""
                INSERT OR IGNORE INTO sam_cache_quality
                (fingerprint,video_sha256,analysis_id,policy,usable,quality_json,checked_at_utc)
                VALUES(@key,@video,@analysis,@policy,@usable,@json,@date)
                """;
            sql.Parameters.AddWithValue("@key",fingerprint);sql.Parameters.AddWithValue("@video",video);
            sql.Parameters.AddWithValue("@analysis",analysis);sql.Parameters.AddWithValue("@policy",policy);
            sql.Parameters.AddWithValue("@usable",quality.Usable?1:0);
            sql.Parameters.AddWithValue("@json",JsonSerializer.Serialize(quality));
            sql.Parameters.AddWithValue("@date",DateTimeOffset.UtcNow.ToString("O"));
            await sql.ExecuteNonQueryAsync(ct);
        } finally {gate.Release();}
    }
}

public sealed class SamReusableEvidence(ISamVideoRuntime runtime,PendulumSamCache native,
    SamAdaptiveLiveService segments,SamVideoEvidenceIndex index,ISamPeriodWorker worker)
{
    public const string Policy="AUTOMATIC_PIVOT_PERIOD_CANDIDATE_V1";
    private readonly SemaphoreSlim gate=new(1,1);
    private static readonly JsonSerializerOptions Web=new(JsonSerializerDefaults.Web);
    // Includes reviews and geometry: a later correction gets a new decision, not a permanent blacklist.
    public static string Fingerprint(SamVideoArtifact a)=>SamVideoArchive.Hash(Policy+"\n"+
        JsonSerializer.Serialize(new{a.State.AnalysisId,a.Receipt,a.Suspension,a.SuspensionReview,a.Derivation},Web));
    public async Task<SamCacheQuality> AssessAsync(SamVideoArtifact archive,SamVideoSource source,
        string study,string role,CancellationToken ct)
    {
        if(archive.Receipt.OriginalVideoSha256!=source.Sha256 ||
           SamVideoArchive.Hash(archive.RawResponseJson)!=archive.Receipt.ResponseJsonSha256)
            throw new InvalidDataException("Cached evidence identity mismatch.");
        var fingerprint=Fingerprint(archive);
        var saved=await index.ReadQualityAsync(fingerprint,ct);if(saved is not null)return saved;
        string json;
        try {json=await worker.MeasureAsync(new{operation="cache-quality",archive,studyId=study,role,
            videoSha256=source.Sha256,sourceWidth=source.Width,sourceHeight=source.Height},ct);}
        catch(InvalidDataException) {
            var rejected=new SamCacheQuality(false,"INVALID_MEASUREMENT_EVIDENCE",null,0,0,archive.Receipt.SourceFrameCount);
            await index.WriteQualityAsync(fingerprint,source.Sha256,archive.State.AnalysisId!,Policy,rejected,ct);return rejected;
        }
        var quality=JsonSerializer.Deserialize<SamCacheQuality>(json,Web)??throw new InvalidDataException("Missing quality verdict.");
        if(quality.Usable && (quality.PeriodSeconds is not >0 || quality.UsedCycles<3 ||
            quality.DetectedFrames<=0 || quality.TotalFrames<quality.DetectedFrames))
            throw new InvalidDataException("Invalid quality verdict.");
        await index.WriteQualityAsync(fingerprint,source.Sha256,archive.State.AnalysisId!,Policy,quality,ct);
        return quality;
    }
    // No model transport is used here. Revalidate provenance before consulting the SQLite verdict.
    public async Task<SamReusableResult> FindAsync(string study,string role,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try {
            var source=await runtime.GetVerifiedSourceAsync(study,role,ct);
            var excluded=0;SamCacheQuality? last=null;
            foreach(var batch in await segments.RecoverableResultsAsync(source.Sha256,ct)) {
                SamVideoArtifact archive;
                try {archive=await segments.ReadPlaybackAsync(study,role,batch.BatchId,ct);}
                catch(Exception ex) when(ex is InvalidDataException or FileNotFoundException) {
                    excluded++;last=new(false,"INVALID_ARCHIVE",null,0,0,0);continue;
                }
                var q=await AssessAsync(archive,source,study,role,ct);
                if(q.Usable)return new(source.Sha256,true,batch.BatchId,batch.BatchId,excluded,q);
                excluded++;last=q;
            }
            await foreach(var archive in native.ReadCandidatesAsync(study,role,ct,()=>{excluded++;last=new(false,"INVALID_ARCHIVE",null,0,0,0);})) {
                var q=await AssessAsync(archive,source,study,role,ct);
                if(q.Usable)return new(source.Sha256,true,archive.State.AnalysisId,null,excluded,q);
                excluded++;last=q;
            }
            return new(source.Sha256,false,null,null,excluded,last);
        } finally {gate.Release();}
    }
}
