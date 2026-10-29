namespace ProofLab.Api;

/// <summary>
/// A paid SAM video fragment can only be prompted by a genuinely observed,
/// authenticated Spark box at an ORIGINAL source frame inside that fragment.
/// This deliberately chooses fewer parts when the full-video scene provides
/// too few source-frame anchors for the nominal 53-frame proposal.
/// It does NOT interpolate, extrapolate, hallucinate, or move a bounding box.
/// </summary>
public sealed record SamAdaptiveGroundingDecision(
    SamAdaptiveSegmentPlan Plan,int IdealParts,
    int VerifiedSparkSeedCount,bool ReducedForSparseSparkSeeds);

public static class SamAdaptiveGroundedPlan
{
    public static SamAdaptiveGroundingDecision Select(
        int sourceFrames,SamSceneDefinition scene,
        int targetCoreFrames=SamAdaptiveSegmentPlan.TargetCoreFrames,
        int maxInFlight=4,double budgetUsd=0.50)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if(scene.Roles.Length!=1 || scene.Roles[0].Role!="object" ||
           scene.Roles[0].Id!="1")
            throw new InvalidDataException(
                "Exactly one authenticated Spark target is required.");

        var seeds=scene.Seeds
            .Where(x=>x.Role=="object" && x.Frame>=0 && x.Frame<sourceFrames)
            .Select(x=>x.Frame).Distinct().Order().ToArray();
        if(seeds.Length==0)
            throw new InvalidDataException(
                "Spark did not authenticate any source frame for the target.");

        // A plan is NOT safe to upload merely because its frames/cost fit.
        // Every window must contain a real Spark box at a matching frame.
        var ideal=SamAdaptiveSegmentPlan.Create(
            sourceFrames,targetCoreFrames,maxInFlight,budgetUsd,
            alignToTwoFrameIntraKeyframes:true);
        for(var count=ideal.PartCount;count>=1;count--)
        {
            var plan=SamAdaptiveSegmentPlan.Create(
                sourceFrames,targetCoreFrames,maxInFlight,budgetUsd,
                alignToTwoFrameIntraKeyframes:true,
                maximumPartCount:count);
            var covered=plan.Windows.All(window=>
                seeds.Any(frame=>frame>=window.InputStart &&
                    frame<window.InputEndExclusive));
            if(covered)
                return new(plan,ideal.PartCount,seeds.Length,
                    plan.PartCount<ideal.PartCount);
        }
        throw new InvalidDataException(
            "No complete target-grounded segmentation is available.");
    }

    public static SamSceneSeed[] SourceSeeds(
        SamSceneDefinition scene,SamAdaptiveWindow window)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.Seeds
            .Where(x=>x.Role=="object" && x.Frame>=window.InputStart &&
                x.Frame<window.InputEndExclusive)
            .OrderBy(x=>x.Frame).ToArray();
    }

    /// <summary>
    /// The only coordinates in this SAM prompt come from previously
    /// authenticated Spark boxes. The frame number is rebased to the
    /// exact source window; never project a seed into a different frame.
    /// </summary>
    public static string BuildPrompt(SamSceneDefinition scene,
        SamAdaptiveWindow window)
    {
        var target=scene.Roles.Single(x=>x.Role=="object").Id;
        var seeds=SourceSeeds(scene,window);
        if(seeds.Length==0)
            throw new InvalidDataException(
                "No authentic Spark object box lies in this exact video segment.");
        var prompt=string.Join(" ",seeds.Select(s=>
            "<"+(s.Frame-window.InputStart).ToString(
                System.Globalization.CultureInfo.InvariantCulture)+"f>"+
            target+"<|box;x1="+s.X1+";y1="+s.Y1+";x2="+s.X2+
            ";y2="+s.Y2+";w="+scene.Width+";h="+scene.Height+"|>"));
        if(!AssemblyPolicy.IsSamPrompt(prompt))
            throw new InvalidDataException(
                "The rebased source-frame Spark prompt is malformed.");
        return prompt;
    }
}
