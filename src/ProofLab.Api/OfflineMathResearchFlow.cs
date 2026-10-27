using System.Text.Json;
using ProofLab.Science;

namespace ProofLab.Api;

/// <summary>
/// A scripted, reproducible rehearsal of an agent's tool-selection workflow.
/// No model, no credentials, no arbitrary Lean source, no autoapproval.
/// </summary>
public sealed record OfflineResearchEvent(
    string CycleId, int Sequence, string Kind, string Tool,
    string ProposalJson, string Outcome, string? EvidenceId,
    string? EvidenceSha256, DateTimeOffset RecordedAtUtc,
    string ExplanationEs, string ExplanationEn);

public sealed record OfflineResearchCycleResult(
    string CycleId, string ResearchId, string Mode, string ConjectureId,
    string SourceUrl, string ResearchQuestionEs, string ResearchQuestionEn,
    string HypothesisEs, string HypothesisEn,
    string ConclusionEs, string ConclusionEn,
    int ToolCallCount, bool IsAutonomousAi, bool IsScientificDiscovery,
    bool GoldbachFormalProofVerified,
    IReadOnlyList<OfflineResearchEvent> Events);

public sealed class OfflineMathResearchFlow(
    IWebHostEnvironment environment, ResearchWorkflowStore research,
    MathEvidenceWorkflow math, OpenConjectureEvidenceWorkflow finite)
{
    public const string Mode = "SCRIPTED_OFFLINE_REHEARSAL_NOT_AI";
    public const int MaxCyclesPerResearch = 3;
    private const int FiniteLimit = 100;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string Root => Path.Combine(environment.ContentRootPath,
        ".private", "research", "offline-agent-events");

    private string PathFor(string id)
    {
        if (id.Length != 32 || id.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid research identifier.");
        return Path.Combine(Root, id.ToLowerInvariant() + ".jsonl");
    }

    private async Task<List<OfflineResearchEvent>> ReadUnlockedAsync(string id, CancellationToken ct)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return [];
        var rows = await File.ReadAllLinesAsync(path, ct);
        var items = new List<OfflineResearchEvent>();
        foreach (var line in rows.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var item = JsonSerializer.Deserialize<OfflineResearchEvent>(line, Json)
                ?? throw new InvalidDataException("Invalid offline agent event.");
            items.Add(item);
        }
        return items;
    }

    public async Task<IReadOnlyList<OfflineResearchEvent>> ListAsync(
        string researchId, CancellationToken ct)
    {
        _ = await research.GetAsync(researchId, ct);
        await Gate.WaitAsync(ct);
        try { return await ReadUnlockedAsync(researchId, ct); }
        finally { Gate.Release(); }
    }

    public async Task<OfflineResearchCycleResult> RunAsync(
        string researchId, CancellationToken ct)
    {
        var project = await research.GetAsync(researchId, ct);
        if (project.Discipline != "Mathematics" || project.ActiveStage is not (4 or 5))
            throw new ArgumentException(
                "Offline rehearsal requires a mathematics research dossier at stage 4 or 5.");
        // All choices below are scripted. This is NEVER marketed as Muse or autonomous AI.
        var conjecture = OpenConjectureCatalog.Find("GOLDBACH-STRONG");
        if (conjecture.IsExecutableLeanGoal)
            throw new InvalidOperationException("Open conjecture cannot be executed on the host.");

        await Gate.WaitAsync(ct);
        try
        {
            var prior = await ReadUnlockedAsync(researchId, ct);
            if (prior.Select(x => x.CycleId).Distinct(StringComparer.Ordinal).Count() >=
                MaxCyclesPerResearch)
                throw new ArgumentException("Offline rehearsal cycle budget exhausted.");
            // Preflight capacity: two curated Lean attempts and one finite observation.
            if ((await math.ListAsync(researchId, ct)).Count > 22 ||
                (await finite.ListAsync(researchId, ct)).Count >= 12)
                throw new ArgumentException("Insufficient safe tool attempt budget.");

            var cycleId = Guid.NewGuid().ToString("N");
            var events = new List<OfflineResearchEvent>();
            var count = 0;
            async Task RecordAsync(
                string kind, string tool, string proposal, string outcome,
                string? evidenceId, string? sha, string es, string en)
            {
                var entry = new OfflineResearchEvent(cycleId, events.Count + 1,
                    kind, tool, proposal, outcome, evidenceId, sha,
                    DateTimeOffset.UtcNow, es, en);
                Directory.CreateDirectory(Root);
                // Append after the underlying scientific tool has stored its own
                // evidence: errors/failed proofs remain in the original ledgers.
                await File.AppendAllTextAsync(PathFor(researchId),
                    JsonSerializer.Serialize(entry, Json) + Environment.NewLine, ct);
                events.Add(entry);
            }

            // Research stage: a bounded, falsifiable observation, not a universal proof.
            const string finiteProposal =
                "{\"tool\":\"CHECK_GOLDBACH_FINITE\",\"conjectureId\":\"GOLDBACH-STRONG\",\"evenLimit\":100}";
            var finiteAction = ResearchAgentToolPolicy.ValidateProposal(finiteProposal);
            var observation = await finite.RunAsync(researchId,
                finiteAction.ConjectureId!, new GoldbachFiniteRequest(finiteAction.EvenLimit!.Value), ct);
            count++;
            await RecordAsync("TOOL", finiteAction.Tool, finiteProposal, observation.Result.Outcome,
                observation.Result.ExperimentId, observation.Result.ResultSha256,
                "Se examinaron 49 casos pares (4..100). La ausencia de contraejemplo no demuestra Goldbach.",
                "49 even cases (4..100) checked. No counterexample in this range does not prove Goldbach.");

            async Task<MathResearchAttempt> TryLeanAsync(string tactic)
            {
                var proposal = JsonSerializer.Serialize(new {
                    tool = "VERIFY_KNOWN_BENCHMARK", problemId = "NAT-ADD-COMM",
                    strategy = tactic
                });
                var action = ResearchAgentToolPolicy.ValidateProposal(proposal);
                var result = await math.RunAttemptAsync(researchId,
                    new MathVerificationRequest(action.ProblemId!, action.Strategy!),
                    "SCRIPTED_OFFLINE_VALIDATION", ct);
                count++;
                await RecordAsync("TOOL", action.Tool, proposal,
                    result.Verification.Outcome, result.Verification.AttemptId,
                    result.Verification.SourceSha256,
                    LeanResultLanguage.Explain(result.Verification, true),
                    LeanResultLanguage.Explain(result.Verification, false));
                return result;
            }

            var weak = await TryLeanAsync("rfl");
            MathResearchAttempt? stronger = null;
            // Actual compiler feedback controls the next step. Never retry if
            // a verifier is unavailable, times out, or unexpectedly accepts rfl.
            if (weak.Verification.Outcome == "REJECTED")
            {
                stronger = await TryLeanAsync("omega");
            }

            var proofStatus = stronger?.Verification.Outcome == "VERIFIED"
                ? "KNOWN_THEOREM_VERIFIED"
                : "NO_KNOWN_THEOREM_PROOF_VERIFIED";
            var conclusionEs = proofStatus == "KNOWN_THEOREM_VERIFIED"
                ? "Lean verificó un teorema CONOCIDO después de rechazar una táctica. "
                    + "La conjetura de Goldbach sigue SIN RESOLVER; evidencia finita inconclusa."
                : "No se obtuvo verificación formal de referencia. Goldbach continúa SIN RESOLVER; "
                    + "ningún fallo de táctica constituye su refutación.";
            var conclusionEn = proofStatus == "KNOWN_THEOREM_VERIFIED"
                ? "Lean verified a KNOWN theorem after rejecting a tactic. "
                    + "The Goldbach conjecture remains UNSOLVED; finite evidence is inconclusive."
                : "No known benchmark proof was verified. Goldbach remains UNSOLVED; "
                    + "a failed tactic does not disprove a theorem.";

            await RecordAsync("CONCLUSION", "NONE", "{}", "OPEN_CONJECTURE_INCONCLUSIVE",
                null, null, conclusionEs, conclusionEn);
            if (count > ResearchAgentToolPolicy.MaxToolCallsPerCycle)
                throw new InvalidOperationException("Offline tool budget violation.");
            return new OfflineResearchCycleResult(
                cycleId, researchId, Mode, conjecture.Id,
                conjecture.StatusSourceUrl,
                "¿Existen contraejemplos finitos para la conjetura fuerte de Goldbach?",
                "Are there finite counterexamples to the strong Goldbach conjecture?",
                "Cada par en el rango 4..100 debería poseer un testigo de suma prima.",
                "Each even integer in 4..100 is expected to have a prime-sum witness.",
                conclusionEs, conclusionEn, count, IsAutonomousAi: false,
                IsScientificDiscovery: false, GoldbachFormalProofVerified: false,
                events.AsReadOnly());
        }
        finally { Gate.Release(); }
    }

    public async Task<string> RenderLedgerMarkdownAsync(
        string researchId, bool spanish, CancellationToken ct)
    {
        var events = await ListAsync(researchId, ct);
        var lines = new List<string>
        {
            "", "## " + (spanish ? "Ensayo del flujo científico SIN Muse Spark"
                : "Scientific research flow rehearsal WITHOUT Muse Spark"),
            "",
            spanish
                ? "Decisiones programadas; herramientas reales limitadas; la aprobación científica requiere a una persona."
                : "Scripted decisions; bounded real tools; scientific approval remains human-controlled.",
            ""
        };
        if (events.Count == 0)
            lines.Add(spanish ? "_Sin ensayos registrados._" : "_No offline rehearsals recorded._");
        foreach (var item in events)
        {
            lines.Add("- " + item.CycleId + " | " + item.Sequence + " | " +
                item.Kind + " | " + item.Tool + " | " + item.Outcome);
            lines.Add("  - UTC: " + item.RecordedAtUtc.ToString("O"));
            lines.Add("  - JSON: `" + item.ProposalJson + "`");
            if (item.EvidenceId is not null)
                lines.Add("  - ID: `" + item.EvidenceId + "`");
            if (item.EvidenceSha256 is not null)
                lines.Add("  - SHA-256: `" + item.EvidenceSha256 + "`");
            lines.Add("  - " + (spanish ? item.ExplanationEs : item.ExplanationEn));
        }
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
