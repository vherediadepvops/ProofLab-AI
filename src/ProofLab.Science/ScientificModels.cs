namespace ProofLab.Science;

public static class ScientificModels
{
    public const double StandardGravity = 9.80665;

    public static TheoryResult Predict(ExperimentSetup spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec.Kind switch
        {
            ExperimentKind.Pendulum => Pendulum(spec.LengthMeters),
            ExperimentKind.RollingSphere => Sphere(spec.AngleDegrees),
            _ => throw new ArgumentException("Unsupported experiment.")
        };
    }

    private static TheoryResult Pendulum(double? length)
    {
        if (length is null || !double.IsFinite(length.Value) ||
            length < 0.1 || length > 5.0)
            throw new ArgumentException("Pendulum length must be between 0.1 and 5 metres.");

        var period = 2 * Math.PI * Math.Sqrt(length.Value / StandardGravity);
        return new(
            ExperimentKind.Pendulum,
            "Oscillation period", "s", period,
            "T = 2π√(L/g)",
            "Small-angle approximation, fixed pivot, measured pivot-to-centre distance.");
    }

    private static TheoryResult Sphere(double? angle)
    {
        if (angle is null || !double.IsFinite(angle.Value) ||
            angle < 2 || angle > 40)
            throw new ArgumentException("Ramp angle must be between 2° and 40°.");

        var acceleration = (5.0 / 7) * StandardGravity *
                           Math.Sin(angle.Value * Math.PI / 180);
        return new(
            ExperimentKind.RollingSphere,
            "Acceleration along incline", "m/s²", acceleration,
            "a = (5/7)g sin(θ)",
            "Uniform solid sphere rolling without slipping on a fixed incline.");
    }
}
