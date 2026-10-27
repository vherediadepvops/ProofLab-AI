namespace ProofLab.Science;

/// <summary>
/// Human-readable feedback for exported scientific dossiers.
/// This never mutates the raw Lean outcome, proof text or digest.
/// A rejected tactic does not refute a theorem.
/// </summary>
public static class LeanResultLanguage
{
    public static string Outcome(string outcome, bool spanish) => outcome switch
    {
        "VERIFIED" => spanish ? "VERIFICADO" : "VERIFIED",
        "REJECTED" => spanish ? "RECHAZADO" : "REJECTED",
        "TIMED_OUT" => spanish ? "TIEMPO AGOTADO" : "TIMED OUT",
        "UNAVAILABLE" => spanish ? "NO DISPONIBLE" : "UNAVAILABLE",
        _ => spanish ? "SIN CLASIFICAR" : "UNCLASSIFIED"
    };

    public static string Explain(MathVerificationResult result, bool spanish)
    {
        if (result.Outcome == "VERIFIED")
            return result.IsNewDiscovery
                ? spanish
                    ? "Lean aceptó la prueba formal. La originalidad y la equivalencia del enunciado requieren revisión independiente."
                    : "Lean accepted the formal proof. Novelty and statement equivalence require independent review."
                : spanish
                    ? "Lean 4 verificó un teorema ya conocido. No constituye un descubrimiento científico nuevo."
                    : "Lean 4 verified a previously known theorem. This is not a new scientific discovery.";

        if (result.Outcome == "REJECTED")
        {
            if (result.Strategy == "rfl")
                return spanish
                    ? "Lean rechazó «rfl»: ambos lados no son iguales por definición. La táctica no demuestra el teorema, pero tampoco lo refuta."
                    : "Lean rejected «rfl»: the sides are not equal by definition. The tactic does not prove the theorem, but it does not disprove it either.";
            if (result.Strategy == "simp")
                return spanish
                    ? "Lean rechazó «simp»: las reglas de simplificación no lograron demostrar el objetivo. El teorema no queda refutado."
                    : "Lean rejected «simp»: simplification rules did not prove the goal. The theorem is not disproved.";
            return spanish
                ? "Lean no verificó el intento con «" + result.Strategy + "». Esto no refuta el teorema."
                : "Lean did not verify the «" + result.Strategy + "» attempt. This does not disprove the theorem.";
        }
        if (result.Outcome == "TIMED_OUT")
            return spanish
                ? "Se agotó el tiempo de verificación. No existe una prueba confirmada ni refutación."
                : "Verification timed out. No proof or disproof was established.";
        if (result.Outcome == "UNAVAILABLE")
            return spanish
                ? "El verificador Lean no estaba disponible. No se completó ninguna comprobación."
                : "The Lean verifier was unavailable. No verification was completed.";
        return spanish ? "No hay resultado formal verificable." : "No verifiable formal result is available.";
    }
}
