namespace ProofLab.Science;

/// <summary>
/// Curated open problems are research subjects, never executable Lean benchmarks.
/// Source status is time-bounded; the literature must be rechecked before claims.
/// </summary>
public sealed record OpenConjecture(
    string Id, string TitleEs, string TitleEn,
    string StatementEs, string StatementEn,
    string Domain, string ResearchStatus, string StatusCheckedOn,
    string StatusSourceUrl, string LiteratureDoi,
    string FormalizationStatus, bool IsExecutableLeanGoal,
    string LimitationsEs, string LimitationsEn);

public static class OpenConjectureCatalog
{
    private static readonly OpenConjecture[] Items =
    [
        new(
            "GOLDBACH-STRONG", "Conjetura fuerte de Goldbach",
            "Strong Goldbach conjecture",
            "Todo entero par mayor que 2 puede expresarse como suma de dos números primos.",
            "Every even integer greater than 2 is the sum of two prime numbers.",
            "Teoría de números / Number theory", "OPEN_RESEARCH",
            "2026-10-27",
            "https://mathworld.wolfram.com/GoldbachConjecture.html",
            "10.1090/S0025-5718-2013-02787-1",
            "LEAN_STATEMENT_NOT_FORMALIZED_OR_REVIEWED", false,
            "Comprobar casos finitos no demuestra el caso universal; "
                + "la literatura ya documenta verificaciones muchísimo mayores. "
                + "No existe una traducción Lean revisada de esta conjetura en ProofLab.",
            "Finite checks do not prove the universal statement; prior literature "
                + "already verifies vastly larger ranges. ProofLab has no "
                + "reviewed Lean formalization of this conjecture.")
    ];

    public static IReadOnlyList<OpenConjecture> GetAll() => Array.AsReadOnly(Items);

    public static OpenConjecture Find(string? id) =>
        Items.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal))
        ?? throw new ArgumentException("Unknown curated open conjecture.");

    public static bool CanExecuteInHost(string? id)
    {
        _ = Find(id);
        return false; // Open conjectures are never delegated to host Lean.
    }
}
