using System.Text.Json;
namespace ProofLab.Api;

public sealed record ResearchFrozenSource(string AnalysisId, string? AdaptiveBatchId)
{
    public static ResearchFrozenSource? Resolve(MuseExperimentState state, string studyId, string videoSha)
    {
        if (state.Prediction is null) return null;
        var observation = state.Observation;
        if (observation is null || state.Prediction.StudyId != studyId ||
            state.Prediction.ObservationId != observation.Id || observation.StudyId != studyId ||
            observation.VideoSha256 != videoSha || !ResearchOperationJournal.ValidId(observation.AnalysisId))
            throw new InvalidDataException("Frozen source identity mismatch.");
        using var payload = JsonDocument.Parse(observation.PayloadJson);
        var identity = payload.RootElement.GetProperty("identity");
        if (identity.GetProperty("analysisId").GetString() != observation.AnalysisId ||
            identity.GetProperty("videoSha256").GetString() != videoSha)
            throw new InvalidDataException("Frozen source payload mismatch.");
        var batch = identity.TryGetProperty("adaptiveBatchId", out var value) &&
            value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (batch is not null && (!ResearchOperationJournal.ValidId(batch) || batch != observation.AnalysisId))
            throw new InvalidDataException("Frozen batch identity mismatch.");
        return new(observation.AnalysisId, batch);
    }
}
