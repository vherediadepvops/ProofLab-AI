namespace ProofLab.Science;

/// <summary>
/// Non-AI temporary laboratory control; never attribute its choice to Muse Spark.
/// Keeping this procedure deterministic allows scientific UX testing without
/// paid inference, before an actual audited model-directed research engine exists.
/// </summary>
public static class PendulumDirectorProtocol
{
    public const string Source = "LOCAL_PROTOCOL_NOT_META_AI_NOT_SAM";

    public static PendulumTrialHypothesis RecommendLocalControl(double lengthMeters)
    {
        _ = ScientificModels.Predict(new ExperimentSetup(ExperimentKind.Pendulum,
            LengthMeters: lengthMeters));
        var followup = Math.Round(lengthMeters * 1.6, 3);
        if (followup > 5)
            followup = Math.Round(lengthMeters * 0.7, 3);
        return PendulumTrialProtocol.Predict(lengthMeters, followup);
    }

    public static bool HasUsableInitialSignal(PendulumSpectralAssessment evidence) =>
        evidence is not null &&
        evidence.Status == PendulumSpectralEstimator.Candidate &&
        evidence.PeriodSeconds is > 0;

    public static (double ObservedChangePercent, double GapPercentagePoints)
        Compare(PendulumTrialHypothesis frozenPrediction,
            double initialMeasuredPeriod, double secondMeasuredPeriod)
    {
        ArgumentNullException.ThrowIfNull(frozenPrediction);
        if (!double.IsFinite(initialMeasuredPeriod) || initialMeasuredPeriod <= 0 ||
            !double.IsFinite(secondMeasuredPeriod) || secondMeasuredPeriod <= 0)
            throw new ArgumentException("Both observations must be positive periods.");
        var observed = (secondMeasuredPeriod / initialMeasuredPeriod - 1) * 100;
        return (observed, observed - frozenPrediction.PredictedIncreasePercent);
    }
}
