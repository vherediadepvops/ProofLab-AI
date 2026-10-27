using System.Text.Json;

namespace ProofLab.Science;

/// <summary>
/// Prepared allowlist for future Muse Spark agent tool proposals.
/// Only validated, typed tool arguments may reach application services.
/// This contract does not execute anything and makes no paid API call.
/// </summary>
public sealed record ResearchToolAction(
    string Tool, string? ConjectureId, int? EvenLimit,
    string? ProblemId, string? Strategy);

public static class ResearchAgentToolPolicy
{
    public const int MaxToolCallsPerCycle = 3;
    public const int MaxProposalCharacters = 1500;
    public static readonly string[] AllowedTools =
        ["CHECK_GOLDBACH_FINITE", "VERIFY_KNOWN_BENCHMARK"];

    public static ResearchToolAction ValidateProposal(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxProposalCharacters)
            throw new ArgumentException("Empty or oversized model tool proposal.");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Tool proposal must be a JSON object.");
        if (!root.TryGetProperty("tool", out var toolNode) ||
            toolNode.ValueKind != JsonValueKind.String)
            throw new ArgumentException("Tool proposal needs a tool identifier.");
        var tool = toolNode.GetString();

        if (tool == "CHECK_GOLDBACH_FINITE")
        {
            RequireOnly(root, ["tool", "conjectureId", "evenLimit"]);
            if (!root.TryGetProperty("conjectureId", out var idNode) ||
                idNode.ValueKind != JsonValueKind.String ||
                idNode.GetString() != "GOLDBACH-STRONG" ||
                !root.TryGetProperty("evenLimit", out var limitNode) ||
                limitNode.ValueKind != JsonValueKind.Number ||
                !limitNode.TryGetInt32(out var evenLimit) ||
                evenLimit is < 4 or > GoldbachFiniteExperiment.MaximumEvenLimit ||
                evenLimit % 2 != 0)
                throw new ArgumentException("Invalid bounded Goldbach tool parameters.");
            _ = OpenConjectureCatalog.Find(idNode.GetString());
            return new ResearchToolAction(tool, "GOLDBACH-STRONG",
                evenLimit, null, null);
        }

        if (tool == "VERIFY_KNOWN_BENCHMARK")
        {
            RequireOnly(root, ["tool", "problemId", "strategy"]);
            if (!root.TryGetProperty("problemId", out var idNode) ||
                idNode.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("strategy", out var strategyNode) ||
                strategyNode.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Missing Lean benchmark identifiers.");
            var id = idNode.GetString();
            var strategy = strategyNode.GetString();
            var fixedProblem = MathResearchCatalog.Find(id);
            _ = MathResearchCatalog.BuildLeanSource(fixedProblem, strategy);
            return new ResearchToolAction(tool, null, null, id, strategy);
        }

        throw new ArgumentException("Tool is not allowlisted; no shell or free-form Lean permitted.");
    }

    private static void RequireOnly(JsonElement root, string[] fields)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in root.EnumerateObject())
        {
            if (!names.Add(field.Name) || !fields.Contains(field.Name, StringComparer.Ordinal))
                throw new ArgumentException("Unexpected or duplicate tool argument.");
        }
    }
}
