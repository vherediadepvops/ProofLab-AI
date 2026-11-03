using System.Text.Json;
using System.Text.Json.Nodes;
namespace ProofLab.Api;

/// <summary>Keep immutable AI input while allowing a subsequent human review.</summary>
public static class MuseObservationIdentity
{
    public static bool MatchesCurrent(MuseObservation original,JsonElement currentIdentity)
    {
        using var payload=JsonDocument.Parse(original.PayloadJson);
        var before=JsonNode.Parse(payload.RootElement.GetProperty("identity").GetRawText()) as JsonObject;
        var current=JsonNode.Parse(currentIdentity.GetRawText()) as JsonObject;
        if(before is null || current is null)return false;
        if(JsonNode.DeepEquals(before,current))return true;
        // This one-way review transition is independent of measurements. Never
        // ignore changes to the video, SAM response, pivot, worker or parameters.
        if(before["objectReview"]?.GetValueKind()!=JsonValueKind.False ||
           current["objectReview"]?.GetValueKind()!=JsonValueKind.True)return false;
        current["objectReview"]=false;
        return JsonNode.DeepEquals(before,current);
    }
}
