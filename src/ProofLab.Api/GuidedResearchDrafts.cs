using ProofLab.Science;

namespace ProofLab.Api;

/// <summary>
/// Transparent, deterministic draft suggestions using curated material and actual
/// persisted evidence. NO LLM and NO scientific approval or invented observation.
/// </summary>
public sealed record GuidedResearchDraft(
    string ResearchId, int Stage, string Language, string Mode,
    IReadOnlyDictionary<string, string> Fields, string EvidenceSource,
    bool HumanReviewRequired, bool IsAutonomousAi, bool IsScientificDiscovery);

public sealed class GuidedResearchDrafts(
    ResearchWorkflowStore research,
    MathEvidenceWorkflow math,
    OpenConjectureEvidenceWorkflow finite)
{
    public async Task<GuidedResearchDraft> PrepareAsync(
        string researchId, int stage, string language, CancellationToken ct)
    {
        if (language is not ("es" or "en"))
            throw new ArgumentException("Use language 'es' or 'en'.");
        var project = await research.GetAsync(researchId, ct);
        if (project.Discipline != "Mathematics" ||
            !project.Title.Contains("Goldbach", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "Guided drafts currently support only explicitly titled Goldbach mathematics dossiers.");
        if (stage != project.ActiveStage || stage is < 1 or > 5)
            throw new ArgumentException("Guided drafts are available only for the active, editable stage.");

        var es = language == "es";
        var conjecture = OpenConjectureCatalog.Find("GOLDBACH-STRONG");
        var sourceUrl = conjecture.StatusSourceUrl;
        var literature = $"https://doi.org/{conjecture.LiteratureDoi}";
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        if (stage == 1)
        {
            fields["question"] = es
                ? "¿Para cada número par entre 4 y 100 existe una pareja de números primos cuya suma sea ese número? Se comprobará el intervalo finito de forma reproducible."
                : "Does every even integer from 4 to 100 have a pair of prime numbers that sums to it? The finite interval will be checked reproducibly.";
            fields["background"] = es
                ? $"La conjetura fuerte de Goldbach sigue catalogada como abierta (revisión {conjecture.StatusCheckedOn}). Ya existen verificaciones computacionales de rangos muchísimo mayores; por tanto, explorar 4..100 no es un hallazgo original ni una demostración universal."
                : $"The strong Goldbach conjecture remains catalogued as open (status review {conjecture.StatusCheckedOn}). Prior computational work verified vastly larger ranges; checking 4..100 is neither original discovery nor a universal proof.";
            fields["sources"] = $"{sourceUrl}\n{literature}\n" +
                (es ? "Fuentes curadas del catálogo científico de ProofLab AI." :
                    "Sources curated in the ProofLab AI scientific catalog.");
        }
        else if (stage == 2)
        {
            fields["hypothesis"] = es
                ? "En cada uno de los 49 números pares de 4 a 100 se encontrará al menos una pareja de primos cuya suma coincide con el valor examinado. Esta hipótesis solo cubre el intervalo."
                : "Each of the 49 even integers from 4 to 100 is expected to have at least one prime pair summing to that integer. This hypothesis concerns only this bounded range.";
            fields["alternatives"] = es
                ? "Podría existir un caso sin testigo en el intervalo, o un error de clasificación de primos o del algoritmo podría producir un resultado aparente. Ambos escenarios requieren comprobación independiente."
                : "A case might lack a witness, or a prime-classification or algorithm defect could produce an apparent failure. Both require independent reproduction.";
            fields["falsification"] = es
                ? "La hipótesis finita sería contradicha por un par entre 4 y 100 sin ninguna representación válida tras enumeración exhaustiva y revisión independiente. Un fallo de software aislado es inconcluso."
                : "A bounded case without any valid prime-pair representation after exhaustive enumeration and independent checking would contradict the finite hypothesis. A software failure alone is inconclusive.";
        }
        else if (stage == 3)
        {
            fields["procedure"] = es
                ? "Protocolo prefijado: enumerar los 49 pares de 4 a 100 mediante GoldbachFiniteExperiment en C#/.NET 10; buscar y registrar parejas de primos. Conservar experimento, hora UTC, rango, resultado e identificadores SHA-256. Como control separado, usar Lean 4 v4.35.0 en un teorema YA CONOCIDO (NAT-ADD-COMM): registrar rechazo de rfl y, según diagnóstico real, probar omega. No ejecutar Lean libre ni llamar a Muse Spark."
                : "Prespecified protocol: enumerate 49 even integers from 4 to 100 with GoldbachFiniteExperiment in C#/.NET 10; search for prime witnesses. Record experiment ID, UTC, bounds, result and SHA-256 digests. As a SEPARATE control, run Lean 4 v4.35.0 on a KNOWN theorem (NAT-ADD-COMM), logging rfl rejection and conditionally trying omega after actual compiler feedback. No free-form Lean or Muse Spark calls.";
            fields["controls"] = es
                ? "Verificar límites 4 y 100 y casos pequeños como 4=2+2 y 6=3+3. Exigir primos verificables y suma exacta; contar 49 valores pares. Lean comprobará solo su enunciado conocido, NO Goldbach. Registrar tanto éxitos como fallos."
                : "Check bounds 4 and 100 and small cases such as 4=2+2 and 6=3+3. Require valid primes and exact sums; count 49 even cases. Lean checks only its separate known statement, NOT Goldbach. Record successes and failures.";
            fields["analysisPlan"] = es
                ? "Métricas fijadas antes de ejecutar: números pares examinados, casos con pareja prima, casos sin pareja, diagnóstico de tácticas Lean y correspondencia entre IDs y SHA-256. Cero contraejemplos en el rango respalda solo el resultado finito, nunca la conjetura general."
                : "Prespecified metrics: even cases checked, cases with prime witnesses, cases lacking witnesses, Lean tactic outcomes and matching evidence IDs/SHA-256. Zero counterexamples within the range only supports a bounded result, never the universal conjecture.";
            fields["ethicsSafety"] = es
                ? "Se emplearán únicamente enteros y teoremas públicos, sin datos personales, experimentos físicos ni permisos de pago. Todo script Lean está restringido a tácticas curadas. La aprobación humana del informe no implica revisión científica independiente."
                : "Only public integers and known mathematical theorems are used, with no personal data, physical experimentation or paid calls. Lean execution is restricted to curated tactics. Human acknowledgment of documentation does not constitute scientific peer review.";
        }
        else
        {
            var mathRows = await math.ListAsync(researchId, ct);
            var finiteRows = await finite.ListAsync(researchId, ct);
            var proof = mathRows
                .OrderByDescending(x => x.Verification.Outcome == "VERIFIED")
                .ThenByDescending(x => x.Verification.CheckedAtUtc)
                .FirstOrDefault();
            var bounded = finiteRows
                .OrderByDescending(x => x.Result.CheckedAtUtc)
                .FirstOrDefault();
            if (proof is null || bounded is null)
                throw new ArgumentException(
                    "Before preparing stage 4/5, execute a real Goldbach finite check AND a known-theorem Lean attempt for this dossier.");
            var v = proof.Verification;
            var g = bounded.Result;
            var leanSource = $"Lean attempt {v.AttemptId}: {v.ProblemId}, tactic {v.Strategy}, " +
                $"outcome {v.Outcome}, SHA-256 {v.SourceSha256}, UTC {v.CheckedAtUtc:O}.";
            var finiteSource = $"Goldbach finite experiment {g.ExperimentId}: " +
                $"4..{g.CheckedThrough}, examined {g.EvenCasesExamined}, " +
                $"prime witnesses {g.CasesWithPrimePair}, outcome {g.Outcome}, " +
                $"SHA-256 {g.ResultSha256}, UTC {g.CheckedAtUtc:O}.";
            if (stage == 4)
            {
                fields["manifest"] = (es
                    ? "Evidencias generadas por las herramientas reales del servidor (NO por Muse Spark):\n"
                    : "Evidence produced by actual server tools (NOT Muse Spark):\n") +
                    finiteSource + "\n" + leanSource + "\n" +
                    (es ? "Lean verificó un objetivo de CONTROL CONOCIDO, no Goldbach."
                        : "Lean checked a KNOWN CONTROL goal, not Goldbach.");
                fields["execution"] = (es
                    ? "Se registró el experimento acotado y se guardaron identificadores y huellas. Resultado computacional: "
                    : "The bounded computation and identifiers/hashes were recorded. Numerical tool outcome: ") +
                    g.Outcome + ". " + (es ? "Control formal independiente, de teorema conocido: "
                        : "Separate formal benchmark on a known theorem: ") + v.Outcome +
                    $". " + (es ? $"Datos observados solo en 4..{g.CheckedThrough}; no hay verificación Lean del enunciado Goldbach." : $"Observed results cover only 4..{g.CheckedThrough}; no Lean proof of Goldbach was executed.");
                fields["provenance"] = (es
                    ? "Origen: motor C# GoldbachFiniteExperiment y verificador Lean 4 limitado. "
                    : "Source: C# GoldbachFiniteExperiment and constrained Lean 4 verifier. ") +
                    $"Goldbach ID {g.ExperimentId}; Lean ID {v.AttemptId}. " +
                    (es ? "Son resultados computacionales y un control formal conocido, no IA autónoma, prueba universal ni nuevo descubrimiento."
                        : "These are finite computations and a known formal proof control, not autonomous AI, universal proof or novel discovery.");
            }
            else
            {
                fields["results"] = (es
                    ? $"El motor examinó {g.EvenCasesExamined} pares del rango 4..{g.CheckedThrough} y registró {g.CasesWithPrimePair} casos con testigos primos. "
                    : $"The engine checked {g.EvenCasesExamined} even cases in 4..{g.CheckedThrough} and logged {g.CasesWithPrimePair} cases with prime witnesses. ") +
                    $"Resultado registrado: {g.Outcome}. " +
                    (es ? "Se contrasta únicamente con la hipótesis finita preregistrada. "
                        : "Only the prespecified finite hypothesis is evaluated. ") +
                    $"Referencia: {g.ExperimentId}, SHA-256 {g.ResultSha256}.";
                fields["verification"] = (es
                    ? "Verificación formal SEPARADA de teorema conocido: "
                    : "SEPARATE formal verification of a known theorem: ") +
                    leanSource + " " +
                    (es ? "Este intento Lean no comprueba la conjetura de Goldbach; comprobar testigos finitos tampoco demuestra el caso universal."
                        : "This Lean attempt does not verify Goldbach; finite prime-pair checks also do not establish its universal truth.");
                fields["limitations"] = (es
                    ? "Conclusión limitada al intervalo computado y al resultado exacto de la herramienta. "
                    : "Conclusion restricted to the computed interval and actual tool outcome. ") +
                    $"Goldbach finite outcome: {g.Outcome}. " +
                    (es ? "No existe formalización Lean revisada de Goldbach ni investigación autónoma con Muse. Los teoremas conocidos no son descubrimientos originales; el control documental no equivale a revisión por pares."
                        : "No reviewed Lean Goldbach formalization or autonomous Muse research was performed. Known theorems are not original discoveries; documentation completeness is not peer review.");
            }
        }

        var defined = ScientificMethodWorkflow.Stage(stage);
        if (defined.Fields.Any(f => !fields.ContainsKey(f.Key)) ||
            fields.Keys.Any(k => !defined.Fields.Any(f => f.Key == k)))
            throw new InvalidOperationException("Guided template fields do not match current scientific stage.");
        // A preview only; never persist draft, mark human review, or approve.
        return new GuidedResearchDraft(researchId, stage, language,
            "CURATED_DETERMINISTIC_OFFLINE_TEMPLATE_NOT_AI", fields,
            stage <= 3 ? "DATED_CURATED_GOLDBACH_CATALOG_AND_FIXED_PROTOCOL"
                : "ACTUAL_RECORDED_LEAN_AND_FINITE_EVIDENCE",
            HumanReviewRequired: true, IsAutonomousAi: false,
            IsScientificDiscovery: false);
    }
}
