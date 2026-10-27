using System.Security.Cryptography;
using System.Text;

namespace ProofLab.Science;

// Every benchmark below is an already known result. None is an open discovery.
public sealed record MathProblem(
    string Id,
    string TitleEnglish,
    string TitleSpanish,
    string StatementEnglish,
    string StatementSpanish,
    string Domain,
    string ResearchStatus,
    string Source,
    string LeanDeclaration,
    string[] AllowedStrategies);

public sealed record MathVerificationRequest(string ProblemId, string Strategy);

public sealed record MathVerificationResult(
    string AttemptId,
    string ProblemId,
    string Strategy,
    string Outcome,
    string VerificationMethod,
    string SourceSha256,
    DateTimeOffset CheckedAtUtc,
    string Diagnostic,
    bool IsNewDiscovery);

public static class MathResearchCatalog
{
    private static readonly MathProblem[] KnownBenchmarks =
    [
        new(
            "NAT-ADD-ZERO", "Adding zero", "Suma con cero",
            "For every natural number n, n + 0 = n.",
            "Para todo número natural n, n + 0 = n.",
            "Number theory", "KNOWN_THEOREM", "Lean 4 core arithmetic",
            "theorem prooflab_add_zero (n : Nat) : n + 0 = n",
            ["rfl", "omega", "simp"]),
        new(
            "NAT-ADD-COMM", "Commutativity of natural addition",
            "Conmutatividad de la suma",
            "For all natural numbers a and b, a + b = b + a.",
            "Para todos los naturales a y b, a + b = b + a.",
            "Number theory", "KNOWN_THEOREM", "Lean 4 core arithmetic",
            "theorem prooflab_add_comm (a b : Nat) : a + b = b + a",
            ["rfl", "omega", "simp"]),
        new(
            "NAT-ADD-BOUND", "Nonnegative increment",
            "Incremento no negativo",
            "For all natural numbers a and b, a ≤ a + b.",
            "Para todos los naturales a y b, a ≤ a + b.",
            "Number theory", "KNOWN_THEOREM", "Lean 4 core arithmetic",
            "theorem prooflab_add_bound (a b : Nat) : a ≤ a + b",
            ["rfl", "omega", "simp"])
    ];

    public static IReadOnlyList<MathProblem> GetProblems() => KnownBenchmarks;

    public static MathProblem Find(string? id) =>
        KnownBenchmarks.FirstOrDefault(x =>
            string.Equals(x.Id, id, StringComparison.Ordinal))
        ?? throw new ArgumentException("Unknown mathematical problem.");

    public static string BuildLeanSource(MathProblem problem, string? strategy)
    {
        // Deliberately accept ONLY three fixed tactics. No arbitrary Lean or
        // user/LLM-generated source code is executed on the host.
        if (strategy is not ("rfl" or "omega" or "simp") ||
            !problem.AllowedStrategies.Contains(strategy, StringComparer.Ordinal))
            throw new ArgumentException("Unapproved proof strategy.");
        if (!KnownBenchmarks.Any(x => ReferenceEquals(x, problem)))
            throw new ArgumentException("Only curated known benchmarks may be executed.");

        return "import Lean\nset_option autoImplicit false\n\n" +
            problem.LeanDeclaration + " := by\n  " + strategy + "\n";
    }

    public static string Sha256(string source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))
            .ToLowerInvariant();
}
