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
    // New requests use fewer seams; archived native plans can still contain eight parts.
    public const int MaximumNewNativeParts=3;
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
    public bool IntraKeyframeAligned { get; }
    public bool OriginalPacketCopy { get; private set; }
    public IReadOnlyList<SamAdaptiveWindow> Windows { get; }

    private SamAdaptiveSegmentPlan(int sourceFrames,int target,int maxInFlight,
        double budget,IReadOnlyList<SamAdaptiveWindow> windows,bool budgetReduced,
        bool intraAligned)
    {
        SourceFrames=sourceFrames;
        RequestedTargetCoreFrames=target;
        MaxInFlight=Math.Min(maxInFlight,windows.Count);
        BudgetCapUsd=budget;
        Windows=windows;
        BudgetReducedPartCount=budgetReduced;
        IntraKeyframeAligned=intraAligned;
        SubmittedFrames=windows.Sum(w=>w.InputFrames);
        BilledFrames=windows.Sum(w=>w.BilledFrames);
        EstimatedSamCostUsd=BilledFrames*SamUsdPerFrame;
    }

    public static SamAdaptiveSegmentPlan Create(int frames,
        int targetCoreFrames=TargetCoreFrames,
        int maxInFlight=4,
        double maximumSamCostUsd=DefaultMaximumSamUsd,
        bool alignToTwoFrameIntraKeyframes=false,
        int? maximumPartCount=null)
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
        if(maximumPartCount is <1 or >MaximumPlannedParts)
            throw new ArgumentOutOfRangeException(nameof(maximumPartCount));
        // Default 53 gives the owner's examples: 1000->19 and 1500->28.
        var ideal=Math.Clamp(
            (int)Math.Round(frames/(double)targetCoreFrames,
                MidpointRounding.AwayFromZero),
            1,Math.Min(maximumPartCount??MaximumPlannedParts,
                Math.Min(MaximumPlannedParts,frames/MinimumBilledFrames)));
        for(var count=ideal;count>=1;count--)
        {
            var windows=CreateWindows(frames,count,alignToTwoFrameIntraKeyframes);
            var billed=windows.Sum(w=>w.BilledFrames);
            if(billed*SamUsdPerFrame<=maximumSamCostUsd+1e-10)
                return new(frames,targetCoreFrames,maxInFlight,maximumSamCostUsd,
                    windows,count!=ideal,alignToTwoFrameIntraKeyframes);
        }
        throw new InvalidOperationException(
            "Even the unsplit video exceeds the owner-approved SAM cost cap.");
    }

    public static SamAdaptiveSegmentPlan RestoreNative(int frames,int target,int maxInFlight,
        IReadOnlyList<SamAdaptiveWindow> windows) {
        if(frames is <50 or >MaximumSourceFrames || target is <50 or >500 || maxInFlight is <1 or >8 ||
           windows.Sum(w=>w.BilledFrames)*SamUsdPerFrame>DefaultMaximumSamUsd+1e-10 || windows.Count is <1 or >8 || windows[0].CoreStart!=0 || windows[^1].CoreEndExclusive!=frames ||
           windows.Where((w,i)=>w.Part!=i || w.InputStart<0 || w.InputStart>w.CoreStart || w.InputEndExclusive<w.CoreEndExclusive ||
             w.InputEndExclusive>frames || w.CoreFrames<50 || (i>0&&w.CoreStart!=windows[i-1].CoreEndExclusive)).Any())
            throw new InvalidDataException("Invalid native segment windows.");
        return new(frames,target,maxInFlight,.50,windows,false,true){OriginalPacketCopy=true};
    }
    public static SamAdaptiveSegmentPlan Native(int frames,int[] keyFrames,int[] safeEnds,
        int[]? seeds,int target=53,int maxInFlight=MaximumNewNativeParts,int maximumParts=MaximumNewNativeParts) {
        if(frames is <50 or >MaximumSourceFrames || maximumParts is <1 or >8 || maxInFlight is <1 or >8 || target is <50 or >500 || !keyFrames.Contains(0) || !safeEnds.Contains(frames))throw new ArgumentException("Invalid native source plan.");
        var keys=keyFrames.Where(x=>x>=50&&x+52<=frames).Distinct().Order().ToArray();
        for(var count=Math.Min(Math.Min(maximumParts,8),Math.Min(frames/50,keys.Length+1));count>=1;count--) {
            var starts=new List<int>{0};
            for(var i=1;i<count;i++) {
                var candidates=keys.Where(k=>k>=starts[^1]+50 && frames-(k+2)>=50*(count-i)).ToArray();
                if(candidates.Length==0)break;
                starts.Add(candidates.MinBy(k=>Math.Abs(k-(double)i*frames/count)));
            }
            if(starts.Count!=count)continue;
            var windows=starts.Select((start,i)=> {
                var coreStart=i==0?0:start+2;
                var coreEnd=i==count-1?frames:starts[i+1]+2;
                var inputEnd=i==count-1?frames:safeEnds.FirstOrDefault(e=>e>=coreEnd+2,frames);
                return new SamAdaptiveWindow(i,coreStart,coreEnd,start,inputEnd);
            }).ToArray();
            if(windows.Any(w=>w.CoreFrames<50) || windows.Sum(w=>w.BilledFrames)*SamUsdPerFrame>.50+1e-10 ||
               (seeds is not null && windows.Any(w=>!seeds.Any(f=>f>=w.InputStart && f<w.InputEndExclusive))))continue;
            return RestoreNative(frames,target,maxInFlight,windows);
        }
        throw new InvalidDataException("No valid native stream-copy plan fits this source.");
    }

    private static IReadOnlyList<SamAdaptiveWindow> CreateWindows(
        int frames,int parts,bool intraAligned)
    {
        var windows=new List<SamAdaptiveWindow>(parts);
        for(var part=0;part<parts;part++)
        {
            var coreStart=(int)((long)part*frames/parts);
            var coreEnd=(int)((long)(part+1)*frames/parts);
            var inputStart=Math.Max(0,coreStart-OverlapFrames);
            if(intraAligned)inputStart-=inputStart%2;
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
