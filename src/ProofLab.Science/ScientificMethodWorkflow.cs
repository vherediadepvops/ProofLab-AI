using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProofLab.Science;

/// <summary>
/// Five-stage documentation and methodological readiness checklist. Passing a
/// gate NEVER implies scientific correctness, mathematical proof, peer review,
/// ethical approval, journal acceptance or ISO certification.
/// </summary>
public sealed record ResearchField(
    string Key, string NameEs, string NameEn, string GuidanceEs, string GuidanceEn,
    int MinimumCharacters);
public sealed record ResearchStageDefinition(
    int Index, string NameEs, string NameEn, string ReportEs, string ReportEn,
    string DescriptionEs, string DescriptionEn,
    string[] ReferenceFrameworks, ResearchField[] Fields);

public sealed record ResearchStageReport(
    int Index, Dictionary<string, string> Fields, string Status,
    int Revision = 1, DateTimeOffset? ApprovedAtUtc = null,
    string? Sha256 = null);

public sealed record ResearchProject(
    string Id, string Title, string Discipline, DateTimeOffset CreatedAtUtc,
    int ActiveStage, List<ResearchStageReport> Reports,
    string Disclaimer = "Completeness gates only; no certification or scientific validation.");

public sealed record ResearchCreateRequest(string Title, string Discipline);
public sealed record ResearchDraftRequest(Dictionary<string, string> Fields);
public sealed record ResearchApprovalRequest(bool ReviewedByHuman);

public static class ScientificMethodWorkflow
{
    private static readonly ResearchStageDefinition[] Definitions =
    [
        new(1, "Problema y antecedentes", "Problem & prior work",
            "Informe de planteamiento", "Problem statement report",
            "Pregunta, relevancia, investigaciones anteriores y fuentes verificables.",
            "Question, significance, prior studies and verifiable sources.",
            ["IMRaD: introduction", "TOP 2025: reporting transparency"],
            [
                new("question", "Pregunta de investigación", "Research question",
                    "Definir una pregunta precisa y delimitada.", "Specify a focused research question.", 25),
                new("background", "Antecedentes y brecha", "Prior work and knowledge gap",
                    "Qué se conoce y qué falta investigar.", "Explain what is known and what remains uncertain.", 45),
                new("sources", "Fuentes identificables", "Identifiable sources",
                    "Citar al menos una publicación, DOI o URL verificable.", "Cite at least one publication, DOI or verifiable URL.", 20)
            ]),
        new(2, "Hipótesis y objetivos", "Hypotheses & objectives",
            "Informe de hipótesis", "Hypothesis report",
            "Hipótesis comprobable, alternativas y condiciones para refutarla.",
            "Testable hypothesis, alternatives and falsification conditions.",
            ["OSF preregistration", "TOP 2025: analysis plan"],
            [
                new("hypothesis", "Hipótesis principal", "Main hypothesis",
                    "Predicción o conjetura expresada con precisión.", "State a precise prediction or conjecture.", 30),
                new("alternatives", "Hipótesis alternativas", "Alternative hypotheses",
                    "Explicar al menos otra posible interpretación.", "Describe at least one alternative explanation.", 25),
                new("falsification", "Criterios de rechazo", "Falsification criteria",
                    "Qué resultado contradiría la hipótesis.", "What result would undermine the hypothesis?", 30)
            ]),
        new(3, "Metodología y protocolo", "Methodology & protocol",
            "Protocolo de investigación", "Research protocol",
            "Método y análisis previstos, controles, riesgos y autorización humana.",
            "Prespecified method, analysis, controls, risks and human acknowledgement.",
            ["OSF preregistration", "TOP 2025: study protocol"],
            [
                new("procedure", "Procedimiento reproducible", "Reproducible procedure",
                    "Detallar pasos, herramientas y versiones.", "Detail the steps, tools and versions.", 50),
                new("controls", "Controles y criterios", "Controls and decision criteria",
                    "Variables, casos límite, controles y exclusiones.", "Variables, edge cases, controls and exclusions.", 35),
                new("analysisPlan", "Plan de análisis previo", "Prespecified analysis plan",
                    "Explicar métricas y condiciones antes de mirar resultados.", "Define metrics and decisions before observing results.", 40),
                new("ethicsSafety", "Ética y seguridad", "Ethics and safety",
                    "Evaluar riesgos y permisos aplicables; no equivale a aprobación ética.", "Consider risks and permissions; this is not ethical clearance.", 25)
            ]),
        new(4, "Ejecución y evidencias", "Execution & evidence",
            "Bitácora y manifiesto de evidencias", "Execution log and evidence manifest",
            "Datos originales, bitácoras, procedencia y desviaciones del protocolo.",
            "Original data, logs, provenance and deviations from the protocol.",
            ["FAIR data principles", "W3C PROV-O"],
            [
                new("manifest", "Manifiesto de evidencias", "Evidence manifest",
                    "Identificadores de archivos/datos y su origen; no inventar observaciones.", "Record evidence IDs, origin and file references; never invent observations.", 40),
                new("execution", "Bitácora de ejecución", "Execution log",
                    "Intentos, resultados negativos, hora, software y desviaciones.", "Attempts, negative results, time, software and deviations.", 40),
                new("provenance", "Procedencia y límites", "Provenance and limitations",
                    "Quién o qué obtuvo los datos; si son simulados o reales.", "Who or what collected data; indicate simulated or real.", 35)
            ]),
        new(5, "Análisis y verificación", "Analysis & verification",
            "Informe científico y expediente final", "Scientific report and verification dossier",
            "Contrastar hipótesis, comprobar resultados y declarar las limitaciones.",
            "Test hypotheses, check results and disclose limitations.",
            ["ICMJE: IMRaD", "TOP 2025: computational transparency", "JCGM 100 when measuring"],
            [
                new("results", "Resultados y análisis", "Results and analysis",
                    "Comparar resultados con la hipótesis y el plan.", "Compare outcomes to the hypothesis and prespecified plan.", 40),
                new("verification", "Verificación independiente", "Independent verification",
                    "Indicar prueba Lean, reproducción o revisión, o declarar que está pendiente.", "Give Lean or reproduction evidence or state that verification is pending.", 40),
                new("limitations", "Conclusión y limitaciones", "Conclusion and limitations",
                    "Distinguir confirmado, refutado o inconcluso sin exageraciones.", "Distinguish verified, refuted and inconclusive outcomes without overclaiming.", 40)
            ])
    ];

    public static IReadOnlyList<ResearchStageDefinition> Stages => Definitions;

    public static ResearchStageDefinition Stage(int stage) =>
        Definitions.FirstOrDefault(d => d.Index == stage)
        ?? throw new ArgumentException("Unknown research stage.");

    public static ResearchProject NewProject(string? title, string? discipline)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length is < 8 or > 160)
            throw new ArgumentException("Research title must contain 8 to 160 characters.");
        if (discipline is not ("Mathematics" or "Physics" or "Astronomy" or "Materials"))
            throw new ArgumentException("Select a supported research discipline.");
        return new ResearchProject(Guid.NewGuid().ToString("N"), title.Trim(), discipline,
            DateTimeOffset.UtcNow, 1,
            Definitions.Select(s => new ResearchStageReport(s.Index,
                new Dictionary<string, string>(StringComparer.Ordinal),
                s.Index == 1 ? "DRAFT" : "LOCKED")).ToList());
    }

    public static IReadOnlyList<string> Validate(ResearchStageReport report)
    {
        var def = Stage(report.Index);
        var issues = new List<string>();
        foreach (var required in def.Fields)
        {
            report.Fields.TryGetValue(required.Key, out var value);
            if ((value ?? "").Trim().Length < required.MinimumCharacters)
                issues.Add(required.Key + ": at least " + required.MinimumCharacters + " characters required.");
        }
        if (report.Index == 1 &&
            (!report.Fields.TryGetValue("sources", out var source) ||
             !(source.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("doi:", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("10.", StringComparison.Ordinal))))
            issues.Add("sources: provide a DOI or URL as a traceable reference.");
        return issues;
    }

    public static ResearchProject SaveDraft(ResearchProject project, int stage,
        Dictionary<string, string>? incoming)
    {
        CheckEditable(project, stage);
        if (incoming is null) throw new ArgumentException("Provide report fields.");
        var def = Stage(stage);
        if (incoming.Keys.Any(x => !def.Fields.Any(f => f.Key == x)))
            throw new ArgumentException("Unknown field for this research stage.");
        if (incoming.Values.Any(x => x is null || x.Length > 12000))
            throw new ArgumentException("Field length exceeds the allowed limit.");
        var updated = project.Reports.Select(r => r.Index == stage
            ? r with { Fields = incoming.ToDictionary(x => x.Key, x => x.Value.Trim()),
                       Status = "DRAFT" } : r).ToList();
        return project with { Reports = updated };
    }

    public static ResearchProject Approve(ResearchProject project, int stage, bool humanReviewed)
    {
        CheckEditable(project, stage);
        if (!humanReviewed)
            throw new ArgumentException("Human completeness review must be acknowledged.");
        var report = project.Reports.Single(x => x.Index == stage);
        var problems = Validate(report);
        if (problems.Count > 0)
            throw new ArgumentException("Incomplete stage: " + string.Join(" | ", problems));

        var payload = JsonSerializer.Serialize(new { project.Id, stage, report.Fields, report.Revision });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var next = stage < 5 ? stage + 1 : 6;
        var updated = project.Reports.Select(r => r.Index == stage
            ? r with { Status = "APPROVED_COMPLETENESS", ApprovedAtUtc = now, Sha256 = hash }
            : r.Index == next ? r with { Status = "DRAFT" } : r).ToList();
        return project with { Reports = updated, ActiveStage = next };
    }

    private static void CheckEditable(ResearchProject project, int stage)
    {
        Stage(stage);
        if (stage != project.ActiveStage)
            throw new ArgumentException("This stage is locked or already approved; preserve the prior report.");
    }
}
