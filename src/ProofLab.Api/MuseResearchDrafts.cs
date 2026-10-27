using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProofLab.Science;

namespace ProofLab.Api;

public sealed record MuseDraftResponse(
    GuidedResearchDraft Draft, string Model, string Provenance,
    string ResponseSha256, DateTimeOffset GeneratedAtUtc,
    int? InputTokens, int? OutputTokens);

/// <summary>
/// Deliberately restricted Meta Model API integration: proposal only.
/// No automatic dossier write, no user-review impersonation, no tool execution.
/// </summary>
public sealed class MuseResearchDrafts(
    GuidedResearchDrafts curated,
    MetaModelClient meta,
    IWebHostEnvironment environment)
{
    public async Task<MuseDraftResponse> PrepareAsync(
        string researchId, int stage, string lang, CancellationToken ct)
    {
        if (!meta.Enabled || !meta.HasKey)
            throw new InvalidOperationException("Meta Model API gated until key, verified credits and owner consent.");
        // Using the server-curated draft avoids forwarding any user-authored report text.
        var reference = await curated.PrepareAsync(researchId, stage, lang, ct);
        var definitions = ScientificMethodWorkflow.Stage(stage).Fields;
        var output = await meta.CreateResearchDraftAsync(stage, lang, definitions, reference.Fields, ct);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (!output.Fields.TryGetValue(definition.Key, out var proposed) ||
                proposed.Length < definition.MinimumCharacters ||
                proposed.Length > 4000 || proposed.Any(ch => ch == '\0'))
                throw new FormatException("Meta returned invalid scientific stage field length.");
            fields[definition.Key] = proposed.Trim();
        }
        if (fields.Count != definitions.Length)
            throw new FormatException("Meta returned fields different from the scientific protocol.");

        // Citation/evidence fields must come from deterministic curated data,
        // not fabricated or rewritten by the model.
        if (stage == 1) fields["sources"] = reference.Fields["sources"];
        if (stage == 4)
        {
            fields["manifest"] = reference.Fields["manifest"];
            fields["provenance"] = reference.Fields["provenance"];
        }
        if (stage == 5)
        {
            fields["results"] = reference.Fields["results"];
            fields["verification"] = reference.Fields["verification"];
        }
        // Do not let a model assertion substitute for mathematical verification.
        var note = lang == "es"
            ? " Propuesta de Muse Spark NO VERIFICADA: no demuestra Goldbach, no reemplaza revisión humana."
            : " UNVERIFIED Muse Spark suggestion: does not prove Goldbach or replace human review.";
        var narrativeKey = stage switch
        {
            1 => "background", 2 => "hypothesis", 3 => "procedure",
            4 => "execution", 5 => "limitations", _ => throw new ArgumentException("Invalid stage.")
        };
        fields[narrativeKey] += note;

        var canonical = JsonSerializer.Serialize(fields);
        var sha = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var preview = new GuidedResearchDraft(researchId, stage, lang,
            "MUSE_SPARK_LIVE_UNVERIFIED_DRAFT", fields,
            "META_MODEL_API_OUTPUT_WITH_CURATED_SOURCE_AND_VERIFIED_EVIDENCE_GATES",
            HumanReviewRequired: true, IsAutonomousAi: false,
            IsScientificDiscovery: false);
        // Private non-sensitive metadata audit, no key, no raw model prompts.
        var logDir = Path.Combine(environment.ContentRootPath,
            ".private", "research", "muse-draft-metadata");
        Directory.CreateDirectory(logDir);
        var audit = new {
            researchId, stage, lang, model = "muse-spark-1.3",
            mode = preview.Mode, responseSha256 = sha, utc = now,
            inputTokens = output.InputTokens, outputTokens = output.OutputTokens,
            unverified = true
        };
        await File.AppendAllTextAsync(Path.Combine(logDir, researchId + ".jsonl"),
            JsonSerializer.Serialize(audit) + Environment.NewLine, ct);
        return new MuseDraftResponse(preview, "muse-spark-1.3",
            "LIVE_META_MODEL_API_OUTPUT_UNVERIFIED", sha, now,
            output.InputTokens, output.OutputTokens);
    }
}
