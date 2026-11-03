namespace ProofLab.Api;

public sealed record LocalPreparedMedia(string Path,string Directory,string Sha256,long Bytes,
    KnotFrame[] Frames,double[] SourcePts,bool Reused,SamAdaptiveClipManifest Manifest);

public sealed record LocalPreparationStatus(string SourceSha256,string State,string Phase,
    long ElapsedMs,int ReferenceFrames);

public interface ILocalVideoPreparation
{
    void Queue(SamVideoSource source);
    Task<LocalPreparedMedia> PrepareAsync(SamVideoSource source,CancellationToken ct);
    Task<LocalPreparationStatus?> StatusAsync(SamVideoSource source,CancellationToken ct);
    Task<SamAdaptiveClipManifest> PrepareSamAsync(SamVideoSource source,IReadOnlyList<double> pts,
        SamAdaptiveSegmentPlan plan,string freshRunDirectory,CancellationToken ct);
}
