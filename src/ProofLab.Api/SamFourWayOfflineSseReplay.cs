using System.Text;
using System.Text.Json;

namespace ProofLab.Api;

/// <summary>
/// Converts a previously completed, PRIVATE full-reference SAM response
/// slice into four synthetic SSE event sequences. This allows the exact
/// production SSE parser to be tested with large REAL stored mask payloads,
/// WITHOUT contacting Meta or claiming independent provider detections.
/// All progress events must clearly say OFFLINE_REFERENCE_REPLAY.
/// </summary>
public static class SamFourWayOfflineSseReplay
{
    public static async Task<SamVideoSseResult> ReplayAsync(
        string completedSegmentResponse,
        Func<SamVideoSseProgress,ValueTask>? progress,
        CancellationToken ct, int deltaChars=16_000)
    {
        if(deltaChars is < 256 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(deltaChars));
        var wire=SamFourWayMaskAssembler.OutputWire(completedSegmentResponse);
        if(wire.Length>SamVideoSseParser.MaximumOutputChars)
            throw new InvalidDataException("Offline SSE fixture exceeds output limit.");
        var events=new StringBuilder(wire.Length+wire.Length/6+1024);
        for(var start=0;start<wire.Length;start+=deltaChars)
        {
            var length=Math.Min(deltaChars,wire.Length-start);
            events.Append("data: ").Append(JsonSerializer.Serialize(new {
                type="response.output_text.delta",
                delta=wire.Substring(start,length)
            })).Append("\n\n");
        }
        events.Append("data: ").Append(JsonSerializer.Serialize(new {
            type="response.completed",response=new{
                status="completed",id="offline-fixture-no-provider-call"
            }
        })).Append("\n\n");
        using var reader=new StringReader(events.ToString());
        if(progress is not null)
            await progress(new("OFFLINE_REFERENCE_REPLAY",0,0,0));
        var result=await SamVideoSseParser.ReadAsync(reader,progress,ct);
        if(!string.Equals(wire,result.OutputText,StringComparison.Ordinal))
            throw new InvalidDataException("Offline SSE changed SAM mask payload bytes.");
        return result;
    }
}
