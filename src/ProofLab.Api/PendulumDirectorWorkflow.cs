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
public sealed record DirectorStartRequest(
    double BaselineLengthMeters,
    double AngleDegrees = 15,
    double MassGrams = 300,
    bool IncludeSecondTrial = true,
    string FollowUpMode = "recommended",
    double? SelectedFollowUpLengthMeters = null);
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
    string LimitationsEn,
    double? PredictedBaselinePeriodSeconds = null,
    double? PredictedFollowUpPeriodSeconds = null,
    double? BaselinePeriodDiscrepancyPercent = null,
    double? FollowUpPeriodDiscrepancyPercent = null);
public sealed record DirectorSingleComparison(
    DateTimeOffset ComparedAtUtc,
    double PredictedNonlinearPeriodSeconds,
    double PredictedSmallAnglePeriodSeconds,
    double ObservedLocalPeriodSeconds,
    double RelativePeriodErrorPercent,
    string Status,
    string LimitationsEs,
    string LimitationsEn);
public sealed record DirectorSession(
    string Id, double BaselineLengthMeters, DateTimeOffset CreatedAtUtc,
    string Step, string ModelStatus,
    DirectorVideoEvidence? Baseline,
    DirectorHumanReview? HumanReview,
    DirectorProposal? Proposal,
    DirectorVideoEvidence? Followup,
    DirectorComparison? Comparison,
    double AngleDegrees = 15,
    double MassGrams = 300,
    bool IncludeSecondTrial = true,
    string FollowUpMode = "recommended",
    double? SelectedFollowUpLengthMeters = null,
    DirectorSingleComparison? SingleComparison = null,
    string? ModelSha256 = null, MusePrediction? MusePrediction = null);

public sealed class PendulumDirectorWorkflow(
    IWebHostEnvironment environment, VideoMediaDecoder decoder, MuseExperimentStore? muse = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { WriteIndented = true };
    private readonly SemaphoreSlim gate = new(1, 1);
    private string Root => Path.Combine(environment.ContentRootPath,
        ".private", "physics", "director");
    private static readonly string NotAi = PendulumDirectorProtocol.Source;
    public bool DecoderAvailable => decoder.Available;

    /// <summary>
    /// Resolve the exact private uploaded original for scientific processing.
    /// Never substitute the 12-FPS review proxy. Recheck the disk SHA-256
    /// before allowing a third-party video submission.
    /// </summary>
    public async Task<(DirectorVideoEvidence Evidence, string OriginalPath)> GetVerifiedOriginalAsync(
        string id, string role, CancellationToken ct)
    {
        var (record,path)=await GetOriginalForQuoteAsync(id,role,ct);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 128 * 1024, useAsync: true);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        if (!string.Equals(digest, record.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source video SHA-256 mismatch.");
        return (record, path);
    }

    /// <summary>Read-only quote resolution; full hash validation remains mandatory at submission/playback.</summary>
    public async Task<(DirectorVideoEvidence Evidence,string OriginalPath)> GetOriginalForQuoteAsync(
        string id,string role,CancellationToken ct)
    {
        if (role is not ("baseline" or "followup"))
            throw new ArgumentException("Invalid video role.");
        var session = await ReadAsync(id, ct);
        var record = role == "baseline" ? session.Baseline : session.Followup;
        if (record is null)
            throw new FileNotFoundException("No original video for this trial.");
        var ext = VideoClipPolicy.CheckNameAndSize(record.OriginalFilename, record.Bytes);
        var path = PathFor(session.Id, role + ext);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != record.Bytes)
            throw new InvalidDataException("Source video missing or size mismatch.");
        return (record,path);
    }

    private static void ValidateDraft(DirectorStartRequest input)
    {
        _ = PendulumMathematicalModel.Predict(
            new PendulumMathematicalRequest(input.BaselineLengthMeters,
                input.AngleDegrees,input.MassGrams,
                input.IncludeSecondTrial && input.FollowUpMode == "manual"
                    ? input.SelectedFollowUpLengthMeters
                    : null));
        if(input.FollowUpMode is not ("manual" or "recommended"))
            throw new ArgumentException("Select manual or recommended follow-up.");
        if(input.IncludeSecondTrial && input.FollowUpMode == "manual" &&
           input.SelectedFollowUpLengthMeters is null)
            throw new ArgumentException("A manual second length is required.");
        if(!input.IncludeSecondTrial && input.SelectedFollowUpLengthMeters is not null)
            throw new ArgumentException("Second length is not allowed when follow-up is disabled.");
    }

    public async Task<DirectorSession> StartAsync(DirectorStartRequest input, CancellationToken ct)
    {
        ValidateDraft(input);
        var id = Guid.NewGuid().ToString("N");
        var metadata = new DirectorSession(id, input.BaselineLengthMeters,
            DateTimeOffset.UtcNow, "AWAITING_FIRST_VIDEO", NotAi,
            null, null, null, null, null,
            input.AngleDegrees,input.MassGrams,input.IncludeSecondTrial,
            input.FollowUpMode,input.IncludeSecondTrial &&
                input.FollowUpMode=="manual"
                ? input.SelectedFollowUpLengthMeters : null);
        metadata = metadata with { ModelSha256 = CalculateModelHash(metadata) };
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Folder(id));
            await WriteNewAsync(PathFor(id, "session.json"), metadata, ct);
            return metadata;
        }
        finally { gate.Release(); }
    }

    // The recovery code exists before media. Parameters remain a draft until
    // the first recording is registered; frozen evidence is never rewritten.
    public async Task<DirectorSession> UpdateDraftAsync(string id, DirectorStartRequest input, CancellationToken ct)
    {
        ValidateDraft(input);
        await gate.WaitAsync(ct);
        try
        {
            var current = await LoadUnlockedAsync(id, ct);
            if (current.Baseline is not null || current.Proposal is not null || current.MusePrediction is not null)
                throw new InvalidOperationException("Research parameters are frozen after the first video.");
            var next = current with {
                BaselineLengthMeters = input.BaselineLengthMeters, AngleDegrees = input.AngleDegrees,
                MassGrams = input.MassGrams, IncludeSecondTrial = input.IncludeSecondTrial,
                FollowUpMode = input.FollowUpMode, SelectedFollowUpLengthMeters =
                    input.IncludeSecondTrial && input.FollowUpMode == "manual" ? input.SelectedFollowUpLengthMeters : null
            };
            next = next with { ModelSha256 = CalculateModelHash(next) };
            var pending = PathFor(id, "draft-" + Guid.NewGuid().ToString("N") + ".tmp");
            try {
                await WriteNewAsync(pending, next, ct);
                File.Move(pending, PathFor(id, "session.json"), overwrite: true);
            } finally { if (File.Exists(pending)) File.Delete(pending); }
            return next;
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
        try
        {
            using var studyLock=role=="followup"?muse?.LockStudy(id):null;
            var session = await LoadUnlockedAsync(id, ct);
            if (role == "baseline" && session.Baseline is not null)
                throw new ArgumentException("Initial video is already registered.");
            if (role == "followup" && (session.MusePrediction is null || session.Followup is not null))
                throw new ArgumentException("Freeze a Muse prediction in SQLite before the second video.");
            if (role == "baseline" && session.Proposal is not null)
                throw new ArgumentException("The first video cannot be replaced after a proposal.");

            var videoId = Guid.NewGuid().ToString("N");
            var folder = Folder(id);
            rawPath = Path.Combine(folder, role + extension);
            posterPath = Path.Combine(folder, role + ".jpg");
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
            if(role=="followup" && hash==session.Baseline?.Sha256)
                throw new ArgumentException("The second experiment must use a different video.");
            var media = await decoder.ProbeAsync(rawPath,ct);
            var tracking = await decoder.TrackAsync(rawPath, ct);
            var spectral = PendulumSpectralEstimator.Estimate(
                tracking.Observations, tracking.DetectionRate);
            bool poster = false;
            try { await decoder.CreatePosterAsync(rawPath, posterPath, ct); poster = true; }
            catch(ArgumentException) { /* A missing poster is not proof of bad original. */ }
            // Browser playback streams the original. Do not spend time creating
            // a reduced-resolution/cadence proxy for newly uploaded studies.
            const bool preview = false;
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
            if(!session.IncludeSecondTrial)
                throw new ArgumentException("This study was configured for one trial only.");
            if(session.Proposal is not null) return session; // frozen once written
            if(session.Baseline is null || session.HumanReview?.BobConfirmed != true)
                throw new ArgumentException("First review the observed bob movement.");
            // A fixed demonstrator control; MUST NOT be attributed to Muse Spark.
            var manual = session.FollowUpMode == "manual";
            var lengths = manual
                ? PendulumTrialProtocol.Predict(
                    session.BaselineLengthMeters,
                    session.SelectedFollowUpLengthMeters ??
                        throw new InvalidDataException("No manual length selected."))
                : PendulumDirectorProtocol.RecommendLocalControl(
                    session.BaselineLengthMeters);
            // Freeze the nonlinear finite-angle period displayed by the workbench,
            // not a different small-angle approximation.
            var prediction = PendulumDirectorProtocol.UseNonlinearModel(
                lengths,session.AngleDegrees,session.MassGrams);
            var source = manual ? "USER_SELECTED_LENGTH_NOT_META_AI_NOT_SAM" : NotAi;
            var stamp = DateTimeOffset.UtcNow;
            var sha = CalculateProposalHash(id, session.Baseline.Sha256,
                prediction, source, stamp);
            var proposal = new DirectorProposal(source,
                manual
                    ? "Longitud de control elegida por el usuario, no por Muse Spark."
                    : "Control local predefinido para comprobar la relación del período con la longitud; NO es una decisión de Muse Spark.",
                manual
                    ? "The follow-up length was chosen by the user, not Muse Spark."
                    : "Preset local length-control experiment, NOT a Muse Spark decision.",
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
            if(!session.IncludeSecondTrial)
                throw new ArgumentException("Use single-trial comparison for this study.");
            if(session.Baseline?.Spectral.PeriodSeconds is not double first ||
               session.Followup?.Spectral.PeriodSeconds is not double second ||
               session.Proposal is null)
                throw new ArgumentException("Two usable videos and a frozen proposal are required.");
            var (observed, gap) = PendulumDirectorProtocol.Compare(
                session.Proposal.Theory,first,second);
            var theoretical = session.Proposal.Theory.PredictedIncreasePercent;
            var predictedFirst = session.Proposal.Theory.ExpectedBaselinePeriodSeconds;
            var predictedSecond = session.Proposal.Theory.ExpectedFollowUpPeriodSeconds;
            var firstGap = Math.Abs(first-predictedFirst)/predictedFirst*100;
            var secondGap = Math.Abs(second-predictedSecond)/predictedSecond*100;
            var conclusion = new DirectorComparison(DateTimeOffset.UtcNow,
                "PRELIMINARY_LOCAL_COMPARISON_NOT_AI_SAM_OR_CERTIFIED",
                NotAi,first,second,theoretical,observed,gap,
                "Las coordenadas proceden de sustracción local de fondo, no de SAM. La confirmación humana es declarativa, no revisión independiente. El error de muestreo, desenfoque y sesgo de seguimiento sigue pendiente.",
                "Coordinates come from local background subtraction, not SAM. User acknowledgement is not independent validation. Frame timing, blur and tracker bias remain unquantified.",
                predictedFirst, predictedSecond, firstGap, secondGap);
            await WriteNewAsync(PathFor(id,"comparison.json"),conclusion,ct);
            return await LoadUnlockedAsync(id,ct);
        }
        finally{gate.Release();}
    }

    /// <summary>
    /// Compare one real locally-tracked period with theory, without requiring
    /// a second video. User's bob check is an acknowledgement only.
    /// </summary>
    public async Task<DirectorSession> CompareSingleAsync(string id,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var session=await LoadUnlockedAsync(id,ct);
            if(session.IncludeSecondTrial)
                throw new ArgumentException("This research requires the planned follow-up video.");
            if(session.SingleComparison is not null) return session;
            if(session.HumanReview?.BobConfirmed != true ||
               session.Baseline?.Spectral.PeriodSeconds is not double observed)
                throw new ArgumentException("Review one usable video before comparison.");
            var predicted=PendulumMathematicalModel.Predict(
                new PendulumMathematicalRequest(session.BaselineLengthMeters,
                    session.AngleDegrees,session.MassGrams)).Baseline;
            var relativeError=Math.Abs(observed-predicted.NonlinearPeriodSeconds) /
                              predicted.NonlinearPeriodSeconds*100;
            var result=new DirectorSingleComparison(
                DateTimeOffset.UtcNow,
                predicted.NonlinearPeriodSeconds,
                predicted.SmallAnglePeriodSeconds,
                observed, relativeError,
                "PRELIMINARY_LOCAL_SINGLE_VIDEO_NOT_SAM_OR_VALIDATED",
                "Período extraído de coordenadas automáticas sin segmentación SAM; " +
                "el reconocimiento humano es una declaración, no auditoría científica. " +
                "El error numérico no incluye incertidumbre sistemática, escala o tiempo.",
                "Period comes from unverified local tracking, not SAM; " +
                "the user's acknowledgement is not independent scientific validation. " +
                "Numerical disagreement does not represent a complete uncertainty budget.");
            await WriteNewAsync(PathFor(id,"single-comparison.json"),result,ct);
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
        if(original.Id!=id.ToLowerInvariant() ||
           original.ModelSha256 is not null &&
           !string.Equals(original.ModelSha256,CalculateModelHash(original),
               StringComparison.Ordinal))
            throw new InvalidDataException("Theoretical model snapshot integrity mismatch.");
        var first=await ReadOptionalAsync<DirectorVideoEvidence>(PathFor(id,"baseline.json"),ct);
        var review=await ReadOptionalAsync<DirectorHumanReview>(PathFor(id,"human-review.json"),ct);
        var proposal=await ReadOptionalAsync<DirectorProposal>(PathFor(id,"proposal.json"),ct);
        var second=await ReadOptionalAsync<DirectorVideoEvidence>(PathFor(id,"followup.json"),ct);
        var musePrediction=muse?.Prediction(id);
        var result=await ReadOptionalAsync<DirectorComparison>(PathFor(id,"comparison.json"),ct);
        var single=await ReadOptionalAsync<DirectorSingleComparison>(PathFor(id,"single-comparison.json"),ct);
        if(first is not null && first.Role!="baseline" ||
           second is not null && second.Role!="followup" ||
           review is not null && (review.VideoSha256!=first?.Sha256 ||
                                  !review.BobConfirmed) ||
           proposal is not null &&
             (proposal.BaselineSha256!=first?.Sha256 ||
              review is null ||
              proposal.Source is not ("LOCAL_PROTOCOL_NOT_META_AI_NOT_SAM" or
                "USER_SELECTED_LENGTH_NOT_META_AI_NOT_SAM") ||
              proposal.PredictionSha256!=CalculateProposalHash(
                  id,proposal.BaselineSha256,proposal.Theory,
                  proposal.Source,proposal.RegisteredAtUtc)) ||
           second is not null && proposal is null && musePrediction is null ||
           result is not null && (proposal is null || second is null ||
                                  !original.IncludeSecondTrial) ||
           single is not null && (original.IncludeSecondTrial ||
              review is null || first is null || proposal is not null) ||
           first is not null && first.UploadedAtUtc < original.CreatedAtUtc ||
           review is not null && (first is null ||
                                  review.ReviewedAtUtc < first.UploadedAtUtc) ||
           proposal is not null && (review is null ||
                                    proposal.RegisteredAtUtc < review.ReviewedAtUtc) ||
           second is not null && ((musePrediction is not null && second.UploadedAtUtc < musePrediction.FrozenAtUtc) ||
                                  (musePrediction is null && (proposal is null || second.UploadedAtUtc < proposal.RegisteredAtUtc))) ||
           result is not null && (second is null ||
                                  result.ComparedAtUtc < second.UploadedAtUtc))
            throw new InvalidDataException("Research order/provenance mismatch.");
        var step=musePrediction is not null ? (second is null ? "MUSE_PREDICTION_FROZEN_AWAITING_VIDEO_2" : "MUSE_FOLLOWUP_READY_FOR_SAM"):
            single is not null ? "SINGLE_COMPARED_PRELIMINARY":
            result is not null ? "COMPARED_PRELIMINARY":
            second is not null ? "READY_TO_COMPARE":
            proposal is not null ? "AWAITING_SECOND_VIDEO":
            review is not null ? "READY_TO_PROPOSE":
            first is not null && first.Spectral.PeriodSeconds is not null ?
                "AWAITING_HUMAN_BOB_REVIEW":
            first is not null ? "INITIAL_VIDEO_NEEDS_REVIEW":"AWAITING_FIRST_VIDEO";
        return original with {
            Step=step, ModelStatus=NotAi,Baseline=first,HumanReview=review,
            Proposal=proposal,Followup=second,Comparison=result,
            SingleComparison=single, MusePrediction=musePrediction
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
    /// A local integrity checksum over pre-observation theoretical parameters.
    /// It cannot establish independent publication or video acquisition time.
    /// </summary>
    private static string CalculateModelHash(DirectorSession s)
    {
        var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            protocol="PROOFLAB_THEORY_SNAPSHOT_V1",
            modelVersion=PendulumMathematicalModel.Provenance,
            s.Id,s.CreatedAtUtc,s.BaselineLengthMeters,
            s.AngleDegrees,s.MassGrams,s.IncludeSecondTrial,
            s.FollowUpMode,s.SelectedFollowUpLengthMeters
        },Json));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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
