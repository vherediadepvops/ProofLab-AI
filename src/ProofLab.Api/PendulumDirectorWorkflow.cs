using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProofLab.Science;

namespace ProofLab.Api;

/// <summary>
/// Guided two-video research protocol. Not a Muse or SAM research agent yet.
/// The initial observation is stored BEFORE the follow-up proposal is committed.
/// Files are local-only; no internet-facing anonymous uploads, Meta usage or
/// unsafe execution of AI-generated instructions.
/// </summary>
public sealed record DirectorStartRequest(double BaselineLengthMeters);
public sealed record DirectorHumanReviewRequest(bool BobConfirmed);
public sealed record DirectorVideoEvidence(
    string Role, string VideoId, string OriginalFilename, long Bytes,
    string Sha256, DateTimeOffset UploadedAtUtc,
    VideoStreamMetadata Media, LocalMotionTrack Tracking,
    PendulumSpectralAssessment Spectral, bool PosterAvailable,
    bool PreviewAvailable = false);
public sealed record DirectorHumanReview(
    DateTimeOffset ReviewedAtUtc, string VideoSha256, bool BobConfirmed,
    string EvidenceLevel);
public sealed record DirectorProposal(
    string Source, string ReasonEs, string ReasonEn,
    DateTimeOffset RegisteredAtUtc, string BaselineSha256,
    PendulumTrialHypothesis Theory, string PredictionSha256);
public sealed record DirectorComparison(
    DateTimeOffset ComparedAtUtc, string Status, string Source,
    double BaselinePeriodSeconds, double FollowUpPeriodSeconds,
    double PredictedIncreasePercent, double ObservedIncreasePercent,
    double ChangeGapPercentagePoints, string LimitationsEs,
    string LimitationsEn);
public sealed record DirectorSession(
    string Id, double BaselineLengthMeters, DateTimeOffset CreatedAtUtc,
    string Step, string ModelStatus,
    DirectorVideoEvidence? Baseline,
    DirectorHumanReview? HumanReview,
    DirectorProposal? Proposal,
    DirectorVideoEvidence? Followup,
    DirectorComparison? Comparison);

public sealed class PendulumDirectorWorkflow(
    IWebHostEnvironment environment, VideoMediaDecoder decoder)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { WriteIndented = true };
    private readonly SemaphoreSlim gate = new(1, 1);
    private string Root => Path.Combine(environment.ContentRootPath,
        ".private", "physics", "director");
    private static readonly string NotAi = PendulumDirectorProtocol.Source;
    public bool DecoderAvailable => decoder.Available;

    public async Task<DirectorSession> StartAsync(
        DirectorStartRequest input, CancellationToken ct)
    {
        _ = ScientificModels.Predict(new ExperimentSetup(ExperimentKind.Pendulum,
            LengthMeters: input.BaselineLengthMeters)); // independent validity gate
        var id = Guid.NewGuid().ToString("N");
        var metadata = new DirectorSession(id, input.BaselineLengthMeters,
            DateTimeOffset.UtcNow, "AWAITING_FIRST_VIDEO", NotAi,
            null, null, null, null, null);
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Folder(id));
            await WriteNewAsync(PathFor(id, "session.json"), metadata, ct);
            return metadata;
        }
        finally { gate.Release(); }
    }

    public async Task<DirectorSession> ReadAsync(string id, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return await LoadUnlockedAsync(id, ct); }
        finally { gate.Release(); }
    }

    public async Task<DirectorSession> UploadAsync(
        string id, string role, string filename, Stream input,
        long bytes, CancellationToken ct)
    {
        var extension = VideoClipPolicy.CheckNameAndSize(filename, bytes);
        if (role is not ("baseline" or "followup"))
            throw new ArgumentException("Expected baseline or followup.");
        if (!decoder.Available)
            throw new InvalidOperationException("Local FFmpeg is not available.");
        await gate.WaitAsync(ct);
        string? rawPath = null;
        string? posterPath = null;
        string? previewPath = null;
        try
        {
            var session = await LoadUnlockedAsync(id, ct);
            if (role == "baseline" && session.Baseline is not null)
                throw new ArgumentException("Initial video is already registered.");
            if (role == "followup" && (session.Proposal is null ||
                                       session.Followup is not null))
                throw new ArgumentException("Register a proposal before the second video.");
            if (role == "baseline" && session.Proposal is not null)
                throw new ArgumentException("The first video cannot be replaced after a proposal.");

            var videoId = Guid.NewGuid().ToString("N");
            var folder = Folder(id);
            rawPath = Path.Combine(folder, role + extension);
            posterPath = Path.Combine(folder, role + ".jpg");
            previewPath = Path.Combine(folder, role + "-review.mp4");
            await using(var dest = new FileStream(rawPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 128 * 1024, true))
            {
                var readBuffer = new byte[128 * 1024];
                long copied = 0;
                while(true)
                {
                    var n = await input.ReadAsync(readBuffer, ct);
                    if(n == 0) break;
                    copied += n;
                    if (copied > VideoClipPolicy.MaximumBytes)
                        throw new ArgumentException("Video exceeds size limit.");
                    await dest.WriteAsync(readBuffer.AsMemory(0,n),ct);
                }
                if(copied != bytes) throw new ArgumentException("Incomplete video upload.");
            }

            string hash;
            await using(var source = File.OpenRead(rawPath))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(source,ct))
                    .ToLowerInvariant();
            var media = await decoder.ProbeAsync(rawPath,ct);
            var tracking = await decoder.TrackAsync(rawPath, ct);
            var spectral = PendulumSpectralEstimator.Estimate(
                tracking.Observations, tracking.DetectionRate);
            bool poster = false;
            try { await decoder.CreatePosterAsync(rawPath, posterPath, ct); poster = true; }
            catch(ArgumentException) { /* A missing poster is not proof of bad original. */ }
            bool preview = false;
            try {
                await decoder.CreatePlayablePreviewAsync(rawPath, previewPath, ct);
                preview = true;
            }
            catch(ArgumentException) { /* Keep original if proxy encoding fails. */ }
            var record = new DirectorVideoEvidence(role,videoId,filename,bytes,hash,
                DateTimeOffset.UtcNow,media,tracking,spectral,poster,preview);
            await WriteNewAsync(PathFor(id, role + ".json"),record,ct);
            return await LoadUnlockedAsync(id,ct);
        }
        catch
        {
            if(rawPath is not null && File.Exists(rawPath) &&
               !File.Exists(PathFor(id,role + ".json")))
                File.Delete(rawPath);
            if(posterPath is not null && File.Exists(posterPath) &&
               !File.Exists(PathFor(id,role + ".json")))
                File.Delete(posterPath);
            if(previewPath is not null && File.Exists(previewPath) &&
               !File.Exists(PathFor(id,role + ".json")))
                File.Delete(previewPath);
            throw;
        }
        finally{gate.Release();}
    }

    public async Task<DirectorSession> ConfirmBaselineAsync(string id,
        bool bobConfirmed, CancellationToken ct)
    {
        if(!bobConfirmed)
            throw new ArgumentException("A human must review the tracked object first.");
        await gate.WaitAsync(ct);
        try
        {
            var session = await LoadUnlockedAsync(id,ct);
            if(session.Baseline is null || session.Proposal is not null)
                throw new ArgumentException("Review requires the first video before a proposal.");
            if(!PendulumDirectorProtocol.HasUsableInitialSignal(session.Baseline.Spectral))
                throw new ArgumentException("No usable local period; manual review is required.");
            if(session.HumanReview is not null) return session;
            var review = new DirectorHumanReview(DateTimeOffset.UtcNow,
                session.Baseline.Sha256,true,
                "USER_ACKNOWLEDGEMENT_NOT_INDEPENDENT_SCIENTIFIC_REVIEW");
            await WriteNewAsync(PathFor(id,"human-review.json"),review,ct);
            return await LoadUnlockedAsync(id,ct);
        }
        finally{gate.Release();}
    }

    public async Task<DirectorSession> ProposeLocalControlAsync(
        string id,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var session = await LoadUnlockedAsync(id,ct);
            if(session.Proposal is not null) return session; // frozen once written
            if(session.Baseline is null || session.HumanReview?.BobConfirmed != true)
                throw new ArgumentException("First review the observed bob movement.");
            // A fixed demonstrator control; MUST NOT be attributed to Muse Spark.
            var prediction = PendulumDirectorProtocol.RecommendLocalControl(
                session.BaselineLengthMeters);
            var stamp = DateTimeOffset.UtcNow;
            var sha = CalculateProposalHash(id, session.Baseline.Sha256,
                prediction, NotAi, stamp);
            var proposal = new DirectorProposal(NotAi,
                "Control local predefinido: variar la longitud para comprobar la ley T∝√L. NO es una decisión de Muse Spark.",
                "Local preset: change length to test T∝√L. This is NOT a Muse Spark decision.",
                stamp,session.Baseline.Sha256,prediction,sha);
            await WriteNewAsync(PathFor(id,"proposal.json"),proposal,ct);
            return await LoadUnlockedAsync(id,ct);
        }
        finally{gate.Release();}
    }

    public async Task<DirectorSession> CompareAsync(string id,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var session = await LoadUnlockedAsync(id,ct);
            if(session.Comparison is not null)return session;
            if(session.Baseline?.Spectral.PeriodSeconds is not double first ||
               session.Followup?.Spectral.PeriodSeconds is not double second ||
               session.Proposal is null)
                throw new ArgumentException("Two usable videos and a frozen proposal are required.");
            var (observed, gap) = PendulumDirectorProtocol.Compare(
                session.Proposal.Theory,first,second);
            var theoretical = session.Proposal.Theory.PredictedIncreasePercent;
            var conclusion = new DirectorComparison(DateTimeOffset.UtcNow,
                "PRELIMINARY_LOCAL_COMPARISON_NOT_AI_SAM_OR_CERTIFIED",
                NotAi,first,second,theoretical,observed,gap,
                "Las coordenadas proceden de sustracción local de fondo, no de SAM. La confirmación humana es declarativa, no revisión independiente. El error de muestreo, desenfoque y sesgo de seguimiento sigue pendiente.",
                "Coordinates come from local background subtraction, not SAM. User acknowledgement is not independent validation. Frame timing, blur and tracker bias remain unquantified.");
            await WriteNewAsync(PathFor(id,"comparison.json"),conclusion,ct);
            return await LoadUnlockedAsync(id,ct);
        }
        finally{gate.Release();}
    }

    public async Task<byte[]> PosterAsync(string id,string role,CancellationToken ct)
    {
        if(role is not ("baseline" or "followup"))
            throw new ArgumentException("Invalid video role.");
        var session = await ReadAsync(id,ct);
        var evidence = role == "baseline" ? session.Baseline : session.Followup;
        if(evidence?.PosterAvailable != true)
            throw new FileNotFoundException("Poster not available.");
        var path=PathFor(id,role+".jpg");
        var file=new FileInfo(path);
        if(!file.Exists || file.Length is < 1000 or > 2_000_000)
            throw new FileNotFoundException("Invalid poster.");
        return await File.ReadAllBytesAsync(path,ct);
    }

    public async Task<byte[]> PreviewAsync(string id,string role,CancellationToken ct)
    {
        if(role is not ("baseline" or "followup"))
            throw new ArgumentException("Invalid video role.");
        var session=await ReadAsync(id,ct);
        var evidence=role=="baseline" ? session.Baseline : session.Followup;
        if(evidence?.PreviewAvailable!=true)
            throw new FileNotFoundException("Review video proxy not available.");
        var path=PathFor(id,role+"-review.mp4");
        var file=new FileInfo(path);
        if(!file.Exists || file.Length is < 10_000 or > 30_000_000)
            throw new FileNotFoundException("Invalid preview file.");
        return await File.ReadAllBytesAsync(path,ct);
    }

    private async Task<DirectorSession> LoadUnlockedAsync(string id,CancellationToken ct)
    {
        var original=await ReadOptionalAsync<DirectorSession>(PathFor(id,"session.json"),ct)
            ?? throw new FileNotFoundException("Director session not found.");
        if(original.Id!=id.ToLowerInvariant())
            throw new InvalidDataException("Session identity mismatch.");
        var first=await ReadOptionalAsync<DirectorVideoEvidence>(PathFor(id,"baseline.json"),ct);
        var review=await ReadOptionalAsync<DirectorHumanReview>(PathFor(id,"human-review.json"),ct);
        var proposal=await ReadOptionalAsync<DirectorProposal>(PathFor(id,"proposal.json"),ct);
        var second=await ReadOptionalAsync<DirectorVideoEvidence>(PathFor(id,"followup.json"),ct);
        var result=await ReadOptionalAsync<DirectorComparison>(PathFor(id,"comparison.json"),ct);
        if(first is not null && first.Role!="baseline" ||
           second is not null && second.Role!="followup" ||
           review is not null && (review.VideoSha256!=first?.Sha256 ||
                                  !review.BobConfirmed) ||
           proposal is not null &&
             (proposal.BaselineSha256!=first?.Sha256 ||
              review is null || proposal.Source!=NotAi ||
              proposal.PredictionSha256!=CalculateProposalHash(
                  id,proposal.BaselineSha256,proposal.Theory,
                  proposal.Source,proposal.RegisteredAtUtc)) ||
           second is not null && proposal is null ||
           result is not null && (proposal is null || second is null) ||
           first is not null && first.UploadedAtUtc < original.CreatedAtUtc ||
           review is not null && (first is null ||
                                  review.ReviewedAtUtc < first.UploadedAtUtc) ||
           proposal is not null && (review is null ||
                                    proposal.RegisteredAtUtc < review.ReviewedAtUtc) ||
           second is not null && (proposal is null ||
                                  second.UploadedAtUtc < proposal.RegisteredAtUtc) ||
           result is not null && (second is null ||
                                  result.ComparedAtUtc < second.UploadedAtUtc))
            throw new InvalidDataException("Research order/provenance mismatch.");
        var step=result is not null ? "COMPARED_PRELIMINARY":
            second is not null ? "READY_TO_COMPARE":
            proposal is not null ? "AWAITING_SECOND_VIDEO":
            review is not null ? "READY_TO_PROPOSE":
            first is not null && first.Spectral.PeriodSeconds is not null ?
                "AWAITING_HUMAN_BOB_REVIEW":
            first is not null ? "INITIAL_VIDEO_NEEDS_REVIEW":"AWAITING_FIRST_VIDEO";
        return original with {
            Step=step, ModelStatus=NotAi,Baseline=first,HumanReview=review,
            Proposal=proposal,Followup=second,Comparison=result
        };
    }

    private static async Task<T?> ReadOptionalAsync<T>(string path,CancellationToken ct)
    {
        if(!File.Exists(path))return default;
        await using var stream=File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream,Json,ct)
            ?? throw new InvalidDataException("Stored research item is invalid.");
    }

    private static async Task WriteNewAsync<T>(string path,T input,CancellationToken ct)
    {
        await using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,
            FileShare.None,4096,true);
        await JsonSerializer.SerializeAsync(stream,input,Json,ct);
    }

    /// <summary>
    /// A reproducible local checksum, NOT a trusted external timestamp or
    /// cryptographic proof that the video was filmed after a proposal.
    /// </summary>
    private static string CalculateProposalHash(
        string id, string firstVideoSha, PendulumTrialHypothesis prediction,
        string source, DateTimeOffset utc)
    {
        var plain = JsonSerializer.Serialize(new {
            sessionId=id.ToLowerInvariant(),firstVideoSha,
            model=prediction,proposedBy=source,utc
        },Json);
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(plain)))
            .ToLowerInvariant();
    }

    private string Folder(string id)
    {
        if(string.IsNullOrWhiteSpace(id) || id.Length!=32 ||
           id.Any(c=>!Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid research identifier.");
        return Path.Combine(Root,id.ToLowerInvariant());
    }

    private string PathFor(string id,string file) => Path.Combine(Folder(id),file);
}
