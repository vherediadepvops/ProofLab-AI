using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProofLab.Science;

namespace ProofLab.Api;

/// <summary>
/// Local-only prototype storage. Research reports are stored under .private/
/// and never synchronized to public Git automatically. Each approval is logged,
/// and prior approved reports cannot be silently edited through this API.
/// Not an authenticated multi-user audit/compliance service.
/// </summary>
public sealed class ResearchWorkflowStore(IWebHostEnvironment environment)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private string Root => Path.Combine(environment.ContentRootPath, ".private", "research");

    private string PathFor(string id)
    {
        if (id.Length != 32 || id.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid research identifier.");
        return Path.Combine(Root, id.ToLowerInvariant() + ".json");
    }

    public async Task<ResearchProject> CreateAsync(
        ResearchCreateRequest input, CancellationToken ct)
    {
        var project = ScientificMethodWorkflow.NewProject(input.Title, input.Discipline);
        await gate.WaitAsync(ct);
        try
        {
            await WriteAsync(project, ct);
            await AuditAsync(project.Id, "CREATED", 1, null, ct);
            return project;
        }
        finally { gate.Release(); }
    }

    public async Task<ResearchProject> GetAsync(string id, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return await ReadAsync(id, ct); }
        finally { gate.Release(); }
    }

    public async Task<ResearchProject> SaveDraftAsync(
        string id, int stage, ResearchDraftRequest input, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var project = ScientificMethodWorkflow.SaveDraft(
                await ReadAsync(id, ct), stage, input.Fields);
            await WriteAsync(project, ct);
            await AuditAsync(id, "DRAFT_SAVED", stage, null, ct);
            return project;
        }
        finally { gate.Release(); }
    }

    public async Task<ResearchProject> ApproveAsync(
        string id, int stage, ResearchApprovalRequest input, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var project = ScientificMethodWorkflow.Approve(
                await ReadAsync(id, ct), stage, input.ReviewedByHuman);
            await WriteAsync(project, ct);
            var hash = project.Reports.Single(x => x.Index == stage).Sha256;
            await AuditAsync(id, "COMPLETENESS_ACKNOWLEDGED_NOT_SCIENTIFIC_VALIDATION",
                stage, hash, ct);
            return project;
        }
        finally { gate.Release(); }
    }

    public async Task<string> ReportAsync(string id, int stage, string lang, CancellationToken ct)
    {
        var project = await GetAsync(id, ct);
        var definition = ScientificMethodWorkflow.Stage(stage);
        var report = project.Reports.Single(x => x.Index == stage);
        var es = string.Equals(lang, "es", StringComparison.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.AppendLine("# ProofLab AI — " + (es ? definition.ReportEs : definition.ReportEn))
            .AppendLine().AppendLine("**" + (es ? "ID de investigación" : "Research ID") + ":** " + project.Id)
            .AppendLine("**" + (es ? "Título" : "Title") + ":** " + project.Title)
            .AppendLine("**" + (es ? "Disciplina" : "Discipline") + ":** " +
                (es && project.Discipline == "Mathematics" ? "Matemáticas" : project.Discipline))
            .AppendLine("**" + (es ? "Etapa" : "Stage") + ":** " + stage + "/5")
            .AppendLine("**" + (es ? "Estado" : "Status") + ":** " +
                (report.Status == "APPROVED_COMPLETENESS"
                    ? es ? "INFORME COMPLETO (SIN VALIDACIÓN CIENTÍFICA)" : "DOCUMENT COMPLETE (NOT SCIENTIFIC VALIDATION)"
                    : es ? "BORRADOR" : "DRAFT"))
            .AppendLine("**" + (es ? "Versión" : "Version") + ":** " + report.Revision)
            .AppendLine("**" + (es ? "Aprobado en UTC" : "UTC approved") + ":** " +
                (report.ApprovedAtUtc?.ToString("O") ?? (es ? "sin aprobar" : "not approved")))
            .AppendLine("**SHA-256:** " + (report.Sha256 ?? (es ? "sin congelar" : "not frozen")))
            .AppendLine()
            .AppendLine("## " + (es ? "Campos del informe" : "Report fields"));
        foreach (var field in definition.Fields)
        {
            sb.AppendLine().AppendLine("### " + (es ? field.NameEs : field.NameEn))
                .AppendLine()
                .AppendLine(report.Fields.TryGetValue(field.Key, out var value)
                    ? value : "_Pending_");
        }
        sb.AppendLine().AppendLine("## " + (es ? "Marco de referencia" : "Reference framework"));
        foreach (var reference in definition.ReferenceFrameworks)
            sb.AppendLine("- " + reference);
        sb.AppendLine().AppendLine("> " +
            (es ? "APROBADO significa únicamente documentación completa y revisión humana declarada. No certifica hallazgos científicos, cumplimiento normativo o revisión por pares."
                : "APPROVED means documentation completeness and acknowledged human review only. It does not certify scientific claims, standards compliance or peer review."));
        return sb.ToString();
    }

    public async Task<string> ScientificPaperAsync(string id, string lang, CancellationToken ct)
    {
        var project = await GetAsync(id, ct);
        if (project.ActiveStage <= 5 ||
            project.Reports.Any(r => r.Status != "APPROVED_COMPLETENESS"))
            throw new ArgumentException("All five reports must be complete before exporting a paper.");

        var es = string.Equals(lang, "es", StringComparison.OrdinalIgnoreCase);
        string Read(int stage, string key)
        {
            var report = project.Reports.Single(r => r.Index == stage);
            return report.Fields.TryGetValue(key, out var value) ? value : (es ? "_Sin información_" : "_Not reported_");
        }

        var sb = new StringBuilder();
        sb.AppendLine("# " + project.Title)
            .AppendLine()
            .AppendLine(es ? "**Informe de investigación generado a partir de informes cerrados.**"
                : "**Research report assembled from approved documentation stages.**")
            .AppendLine()
            .AppendLine("ProofLab AI | " + project.Id)
            .AppendLine((es ? "Generado en UTC: " : "Generated UTC: ") +
                DateTimeOffset.UtcNow.ToString("O"))
            .AppendLine()
            .AppendLine("## " + (es ? "Resumen" : "Abstract"))
            .AppendLine()
            .AppendLine(Read(1, "question"))
            .AppendLine()
            .AppendLine("## " + (es ? "Introducción y antecedentes" : "Introduction and prior work"))
            .AppendLine()
            .AppendLine(Read(1, "background"))
            .AppendLine()
            .AppendLine("## " + (es ? "Hipótesis y alternativas" : "Hypotheses and alternatives"))
            .AppendLine()
            .AppendLine(Read(2, "hypothesis"))
            .AppendLine()
            .AppendLine(Read(2, "alternatives"))
            .AppendLine()
            .AppendLine("**" + (es ? "Condiciones de refutación" : "Falsification criteria") + "**")
            .AppendLine()
            .AppendLine(Read(2, "falsification"))
            .AppendLine()
            .AppendLine("## " + (es ? "Métodos" : "Methods"))
            .AppendLine()
            .AppendLine(Read(3, "procedure"))
            .AppendLine()
            .AppendLine(Read(3, "controls"))
            .AppendLine()
            .AppendLine(Read(3, "analysisPlan"))
            .AppendLine()
            .AppendLine("**" + (es ? "Ética y seguridad" : "Ethics and safety") + "**")
            .AppendLine()
            .AppendLine(Read(3, "ethicsSafety"))
            .AppendLine()
            .AppendLine("## " + (es ? "Datos y ejecución" : "Data and execution"))
            .AppendLine()
            .AppendLine(Read(4, "manifest"))
            .AppendLine()
            .AppendLine(Read(4, "execution"))
            .AppendLine()
            .AppendLine(Read(4, "provenance"))
            .AppendLine()
            .AppendLine("## " + (es ? "Resultados" : "Results"))
            .AppendLine()
            .AppendLine(Read(5, "results"))
            .AppendLine()
            .AppendLine("## " + (es ? "Discusión, verificación y limitaciones"
                : "Discussion, verification and limitations"))
            .AppendLine()
            .AppendLine(Read(5, "verification"))
            .AppendLine()
            .AppendLine(Read(5, "limitations"))
            .AppendLine()
            .AppendLine("## " + (es ? "Referencias" : "References"))
            .AppendLine()
            .AppendLine(Read(1, "sources"))
            .AppendLine()
            .AppendLine("## " + (es ? "Procedencia documental" : "Document provenance"));
        foreach (var report in project.Reports)
            sb.AppendLine((es ? "- Etapa " : "- Stage ") + report.Index + ": " +
                (report.Sha256 ?? (es ? "ninguna" : "none")) +
                " | " + (report.ApprovedAtUtc?.ToString("O") ??
                    (es ? "desconocido" : "unknown")));
        sb.AppendLine().AppendLine("> " + (es
            ? "Documento estructurado automáticamente. Ninguna aprobación de etapa certifica resultados, revisión por pares o descubrimientos originales."
            : "Automatically structured document. Stage approvals do not certify results, peer review or original discoveries."));
        return sb.ToString();
    }

    private async Task<ResearchProject> ReadAsync(string id, CancellationToken ct)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) throw new FileNotFoundException("Research project not found.");
        var content = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<ResearchProject>(content, Json)
            ?? throw new InvalidDataException("Research state could not be parsed.");
    }

    private async Task WriteAsync(ResearchProject project, CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        var path = PathFor(project.Id);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(project, Json), ct);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task AuditAsync(string id, string action, int stage, string? digest, CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        var eventLine = JsonSerializer.Serialize(new
        {
            researchId = id, action, stage,
            utc = DateTimeOffset.UtcNow, sha256 = digest
        }) + Environment.NewLine;
        await File.AppendAllTextAsync(Path.Combine(Root, "audit.jsonl"), eventLine, ct);
    }
}
