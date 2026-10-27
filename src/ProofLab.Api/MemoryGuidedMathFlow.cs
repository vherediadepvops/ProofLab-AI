using System.Text.Json;
using ProofLab.Science;

namespace ProofLab.Api;

// Offline, memory-informed replay of curated known-theorem Lean checks.
// Goldbach remains open; retrieval cannot substitute for fresh verification.
public sealed record MemoryCycleRequest(string ProblemId);
public sealed record MemoryCycleResult(
    string CycleId, string ResearchId, string ProblemId, string Mode,
    MemoryAdvice Advice, IReadOnlyList<MathResearchAttempt> Attempts,
    string FinalOutcome, string ConclusionEs, string ConclusionEn,
    bool IsAutonomousAi, bool IsNewDiscovery);

public sealed class MemoryGuidedMathFlow(
    IWebHostEnvironment environment,
    ResearchWorkflowStore research,
    MathEvidenceWorkflow math,
    ScientificMemoryStore memory)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string Root => Path.Combine(environment.ContentRootPath,
        ".private", "research", "memory-cycle-traces");
    private const int MaximumCyclesPerResearch = 3;

    private static void ValidateResearchId(string id)
    {
        if (id.Length != 32 || id.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid research identifier.");
    }
    private string PathFor(string id)
    {
        ValidateResearchId(id);
        return Path.Combine(Root, id.ToLowerInvariant() + ".jsonl");
    }

    public async Task<IReadOnlyList<MemoryCycleResult>> ListAsync(
        string researchId, CancellationToken ct)
    {
        _ = await research.GetAsync(researchId, ct);
        await gate.WaitAsync(ct);
        try { return await ReadUnlockedAsync(researchId, ct); }
        finally { gate.Release(); }
    }

    private async Task<List<MemoryCycleResult>> ReadUnlockedAsync(
        string researchId, CancellationToken ct)
    {
        var path = PathFor(researchId);
        if (!File.Exists(path)) return [];
        var records = new List<MemoryCycleResult>();
        foreach (var row in await File.ReadAllLinesAsync(path, ct))
        {
            if (string.IsNullOrWhiteSpace(row)) continue;
            var result = JsonSerializer.Deserialize<MemoryCycleResult>(row, Json)
                ?? throw new InvalidDataException("Invalid memory-cycle record.");
            if (result.ResearchId != researchId)
                throw new InvalidDataException("Memory-cycle research ID mismatch.");
            records.Add(result);
        }
        return records;
    }

    public async Task<MemoryCycleResult> RunAsync(
        string researchId, MemoryCycleRequest request, CancellationToken ct)
    {
        var project = await research.GetAsync(researchId, ct);
        if (project.Discipline != "Mathematics" ||
            project.ActiveStage is not (4 or 5))
            throw new ArgumentException(
                "Memory-guided Lean checks require a mathematics dossier at stage 4 or 5.");
        var problem = MathResearchCatalog.Find(request.ProblemId);
        if (problem.ResearchStatus != "KNOWN_THEOREM")
            throw new ArgumentException("Memory-guided checks only support known Lean benchmarks.");

        await gate.WaitAsync(ct);
        try
        {
            if ((await ReadUnlockedAsync(researchId, ct)).Count >= MaximumCyclesPerResearch)
                throw new ArgumentException("Memory-cycle limit reached.");
            var prior = await math.ListAsync(researchId, ct);
            if (prior.Count > 22)
                throw new ArgumentException("Not enough real Lean attempt slots.");

            var advice = await memory.RecallAsync(problem.Id, ct);
            var attempts = new List<MathResearchAttempt>(2);
            async Task<MathResearchAttempt> VerifyAsync(string tactic)
            {
                var evidence = await math.RunAttemptAsync(researchId,
                    new MathVerificationRequest(problem.Id, tactic),
                    "MEMORY_GUIDED_OFFLINE", ct);
                attempts.Add(evidence);
                return evidence;
            }

            var first = await VerifyAsync(advice.Strategy);
            // Actual Lean rejection, rather than an LLM prediction or remembered
            // failure, governs whether a second tactic is attempted.
            if (first.Verification.Strategy == "rfl" &&
                first.Verification.Outcome == "REJECTED")
                await VerifyAsync("omega");

            var last = attempts[^1].Verification;
            var valid = last.Outcome == "VERIFIED";
            var es = valid
                ? "Lean volvió a verificar un TEOREMA CONOCIDO. La memoria solo orientó la estrategia; no hay descubrimiento original."
                : "No existe una prueba validada en este ciclo. Un intento fallido no refuta el enunciado.";
            var en = valid
                ? "Lean independently reverified a KNOWN THEOREM. Memory guided tactic selection; no novel discovery."
                : "No valid proof from this cycle. A failed tactic does not disprove a mathematical statement.";
            var cycle = new MemoryCycleResult(Guid.NewGuid().ToString("N"),
                researchId, problem.Id, "MEMORY_GUIDED_OFFLINE_NOT_AI", advice,
                attempts.AsReadOnly(), last.Outcome, es, en,
                IsAutonomousAi: false, IsNewDiscovery: false);
            Directory.CreateDirectory(Root);
            await File.AppendAllTextAsync(PathFor(researchId),
                JsonSerializer.Serialize(cycle, Json) + Environment.NewLine, ct);
            return cycle;
        }
        finally { gate.Release(); }
    }

    public async Task<string> RenderMarkdownAsync(
        string researchId, bool spanish, CancellationToken ct)
    {
        var items = await ListAsync(researchId, ct);
        var lines = new List<string> {
            "",
            "## " + (spanish ? "Memoria científica: experiencias Lean recuperadas"
                : "Scientific memory: recalled Lean experiences"),
            "",
            spanish
                ? "Solo objetivos Lean conocidos y exactos. Se verifica de nuevo cada estrategia; no es IA ni descubrimiento."
                : "Exact known Lean goals only. Every recalled tactic is rechecked; no AI inference or novel discovery.",
            ""
        };
        if (items.Count == 0)
            lines.Add(spanish ? "_Sin ciclos de memoria._" : "_No memory-guided cycles._");
        foreach (var item in items)
        {
            lines.Add("- " + item.CycleId + " | " + item.ProblemId + " | " +
                item.Advice.SelectionMode + " | " + item.FinalOutcome);
            if (item.Advice.SourceAttemptId is not null)
                lines.Add("  - " + (spanish ? "Memoria de intento" : "Recalled attempt") +
                    ": " + item.Advice.SourceAttemptId + " (" +
                    item.Advice.SourceResearchId + ")");
            lines.Add("  - " + (spanish ? "Nueva evidencia" : "New evidence") +
                ": " + string.Join(", ", item.Attempts.Select(x =>
                    x.Verification.AttemptId + "/" + x.Verification.Outcome)));
            lines.Add("  - " + (spanish ? item.ConclusionEs : item.ConclusionEn));
        }
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
