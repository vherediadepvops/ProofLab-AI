using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ProofLab.Api;

/// <summary>
/// Four frame-exact windows for a future operator-approved SAM experiment.
/// Pure arithmetic: never invokes Meta, FFmpeg, or a payment gate.
/// Frame windows overlap so different SAM object IDs can be reconciled
/// against the SAME source frames without inventing a continuous track.
/// </summary>
public sealed record SamFourWayWindow(
    int Part, int CoreStart, int CoreEndExclusive,
    int InputStart, int InputEndExclusive,
    double InputFirstPts, double InputLastPts)
{
    public int CoreFrames => CoreEndExclusive-CoreStart;
    public int InputFrames => InputEndExclusive-InputStart;
    public int GlobalFrame(int localFrame)
    {
        if(localFrame<0 || localFrame>=InputFrames)
            throw new ArgumentOutOfRangeException(nameof(localFrame));
        return InputStart+localFrame;
    }
}

public sealed class SamFourWayPlan
{
    public const int Parts=4;
    public const int Overlap=2;
    public const double PricePerFrame=0.0002;
    public IReadOnlyList<SamFourWayWindow> Windows { get; }
    public int SourceFrames { get; }
    public int SubmittedFrames { get; }
    public double EstimatedSamCostUsd { get; }
    public string SourcePtsSha256 { get; }
    private static string TimelineFingerprint(IReadOnlyList<double> pts)
    {
        var wire=string.Join("\n",pts.Select(x=>
            x.ToString("R",CultureInfo.InvariantCulture)));
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(wire))).ToLowerInvariant();
    }

    private SamFourWayPlan(IReadOnlyList<SamFourWayWindow> windows,
        IReadOnlyList<double> sourcePts)
    {
        Windows=windows;
        SourceFrames=sourcePts.Count;
        SourcePtsSha256=TimelineFingerprint(sourcePts);
        SubmittedFrames=windows.Sum(w=>w.InputFrames);
        EstimatedSamCostUsd=windows.Sum(w=>
            Math.Max(50,w.InputFrames)*PricePerFrame);
    }

    public static SamFourWayPlan Create(IReadOnlyList<double> pts)
    {
        ArgumentNullException.ThrowIfNull(pts);
        if(pts.Count is < 200 or > 2500)
            throw new ArgumentException("Four SAM parts require 200–2500 source frames.");
        if(!double.IsFinite(pts[0]) || Math.Abs(pts[0])>0.005)
            throw new ArgumentException("Frame timeline must begin at zero.");
        for(var i=1;i<pts.Count;i++)
            if(!double.IsFinite(pts[i]) || pts[i]<=pts[i-1])
                throw new ArgumentException("Original frame PTS must increase strictly.");
        var result=new List<SamFourWayWindow>(Parts);
        for(var part=0;part<Parts;part++)
        {
            var coreStart=(int)((long)part*pts.Count/Parts);
            var coreEnd=(int)((long)(part+1)*pts.Count/Parts);
            var first=Math.Max(0,coreStart-Overlap);
            var last=Math.Min(pts.Count,coreEnd+Overlap);
            if(coreEnd-coreStart<50 || last-first<50)
                throw new ArgumentException("Each SAM request requires >=50 frames.");
            result.Add(new(part,coreStart,coreEnd,first,last,
                pts[first],pts[last-1]));
        }
        var plan=new SamFourWayPlan(result,pts);
        // The sum, not just one request, is subject to the owner-approved cap.
        if(plan.EstimatedSamCostUsd>0.500000001)
            throw new ArgumentException(
                "Parallel overlap would exceed the aggregate US$0.50 SAM estimate.");
        return plan;
    }

    public bool MatchesSourcePts(IReadOnlyList<double> sourcePts) =>
        sourcePts.Count==SourceFrames &&
        TimelineFingerprint(sourcePts)==SourcePtsSha256;

    public double LocalFramePts(IReadOnlyList<double> sourcePts,
        SamFourWayWindow window,int localFrame)
    {
        var original=window.GlobalFrame(localFrame);
        if(!MatchesSourcePts(sourcePts))
            throw new ArgumentException("The source timeline changed.");
        return sourcePts[original]-window.InputFirstPts;
    }
}

/// <summary>
/// One decoded mask-centroid observation in a SEGMENT-local frame.
/// The real mask stays in the provider response; coordinates alone do not
/// prove provider provenance or scientific bob identity.
/// </summary>
public sealed record SamFourWayDetection(
    int LocalFrame,string ObjectId,
    double CentroidX,double CentroidY,
    double Left,double Top,double Right,double Bottom);

public sealed record SamFourWaySegment(
    int Part,string OriginalVideoSha256,string SceneKey,
    bool ProviderCompleted,IReadOnlyList<SamFourWayDetection> Masks);

public sealed record SamFourWayFrame(
    int FrameIndex,double SourcePts,string ObjectId,
    double CentroidX,double CentroidY,int Part);

public sealed record SamFourWaySeam(
    int FirstPart,int SecondPart,int VerifiedCommonFrames,
    bool IdentityVerified);

public sealed record SamFourWayMerge(
    string SourceSha256,string SceneKey,int SourceFrames,
    IReadOnlyList<SamFourWayFrame> ObservedFrames,
    IReadOnlyList<SamFourWaySeam> Seams)
{
    public double Coverage => (double)ObservedFrames.Count/SourceFrames;
    public bool EverySeamVerified => Seams.All(s=>s.IdentityVerified);
}

/// <summary>
/// Conservative reconciliation. A different SAM object ID is permitted
/// between parts ONLY if matching overlap masks verify its geometry.
/// Missing frames stay missing; unverified seams block scientific derivatives
/// across that boundary. No synthetic mask or position is ever generated.
/// </summary>
public static class SamFourWayStitcher
{
    private static bool Match(SamFourWayDetection a,SamFourWayDetection b)
    {
        var ax=a.CentroidX-b.CentroidX;
        var ay=a.CentroidY-b.CentroidY;
        var span=Math.Min(Math.Min(a.Right-a.Left,a.Bottom-a.Top),
            Math.Min(b.Right-b.Left,b.Bottom-b.Top));
        if(span<=0 || Math.Sqrt(ax*ax+ay*ay)>Math.Max(3,span*0.45))
            return false;
        var x=Math.Max(0,Math.Min(a.Right,b.Right)-Math.Max(a.Left,b.Left));
        var y=Math.Max(0,Math.Min(a.Bottom,b.Bottom)-Math.Max(a.Top,b.Top));
        var intersection=x*y;
        var union=(a.Right-a.Left)*(a.Bottom-a.Top)+
            (b.Right-b.Left)*(b.Bottom-b.Top)-intersection;
        return union>0 && intersection/union>=0.40;
    }

    public static SamFourWayMerge Merge(
        SamFourWayPlan plan,IReadOnlyList<double> pts,
        string sourceSha,string sceneKey,IReadOnlyList<SamFourWaySegment> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        static bool Hex64(string? v)=>v is {Length:64} &&
            v.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
        if(chunks.Count!=SamFourWayPlan.Parts || pts.Count!=plan.SourceFrames ||
           !Hex64(sourceSha) || !Hex64(sceneKey) ||
           !plan.MatchesSourcePts(pts))
            throw new InvalidDataException("Incomplete four-part SAM provenance.");
        var frameMaps=new Dictionary<int,SamFourWayDetection>[SamFourWayPlan.Parts];
        for(var i=0;i<SamFourWayPlan.Parts;i++)
        {
            var chunk=chunks.SingleOrDefault(x=>x.Part==i);
            if(chunk is null || !chunk.ProviderCompleted ||
               chunk.OriginalVideoSha256!=sourceSha ||
               chunk.SceneKey!=sceneKey)
                throw new InvalidDataException("One SAM segment is incomplete or mismatched.");
            var window=plan.Windows[i];
            var rows=new Dictionary<int,SamFourWayDetection>();
            string? objectId=null;
            foreach(var mask in chunk.Masks)
            {
                if(mask.LocalFrame<0 || mask.LocalFrame>=window.InputFrames ||
                   string.IsNullOrWhiteSpace(mask.ObjectId) ||
                   !double.IsFinite(mask.CentroidX) ||
                   !double.IsFinite(mask.CentroidY) ||
                   !double.IsFinite(mask.Left) || !double.IsFinite(mask.Top) ||
                   !double.IsFinite(mask.Right) || !double.IsFinite(mask.Bottom) ||
                   mask.Left>=mask.Right || mask.Top>=mask.Bottom ||
                   mask.CentroidX<mask.Left || mask.CentroidX>=mask.Right ||
                   mask.CentroidY<mask.Top || mask.CentroidY>=mask.Bottom ||
                   (objectId is not null && objectId!=mask.ObjectId))
                    throw new InvalidDataException("Ambiguous or invalid segment mask.");
                objectId=mask.ObjectId;
                if(!rows.TryAdd(window.GlobalFrame(mask.LocalFrame),mask))
                    throw new InvalidDataException("Duplicate SAM mask for a source frame.");
            }
            frameMaps[i]=rows;
        }

        var seams=new List<SamFourWaySeam>(3);
        for(var i=0;i<3;i++)
        {
            var a=frameMaps[i];var b=frameMaps[i+1];
            var verified=0;
            foreach(var frame in a.Keys.Intersect(b.Keys))
            {
                if(!Match(a[frame],b[frame]))
                    throw new InvalidDataException(
                        "SAM segment overlap has contradictory object geometry.");
                verified++;
            }
            seams.Add(new(i,i+1,verified,verified>0));
        }
        var frames=new List<SamFourWayFrame>(plan.SourceFrames);
        for(var i=0;i<4;i++)
        {
            var window=plan.Windows[i];
            for(var frame=window.CoreStart;frame<window.CoreEndExclusive;frame++)
            {
                if(!frameMaps[i].TryGetValue(frame,out var mask))continue;
                frames.Add(new(frame,pts[frame],mask.ObjectId,
                    mask.CentroidX,mask.CentroidY,i));
            }
        }
        return new(sourceSha,sceneKey,plan.SourceFrames,frames,seams);
    }
}
