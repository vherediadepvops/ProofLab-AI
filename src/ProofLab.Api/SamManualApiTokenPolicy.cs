namespace ProofLab.Api;

/// <summary>
/// Accepts a single manually pasted Meta key for one operator-initiated
/// request. The key is never stored, displayed, audited or logged.
/// This policy does NOT authorize paid requests: existing backend gates,
/// attempt locks and per-process safety budget remain mandatory.
/// </summary>
public static class SamManualApiTokenPolicy
{
    public static bool Accepts(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length is < 24 or > 1024 ||
            !(token.StartsWith("LLM_", StringComparison.Ordinal) ||
              token.StartsWith("LLM|", StringComparison.Ordinal)))
            return false;
        foreach (var character in token)
            if (character is < '!' or > '~') return false;
        return true;
    }
}
