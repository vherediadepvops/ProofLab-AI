using System.Text.Json;

namespace ProofLab.Api;

public sealed record SuspensionAuditPoint(int Frame,string Decision,string ReferenceKind,bool Visible,
    double X,double Y,double Confidence,string VisualReason,double TimeSeconds);
public sealed record SuspensionAuditEvidence(string Camera,SuspensionAuditPoint[] Points,SuspensionConsensus Consensus);

/// <summary>
/// Nine independently reviewed full-image contact positions. Original fifty-point
/// observations remain untouched. All ordinals and times refer to original frames;
/// no interpolated observations or fallback to an unconfirmed original candidate.
/// </summary>
public static class SuspensionAuditPolicy
{
    public const string Protocol="SPARK_ORIGINAL_50_CONTACT_AUDIT_V2";
    public const int ReferenceCount=9;
    private static readonly int[] Ordinals=[0,6,12,18,24,31,37,43,49];
    public static int[] RepresentativeOrdinals()=>Ordinals.ToArray();

    public static SuspensionAuditEvidence Parse(JsonElement audit,AssemblyEvidence draft,int[] reviewedOrdinals)
    {
        ValidateSource(draft,reviewedOrdinals);
        try
        {
            RequireProperties(audit,["camera","points"]);
            var camera=audit.GetProperty("camera").GetString()!;
            var rows=audit.GetProperty("points").EnumerateArray().ToArray();
            if(rows.Length!=ReferenceCount)throw new FormatException("Exactly nine suspension audit decisions required.");
            var points=new SuspensionAuditPoint[ReferenceCount];
            for(var i=0;i<rows.Length;i++)
            {
                var row=rows[i];
                RequireProperties(row,["frame","decision","reference_kind","visible","x","y","confidence","visual_reason"]);
                var ordinal=row.GetProperty("frame").GetInt32();
                if(ordinal!=reviewedOrdinals[i])throw new FormatException("Audit decisions must preserve original reviewed ordinals and order.");
                points[i]=new(ordinal,row.GetProperty("decision").GetString()!,row.GetProperty("reference_kind").GetString()!,
                    row.GetProperty("visible").GetBoolean(),row.GetProperty("x").GetDouble(),row.GetProperty("y").GetDouble(),
                    row.GetProperty("confidence").GetDouble(),row.GetProperty("visual_reason").GetString()!,
                    draft.Frames[ordinal].TimeSeconds);
            }
            return ValidateAndResolve(camera,points,draft,reviewedOrdinals);
        }
        catch(Exception error) when(error is InvalidOperationException or KeyNotFoundException or OverflowException or JsonException)
        {
            throw new FormatException("Invalid suspension audit response.",error);
        }
    }

    public static SuspensionAuditEvidence ValidateAndResolve(string camera,SuspensionAuditPoint[] points,
        AssemblyEvidence draft,int[] reviewedOrdinals)
    {
        ValidateSource(draft,reviewedOrdinals);
        if(camera is not ("STATIC" or "MOVING" or "UNCLEAR") || points is null || points.Length!=ReferenceCount)
            throw new FormatException("Invalid suspension audit camera or point count.");
        var width=draft.Frames[0].Width;var height=draft.Frames[0].Height;
        var limit=Math.Max(3,Math.Min(width,height)*.005);
        var contradictoryConfirmation=false;
        for(var i=0;i<points.Length;i++)
        {
            var point=points[i];
            if(point is null || point.Frame!=reviewedOrdinals[i] || point.TimeSeconds!=draft.Frames[reviewedOrdinals[i]].TimeSeconds ||
                !double.IsFinite(point.TimeSeconds) || point.Decision is not ("CONFIRMED" or "CORRECTED" or "UNCERTAIN") ||
                point.ReferenceKind is not ("FIXED_CORD_CONTACT" or "MOVING_CONTACT" or "UNCLEAR") ||
                string.IsNullOrWhiteSpace(point.VisualReason) || point.VisualReason.Length>1000 ||
                !double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Confidence) ||
                point.X is <0 or >=1000 || point.Y is <0 or >=1000 || point.Confidence is <0 or >1)
                throw new FormatException("Invalid suspension audit coordinate, source correspondence or explanation.");
            if(point.Decision=="UNCERTAIN")
            {
                if(point.Visible || point.X!=0 || point.Y!=0 || point.Confidence!=0 || point.ReferenceKind=="FIXED_CORD_CONTACT")
                    throw new FormatException("An uncertain suspension decision cannot claim usable coordinates.");
            }
            else if(!point.Visible || point.ReferenceKind!="FIXED_CORD_CONTACT")
                throw new FormatException("A located contact requires visible coordinates and an explicit reference kind.");
            if(point.Decision=="CONFIRMED")
            {
                var candidate=draft.Proposal.Suspension.Points[point.Frame];
                if(!candidate.Visible || Distance(point.X,point.Y,candidate.X,candidate.Y,width,height)>limit)
                    contradictoryConfirmation=true;
            }
        }
        var verified=points.ToArray();
        return new(camera,verified,Resolve(camera,verified,draft.Frames,width,height,limit,contradictoryConfirmation));
    }

    // Called when an audited automatic scene is reloaded. Stored consensus is
    // checked against the authenticated review positions rather than trusted.
    public static void Validate(SuspensionAuditEvidence audit,AssemblyEvidence draft)
    {
        if(audit is null || audit.Consensus is null)throw new InvalidDataException("Suspension audit is required.");
        var rebuilt=ValidateAndResolve(audit.Camera,audit.Points,draft,RepresentativeOrdinals());
        if(JsonSerializer.Serialize(audit)!=JsonSerializer.Serialize(rebuilt))
            throw new InvalidDataException("Suspension audit consensus differs from its preserved observations.");
    }

    private static void ValidateSource(AssemblyEvidence draft,int[] reviewedOrdinals)
    {
        if(draft.Frames is not {Length:50} || reviewedOrdinals is null || !reviewedOrdinals.SequenceEqual(Ordinals) ||
            draft.Proposal.Suspension.Points is not {Length:50})
            throw new InvalidDataException("Suspension audit requires the nine representative ordinals from fifty original references.");
        var frames=draft.Frames;var original=draft.Proposal.Suspension.Points;
        for(var i=0;i<frames.Length;i++)
        {
            var frame=frames[i];var point=original[i];
            if(frame is null || point is null || frame.Index<0 || !double.IsFinite(frame.TimeSeconds) || frame.TimeSeconds<0 ||
                frame.Width is <2 or >8192 || frame.Height is <2 or >8192 ||
                frame.Width!=frames[0].Width || frame.Height!=frames[0].Height || point.Frame!=i ||
                !double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Confidence) ||
                point.X is <0 or >=1000 || point.Y is <0 or >=1000 || point.Confidence is <0 or >1 ||
                (i>0 && (frame.Index<=frames[i-1].Index || frame.TimeSeconds<=frames[i-1].TimeSeconds)))
                throw new InvalidDataException("Invalid original suspension audit references.");
        }
    }

    private static void RequireProperties(JsonElement element,string[] required)
    {
        if(element.ValueKind!=JsonValueKind.Object)throw new FormatException("An audit object is required.");
        var names=element.EnumerateObject().Select(p=>p.Name).ToArray();
        if(names.Length!=required.Length || names.Distinct(StringComparer.Ordinal).Count()!=names.Length ||
            required.Any(name=>!names.Contains(name,StringComparer.Ordinal)))
            throw new FormatException("Audit response fields do not match the protocol.");
    }

    private static double Distance(double ax,double ay,double bx,double by,int width,int height)=>
        Math.Sqrt(Math.Pow((ax-bx)*width/1000,2)+Math.Pow((ay-by)*height/1000,2));
    private static double Median(IEnumerable<double> values)
    {
        var sorted=values.Order().ToArray();return (sorted[(sorted.Length-1)/2]+sorted[sorted.Length/2])/2;
    }
    private static SuspensionConsensus Resolve(string camera,SuspensionAuditPoint[] points,KnotFrame[] source,int width,int height,double limit,bool contradictoryConfirmation)
    {
        var usable=points.Where(p=>p.Visible && p.Decision is "CONFIRMED" or "CORRECTED" &&
            p.ReferenceKind=="FIXED_CORD_CONTACT" && p.Confidence>=.8).ToArray();
        SuspensionConsensus Empty(string status)=>new(status,camera,[],ReferenceCount,usable.Length,0,null,null,0);
        if(points.Any(p=>p.ReferenceKind=="MOVING_CONTACT"))return Empty("MOVING_CORD_CONTACT");
        if(contradictoryConfirmation)return Empty("CONTRADICTORY_CONFIRMATION");
        if(camera!="STATIC")return Empty("CAMERA_UNRESOLVED");
        var minimum=Math.Max(5,(int)Math.Ceiling(ReferenceCount*.6));
        if(usable.Length<minimum)return Empty("INSUFFICIENT_OBSERVATIONS");
        double DistancePoints(SuspensionAuditPoint a,SuspensionAuditPoint b)=>Distance(a.X,a.Y,b.X,b.Y,width,height);
        var best=usable.Select(p=>usable.Where(q=>DistancePoints(p,q)<=limit).ToArray()).OrderByDescending(g=>g.Length).First();
        var x=Median(best.Select(p=>p.X));var y=Median(best.Select(p=>p.Y));
        best=usable.Where(p=>Distance(p.X,p.Y,x,y,width,height)<=limit).ToArray();
        var outside=usable.Except(best).ToArray();
        var rival=outside.Length==0?0:outside.Max(p=>outside.Count(q=>DistancePoints(p,q)<=limit));
        if(best.Length<minimum || best.Length<usable.Length*.6 || rival>=best.Length*.5)
            return Empty("NO_DOMINANT_CLUSTER");
        var start=source[0].TimeSeconds;var duration=source[^1].TimeSeconds-start;
        var times=best.Select(p=>p.TimeSeconds).Order().ToArray();
        var bins=times.Select(t=>Math.Min(4,(int)(5*(t-start)/duration))).Distinct().Count();
        var coverage=bins<3?0:(times[^1]-times[0])/duration;
        if(coverage<.6)return Empty("INSUFFICIENT_TIME_COVERAGE");
        x=Median(best.Select(p=>p.X));y=Median(best.Select(p=>p.Y));
        var spread=best.Max(p=>Distance(p.X,p.Y,x,y,width,height));
        if(spread>limit)return Empty("NO_DOMINANT_CLUSTER");
        return new("CONSENSUS_ESTIMATE",camera,best.Select(p=>p.Frame).ToArray(),ReferenceCount,usable.Length,coverage,x,y,spread);
    }
}
