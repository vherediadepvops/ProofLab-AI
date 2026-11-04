using System.Text.Json;
using ProofLab.Science;

namespace ProofLab.Api;

public sealed record DirectorHumanReviewRequest(bool BobConfirmed,MusePrepareRequest? SamEvidence=null);

// Constructed only after the service has resolved an authenticated archive and
// the server's own measurement. No period or assessment is accepted from HTTP.
internal sealed record ValidatedSamBaselineReview(string StudyId,string VideoSha256,string AnalysisId);

internal static class DirectorBaselineReviewPolicy
{
    internal static void RequireHuman(bool bobConfirmed)
    {
        if(!bobConfirmed)throw new ArgumentException("A human must review the tracked object first.");
    }

    internal static void RequireLocalPeriod(PendulumSpectralAssessment? localPeriod)
    {
        if(localPeriod is null || !PendulumDirectorProtocol.HasUsableInitialSignal(localPeriod))
            throw new ArgumentException("No usable local period; manual review is required.");
    }

    internal static ValidatedSamBaselineReview FromSamObservation(string studyId,
        MusePrepareRequest request,MuseObservation observation)
    {
        // PrepareAsync has already compared the archive, source and current pivot.
        // Bind the actual measurement to that same baseline, including its role.
        using var payload=JsonDocument.Parse(observation.PayloadJson);
        var root=payload.RootElement;
        if(!root.TryGetProperty("identity",out var identity) ||
           !root.TryGetProperty("measurement",out var measurement) ||
           !measurement.TryGetProperty("source",out var source) ||
           observation.StudyId!=studyId || observation.VideoSha256!=request.VideoSha256 ||
           observation.AnalysisId!=request.AnalysisId ||
           KnotPolicy.Hash(observation.PayloadJson)!=observation.PayloadSha256 ||
           KnotPolicy.Hash(identity.GetRawText())!=observation.Id ||
           Text(identity,"studyId")!=studyId || Text(identity,"videoSha256")!=request.VideoSha256 ||
           Text(identity,"analysisId")!=request.AnalysisId ||
           Text(identity,"pivotRevisionId")!=request.PivotRevisionId ||
           Text(identity,"adaptiveBatchId")!=request.AdaptiveBatchId ||
           Text(source,"studyId")!=studyId || Text(source,"role")!="baseline" ||
           Text(source,"originalVideoSha256")!=request.VideoSha256 ||
           Text(source,"analysisId")!=request.AnalysisId ||
           Text(source,"providerResponseSha256") is not {Length:64} responseSha ||
           responseSha!=Text(identity,"responseSha256") ||
           Text(source,"pivotRevisionId")!=request.PivotRevisionId)
            throw new InvalidDataException("SAM review evidence does not match this baseline.");

        // This confirms a usable measured period and the user's object review,
        // not an independently verified pivot or a scientific classification.
        if(!measurement.TryGetProperty("assessment",out var assessment) ||
           Text(assessment,"status")!="CANDIDATE" ||
           !assessment.TryGetProperty("periodSeconds",out var value) ||
           value.ValueKind!=JsonValueKind.Number || !value.TryGetDouble(out var period) ||
           !double.IsFinite(period) || period<=0)
            throw new ArgumentException("No usable SAM period; review the tracking and measurement limits.");
        return new(studyId,request.VideoSha256,request.AnalysisId);
    }

    private static string? Text(JsonElement value,string name)=>
        value.ValueKind==JsonValueKind.Object && value.TryGetProperty(name,out var field) &&
        field.ValueKind==JsonValueKind.String ? field.GetString() : null;
}
