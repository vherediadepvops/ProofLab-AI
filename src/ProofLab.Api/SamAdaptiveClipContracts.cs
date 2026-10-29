namespace ProofLab.Api;

public sealed record SamAdaptiveClip(
    int Part,int InputStart,int InputEndExclusive,int CoreStart,int CoreEndExclusive,
    int Frames,string RelativePath,long Bytes,string VideoSha256,
    string SourceFrameHashSha256,double MaximumPtsErrorSeconds,int BilledFrames);

public sealed record SamAdaptiveClipManifest(
    string OriginalVideoSha256,string OriginalPtsSha256,int OriginalFrames,
    int SubmittedFrames,int BilledFrames,double EstimatedSamUsd,
    string CutProtocol,IReadOnlyList<SamAdaptiveClip> Clips,long PrepareElapsedMs);

public interface ISamAdaptiveClipBuilder {
    Task<SamAdaptiveClipManifest> PrepareAsync(SamVideoSource source,IReadOnlyList<double> pts,
        SamAdaptiveSegmentPlan plan,string outputDirectory,CancellationToken ct);
}

