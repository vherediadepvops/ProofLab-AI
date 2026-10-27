using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProofLab.Science;

namespace ProofLab.Api;

public sealed record PendulumPreregisterRequest(
    double BaselineLengthMeters, double FollowUpLengthMeters);

public sealed record PendulumPreregisteredStudy(
    string Id,
    DateTimeOffset PreregisteredAtUtc,
    PendulumTrialHypothesis Hypothesis,
    string PredictionSha256,
    string Status,
    string Limitation);

public sealed record PendulumCompareRequest(
    EvidenceRequest Baseline,
    EvidenceRequest FollowUp);

public sealed record PendulumComparisonRecord(
    string EvaluationId,
    string PreregistrationId,
    DateTimeOffset EvaluatedAtUtc,
    string PredictionSha256,
    string InputSha256,
    PendulumTrialComparison Comparison,
    string Status,
    string IntegrityLimit);

internal sealed record StoredPendulumEvaluation(
    PendulumComparisonRecord Summary,
    PendulumCompareRequest Observations);

/// <summary>
/// Local-only immutable prediction file followed by evidence submissions.
/// Hashes detect accidental edits; they are NOT independent notarization.
/// No paid API, hidden model input or unverifiable claim of a real video.
/// </summary>
public sealed class PendulumResearchStore(IWebHostEnvironment environment)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly SemaphoreSlim gate = new(1, 1);
    private string Root => Path.Combine(environment.ContentRootPath,
        ".private", "physics", "pendulum");

    public async Task<PendulumPreregisteredStudy> PreregisterAsync(
        PendulumPreregisterRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prediction = PendulumTrialProtocol.Predict(
            request.BaselineLengthMeters, request.FollowUpLengthMeters);
        var id = Guid.NewGuid().ToString("N");
        var at = DateTimeOffset.UtcNow;
        var record = new PendulumPreregisteredStudy(
            id, at, prediction, PredictionHash(id, at, prediction),
            "PREDICTION_REGISTERED_BEFORE_EVIDENCE_SUBMISSION",
            "Local timestamp and SHA-256 are not trusted proof of filming time or original discovery.");

        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Root);
            await using var stream = new FileStream(StudyPath(id),
                FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, record, Json, ct);
            return record;
        }
        finally { gate.Release(); }
    }

    public async Task<PendulumPreregisteredStudy> GetAsync(
        string id, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return await ReadUnlockedAsync(id, ct); }
        finally { gate.Release(); }
    }

    public async Task<PendulumComparisonRecord> CompareAsync(
        string id, PendulumCompareRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        await gate.WaitAsync(ct);
        try
        {
            var study = await ReadUnlockedAsync(id, ct);
            var comparisonsFolder = Path.Combine(Root, id.ToLowerInvariant() + "-evaluations");
            if (Directory.Exists(comparisonsFolder) &&
                Directory.EnumerateFiles(comparisonsFolder, "*.json").Take(5).Count() >= 5)
                throw new ArgumentException("Maximum five submissions per study.");

            var comparison = PendulumTrialProtocol.Compare(
                study.Hypothesis, request.Baseline, request.FollowUp);
            var dataJson = JsonSerializer.Serialize(request, Json);
            var inputHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(dataJson))).ToLowerInvariant();
            var summary = new PendulumComparisonRecord(
                Guid.NewGuid().ToString("N"), id.ToLowerInvariant(),
                DateTimeOffset.UtcNow, study.PredictionSha256,
                inputHash, comparison,
                "EVALUATED_LOCALLY_NO_META_TOKENS",
                "Local checksums are not cryptographic third-party attestation. " +
                "Video acquisition and uncertainty have not been independently verified.");

            Directory.CreateDirectory(comparisonsFolder);
            var file = Path.Combine(comparisonsFolder, summary.EvaluationId + ".json");
            await using var stream = new FileStream(file,
                FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(
                stream, new StoredPendulumEvaluation(summary, request), Json, ct);
            return summary;
        }
        finally { gate.Release(); }
    }

    private async Task<PendulumPreregisteredStudy> ReadUnlockedAsync(
        string id, CancellationToken ct)
    {
        var path = StudyPath(id);
        if (!File.Exists(path))
            throw new FileNotFoundException("Preregistration not found.", path);
        await using var stream = File.OpenRead(path);
        var record = await JsonSerializer.DeserializeAsync<PendulumPreregisteredStudy>(
            stream, Json, ct) ?? throw new InvalidDataException("Missing preregistration.");
        if (record.Id != id.ToLowerInvariant() ||
            !string.Equals(record.PredictionSha256,
                PredictionHash(record.Id, record.PreregisteredAtUtc, record.Hypothesis),
                StringComparison.Ordinal))
            throw new InvalidDataException("Preregistration integrity check failed.");
        return record;
    }

    private string StudyPath(string id)
    {
        if (id is null || id.Length != 32 || id.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid preregistration identifier.");
        return Path.Combine(Root, id.ToLowerInvariant() + ".json");
    }

    private static string PredictionHash(
        string id, DateTimeOffset at, PendulumTrialHypothesis p)
    {
        static string D(double x) => x.ToString("R", CultureInfo.InvariantCulture);
        var plain = string.Join("|", "PENDULUM-TRIAL-V1", id,
            at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            D(p.BaselineLengthMeters), D(p.FollowUpLengthMeters),
            D(p.ExpectedBaselinePeriodSeconds), D(p.ExpectedFollowUpPeriodSeconds),
            D(p.PredictedIncreasePercent), p.Formula, p.Assumptions);
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(plain))).ToLowerInvariant();
    }
}
