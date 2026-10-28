using ProofLab.Science;

namespace ProofLab.Api;

public sealed class SamVideoRuntime(
    PendulumDirectorWorkflow director, VideoMediaDecoder decoder,
    MetaLiveAccess meta) : ISamVideoRuntime
{
    public async Task<SamVideoSource> GetVerifiedSourceAsync(
        string studyId, string role, CancellationToken ct)
    {
        var (video, path) = await director.GetVerifiedOriginalAsync(studyId, role, ct);
        return new(video.Sha256, video.VideoId, path, video.Media.Codec,
            video.Media.Width, video.Media.Height, video.Media.DurationSeconds);
    }

    public Task<double[]> ReadFramePtsAsync(string path, CancellationToken ct) =>
        decoder.ReadVideoFramePtsAsync(path, ct);

    public Task CreateMp4Async(string source, string destination, CancellationToken ct) =>
        decoder.CreateSamCompatibleMp4Async(source, destination, ct);

    public bool LiveCallsPermitted => meta.Status().LiveCallsPermitted;
    public void ReserveOrThrow() => meta.ReserveOrThrow();
    public bool CanRunWithToken(string? oneShotMetaToken) =>
        meta.Status(oneShotMetaToken).LiveCallsPermitted;
    public void ReserveOrThrow(string? oneShotMetaToken) => meta.ReserveOrThrow(oneShotMetaToken);
}