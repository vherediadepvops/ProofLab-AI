namespace ProofLab.Api;

/// <summary>A reviewed body reference, optionally paired with an independently observed initial interior point.</summary>
public sealed record SamPrincipalReference(string Protocol,SamSceneSeed Seed,
    int CandidateCount,int[] RejectedFrameIndices,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SamScenePoint? InitialBodyPoint=null);

public static class SamPrincipalReferencePolicy
{
    public const string InitialPointProtocol="SAM_INITIAL_BODY_POINT_WITH_CLEAR_REFERENCES_V3";
    public const string LegacyProtocol="SAM_SINGLE_PRINCIPAL_REFERENCE_V1";
    public const string Protocol="SAM_SINGLE_PRINCIPAL_REFERENCE_V2";

    // These broad relative bounds only reject implausible reference geometry.
    // They do not prove that the box contains the complete physical object.
    // Earlier candidates preserve the earliest observed coverage and avoid
    // later prompt boxes resetting an otherwise continuous track.
    public static SamPrincipalReference Select(SamSceneDefinition scene,SamAdaptiveWindow window)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if(scene.Width<2 || scene.Height<2 || scene.Roles.Length!=1 ||
           scene.Roles[0].Role!="object" || scene.Roles[0].Id!="1")
            throw new InvalidDataException("A single authenticated object scene is required.");
        var candidates=SamAdaptiveGroundedPlan.SourceSeeds(scene,window);
        if(candidates.Length==0 || candidates.Select(s=>s.Frame).Distinct().Count()!=candidates.Length)
            throw new InvalidDataException("No unique source-frame object references are available.");
        // V1 archives did not record visual eligibility and retain their original policy.
        // New reviews explicitly distinguish a whole body from a hand, occlusion,
        // or clipped crop; plausible geometry alone cannot establish that distinction.
        var reviewed=scene.BodyReferences;
        var eligible=candidates;
        if(reviewed is not null)
        {
            var sourceFrames=scene.Seeds.Select(s=>s.Frame).ToHashSet();
            if(reviewed.Any(q=>q is null) || reviewed.Length!=sourceFrames.Count ||
               reviewed.Select(q=>q.Frame).Distinct().Count()!=reviewed.Length ||
               reviewed.Any(q=>!sourceFrames.Contains(q.Frame)))
                throw new InvalidDataException("The reviewed body quality does not match its source-frame references.");
            var byFrame=reviewed.ToDictionary(q=>q.Frame);
            eligible=candidates.Where(s=>byFrame[s.Frame] is
                {WholeBodyVisible:true,Occluded:false,Clipped:false}).ToArray();
            if(eligible.Length<2)
                throw new InvalidDataException("At least two complete, unobstructed body references are required.");
        }
        var valid=eligible.Where(s=>s.X1>=0 && s.Y1>=0 && s.X2<scene.Width && s.Y2<scene.Height &&
            s.X2-s.X1>=4 && s.Y2-s.Y1>=4).ToArray();
        if(valid.Length==0)throw new InvalidDataException("No credible principal object reference is available.");
        static double Area(SamSceneSeed s)=>(double)(s.X2-s.X1)*(s.Y2-s.Y1);
        static double Aspect(SamSceneSeed s)=>(double)(s.X2-s.X1)/(s.Y2-s.Y1);
        static double Median(IEnumerable<double> values){
            var a=values.Order().ToArray();return (a[(a.Length-1)/2]+a[a.Length/2])/2;
        }
        var area=Median(valid.Select(Area));var aspect=Median(valid.Select(Aspect));
        var credible=valid.Where(s=>Area(s)>=area/4 && Area(s)<=area*4 &&
            Aspect(s)>=aspect/3 && Aspect(s)<=aspect*3).OrderBy(s=>s.Frame).ToArray();
        if(credible.Length==0)throw new InvalidDataException("No coherent principal object reference is available.");
        if(reviewed is not null && credible.Length<2)
            throw new InvalidDataException("At least two coherent complete-body references are required.");
        var point=scene.InitialBodyPoint;
        if(point is not null && (point.Frame!=0 || point.X<0 || point.Y<0 || point.X>=scene.Width || point.Y>=scene.Height ||
            scene.Origin!=AssemblyPolicy.GroundingOrigin || reviewed is null || credible.Length<2 || window.InputStart!=0))
            throw new InvalidDataException("Initial body point requires complete-source, reviewed object identity.");
        return new(point is not null?InitialPointProtocol:reviewed is null?LegacyProtocol:Protocol,credible[0],candidates.Length,
            candidates.Where(s=>!credible.Contains(s)).Select(s=>s.Frame).Order().ToArray(),point);
    }

    public static string BuildPrompt(SamSceneDefinition scene,SamAdaptiveWindow window,
        SamPrincipalReference reference)
    {
        var expected=Select(scene,window);
        if(reference.Protocol!=expected.Protocol || reference.Seed!=expected.Seed || reference.InitialBodyPoint!=expected.InitialBodyPoint ||
           reference.CandidateCount!=expected.CandidateCount || reference.RejectedFrameIndices is null ||
           !reference.RejectedFrameIndices.SequenceEqual(expected.RejectedFrameIndices))
            throw new InvalidDataException("The saved principal reference does not match its scene and policy.");
        // A visible interior pixel may be usable while the complete initial outline is occluded.
        // Its independent review is preserved; the later clear box corroborates identity, not a new reset.
        if(reference.InitialBodyPoint is {} point)
            return $"<0f>1<|point;x={point.X};y={point.Y};w={scene.Width};h={scene.Height}|>";
        return SamAdaptiveGroundedPlan.BuildPrompt(scene with{Seeds=[reference.Seed]},window);
    }
}
