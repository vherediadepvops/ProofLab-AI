using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace ProofLab.Api;

/// <summary>Private append-only observations and predictions; attempts retain raw failures.</summary>
public sealed class MuseExperimentStore
{
    private readonly string connectionString,lockFolder;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public MuseExperimentStore(IWebHostEnvironment env)
    {
        var root=Path.Combine(env.ContentRootPath,".private","physics");
        Directory.CreateDirectory(root);lockFolder=Path.Combine(root,"muse-locks");Directory.CreateDirectory(lockFolder);
        connectionString=new SqliteConnectionStringBuilder {
            DataSource=Path.Combine(root,"muse-experiments.sqlite"),Pooling=false,DefaultTimeout=10
        }.ToString();
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS muse_observations(
              id TEXT PRIMARY KEY,study_id TEXT NOT NULL,video_sha TEXT NOT NULL,analysis_id TEXT NOT NULL,
              created TEXT NOT NULL,payload TEXT NOT NULL,payload_sha TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS muse_attempts(
              observation_id TEXT PRIMARY KEY,study_id TEXT NOT NULL,started TEXT NOT NULL,status TEXT NOT NULL,
              raw_response TEXT,response_sha TEXT,evaluation TEXT);
            CREATE TABLE IF NOT EXISTS muse_predictions(
              study_id TEXT PRIMARY KEY,observation_id TEXT NOT NULL,response_id TEXT NOT NULL,
              frozen TEXT NOT NULL,payload TEXT NOT NULL,payload_sha TEXT NOT NULL);
            CREATE TRIGGER IF NOT EXISTS muse_observation_no_update BEFORE UPDATE ON muse_observations
              BEGIN SELECT RAISE(ABORT,'Immutable observation'); END;
            CREATE TRIGGER IF NOT EXISTS muse_observation_no_delete BEFORE DELETE ON muse_observations
              BEGIN SELECT RAISE(ABORT,'Immutable observation'); END;
            CREATE TRIGGER IF NOT EXISTS muse_prediction_no_update BEFORE UPDATE ON muse_predictions
              BEGIN SELECT RAISE(ABORT,'Immutable prediction'); END;
            CREATE TRIGGER IF NOT EXISTS muse_prediction_no_delete BEFORE DELETE ON muse_predictions
              BEGIN SELECT RAISE(ABORT,'Immutable prediction'); END;
            """;
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open(){var db=new SqliteConnection(connectionString);db.Open();return db;}
    public static void StudyId(string id) {
        if(string.IsNullOrEmpty(id)||id.Length!=32||id.Any(c=>!Uri.IsHexDigit(c)))throw new ArgumentException("Invalid study.");
    }
    public static void HashId(string id) {
        if(string.IsNullOrEmpty(id)||id.Length!=64||id.Any(c=>!Uri.IsHexDigit(c)))throw new ArgumentException("Invalid observation.");
    }
    public FileStream LockStudy(string id) {
        StudyId(id);
        return new FileStream(Path.Combine(lockFolder,id.ToLowerInvariant()+".lock"),
            FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    }
    public MuseObservation? Observation(string id)
    {
        HashId(id);using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT id,study_id,video_sha,analysis_id,created,payload,payload_sha FROM muse_observations WHERE id=$id";
        cmd.Parameters.AddWithValue("$id",id);using var r=cmd.ExecuteReader();
        if(!r.Read())return null;
        var o=new MuseObservation(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),
            DateTimeOffset.Parse(r.GetString(4)),r.GetString(5),r.GetString(6));
        using var doc=JsonDocument.Parse(o.PayloadJson);
        if(KnotPolicy.Hash(o.PayloadJson)!=o.PayloadSha256 ||
           KnotPolicy.Hash(doc.RootElement.GetProperty("identity").GetRawText())!=o.Id ||
           doc.RootElement.GetProperty("identity").GetProperty("studyId").GetString()!=o.StudyId)
            throw new InvalidDataException("Observation integrity failure.");
        return o;
    }
    public MuseObservation SaveObservation(MuseObservation o)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT OR IGNORE INTO muse_observations VALUES($id,$s,$v,$a,$t,$p,$h)";
        cmd.Parameters.AddWithValue("$id",o.Id);cmd.Parameters.AddWithValue("$s",o.StudyId);
        cmd.Parameters.AddWithValue("$v",o.VideoSha256);cmd.Parameters.AddWithValue("$a",o.AnalysisId);
        cmd.Parameters.AddWithValue("$t",o.CreatedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$p",o.PayloadJson);cmd.Parameters.AddWithValue("$h",o.PayloadSha256);
        cmd.ExecuteNonQuery();
        var existing=Observation(o.Id)!;
        if(existing.PayloadSha256!=o.PayloadSha256)throw new InvalidDataException("Observation collision.");
        return existing;
    }
    public MuseObservation? LatestObservation(string study)
    {
        StudyId(study);using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT id FROM muse_observations WHERE study_id=$s ORDER BY created DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$s",study);
        return cmd.ExecuteScalar() is string id?Observation(id):null;
    }
    public string? AttemptStatus(string observationId)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT status FROM muse_attempts WHERE observation_id=$id";
        cmd.Parameters.AddWithValue("$id",observationId);return cmd.ExecuteScalar() as string;
    }
    public void BeginAttempt(MuseObservation o)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO muse_attempts(observation_id,study_id,started,status) VALUES($id,$s,$t,'IN_FLIGHT_NO_RETRY')";
        cmd.Parameters.AddWithValue("$id",o.Id);cmd.Parameters.AddWithValue("$s",o.StudyId);
        cmd.Parameters.AddWithValue("$t",DateTimeOffset.UtcNow.ToString("O"));cmd.ExecuteNonQuery();
    }
    public void SaveRaw(string id,string raw)
    {
        if(raw.Length>262144)throw new InvalidDataException("Provider response limit.");
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="UPDATE muse_attempts SET raw_response=$r,response_sha=$h,status='RAW_RECEIVED' WHERE observation_id=$id AND raw_response IS NULL";
        cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$r",raw);
        cmd.Parameters.AddWithValue("$h",KnotPolicy.Hash(raw));
        if(cmd.ExecuteNonQuery()!=1)throw new InvalidOperationException("Attempt already completed.");
    }
    public void Fail(string id,string status)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="UPDATE muse_attempts SET status=$s WHERE observation_id=$id AND evaluation IS NULL";
        cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$s",status);cmd.ExecuteNonQuery();
    }
    public MuseEvaluation? Evaluation(string observationId)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT raw_response,response_sha,evaluation FROM muse_attempts WHERE observation_id=$id";
        cmd.Parameters.AddWithValue("$id",observationId);using var r=cmd.ExecuteReader();
        if(!r.Read()||r.IsDBNull(2))return null;
        var raw=r.GetString(0);var sha=r.GetString(1);
        var e=JsonSerializer.Deserialize<MuseEvaluation>(r.GetString(2),Json)!;
        var o=Observation(observationId)??throw new InvalidDataException("Missing observation.");
        using var doc=JsonDocument.Parse(o.PayloadJson);
        var receipt=MuseExperimentTransport.Validate(raw);
        var answer=MuseExperimentPolicy.Parse(KnotMetaTransport.OutputText(raw),doc.RootElement);
        if(e.ResponseId!=receipt.ProviderResponseId||e.InputTokens!=receipt.InputTokens||e.OutputTokens!=receipt.OutputTokens||
           KnotPolicy.Hash(raw)!=sha||e.RawResponseSha256!=sha||e.ObservationId!=o.Id||e.StudyId!=o.StudyId||
           e.Model!=MuseExperimentPolicy.Model||JsonSerializer.Serialize(e.Answer,Json)!=JsonSerializer.Serialize(answer,Json))
            throw new InvalidDataException("Evaluation integrity failure.");
        return e;
    }
    public MusePrediction? Prediction(string study)
    {
        StudyId(study);using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT observation_id,response_id,frozen,payload,payload_sha FROM muse_predictions WHERE study_id=$s";
        cmd.Parameters.AddWithValue("$s",study);using var r=cmd.ExecuteReader();
        if(!r.Read())return null;
        var p=new MusePrediction(study,r.GetString(0),r.GetString(1),DateTimeOffset.Parse(r.GetString(2)),r.GetString(3),r.GetString(4));
        var o=Observation(p.ObservationId)??throw new InvalidDataException("Prediction input missing.");
        var e=Evaluation(o.Id)??throw new InvalidDataException("Prediction evaluation missing.");
        var expected=JsonSerializer.Serialize(MuseExperimentPolicy.FreezePayload(o,e,p.FrozenAtUtc),Json);
        if(KnotPolicy.Hash(p.PayloadJson)!=p.Sha256||expected!=p.PayloadJson||p.ResponseId!=e.ResponseId||
           e.Answer.Decision!="PROPOSE_EXPERIMENT"||o.StudyId!=study||p.FrozenAtUtc<o.CreatedAtUtc)
            throw new InvalidDataException("Frozen prediction integrity failure.");
        return p;
    }
    public void Complete(MuseObservation o,MuseEvaluation e)
    {
        using var db=Open();using var tx=db.BeginTransaction();
        if(e.Answer.Decision=="PROPOSE_EXPERIMENT") {
            var stamp=DateTimeOffset.UtcNow;
            var json=JsonSerializer.Serialize(MuseExperimentPolicy.FreezePayload(o,e,stamp),Json);
            using var freeze=db.CreateCommand();freeze.Transaction=tx;
            freeze.CommandText="INSERT INTO muse_predictions VALUES($s,$o,$r,$t,$p,$h)";
            freeze.Parameters.AddWithValue("$s",o.StudyId);freeze.Parameters.AddWithValue("$o",o.Id);
            freeze.Parameters.AddWithValue("$r",e.ResponseId);freeze.Parameters.AddWithValue("$t",stamp.ToString("O"));
            freeze.Parameters.AddWithValue("$p",json);freeze.Parameters.AddWithValue("$h",KnotPolicy.Hash(json));freeze.ExecuteNonQuery();
        }
        using var cmd=db.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="UPDATE muse_attempts SET evaluation=$e,status='COMPLETED' WHERE observation_id=$id AND response_sha=$h AND evaluation IS NULL";
        cmd.Parameters.AddWithValue("$id",o.Id);cmd.Parameters.AddWithValue("$h",e.RawResponseSha256);
        cmd.Parameters.AddWithValue("$e",JsonSerializer.Serialize(e,Json));
        if(cmd.ExecuteNonQuery()!=1)throw new InvalidDataException("Attempt response mismatch.");
        tx.Commit();
    }
}
