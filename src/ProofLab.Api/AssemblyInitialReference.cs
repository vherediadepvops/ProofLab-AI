namespace ProofLab.Api;

public sealed partial class AssemblyLocator
{
    public async Task<string?> InitialReferenceRunAsync(string id,string role,SamSceneDefinition scene,CancellationToken ct)
    {
        var (video,_,_)=await Source(id,role,ct,true);
        if(video.Sha256!=scene.VideoSha256)throw new InvalidDataException("Source changed.");
        foreach(var run in await FullRuns(video.Sha256,ct))
            if(run.Reviewed is not null && AssemblyPolicy.Ground(run.Reviewed)?.Key==scene.Key)
                return run.Attempt.RunId;
        return null;
    }

    // Reuse only original, authenticated anchor images from the exact Spark run.
    // Neither this read nor the human review endpoint invokes a provider.
    public async Task<AssemblyInitialReferenceFrames?> InitialReferenceFramesAsync(
        string id,string role,string runId,CancellationToken ct)
    {
        var (video,_,_)=await Source(id,role,ct,true);
        var run=(await FullRuns(video.Sha256,ct,runId)).SingleOrDefault() ??
            throw new FileNotFoundException("Requested Spark run unavailable.");
        var evidence=run.Reviewed;
        var scene=evidence is null?null:AssemblyPolicy.Ground(evidence);
        if(scene?.InitialBodyPoint is null || run.Evidence is null)
            throw new InvalidOperationException("The selected run has no verified initial body reference.");
        var original=run.Evidence.Frames;
        var clear=scene.Seeds.Where(s=>s.Role=="object" &&
            scene.BodyReferences?.Any(q=>q.Frame==s.Frame && q.WholeBodyVisible && !q.Occluded && !q.Clipped)==true)
            .OrderBy(s=>s.Frame).FirstOrDefault() ??
            throw new InvalidDataException("No clear comparison frame is available.");
        var firstOrdinal=Array.FindIndex(original,f=>f.Index==0);
        var clearOrdinal=Array.FindIndex(original,f=>f.Index==clear.Frame);
        if(firstOrdinal<0 || clearOrdinal<0 || original[firstOrdinal].Width!=scene.Width ||
           original[firstOrdinal].Height!=scene.Height)
            throw new InvalidDataException("The exact native initial frame is unavailable.");
        var first=await File.ReadAllBytesAsync(Path.Combine(run.Root,$"frame-{firstOrdinal}.jpg"),ct);
        var clean=await File.ReadAllBytesAsync(Path.Combine(run.Root,$"frame-{clearOrdinal}.jpg"),ct);
        // FullRuns validates all hashes; verify the bytes returned too to reject a concurrent replacement.
        static string BytesHash(byte[] bytes)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        if(BytesHash(first)!=original[firstOrdinal].Sha256 || BytesHash(clean)!=original[clearOrdinal].Sha256)
            throw new InvalidDataException("Original reference image changed while reading.");
        return new(runId,scene,original[firstOrdinal],first,original[clearOrdinal],clean);
    }
}
