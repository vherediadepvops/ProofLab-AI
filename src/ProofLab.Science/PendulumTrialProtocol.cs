namespace ProofLab.Science;

/// <summary>
/// A predeclared, independently calculable small-angle prediction for two pendulum lengths.
/// These values are theoretical; they are not observations or AI-generated discoveries.
/// </summary>
public sealed record PendulumTrialHypothesis(
    double BaselineLengthMeters,
    double FollowUpLengthMeters,
    double ExpectedBaselinePeriodSeconds,
    double ExpectedFollowUpPeriodSeconds,
    double PredictedIncreasePercent,
    string Formula,
    string Assumptions);

public sealed record PendulumTrialComparison(
    ScientificResult Baseline,
    ScientificResult FollowUp,
    double PredictedIncreasePercent,
    double ObservedIncreasePercent,
    double DifferencePercentagePoints,
    string EvidenceClassification,
    string ScientificConclusion,
    string Limitations);

public static class PendulumTrialProtocol
{
    public static PendulumTrialHypothesis Predict(double baselineLengthMeters, double followUpLengthMeters)
    {
        var first = ScientificModels.Predict(new ExperimentSetup(
            ExperimentKind.Pendulum, LengthMeters: baselineLengthMeters));
        var second = ScientificModels.Predict(new ExperimentSetup(
            ExperimentKind.Pendulum, LengthMeters: followUpLengthMeters));

        if (Math.Abs(baselineLengthMeters - followUpLengthMeters) < 1e-9)
            throw new ArgumentException("The follow-up length must differ from the baseline length.");

        return new PendulumTrialHypothesis(
            baselineLengthMeters,
            followUpLengthMeters,
            first.ExpectedValue,
            second.ExpectedValue,
            (second.ExpectedValue / first.ExpectedValue - 1) * 100,
            "T = 2π√(L/g); T₂/T₁ = √(L₂/L₁)",
            "Small angle, same local gravity, rigid pivot, measured pivot-to-bob-centre distance.");
    }

    public static PendulumTrialComparison Compare(
        PendulumTrialHypothesis hypothesis,
        EvidenceRequest baseline,
        EvidenceRequest followUp)
    {
        ArgumentNullException.ThrowIfNull(hypothesis);
        ValidateEvidence(baseline, hypothesis.BaselineLengthMeters);
        ValidateEvidence(followUp, hypothesis.FollowUpLengthMeters);

        var first = EvidenceAnalyzer.Analyze(baseline);
        var second = EvidenceAnalyzer.Analyze(followUp);
        var observedChange = (second.ObservedValue / first.ObservedValue - 1) * 100;
        var gap = observedChange - hypothesis.PredictedIncreasePercent;

        // No result from a user-uploaded sequence can attest to a physical video or
        // its acquisition time; the provenance must remain explicit and conservative.
        var classification = baseline.Origin == EvidenceOrigin.Simulated ||
                             followUp.Origin == EvidenceOrigin.Simulated
            ? "SYNTHETIC_OR_MIXED_NOT_PHYSICAL_EVIDENCE"
            : "USER_SUPPLIED_COORDINATES_NOT_INDEPENDENTLY_AUTHENTICATED";

        return new PendulumTrialComparison(
            first, second,
            hypothesis.PredictedIncreasePercent,
            observedChange,
            gap,
            classification,
            "DESCRIPTIVE_COMPARISON_ONLY_NOT_A_CONFIRMED_DISCOVERY",
            "No independently authenticated source video or timestamp; no uncertainty " +
            "interval, blinded acquisition check, or model-discrimination test. " +
            "The preregistration time predates data submission, not necessarily data recording.");
    }

    private static void ValidateEvidence(EvidenceRequest? request, double expectedLength)
    {
        if (request is null || request.Setup is null ||
            request.Setup.Kind != ExperimentKind.Pendulum ||
            request.Setup.LengthMeters is null ||
            Math.Abs(request.Setup.LengthMeters.Value - expectedLength) > 1e-9)
            throw new ArgumentException("Observation setup must match the preregistered pendulum length.");

        if (string.IsNullOrWhiteSpace(request.Reference) ||
            request.Reference.Length > 200)
            throw new ArgumentException("Each sequence needs a short source reference.");

        if (!Enum.IsDefined(request.Origin))
            throw new ArgumentException("Unsupported observation provenance.");
    }
}
