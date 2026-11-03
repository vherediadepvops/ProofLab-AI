namespace ProofLab.Api;

/// <summary>An exact existing Spark box; never a manufactured or interpolated target.</summary>
public sealed record SamPrincipalReference(string Protocol,SamSceneSeed Seed,
    int CandidateCount,int[] RejectedFrameIndices);

public static class SamPrincipalReferencePolicy
{
    public const string Protocol="SAM_SINGLE_PRINCIPAL_REFERENCE_V1";

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
        var valid=candidates.Where(s=>s.X1>=0 && s.Y1>=0 && s.X2<scene.Width && s.Y2<scene.Height &&
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
        return new(Protocol,credible[0],candidates.Length,
            candidates.Where(s=>!credible.Contains(s)).Select(s=>s.Frame).Order().ToArray());
    }

    public static string BuildPrompt(SamSceneDefinition scene,SamAdaptiveWindow window,
        SamPrincipalReference reference)
    {
        var expected=Select(scene,window);
        if(reference.Protocol!=Protocol || reference.Seed!=expected.Seed ||
           reference.CandidateCount!=expected.CandidateCount || reference.RejectedFrameIndices is null ||
           !reference.RejectedFrameIndices.SequenceEqual(expected.RejectedFrameIndices))
            throw new InvalidDataException("The saved principal reference does not match its scene and policy.");
        return SamAdaptiveGroundedPlan.BuildPrompt(scene with{Seeds=[reference.Seed]},window);
    }
}
