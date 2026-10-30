using System.Text.RegularExpressions;

namespace ProofLab.Api;

/// <summary>
/// Continuous recovery starts from an actual SAM observation at source frame 0.
/// The original Spark boxes remain unchanged. Never move a later box to frame 0.
/// </summary>
public static class SamAdaptiveRecoveryPrompt
{
    public static string Build(SamSceneDefinition scene, SamAdaptiveWindow continuous,
        SamAdaptiveWindow priorWindow, string ptsSha,
        SamAdaptivePartReceipt receipt, string raw)
    {
        if(continuous.InputStart!=0 || priorWindow.Part!=0 || priorWindow.InputStart!=0 ||
           receipt.Part!=0 || receipt.InputStart!=0 ||
           receipt.InputEndExclusive!=priorWindow.InputEndExclusive ||
           receipt.CoreStart!=priorWindow.CoreStart || receipt.CoreEndExclusive!=priorWindow.CoreEndExclusive ||
           receipt.OriginalVideoSha256!=scene.VideoSha256 ||
           receipt.SparkSceneKey!=scene.Key || receipt.SourcePtsSha256!=ptsSha ||
           receipt.ResponseSha256!=SamVideoArchive.Hash(raw) ||
           receipt.PromptSha256!=SamVideoArchive.Hash(SamAdaptiveGroundedPlan.BuildPrompt(scene,priorWindow)) ||
           receipt.Status!="COMPLETED_PROVIDER_STREAM_PENDING_HUMAN_VISUAL_REVIEW")
            throw new InvalidDataException("Previous SAM reference provenance changed.");
        var original=SamAdaptiveGroundedPlan.BuildPrompt(scene,continuous);
        if(scene.Seeds.Any(s=>s.Role=="object" && s.Frame==0))return original;
        var lines=SamFourWayMaskAssembler.OutputWire(raw).Split('\n')
            .Select(x=>x.TrimEnd('\r')).Where(x=>x.StartsWith("<0f>",StringComparison.Ordinal)).ToArray();
        if(lines.Length!=1 || lines[0].Length>200_000)
            throw new InvalidDataException("No unique authenticated SAM observation at source frame zero.");
        var match=Regex.Match(lines[0],
            @"^<0f>1(<\|box;x1=(\d+);y1=(\d+);x2=(\d+);y2=(\d+);w=(\d+);h=(\d+)\|>)<\|mask;[^\r\n]+\|>$",
            RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
        if(!match.Success)throw new InvalidDataException("Invalid first-frame SAM reference.");
        var values=new int[6];
        for(var i=0;i<6;i++)
            if(!int.TryParse(match.Groups[i+2].Value,out values[i]))
                throw new InvalidDataException("Invalid first-frame SAM reference dimensions.");
        var (x1,y1,x2,y2,w,h)=(values[0],values[1],values[2],values[3],values[4],values[5]);
        if(w!=scene.Width || h!=scene.Height || x1<0 || y1<0 || x2<=x1 || y2<=y1 || x2>=w || y2>=h)
            throw new InvalidDataException("First-frame SAM reference dimensions disagree with Spark.");
        var prompt="<0f>1"+match.Groups[1].Value+" "+original;
        if(!AssemblyPolicy.IsSamPrompt(prompt))throw new InvalidDataException("Invalid continuous SAM prompt.");
        return prompt;
    }
}
