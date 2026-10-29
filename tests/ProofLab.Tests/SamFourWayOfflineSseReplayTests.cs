using System.Text.Json;
using ProofLab.Api;

namespace ProofLab.Tests;

public sealed class SamFourWayOfflineSseReplayTests
{
    private static string Envelope(string wire) =>
        JsonSerializer.Serialize(new {
            model="sam-3.1",status="completed",
            output=new[]{new{content=new[]{new{type="output_text",text=wire}}}}
        });

    [Fact]
    public async Task ReplaysMultipleRealSseDeltasWithoutChangingSourceMaskText()
    {
        var wire=string.Concat(Enumerable.Range(0,1000).Select(i=>
            "<"+i+"f>0<|box;x1=1;y1=2;x2=3;y2=4;w=5;h=6|>"+
            "<|mask;x=1;y=2;data=3,3,SYNTHETIC|>\n"));
        var events=new List<SamVideoSseProgress>();
        var parsed=await SamFourWayOfflineSseReplay.ReplayAsync(
            Envelope(wire),
            p=>{events.Add(p);return ValueTask.CompletedTask;},
            CancellationToken.None,deltaChars:1000);
        Assert.Equal(wire,parsed.OutputText);
        Assert.True(parsed.DeltaEvents>50);
        Assert.Contains(events,e=>e.Stage=="OFFLINE_REFERENCE_REPLAY");
        Assert.Contains(events,e=>e.Stage=="SSE_DELTA_PROGRESS");
        Assert.Contains(events,e=>e.Stage=="SSE_COMPLETED");
        Assert.DoesNotContain(events,e=>e.Stage.Contains("PROVIDER_RECEIPT"));
    }

    [Fact]
    public async Task RejectsInvalidOfflineSseFixtureAndDeltaLimit()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>
            SamFourWayOfflineSseReplay.ReplayAsync(
                Envelope("test"),null,CancellationToken.None,deltaChars:2));
        await Assert.ThrowsAsync<InvalidDataException>(()=>
            SamFourWayOfflineSseReplay.ReplayAsync(
                "{\"model\":\"sam-3.1\",\"status\":\"failed\",\"output\":[]}",
                null,CancellationToken.None));
    }
}
