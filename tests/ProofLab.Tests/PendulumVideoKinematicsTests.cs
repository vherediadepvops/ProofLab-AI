using ProofLab.Science;

namespace ProofLab.Tests;

public sealed class PendulumVideoKinematicsTests
{
    private const double L = .5;
    private const double PivotX = 240, PivotY = 40;
    private static IReadOnlyList<VideoPixelMotionSample> MakeTrack(
        double seconds = 18, double period = 1.45, double amplitudeDegrees = 15,
        double radius = 157, double noise = 0)
    {
        return Enumerable.Range(0, (int)(seconds * 12))
            .Select(i =>
            {
                var t = i / 12d;
                var a = amplitudeDegrees * Math.PI / 180d *
                    Math.Cos(2 * Math.PI * t / period);
                var dx = radius * Math.Sin(a);
                var dy = radius * Math.Cos(a);
                return new VideoPixelMotionSample(t,
                    PivotX + dx + noise*Math.Sin(11*i),
                    PivotY + dy + noise*Math.Cos(7*i), 8);
            }).ToArray();
    }

    [Fact]
    public void AnglesAndAngularVelocityUseObservedPixelsNotTheoreticalPeriod()
    {
        var actualPeriod = 1.61; // deliberately different from the ideal model
        var results = PendulumVideoKinematics.Analyze(MakeTrack(period:actualPeriod),
            new PendulumPivotInput(PivotX,PivotY,L,false), 15);
        Assert.Equal(PendulumVideoKinematics.Version, results.Method);
        Assert.Contains("NOT_SAM",results.Method);
        Assert.True(results.DerivativePoints > 170);
        Assert.Equal(results.SourcePoints,results.Samples.Count);
        Assert.InRange(results.MedianRadiusPixels,156,158);
        Assert.True(results.RadiusMedianAbsoluteDeviationPercent < .01);
        Assert.False(results.PhysicalUnitsEnabled);
        Assert.All(results.Samples, sample =>
        {
            Assert.Null(sample.TangentialSpeedMetersPerSecond);
            Assert.Null(sample.CentripetalAccelerationMetersPerSecondSquared);
        });
        var peakObserved=results.ObservedMaximumAngularSpeedRadPerSec;
        Assert.NotNull(peakObserved);
        var expected=15*Math.PI/180 * 2*Math.PI/actualPeriod;
        Assert.InRange(peakObserved!.Value,expected*.84,expected*1.10);
        Assert.True(Math.Abs(peakObserved.Value-
            results.TheoreticalMaxAngularSpeedRadPerSec!.Value)>.05);
    }

    [Fact]
    public void RealMotionHasHighSpeedAtCenterAndNearZeroAtExtremes_InCleanFixture()
    {
        var results=PendulumVideoKinematics.Analyze(MakeTrack(),
            new PendulumPivotInput(PivotX,PivotY,L,false));
        Assert.True(results.ObservedCenterAngularSpeedRadPerSec > .9);
        Assert.True(results.ObservedExtremeAngularSpeedRadPerSec <
                    results.ObservedCenterAngularSpeedRadPerSec * .55);
        var theta0=results.Samples[0];
        Assert.InRange(theta0.AngleDegrees,14.99,15.01);
        var nearBottom=results.Samples.Where(s => s.IsValid &&
            Math.Abs(s.AngleDegrees)<1.5)
            .OrderBy(s=>Math.Abs(s.AngleDegrees)).First();
        Assert.True(Math.Abs(nearBottom.AngularVelocityRadiansPerSecond!.Value)>.9);
        // No theoretical force is used to clamp endpoints to zero.
        Assert.True(results.Samples.All(s=>double.IsFinite(s.AngleDegrees)));
    }

    [Fact]
    public void PhysicalUnitsRequireExplicitFrontCameraAcknowledgement()
    {
        var track=MakeTrack();
        var uncalibrated=PendulumVideoKinematics.Analyze(track,
            new PendulumPivotInput(PivotX,PivotY,L,false));
        var approved=PendulumVideoKinematics.Analyze(track,
            new PendulumPivotInput(PivotX,PivotY,L,true));
        Assert.False(uncalibrated.PhysicalUnitsEnabled);
        Assert.True(approved.PhysicalUnitsEnabled);
        var sample=approved.Samples.First(s=>s.IsValid);
        Assert.Equal(Math.Abs(sample.AngularVelocityRadiansPerSecond!.Value)*L,
            sample.TangentialSpeedMetersPerSecond!.Value,9);
        Assert.Equal(sample.AngularAccelerationRadiansPerSecondSquared!.Value*L,
            sample.TangentialAccelerationMetersPerSecondSquared!.Value,9);
        Assert.Equal(Math.Pow(sample.AngularVelocityRadiansPerSecond!.Value,2)*L,
            sample.CentripetalAccelerationMetersPerSecondSquared!.Value,9);
        Assert.Contains("no independent",approved.LimitationsEn);
    }

    [Fact]
    public void NoisyPhysicalRadiusRefusesPhysicalScaleEvenWhenRequested()
    {
        var noisy=MakeTrack(noise:23);
        var result=PendulumVideoKinematics.Analyze(noisy,
            new PendulumPivotInput(PivotX,PivotY,L,true));
        Assert.False(result.PhysicalUnitsEnabled);
        Assert.All(result.Samples, s=>
            Assert.Null(s.TangentialSpeedMetersPerSecond));
    }

    [Fact]
    public void MissingFramesDoNotInventAngularVelocityAcrossGap()
    {
        var track=MakeTrack().Where(p=>p.TimeSeconds<4 || p.TimeSeconds>5.2)
            .ToArray();
        var result=PendulumVideoKinematics.Analyze(track,
            new PendulumPivotInput(PivotX,PivotY,L,false));
        var nearBreak=result.Samples.Where(x=>x.TimeSeconds>3.9 &&
                                                 x.TimeSeconds<5.5).ToArray();
        Assert.Contains(nearBreak, x=>!x.IsValid);
        Assert.True(result.DerivativePoints < result.SourcePoints-4);
    }

    [Fact]
    public void StationaryBobCannotShowNonzeroVelocity()
    {
        var track=Enumerable.Range(0,200).Select(i=>
            new VideoPixelMotionSample(i/12d,240,190,9)).ToArray();
        var result=PendulumVideoKinematics.Analyze(track,
            new PendulumPivotInput(PivotX,PivotY,L,false));
        Assert.True((result.ObservedMaximumAngularSpeedRadPerSec ?? 0)<.00001);
        Assert.True(result.Samples.Where(x=>x.IsValid).All(s=>
            Math.Abs(s.AngularVelocityRadiansPerSecond!.Value)<.00001));
    }

    [Theory]
    [InlineData(-1,40)]
    [InlineData(241,double.NaN)]
    [InlineData(241,400)]
    public void InvalidPivotCoordinatesFailClosed(double x,double y)
    {
        Assert.Throws<ArgumentException>(()=>
            PendulumVideoKinematics.Analyze(MakeTrack(),
                new PendulumPivotInput(x,y,L,false)));
    }

    [Fact]
    public void UnsortedInputCannotCreateScientificReport()
    {
        var bad=MakeTrack().ToArray();
        bad[12]=bad[0];
        Assert.Throws<ArgumentException>(()=>
            PendulumVideoKinematics.Analyze(bad,
                new PendulumPivotInput(PivotX,PivotY,L,false)));
    }
}
