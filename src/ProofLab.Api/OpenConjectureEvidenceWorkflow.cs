using System.Text.Json;
using ProofLab.Science;

namespace ProofLab.Api;

public sealed record ConjectureFiniteAttempt(
    string ResearchId, string ExecutionMode, GoldbachFiniteResult Result);

/// <summary>
/// Append-only computational evidence attached to an existing stage-4/5 dossier.
/// No AI, no arbitrary Lean code, no paid requests.
/// </summary>
public sealed class OpenConjectureEvidenceWorkflow(
    IWebHostEnvironment environment, ResearchWorkflowStore research)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string Root => Path.Combine(environment.ContentRootPath,
        ".private", "research", "conjecture-observations");

    private string PathFor(string researchId)
    {
        if (researchId.Length != 32 || researchId.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid research identifier.");
        return Path.Combine(Root, researchId.ToLowerInvariant() + ".jsonl");
    }

    private async Task<List<ConjectureFiniteAttempt>> ReadUnlockedAsync(
        string researchId, CancellationToken ct)
    {
        var path = PathFor(researchId);
        if (!File.Exists(path)) return [];
        var lines = await File.ReadAllLinesAsync(path, ct);
        var records = new List<ConjectureFiniteAttempt>();
        foreach (var line in lines.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var record = JsonSerializer.Deserialize<ConjectureFiniteAttempt>(line, Json)
                ?? throw new InvalidDataException("Corrupted finite experiment ledger.");
            if (record.ResearchId != researchId)
                throw new InvalidDataException("Research ID mismatch in finite evidence.");
            records.Add(record);
        }
        return records;
    }

    public async Task<IReadOnlyList<ConjectureFiniteAttempt>> ListAsync(
        string researchId, CancellationToken ct)
    {
        _ = await research.GetAsync(researchId, ct);
        await gate.WaitAsync(ct);
        try { return await ReadUnlockedAsync(researchId, ct); }
        finally { gate.Release(); }
    }

    public async Task<ConjectureFiniteAttempt> RunAsync(
        string researchId, string conjectureId, GoldbachFiniteRequest input, CancellationToken ct)
    {
        var project = await research.GetAsync(researchId, ct);
        if (project.Discipline != "Mathematics" || project.ActiveStage is not (4 or 5))
            throw new ArgumentException("Finite conjecture checks require a math research project at stage 4 or 5.");
        if (OpenConjectureCatalog.Find(conjectureId).Id != "GOLDBACH-STRONG")
            throw new ArgumentException("No finite experiment is available for that conjecture.");

        // Research documents are approved manually; this never approves an
        // experiment or fulfills the existing Lean evidence gate.
        await gate.WaitAsync(ct);
        try
        {
            var previous = await ReadUnlockedAsync(researchId, ct);
            if (previous.Count >= 12)
                throw new ArgumentException("Finite experiment limit reached for this research.");
            var observation = GoldbachFiniteExperiment.Run(input.EvenLimit, ct);
            var record = new ConjectureFiniteAttempt(
                researchId, "DETERMINISTIC_FINITE_COMPUTATION_NOT_AI", observation);
            Directory.CreateDirectory(Root);
            await File.AppendAllTextAsync(PathFor(researchId),
                JsonSerializer.Serialize(record, Json) + Environment.NewLine, ct);
            return record;
        }
        finally { gate.Release(); }
    }

    public async Task<string> RenderLedgerMarkdownAsync(
        string researchId, bool spanish, CancellationToken ct)
    {
        var records = await ListAsync(researchId, ct);
        var lines = new List<string> {
            "",
            "## " + (spanish ? "Exploración computacional de conjeturas abiertas"
                : "Computational exploration of open conjectures"),
            spanish
                ? "Resultados acotados, NO demostraciones Lean ni descubrimientos originales."
                : "Bounded results, NOT Lean proofs or novel discoveries.",
            ""
        };
        if (records.Count == 0)
            lines.Add(spanish ? "_Sin observaciones computacionales._"
                : "_No computational observations._");
        foreach (var record in records)
        {
            var r = record.Result;
            lines.Add("- " + r.ExperimentId + " | " + r.ConjectureId + " | " +
                r.Outcome + " | " + r.CheckedAtUtc.ToString("O"));
            lines.Add("  - " + (spanish ? "Pares examinados" : "Even cases checked")
                + ": " + r.EvenCasesExamined + " (4.." + r.CheckedThrough + ")");
            lines.Add("  - SHA-256 input: `" + r.InputSha256 + "`");
            lines.Add("  - SHA-256 result: `" + r.ResultSha256 + "`");
            lines.Add("  - " + (spanish ? r.LimitationsEs : r.LimitationsEn));
        }
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
