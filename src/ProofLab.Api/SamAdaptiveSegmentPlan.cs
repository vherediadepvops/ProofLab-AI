using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ProofLab.Api;

/// <summary>
/// Dynamic frame-exact proposal for a FUTURE authorized SAM experiment.
/// Pure offline arithmetic. The 50-frame minimum is a BILLING floor per
/// request, not a proven throughput optimum or a hard video-length minimum.
/// No Meta calls, FFmpeg or spending gates are connected to this planner.
/// </summary>
public sealed record SamAdaptiveWindow(
    int Part,int CoreStart,int CoreEndExclusive,
    int InputStart,int InputEndExclusive)
{
    public int CoreFrames => CoreEndExclusive-CoreStart;
    public int InputFrames => InputEndExclusive-InputStart;
    public int BilledFrames => Math.Max(SamAdaptiveSegmentPlan.MinimumBilledFrames,InputFrames);
    public int GlobalFrame(int localFrame)
    {
        if(localFrame<0 || localFrame>=InputFrames)
            throw new ArgumentOutOfRangeException(nameof(localFrame));
        return InputStart+localFrame;
    }
}

public sealed class SamAdaptiveSegmentPlan
{
    public const int MinimumBilledFrames=50;
    public const int TargetCoreFrames=53;
    public const int OverlapFrames=2;
    public const int MaximumSourceFrames=2500; // current ProofLab preflight limit
    public const int MaximumPlannedParts=50;
    public const int MaximumConcurrency=32;
    public const double SamUsdPerFrame=0.0002;
    public const double DefaultMaximumSamUsd=0.50;

    public int SourceFrames { get; }
    public int RequestedTargetCoreFrames { get; }
    public int PartCount => Windows.Count;
    public int MaxInFlight { get; }
    public double BudgetCapUsd { get; }
    public int SubmittedFrames { get; }
    public int BilledFrames { get; }
    public int OverlapExtraFrames => SubmittedFrames-SourceFrames;
    public double EstimatedSamCostUsd { get; }
    public double UnsplitSamCostUsd => Math.Max(MinimumBilledFrames,SourceFrames)*SamUsdPerFrame;
    public bool BudgetReducedPartCount { get; }
    public IReadOnlyList<SamAdaptiveWindow> Windows { get; }

    private SamAdaptiveSegmentPlan(int sourceFrames,int target,int maxInFlight,
        double budget,IReadOnlyList<SamAdaptiveWindow> windows,bool budgetReduced)
    {
        SourceFrames=sourceFrames;
        RequestedTargetCoreFrames=target;
        MaxInFlight=Math.Min(maxInFlight,windows.Count);
        BudgetCapUsd=budget;
        Windows=windows;
        BudgetReducedPartCount=budgetReduced;
        SubmittedFrames=windows.Sum(w=>w.InputFrames);
        BilledFrames=windows.Sum(w=>w.BilledFrames);
        EstimatedSamCostUsd=BilledFrames*SamUsdPerFrame;
    }

    public static SamAdaptiveSegmentPlan Create(int frames,
        int targetCoreFrames=TargetCoreFrames,
        int maxInFlight=4,
        double maximumSamCostUsd=DefaultMaximumSamUsd)
    {
        if(frames is < MinimumBilledFrames or > MaximumSourceFrames)
            throw new ArgumentOutOfRangeException(nameof(frames),
                "Source frame count is outside current SAM video preflight limits.");
        if(targetCoreFrames is < MinimumBilledFrames or > 500)
            throw new ArgumentOutOfRangeException(nameof(targetCoreFrames));
        if(maxInFlight is <1 or >MaximumConcurrency)
            throw new ArgumentOutOfRangeException(nameof(maxInFlight),
                "Concurrent requests must be explicitly bounded.");
        if(!double.IsFinite(maximumSamCostUsd) ||
           maximumSamCostUsd<=0 || maximumSamCostUsd>DefaultMaximumSamUsd)
            throw new ArgumentOutOfRangeException(nameof(maximumSamCostUsd),
                "The dynamic proposal never raises the existing US$0.50 SAM cap.");
        // Default 53 gives the owner's examples: 1000->19 and 1500->28.
        var ideal=Math.Clamp(
            (int)Math.Round(frames/(double)targetCoreFrames,
                MidpointRounding.AwayFromZero),
            1,Math.Min(MaximumPlannedParts,frames/MinimumBilledFrames));
        for(var count=ideal;count>=1;count--)
        {
            var windows=CreateWindows(frames,count);
            var billed=windows.Sum(w=>w.BilledFrames);
            if(billed*SamUsdPerFrame<=maximumSamCostUsd+1e-10)
                return new(frames,targetCoreFrames,maxInFlight,maximumSamCostUsd,
                    windows,count!=ideal);
        }
        throw new InvalidOperationException(
            "Even the unsplit video exceeds the owner-approved SAM cost cap.");
    }

    private static IReadOnlyList<SamAdaptiveWindow> CreateWindows(int frames,int parts)
    {
        var windows=new List<SamAdaptiveWindow>(parts);
        for(var part=0;part<parts;part++)
        {
            var coreStart=(int)((long)part*frames/parts);
            var coreEnd=(int)((long)(part+1)*frames/parts);
            var inputStart=Math.Max(0,coreStart-OverlapFrames);
            var inputEnd=Math.Min(frames,coreEnd+OverlapFrames);
            if(coreEnd-coreStart<MinimumBilledFrames ||
               inputEnd-inputStart<MinimumBilledFrames)
                throw new InvalidOperationException("A planned SAM segment is too short.");
            windows.Add(new(part,coreStart,coreEnd,inputStart,inputEnd));
        }
        return windows;
    }

    /// <summary>
    /// Optional original-PTS source fingerprint: required before cutting,
    /// uploading or joining any actual recording; never infer PTS from FPS.
    /// </summary>
    public string VerifySourcePts(IReadOnlyList<double> pts)
    {
        if(pts.Count!=SourceFrames || !double.IsFinite(pts[0]) ||
           Math.Abs(pts[0])>0.005)
            throw new InvalidDataException("Source PTS frame count/start mismatch.");
        for(var i=1;i<pts.Count;i++)
            if(!double.IsFinite(pts[i]) || pts[i]<=pts[i-1])
                throw new InvalidDataException("Source PTS are missing or nonmonotonic.");
        var wire=string.Join("\n",pts.Select(t=>
            t.ToString("R",CultureInfo.InvariantCulture)));
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(wire))).ToLowerInvariant();
    }
}
