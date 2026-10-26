namespace ProofLab.Science;

public enum ExperimentKind { Pendulum, RollingSphere }
public enum EvidenceOrigin { Simulated, ManuallyEntered, TrackingCoordinates }

public sealed record ExperimentSetup(
    ExperimentKind Kind,
    double? LengthMeters = null,
    double? AngleDegrees = null);

public sealed record Observation(
    double TimeSeconds,
    double Position);

public sealed record TheoryResult(
    ExperimentKind Kind,
    string Metric,
    string Unit,
    double ExpectedValue,
    string ScientificModel,
    string Assumptions);

public sealed record EvidenceRequest(
    ExperimentSetup Setup,
    IReadOnlyList<Observation> Observations,
    EvidenceOrigin Origin,
    string? Reference = null);

public sealed record ScientificResult(
    string Experiment,
    string Metric,
    string Unit,
    double PredictedValue,
    double ObservedValue,
    double DifferencePercent,
    int SampleCount,
    string Method,
    EvidenceOrigin Origin,
    string EvidenceStatement,
    string Limitations);
