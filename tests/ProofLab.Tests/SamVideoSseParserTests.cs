using System.Text.Json;
using ProofLab.Api;

namespace ProofLab.Tests;

/// <summary>Only synthetic provider protocol fixtures. NO Meta calls.</summary>
public sealed class SamVideoSseParserTests
{
    private static string Event(object value) =>
        "data: " + JsonSerializer.Serialize(value) + "\n\n";

    private static readonly string Completion = Event(new
    {
        type = "response.completed",
        response = new {
            id = "response-test-only",
            status = "completed",
            usage = new { video_frames_processed = 1828 }
        }
    });

    private static async Task<SamVideoSseResult> ParseAsync(
        string data, Func<SamVideoSseProgress, ValueTask>? logger = null,
        int limit = SamVideoSseParser.MaximumOutputChars)
    {
        using var reader = new StringReader(data);
        return await SamVideoSseParser.ReadAsync(
            reader, logger, CancellationToken.None, limit);
    }

    [Fact]
    public async Task CompletedStream_PreservesOnlyDeltasAndFrameUsage()
    {
        const string wire = "<0f>0<|box;x1=1;y1=2;x2=3;y2=4;w=5;h=6|>";
        var events = new List<SamVideoSseProgress>();
        var stream = ": heartbeat\n\n" +
            Event(new { type = "response.created" }) +
            Event(new { type = "response.output_text.delta", delta = wire }) +
            Event(new { type = "response.output_text.done", text = wire }) +
            Completion + "data: [DONE]\n\n";
        var parsed = await ParseAsync(stream, value =>
        {
            events.Add(value);
            return ValueTask.CompletedTask;
        });

        Assert.Equal(wire, parsed.OutputText); // no duplicated output_text.done
        Assert.Equal(1828, parsed.BilledFrames);
        Assert.Equal("response-test-only", parsed.ProviderResponseId);
        Assert.Equal(1, parsed.DeltaEvents);
        Assert.Contains(events, p => p.Stage == "SSE_DELTA_PROGRESS");
        Assert.Contains(events, p => p.Stage == "SSE_COMPLETED");
        Assert.DoesNotContain(events, p => p.Stage.Contains("response-test-only"));
        Assert.DoesNotContain(events, p => p.Stage.Contains("<0f>"));
    }

    [Fact]
    public async Task CompletedWithNoDetections_IsValidZeroMatch()
    {
        var parsed = await ParseAsync(Completion);
        Assert.Empty(parsed.OutputText);
        Assert.Equal(1828, parsed.BilledFrames);
        Assert.Equal(0, parsed.DeltaEvents);
    }

    [Fact]
    public async Task OnlyTextDoneAndCompleted_ReturnsTextWithoutDeltas()
    {
        var text = "<7f>0<|box;x1=1;y1=2;x2=3;y2=4;w=5;h=6|>";
        var stream = Event(new { type = "response.output_text.done", text }) +
                     Completion;
        var parsed = await ParseAsync(stream);
        Assert.Equal(text, parsed.OutputText);
        Assert.Equal(0, parsed.DeltaEvents);
    }

    [Fact]
    public async Task FinalResponseFallback_IsAcceptedWithoutDelta()
    {
        var stream = Event(new {
            type = "response.completed",
            response = new {
                id = "test-final", status = "completed",
                output = new[] { new {
                    content = new[] {
                        new { type = "output_text", text = "<9f>mask-wire-fixture" }
                    }
                }}
            }
        });
        var parsed = await ParseAsync(stream);
        Assert.Equal("<9f>mask-wire-fixture", parsed.OutputText);
    }

    [Fact]
    public async Task ContentPartDoneSuppliesFinalTextWhenProviderHasNoOutputTextDone()
    {
        const string sample="<12f>0<|box;x1=1;y1=2;x2=3;y2=4;w=5;h=6|>";
        var events=Event(new {type="response.content_part.done",
            part=new {type="output_text",text=sample}})+Completion;
        var parsed=await ParseAsync(events);
        Assert.Equal(sample,parsed.OutputText);
        Assert.Equal(1828,parsed.BilledFrames);
    }

    [Fact]
    public async Task FinalTextMismatchIsRejectedRatherThanMisrepresentingMasks()
    {
        var events=Event(new {type="response.output_text.delta",delta="correct"})+
            Event(new {type="response.content_part.done",
                part=new {type="output_text",text="incorrect"}})+Completion;
        var error=await Assert.ThrowsAsync<SamVideoSseException>(()=>ParseAsync(events));
        Assert.Equal("SAM_SSE_FINAL_TEXT_MISMATCH",error.Code);
    }

    [Fact]
    public async Task IncompleteStreamAndDoneWithoutCompleted_AreRejected()
    {
        var stream = Event(new { type = "response.output_text.delta",
                                 delta = "partial-mask" }) +
                     "data: [DONE]\n\n";
        var error = await Assert.ThrowsAsync<SamVideoSseException>(
            () => ParseAsync(stream));
        Assert.Equal("SAM_SSE_MISSING_COMPLETION", error.Code);
    }

    [Fact]
    public async Task ProviderFailureNeverLeaksRawError()
    {
        var stream = Event(new {
            type = "response.failed",
            response = new { error = "PRIVATE_PAYLOAD_SHOULD_NOT_BE_DISCLOSED" }
        });
        var error = await Assert.ThrowsAsync<SamVideoSseException>(
            () => ParseAsync(stream));
        Assert.Equal("SAM_SSE_PROVIDER_TERMINATED", error.Code);
        Assert.DoesNotContain("PRIVATE", error.Message);
    }

    [Fact]
    public async Task BoundedOutputRejectsLargeMaskBeforeAppend()
    {
        var stream = Event(new { type = "response.output_text.delta",
                                 delta = new string('x', 100) }) + Completion;
        var error = await Assert.ThrowsAsync<SamVideoSseException>(
            () => ParseAsync(stream, limit: 20));
        Assert.Equal("SAM_SSE_OUTPUT_LIMIT_EXCEEDED", error.Code);
    }

    [Fact]
    public async Task RecognizesMultilineSseDataEventsAndKeepalives()
    {
        var stream = ": keep-alive\n" +
                     "event: response.completed\n" +
                     "data: {\"type\":\"response.completed\",\n" +
                     "data: \"response\":{\"status\":\"completed\"}}\n\n";
        var parsed = await ParseAsync(stream);
        Assert.Empty(parsed.OutputText);
        Assert.Equal("PROVIDER_RESPONSE_ID_UNAVAILABLE", parsed.ProviderResponseId);
    }
}
