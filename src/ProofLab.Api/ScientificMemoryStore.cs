using Microsoft.Data.Sqlite;
using ProofLab.Science;

namespace ProofLab.Api;

// Local, derived experience index. The original Lean JSONL ledger remains authoritative.
public sealed record MemoryAdvice(
    string ProblemId, string GoalSha256, string LeanVersion,
    string Strategy, string SelectionMode, string? SourceAttemptId,
    string? SourceResearchId, int VerifiedCount, int RejectedCount,
    string ExplanationEs, string ExplanationEn);

public sealed class ScientificMemoryStore
{
    public const string LeanVersion = "leanprover/lean4:v4.35.0";
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string connectionString;

    public ScientificMemoryStore(IWebHostEnvironment environment)
    {
        var directory = Path.Combine(environment.ContentRootPath, ".private", "research");
        Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "scientific-experience.sqlite"),
            Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
        }.ToString();
        using var db = new SqliteConnection(connectionString);
        db.Open();
        // The embedded, versioned SQL file is the single source for this schema.
        // Existing local data is preserved by CREATE IF NOT EXISTS statements.
        using var schemaStream = typeof(ScientificMemoryStore).Assembly
            .GetManifestResourceStream(
                "ProofLab.Api.Database.ScientificExperienceSchemaV1.sql")
            ?? throw new InvalidOperationException("Scientific memory SQL schema is unavailable.");
        using var reader = new StreamReader(schemaStream);
        using var setup = db.CreateCommand();
        setup.CommandText = reader.ReadToEnd();
        setup.ExecuteNonQuery();
    }

    private SqliteConnection Connect() => new(connectionString);
    private static string GoalSha(MathProblem problem) =>
        MathResearchCatalog.Sha256(problem.LeanDeclaration);
    private static bool ValidId(string id) =>
        id.Length == 32 && id.All(Uri.IsHexDigit);

    public async Task RememberAsync(MathResearchAttempt item, CancellationToken ct)
    {
        var proof = item.Verification;
        var problem = MathResearchCatalog.Find(proof.ProblemId);
        if (!ValidId(item.ResearchId) || !ValidId(proof.AttemptId) ||
            problem.ResearchStatus != "KNOWN_THEOREM" ||
            proof.VerificationMethod != "Lean 4 kernel, constrained strategy, known benchmark" ||
            proof.IsNewDiscovery || proof.Outcome is not
                ("VERIFIED" or "REJECTED" or "UNAVAILABLE" or "TIMED_OUT") ||
            MathResearchCatalog.Sha256(MathResearchCatalog.BuildLeanSource(problem, proof.Strategy))
                != proof.SourceSha256)
            throw new ArgumentException("Memory requires authentic curated Lean evidence.");

        await gate.WaitAsync(ct);
        try
        {
            await using var db = Connect();
            await db.OpenAsync(ct);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO lean_experience
                (attempt_id,research_id,problem_id,goal_sha256,lean_version,
                 tactic,outcome,source_sha256,checked_utc)
                VALUES (@id,@research,@problem,@goal,@version,@tactic,
                        @outcome,@source,@time)
                """;
            cmd.Parameters.AddWithValue("@id", proof.AttemptId);
            cmd.Parameters.AddWithValue("@research", item.ResearchId);
            cmd.Parameters.AddWithValue("@problem", problem.Id);
            cmd.Parameters.AddWithValue("@goal", GoalSha(problem));
            cmd.Parameters.AddWithValue("@version", LeanVersion);
            cmd.Parameters.AddWithValue("@tactic", proof.Strategy);
            cmd.Parameters.AddWithValue("@outcome", proof.Outcome);
            cmd.Parameters.AddWithValue("@source", proof.SourceSha256);
            cmd.Parameters.AddWithValue("@time", proof.CheckedAtUtc.ToString("O"));
            if (await cmd.ExecuteNonQueryAsync(ct) == 0)
            {
                await using var previous = db.CreateCommand();
                previous.CommandText = """
                    SELECT research_id,problem_id,tactic,outcome,source_sha256
                    FROM lean_experience WHERE attempt_id=@id
                    """;
                previous.Parameters.AddWithValue("@id", proof.AttemptId);
                await using var row = await previous.ExecuteReaderAsync(ct);
                if (!await row.ReadAsync(ct) || row.GetString(0) != item.ResearchId ||
                    row.GetString(1) != problem.Id || row.GetString(2) != proof.Strategy ||
                    row.GetString(3) != proof.Outcome || row.GetString(4) != proof.SourceSha256)
                    throw new InvalidDataException("Conflicting Lean experience identifier.");
            }
        }
        finally { gate.Release(); }
    }

    public async Task<MemoryAdvice> RecallAsync(string problemId, CancellationToken ct)
    {
        // Match exact curated theorem declaration and pinned Lean toolchain.
        var problem = MathResearchCatalog.Find(problemId);
        var goalSha = GoalSha(problem);
        var evidence = new List<(string id,string research,string tactic,string outcome)>();
        await gate.WaitAsync(ct);
        try
        {
            await using var db = Connect();
            await db.OpenAsync(ct);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = """
                SELECT attempt_id,research_id,tactic,outcome,source_sha256
                FROM lean_experience
                WHERE problem_id=@problem AND goal_sha256=@goal
                  AND lean_version=@version
                ORDER BY checked_utc DESC LIMIT 256
                """;
            cmd.Parameters.AddWithValue("@problem", problem.Id);
            cmd.Parameters.AddWithValue("@goal", goalSha);
            cmd.Parameters.AddWithValue("@version", LeanVersion);
            await using var rows = await cmd.ExecuteReaderAsync(ct);
            while (await rows.ReadAsync(ct))
            {
                var tactic = rows.GetString(2);
                var digest = MathResearchCatalog.Sha256(
                    MathResearchCatalog.BuildLeanSource(problem, tactic));
                if (digest == rows.GetString(4))
                    evidence.Add((rows.GetString(0), rows.GetString(1),
                        tactic, rows.GetString(3)));
            }
        }
        finally { gate.Release(); }

        var verified = evidence.Where(e => e.outcome == "VERIFIED").ToArray();
        var rejected = evidence.Where(e => e.outcome == "REJECTED").ToArray();
        var previous = verified.FirstOrDefault();
        var hasVerified = verified.Length > 0;
        var failedRfl = rejected.FirstOrDefault(e => e.tactic == "rfl");
        var hasFailedRfl = rejected.Any(e => e.tactic == "rfl");
        var strategy = hasVerified ? previous.tactic : hasFailedRfl ? "omega" : "rfl";
        var mode = hasVerified ? "RECHECK_VERIFIED_MEMORY"
            : hasFailedRfl ? "AVOID_REJECTED_MEMORY" : "COLD_START";
        var support = hasVerified ? previous : failedRfl;
        return new MemoryAdvice(problem.Id, goalSha, LeanVersion, strategy, mode,
            hasVerified || hasFailedRfl ? support.id : null,
            hasVerified || hasFailedRfl ? support.research : null,
            verified.Length, rejected.Length,
            mode switch
            {
                "RECHECK_VERIFIED_MEMORY" => "Se prioriza una táctica previamente verificada en el mismo objetivo; Lean volverá a comprobarla.",
                "AVOID_REJECTED_MEMORY" => "Se recuerda un fallo de rfl en el objetivo exacto; se probará omega sin suponer éxito.",
                _ => "Sin memoria compatible: ensayo inicial rfl y lectura del diagnóstico real."
            },
            mode switch
            {
                "RECHECK_VERIFIED_MEMORY" => "A previously verified tactic for this exact goal is prioritized; Lean must check it again.",
                "AVOID_REJECTED_MEMORY" => "An earlier rfl rejection for the exact goal was recalled; omega will be tested without assuming success.",
                _ => "No compatible memory: start with rfl and inspect actual diagnostics."
            });
    }
}
