using System.Text.Json;
using ProofLab.Science;

namespace ProofLab.Api;

/// <summary>
/// An evidence bridge between the scientific method dossier and the constrained
/// Lean 4 benchmark verifier. Proof content and tactics remain server-curated.
/// This is local benchmark automation, NOT an autonomous AI discovery agent.
/// </summary>
public sealed record MathResearchAttempt(
    string ResearchId, string ExecutionMode, string ProblemResearchStatus,
    MathVerificationResult Verification);

public sealed record MathCycleRequest(string ProblemId);

public sealed record MathCycleResult(
    string ResearchId, string ProblemId, string Mode,
    IReadOnlyList<MathResearchAttempt> Attempts,
    string Conclusion, bool IsScientificDiscovery);

public sealed class MathEvidenceWorkflow(
    IWebHostEnvironment environment,
    ResearchWorkflowStore research,
    LeanProofVerifier verifier)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private const int MaximumAttemptsPerResearch = 24;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string Root => Path.Combine(environment.ContentRootPath,
        ".private", "research", "math-attempts");

    private static void EnsureExecutablePhase(ResearchProject project)
    {
        if (project.Discipline != "Mathematics")
            throw new ArgumentException("Lean attempts require a mathematical research project.");
        if (project.ActiveStage is not (4 or 5))
            throw new ArgumentException(
                "Approve the first three research reports before collecting evidence. " +
                "Attempts are allowed only during stage 4 or 5.");
    }

    private string PathFor(string id)
    {
        if (id.Length != 32 || id.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid research identifier.");
        return Path.Combine(Root, id.ToLowerInvariant() + ".jsonl");
    }

    private async Task<List<MathResearchAttempt>> ReadUnlockedAsync(
        string id, CancellationToken cancellationToken)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return [];
        var rows = await File.ReadAllLinesAsync(path, cancellationToken);
        var items = new List<MathResearchAttempt>(rows.Length);
        foreach (var line in rows.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            var record = JsonSerializer.Deserialize<MathResearchAttempt>(line, Json)
                ?? throw new InvalidDataException("Invalid recorded math evidence.");
            if (record.ResearchId != id)
                throw new InvalidDataException("Evidence research ID mismatch.");
            items.Add(record);
        }
        return items;
    }

    public async Task<IReadOnlyList<MathResearchAttempt>> ListAsync(
        string researchId, CancellationToken cancellationToken)
    {
        _ = await research.GetAsync(researchId, cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try { return await ReadUnlockedAsync(researchId, cancellationToken); }
        finally { gate.Release(); }
    }

    public async Task<MathResearchAttempt> RunAttemptAsync(
        string researchId, MathVerificationRequest request,
        string mode, CancellationToken cancellationToken)
    {
        var project = await research.GetAsync(researchId, cancellationToken);
        EnsureExecutablePhase(project);
        var problem = MathResearchCatalog.Find(request.ProblemId);
        if (mode is not ("MANUAL_CONTROL" or "DETERMINISTIC_BASELINE" or "SCRIPTED_OFFLINE_VALIDATION"))
            throw new ArgumentException("Unknown execution mode.");

        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await ReadUnlockedAsync(researchId, cancellationToken);
            if (existing.Count >= MaximumAttemptsPerResearch)
                throw new ArgumentException("Research attempt limit reached.");
            // VerifyAsync accepts no user code. It constructs a file only from
            // immutable predefined theorems and rfl/omega/simp.
            var verification = await verifier.VerifyAsync(request, cancellationToken);
            var result = new MathResearchAttempt(
                researchId, mode, problem.ResearchStatus, verification);
            Directory.CreateDirectory(Root);
            await File.AppendAllTextAsync(PathFor(researchId),
                JsonSerializer.Serialize(result, Json) + Environment.NewLine,
                cancellationToken);
            return result;
        }
        finally { gate.Release(); }
    }

    public async Task<MathCycleResult> RunDeterministicCycleAsync(
        string researchId, MathCycleRequest input, CancellationToken cancellationToken)
    {
        var project = await research.GetAsync(researchId, cancellationToken);
        EnsureExecutablePhase(project);
        _ = MathResearchCatalog.Find(input.ProblemId);
        var previous = await ListAsync(researchId, cancellationToken);
        if (previous.Count > MaximumAttemptsPerResearch - 2)
            throw new ArgumentException("Not enough attempt slots remaining for a cycle.");

        // Intentionally begin with a weak strategy. Lean, not the application,
        // determines which proof is accepted. This baseline is NOT Muse Spark.
        var choices = new[] { "rfl", "omega" };
        var attempts = new List<MathResearchAttempt>(2);
        foreach (var tactic in choices)
        {
            var attempt = await RunAttemptAsync(
                researchId, new MathVerificationRequest(input.ProblemId, tactic),
                "DETERMINISTIC_BASELINE", cancellationToken);
            attempts.Add(attempt);
            if (attempt.Verification.Outcome == "VERIFIED") break;
            if (attempt.Verification.Outcome is "UNAVAILABLE" or "TIMED_OUT") break;
        }

        var final = attempts[^1].Verification;
        var conclusion = final.Outcome == "VERIFIED"
            ? "Lean verified a KNOWN THEOREM in a controlled baseline. No novel discovery."
            : "No formal proof verified by this attempt cycle. The theorem is not disproved.";
        return new MathCycleResult(researchId, input.ProblemId,
            "DETERMINISTIC_BASELINE_NOT_AI", attempts, conclusion,
            IsScientificDiscovery: false);
    }

    public async Task<string> RenderLedgerMarkdownAsync(
        string researchId, bool spanish, CancellationToken cancellationToken)
    {
        var attempts = await ListAsync(researchId, cancellationToken);
        var lines = new List<string>
        {
            "",
            "## " + (spanish
                ? "Evidencia de Lean 4 generada por el servidor"
                : "Server-generated Lean 4 evidence"),
            "",
            spanish
                ? "Solo teoremas conocidos; los resultados son controles de verificación, NO descubrimientos originales."
                : "Known theorems only; these results are verification controls, NOT novel discoveries.",
            ""
        };
        if (attempts.Count == 0)
            lines.Add(spanish ? "_No existen intentos registrados._" : "_No registered attempts._");
        foreach (var a in attempts)
        {
            var x = a.Verification;
            lines.Add("- **" + LeanResultLanguage.Outcome(x.Outcome, spanish) +
                "** | " + x.AttemptId + " | " + x.ProblemId +
                (spanish ? " | táctica " : " | tactic ") + x.Strategy +
                " | " + (a.ExecutionMode switch
                {
                    "DETERMINISTIC_BASELINE" =>
                        spanish ? "CICLO DETERMINISTA (SIN IA)" : "DETERMINISTIC BASELINE (NO AI)",
                    "SCRIPTED_OFFLINE_VALIDATION" =>
                        spanish ? "ENSAYO GUIADO SIN MUSE" : "SCRIPTED REHEARSAL WITHOUT MUSE",
                    _ => spanish ? "CONTROL MANUAL" : "MANUAL CONTROL"
                }) +
                " | " + x.CheckedAtUtc.ToString("O"));
            lines.Add("  - SHA-256 " +
                (spanish ? "del código Lean" : "of Lean source") + ": `" +
                x.SourceSha256 + "`");
            lines.Add("  - " + LeanResultLanguage.Explain(x, spanish));
        }
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public async Task ValidateStageEvidenceAsync(
        ResearchProject project, int stage, CancellationToken cancellationToken)
    {
        if (project.Discipline != "Mathematics" || stage is not (4 or 5))
            return;
        var attempts = await ListAsync(project.Id, cancellationToken);
        if (attempts.Count == 0)
            throw new ArgumentException(
                "Mathematical research stage " + stage +
                " requires an actual server-generated Lean attempt (success or failure).");
        var report = project.Reports.Single(r => r.Index == stage);
        var key = stage == 4 ? "manifest" : "verification";
        var contents = report.Fields.GetValueOrDefault(key) ?? "";
        if (!attempts.Any(a => contents.Contains(
                a.Verification.AttemptId, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException(
                "Stage " + stage + " requires a real recorded attempt ID in '" + key +
                "'. Attach an actual server-generated result, not a fictional citation.");
    }
}
