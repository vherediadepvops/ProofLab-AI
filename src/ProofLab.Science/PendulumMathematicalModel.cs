namespace ProofLab.Science;

/// <summary>
/// Bilingual-friendly deterministic NONLINEAR ideal pendulum. No model calls,
/// no video evidence, no fitting to an observed period and no damping.
/// y = L(1 - cos theta) is height above the lowest position, not a camera pixel.
/// </summary>
public sealed record PendulumMathematicalRequest(
    double LengthMeters,
    double AngleDegrees,
    double MassGrams,
    double? FollowUpLengthMeters = null);

public sealed record PendulumMathematicalSample(
    double TimeSeconds,
    double AngleDegrees,
    double AngularVelocityRadiansPerSecond,
    double HorizontalCentimeters,
    double ElevationCentimeters,
    double KineticJoules,
    double PotentialJoules,
    double TotalJoules);

public sealed record PendulumMathematicalTrial(
    double LengthMeters,
    double InitialAngleDegrees,
    double MassGrams,
    double SmallAnglePeriodSeconds,
    double NonlinearPeriodSeconds,
    double AmplitudeCentimeters,
    double InitialPotentialJoules,
    double MaximumRelativeEnergyDriftPercent,
    IReadOnlyList<PendulumMathematicalSample> Samples);

public sealed record PendulumMathematicalPrediction(
    string Source,
    double StandardGravityMetersPerSecondSquared,
    string Equation,
    string SmallAngleApproximation,
    string Assumptions,
    PendulumMathematicalTrial Baseline,
    PendulumMathematicalTrial? FollowUp,
    double? PredictedPeriodIncreasePercent);

public static class PendulumMathematicalModel
{
    public const string Provenance = "DETERMINISTIC_NONLINEAR_RK4_THEORY_NOT_META_NOT_SAM";
    private const double G = ScientificModels.StandardGravity;
    private const int IntegrationSteps = 2000;
    private const int SampleEvery = 4; // 501 data points at 0.04 sec intervals.
    private const double StepSeconds = 0.01;

    public static void Validate(double lengthMeters, double angleDegrees, double massGrams)
    {
        if (!double.IsFinite(lengthMeters) || lengthMeters is < .1 or > 5)
            throw new ArgumentException("Length must be 0.10 to 5.00 m.");
        if (!double.IsFinite(angleDegrees) || angleDegrees is < 1 or > 85)
            throw new ArgumentException("Initial angle must be 1 to 85 degrees.");
        if (!double.IsFinite(massGrams) || massGrams is < 10 or > 5000)
            throw new ArgumentException("Bob mass must be 10 to 5000 g.");
    }

    public static PendulumMathematicalPrediction Predict(PendulumMathematicalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request.LengthMeters, request.AngleDegrees, request.MassGrams);
        if (request.FollowUpLengthMeters is double second)
        {
            Validate(second, request.AngleDegrees, request.MassGrams);
            if (Math.Abs(second - request.LengthMeters) < 1e-9)
                throw new ArgumentException("Follow-up length must differ from baseline.");
        }

        var baseline = Simulate(request.LengthMeters, request.AngleDegrees,
            request.MassGrams);
        var followup = request.FollowUpLengthMeters is double length
            ? Simulate(length, request.AngleDegrees, request.MassGrams)
            : null;
        var increase = followup is null ? (double?)null :
            100 * (followup.NonlinearPeriodSeconds / baseline.NonlinearPeriodSeconds - 1);
        return new PendulumMathematicalPrediction(
            Provenance, G,
            "d²θ/dt² + (g/L)sin(θ) = 0",
            "Tsmall = 2π√(L/g)",
            "Ideal point mass, rigid massless string, fixed pivot, no air resistance, " +
            "released from rest in a vertical plane. Coordinates from numerical RK4; " +
            "finite-amplitude period computed with the arithmetic-geometric mean. " +
            "Same initial angle is assumed in an optional second trial.",
            baseline, followup, increase);
    }

    private static PendulumMathematicalTrial Simulate(
        double length, double degrees, double grams)
    {
        var massKg = grams / 1000d;
        var radians = degrees * Math.PI / 180d;
        var smallPeriod = 2 * Math.PI * Math.Sqrt(length / G);
        // Exact mathematical period of the ideal finite-amplitude pendulum:
        // 4 sqrt(L/g) K(sin(theta0/2)), AGM(1, cos(theta0/2)) identity.
        double a = 1, b = Math.Cos(radians / 2);
        for (var i = 0; i < 25 && Math.Abs(a - b) > 1e-15; i++)
        {
            var next = (a + b) / 2;
            b = Math.Sqrt(a * b);
            a = next;
        }
        var nonlinearPeriod = smallPeriod / a;

        var theta = radians;
        var velocity = 0d;
        var initialEnergy = massKg * G * length * (1 - Math.Cos(radians));
        var maximumDrift = 0d;
        var output = new List<PendulumMathematicalSample>(IntegrationSteps / SampleEvery + 1);

        void Add(int step)
        {
            var horizontal = length * Math.Sin(theta) * 100d;
            var elevation = length * (1 - Math.Cos(theta)) * 100d;
            var kinetic = .5 * massKg * Math.Pow(length * velocity, 2);
            var potential = massKg * G * length * (1 - Math.Cos(theta));
            var energy = kinetic + potential;
            var relativeDrift = Math.Abs(energy - initialEnergy)
                / Math.Max(1e-12, initialEnergy) * 100;
            maximumDrift = Math.Max(maximumDrift, relativeDrift);
            output.Add(new PendulumMathematicalSample(step * StepSeconds,
                theta * 180d / Math.PI, velocity,
                horizontal, elevation, kinetic, potential, energy));
        }

        double AngularAcceleration(double angle) => -G / length * Math.Sin(angle);
        for (var step = 0; step <= IntegrationSteps; step++)
        {
            if (step % SampleEvery == 0) Add(step);
            if (step == IntegrationSteps) break;
            // Fixed-step classic RK4, no damping or data-driven correction.
            var k1Theta = velocity;
            var k1Omega = AngularAcceleration(theta);
            var k2Theta = velocity + .5 * StepSeconds * k1Omega;
            var k2Omega = AngularAcceleration(theta + .5 * StepSeconds * k1Theta);
            var k3Theta = velocity + .5 * StepSeconds * k2Omega;
            var k3Omega = AngularAcceleration(theta + .5 * StepSeconds * k2Theta);
            var k4Theta = velocity + StepSeconds * k3Omega;
            var k4Omega = AngularAcceleration(theta + StepSeconds * k3Theta);
            theta += StepSeconds / 6 * (k1Theta + 2 * k2Theta + 2 * k3Theta + k4Theta);
            velocity += StepSeconds / 6 * (k1Omega + 2 * k2Omega + 2 * k3Omega + k4Omega);
        }

        return new PendulumMathematicalTrial(length, degrees, grams,
            smallPeriod, nonlinearPeriod,
            100d * length * Math.Sin(radians),
            initialEnergy, maximumDrift, output);
    }
}
