using System.Text.Json;
namespace ProofLab.Api;

// Agreement is an image-space diagnostic, never a calibrated probability.
public sealed record SuspensionConsensus(string Status,string Camera,int[] InlierOrdinals,
    int Submitted,int Usable,double TimeCoverage,double? X,double? Y,double SpreadPixels);

public static class DenseLocalizationPolicy {
    public const string Protocol="SPARK_DENSE_OBJECT_AND_EXIT_50_V1";
    public const string OriginalProtocol="SPARK_ORIGINAL_50_OBJECT_REVIEW_V1";
    public const string OriginalBodyProtocol="SPARK_ORIGINAL_50_BODY_CROPS_V1";
    // Only the original response contributes suspension observations. No extra
    // contact review, provider receipt or inferred physical contact is invented.
    public static AssemblyEvidence FromOriginal(AssemblyEvidence first) {
        if(first.Derivation is not null || first.Proposal.ContactReviews is not null)
            throw new InvalidDataException("Original localization must come from its preserved provider response.");
        return Combine([first]) with {Derivation=OriginalProtocol,
            ResponseSha256=KnotPolicy.Hash(OriginalProtocol+"\n"+first.ResponseSha256)};
    }
    public const int MaximumAttempts=3;
    public static bool IsDense(AssemblyEvidence e)=>e.Proposal.Camera is not null && e.Frames.Length==50;
    private static double Median(IEnumerable<double> values) {
        var a=values.Order().ToArray();return (a[(a.Length-1)/2]+a[a.Length/2])/2;
    }
    public static int[] NextReferences(int count,IEnumerable<int> used) {
        var taken=used.ToHashSet();var available=Enumerable.Range(0,count).Where(i=>!taken.Contains(i)).ToArray();
        if(available.Length<50)return [];
        return Enumerable.Range(0,50).Select(i=>available[(int)Math.Round((available.Length-1)*i/49d)]).ToArray();
    }
    private static double Coverage(KnotFrame[] frames,IEnumerable<int> ordinals) {
        var times=ordinals.Select(i=>frames[i].TimeSeconds).Order().ToArray();
        var start=frames.Min(f=>f.TimeSeconds);var duration=frames.Max(f=>f.TimeSeconds)-start;
        if(times.Length<2 || duration<=0)return 0;
        var bins=times.Select(t=>Math.Min(4,(int)(5*(t-start)/duration))).Distinct().Count();
        return bins<3?0:(times[^1]-times[0])/duration;
    }
    public static bool ObjectReady(AssemblyEvidence e) {
        var observations=e.Proposal.Observations.Where(o=>o.Role=="object" && o.Visible && o.Confidence>=.8).ToArray();
        return e.Proposal.Target?.Status=="CLEAR_SINGLE_OBJECT" && observations.Length>=10 &&
            Coverage(e.Frames,observations.Select(o=>o.Frame))>=.6;
    }
    public static SuspensionConsensus Resolve(KnotFrame[] frames,KnotPoint[] points,string camera) {
        if(frames.Length is <50 or >150 || points.Length!=frames.Length ||
            points.Where((p,i)=>p.Frame!=i).Any() || frames.Select(f=>f.Index).Distinct().Count()!=frames.Length)
            throw new InvalidDataException("Dense suspension correspondence mismatch.");
        if(camera is not ("STATIC" or "MOVING" or "UNCLEAR") ||
            frames.Any(f=>!double.IsFinite(f.TimeSeconds)||f.TimeSeconds<0||f.Width<2||f.Height<2||
                f.Width!=frames[0].Width||f.Height!=frames[0].Height) ||
            points.Any(p=>!double.IsFinite(p.X)||!double.IsFinite(p.Y)||!double.IsFinite(p.Confidence)||
                p.X<0||p.X>=1000||p.Y<0||p.Y>=1000||p.Confidence<0||p.Confidence>1))
            throw new InvalidDataException("Invalid dense suspension coordinates.");
        var usable=points.Where(p=>p.Visible && p.Confidence>=.8).ToArray();
        SuspensionConsensus Empty(string status)=>new(status,camera,[],frames.Length,usable.Length,0,null,null,0);
        if(camera!="STATIC")return Empty("CAMERA_UNRESOLVED");
        if(usable.Length<10)return Empty("INSUFFICIENT_OBSERVATIONS");
        var width=frames[0].Width;var height=frames[0].Height;
        var limit=Math.Max(3,Math.Min(width,height)*.005);
        double Distance(KnotPoint a,KnotPoint b)=>Math.Sqrt(Math.Pow((a.X-b.X)*width/1000,2)+Math.Pow((a.Y-b.Y)*height/1000,2));
        var groups=usable.Select(p=>usable.Where(q=>Distance(p,q)<=limit).ToArray()).OrderByDescending(g=>g.Length).ToArray();
        var best=groups[0];var mx=Median(best.Select(p=>p.X));var my=Median(best.Select(p=>p.Y));
        var center=new KnotPoint(0,true,mx,my,1);
        best=usable.Where(p=>Distance(center,p)<=limit).ToArray();
        var outside=usable.Except(best).ToArray();
        var rival=outside.Length==0?0:outside.Max(p=>outside.Count(q=>Distance(p,q)<=limit));
        if(best.Length<10 || best.Length<usable.Length*.6 || best.Length<frames.Length*.2 || rival>=best.Length*.5)
            return Empty("NO_DOMINANT_CLUSTER");
        var coverage=Coverage(frames,best.Select(p=>p.Frame));
        if(coverage<.6)return Empty("INSUFFICIENT_TIME_COVERAGE");
        mx=Median(best.Select(p=>p.X));my=Median(best.Select(p=>p.Y));center=center with {X=mx,Y=my};
        var spread=best.Max(p=>Distance(center,p));
        if(spread>limit)return Empty("NO_DOMINANT_CLUSTER");
        return new("CONSENSUS_ESTIMATE",camera,best.Select(p=>p.Frame).ToArray(),frames.Length,usable.Length,coverage,mx,my,spread);
    }
    public static KnotProposal Suspension(SuspensionConsensus c,KnotPoint[] points)=>
        new(c.Status=="CONSENSUS_ESTIMATE"?"CANDIDATE_REQUIRES_HUMAN_CONFIRMATION":c.Status,c.X,c.Y,c.SpreadPixels,points);

    // Preserve each object position. Only the fixed suspension is combined statistically.
    public static AssemblyEvidence Combine(IReadOnlyList<AssemblyEvidence> rounds) {
        if(rounds.Count is <1 or >MaximumAttempts || rounds.Any(e=>!IsDense(e)))throw new InvalidDataException("Invalid localization rounds.");
        var first=rounds[0];
        if(rounds.Any(e=>e.VideoSha256!=first.VideoSha256 || e.RunId!=first.RunId || e.CacheKey!=first.CacheKey ||
            e.VideoInput?.SubmittedMp4Sha256!=first.VideoInput?.SubmittedMp4Sha256 || e.Frames.Any(f=>f.Width!=first.Frames[0].Width || f.Height!=first.Frames[0].Height)))
            throw new InvalidDataException("Localization source changed.");
        var frames=rounds.SelectMany(e=>e.Frames).OrderBy(f=>f.Index).ToArray();
        if(frames.Select(f=>f.Index).Distinct().Count()!=frames.Length)throw new InvalidDataException("Repeated localization images.");
        var map=frames.Select((f,i)=>(f.Index,i)).ToDictionary(x=>x.Index,x=>x.i);
        var points=rounds.SelectMany(e=>e.Proposal.Suspension.Points.Select(p=>p with {Frame=map[e.Frames[p.Frame].Index]})).OrderBy(p=>p.Frame).ToArray();
        var camera=rounds.Any(e=>e.Proposal.Camera=="MOVING")?"MOVING":rounds.All(e=>e.Proposal.Camera=="STATIC")?"STATIC":"UNCLEAR";
        var consensus=Resolve(frames,points,camera);
        var objectRound=rounds.FirstOrDefault(ObjectReady) ?? rounds[^1];
        // Later requests cannot overwrite an already resolved target with a worse answer.
        var obs=objectRound.Proposal.Observations.Select(o=>o with {Frame=map[objectRound.Frames[o.Frame].Index]}).ToArray();
        var proposal=objectRound.Proposal with {Observations=obs,Suspension=Suspension(consensus,points),Camera=camera,Consensus=consensus};
        return first with {Frames=frames,Proposal=proposal,Derivation=Protocol,
            ResponseSha256=KnotPolicy.Hash(Protocol+"\n"+string.Join("\n",rounds.Select(e=>e.ResponseSha256))),
            InputTokens=rounds.Sum(e=>e.InputTokens),OutputTokens=rounds.Sum(e=>e.OutputTokens)};
    }
    public static bool Complete(AssemblyEvidence combined)=>ObjectReady(combined) && combined.Proposal.Consensus?.Status=="CONSENSUS_ESTIMATE";
}
