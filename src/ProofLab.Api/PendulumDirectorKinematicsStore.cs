using System.Text.Json;
using ProofLab.Science;

namespace ProofLab.Api;

/// <summary>
/// Local append-only revision of user-marked pivot and unverified pixel
/// kinematics. It does NOT confirm SAM identity, metric calibration or
/// independent scientific review. No Meta requests or changes to source video.
/// </summary>
public sealed record DirectorPivotRequest(
    double PivotXpx, double PivotYpx, string VideoSha256);

public sealed record DirectorKinematicsRecord(
    string StudyId, string Role, string VideoId, string VideoSha256,
    string RevisionId, DateTimeOffset CalculatedAtUtc,
    string PivotProvenance, PendulumKinematicsResult Analysis);

public sealed class PendulumDirectorKinematicsStore(
    IWebHostEnvironment environment, PendulumDirectorWorkflow director)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim gate = new(1, 1);
    private const int MaxRevisionsPerVideo = 20;

    public async Task<DirectorKinematicsRecord> AnalyzeAsync(
        string studyId, string role, DirectorPivotRequest input, CancellationToken ct)
        => await AnalyzeCoreAsync(studyId,role,input,
            "MANUALLY_SELECTED_PIXEL_PIVOT_NOT_INDEPENDENTLY_VERIFIED",ct);

    public Task<DirectorKinematicsRecord> ConfirmMetaAsync(string studyId,string role,
        DirectorPivotRequest input,string evidenceKey,bool adjusted,CancellationToken ct)
    {
        if(evidenceKey.Length!=64 || evidenceKey.Any(c=>!Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid knot evidence key.");
        return AnalyzeCoreAsync(studyId,role,input,
            (adjusted?"META_MUSE_ROPE_EXIT_HUMAN_ADJUSTED:":"META_MUSE_ROPE_EXIT_HUMAN_CONFIRMED:")+evidenceKey,ct);
    }

    private async Task<DirectorKinematicsRecord> AnalyzeCoreAsync(string studyId,string role,
        DirectorPivotRequest input,string provenance,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var session = await director.ReadAsync(studyId, ct);
        var video = VideoFor(session, role);
        if (!string.Equals(video.Sha256, input.VideoSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Video SHA-256 does not match this study.");
        var points = video.Tracking.PixelSamples;
        if (points is null || points.Count < 5)
            throw new ArgumentException(
                "This video has no usable local X/Y samples. Reprocess the original.");
        // A selected pivot enables angular estimation only. Metric XY requires
        // independent calibration and is never enabled by this endpoint.
        var analysis = PendulumVideoKinematics.Analyze(
            points, new PendulumPivotInput(input.PivotXpx, input.PivotYpx,
                role == "baseline" ? session.BaselineLengthMeters :
                    session.Proposal?.Theory.FollowUpLengthMeters ??
                        throw new InvalidDataException("Missing frozen second length."),
                FrontalCameraAssumptionConfirmed: false),
            session.AngleDegrees);
        var when = DateTimeOffset.UtcNow;
        var revisionId = when.ToString("yyyyMMddTHHmmssfffffffZ",
            System.Globalization.CultureInfo.InvariantCulture) + "_" +
            Guid.NewGuid().ToString("N");
        var record = new DirectorKinematicsRecord(
            session.Id, role, video.VideoId, video.Sha256.ToLowerInvariant(),
            revisionId, when,
            provenance, analysis);
        await gate.WaitAsync(ct);
        try
        {
            var folder = Folder(session.Id, role);
            Directory.CreateDirectory(folder);
            if (Directory.GetFiles(folder, "*.json").Length >= MaxRevisionsPerVideo)
                throw new ArgumentException("Maximum local pivot revisions reached.");
            var path = Path.Combine(folder, revisionId + ".json");
            await using var output = new FileStream(path, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await JsonSerializer.SerializeAsync(output, record, Json, ct);
        }
        finally { gate.Release(); }
        return record;
    }

    public async Task<DirectorKinematicsRecord> ReadLatestAsync(
        string studyId, string role, CancellationToken ct)
    {
        var session = await director.ReadAsync(studyId, ct);
        var video = VideoFor(session, role);
        await gate.WaitAsync(ct);
        try
        {
            var folder = Folder(session.Id, role);
            if (!Directory.Exists(folder))
                throw new FileNotFoundException("No pivot revision exists.");
            var latest = Directory.GetFiles(folder, "*.json")
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .FirstOrDefault();
            if (latest is null)
                throw new FileNotFoundException("No pivot revision exists.");
            await using var source = File.OpenRead(latest);
            var record = await JsonSerializer.DeserializeAsync<DirectorKinematicsRecord>(
                source, Json, ct) ??
                    throw new InvalidDataException("Invalid pivot revision.");
            if (record.StudyId != session.Id ||
                record.VideoId != video.VideoId ||
                record.Role != role ||
                !string.Equals(record.VideoSha256, video.Sha256,
                    StringComparison.OrdinalIgnoreCase) ||
                record.Analysis.Method != PendulumVideoKinematics.Version ||
                record.Analysis.PhysicalUnitsEnabled ||
                record.RevisionId + ".json" != Path.GetFileName(latest))
                throw new InvalidDataException("Pivot provenance mismatch.");
            return record;
        }
        finally { gate.Release(); }
    }

    private static DirectorVideoEvidence VideoFor(DirectorSession session, string role)
    {
        if (role is not ("baseline" or "followup"))
            throw new ArgumentException("Invalid research video role.");
        return (role == "baseline" ? session.Baseline : session.Followup) ??
            throw new FileNotFoundException("This experiment has no video for that role.");
    }

    private string Folder(string studyId, string role)
    {
        if (studyId.Length != 32 ||
            studyId.Any(ch => !Uri.IsHexDigit(ch)) ||
            role is not ("baseline" or "followup"))
            throw new ArgumentException("Invalid research identifier or role.");
        return Path.Combine(environment.ContentRootPath, ".private",
            "physics", "director", studyId.ToLowerInvariant(),
            "kinematics", role);
    }
}
