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

    private sealed class LinesReader(IEnumerable<string> lines, Exception? terminalError=null) : TextReader
    {
        private readonly IEnumerator<string> cursor=lines.GetEnumerator();
        public override ValueTask<string?> ReadLineAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if(cursor.MoveNext()) return ValueTask.FromResult<string?>(cursor.Current);
            if(terminalError is not null) throw terminalError;
            return ValueTask.FromResult<string?>(null);
        }
        protected override void Dispose(bool disposing) {if(disposing)cursor.Dispose();base.Dispose(disposing);}
    }

    private static IEnumerable<string> FragmentedStream(string text, bool mismatch=false, bool complete=true)
    {
        for(var start=0;start<text.Length;start+=7402)
        {
            yield return "data: "+JsonSerializer.Serialize(new {type="response.output_text.delta",
                delta=text.Substring(start,Math.Min(7402,text.Length-start))});
            yield return "";
        }
        yield return "data: "+JsonSerializer.Serialize(new {type="response.output_text.done",text});
        yield return "";
        yield return "data: "+JsonSerializer.Serialize(new {type="response.content_part.done",
            part=new {type="output_text",text=mismatch?text[..^1]+"!":text}});
        yield return "";
        if(complete) {yield return Completion.Trim();yield return "";}
    }

    [Fact]
    public async Task LargeFragmentedResponse_WithBothFinalTextEvents_PreservesOutputExactlyOnce()
    {
        // Production-sized regression: thousands of StringBuilder chunks, 14M characters.
        var text=new string('x',13_952_274)+"z";
        using var reader=new LinesReader(FragmentedStream(text));
        var result=await SamVideoSseParser.ReadAsync(reader,null,default);
        Assert.Equal(text,result.OutputText);
        Assert.Equal((text.Length+7401)/7402,result.DeltaEvents);
        Assert.Equal(result.DeltaEvents+3,result.Events);
    }

    [Fact]
    public async Task FragmentedFinalTextMismatch_StillRejectsSameLengthDifferenceAtEnd()
    {
        using var reader=new LinesReader(FragmentedStream(new string('x',100_000),mismatch:true));
        var error=await Assert.ThrowsAsync<SamVideoSseException>(()=>SamVideoSseParser.ReadAsync(reader,null,default));
        Assert.Equal("SAM_SSE_FINAL_TEXT_MISMATCH",error.Code);
        Assert.Equal("SSE_CONTENT_PART_VERIFY_STARTED",error.LastProgress!.Stage);
        Assert.Equal(100_000,error.LastProgress.OutputChars);
    }

    [Fact]
    public async Task FinalTextWithoutCompletion_RemainsUnverifiedAndRetainsPrivateDiagnosticData()
    {
        const string text="private incomplete synthetic mask";
        using var reader=new LinesReader(FragmentedStream(text,complete:false));
        var error=await Assert.ThrowsAsync<SamVideoSseException>(()=>SamVideoSseParser.ReadAsync(reader,null,default));
        Assert.Equal("SAM_SSE_MISSING_COMPLETION",error.Code);
        Assert.Equal(text,error.PendingOutputText);
        Assert.DoesNotContain(text,error.ToString());
    }

    [Fact]
    public async Task BrokenConnection_PreservesCountsAndSafeCauseWithoutExposingErrorMessage()
    {
        const string text="private synthetic output";
        using var reader=new LinesReader(FragmentedStream(text,complete:false),new IOException("secret-token-and-provider-url"));
        var error=await Assert.ThrowsAsync<SamVideoSseException>(()=>SamVideoSseParser.ReadAsync(reader,null,default));
        Assert.Equal("SAM_SSE_READ_INTERRUPTED",error.Code);
        Assert.Equal(typeof(IOException).FullName,error.CauseType);
        Assert.Equal("SSE_CONTENT_PART_DONE",error.LastProgress!.Stage);
        Assert.Equal(text.Length,error.LastProgress.OutputChars);
        Assert.Equal(1,error.LastProgress.DeltaEvents);
        Assert.Equal(text,error.PendingOutputText);
        Assert.DoesNotContain("secret-token",error.ToString());
        Assert.DoesNotContain(text,error.ToString());
    }

    [Fact]
    public async Task CancellationDuringFinalVerification_DoesNotReturnCompletedEvidence()
    {
        using var cancel=new CancellationTokenSource();
        using var reader=new LinesReader(FragmentedStream(new string('x',100_000)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>SamVideoSseParser.ReadAsync(reader,p=>{
            if(p.Stage=="SSE_TEXT_VERIFY_STARTED")cancel.Cancel();
            return ValueTask.CompletedTask;
        },cancel.Token));
    }

    [Fact]
    public async Task MalformedCompletedOutput_HasTypedDiagnosticInsteadOfGenericFailure()
    {
        var error=await Assert.ThrowsAsync<SamVideoSseException>(()=>ParseAsync(Event(new {
            type="response.completed",response=new {status="completed",output=new[]{42}}})));
        Assert.Equal("SAM_SSE_BAD_COMPLETION",error.Code);
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

    [Theory]
    [InlineData("max_output_tokens","max_output_tokens")]
    [InlineData("PRIVATE_TOKEN_IN_UNKNOWN_REASON","unspecified")]
    public async Task IncompleteRetainsOnlyAllowlistedReason(string reason,string expected)
    {
        var stream=Event(new {type="response.incomplete",
            response=new {incomplete_details=new {reason}}});
        var error=await Assert.ThrowsAsync<SamVideoSseException>(()=>ParseAsync(stream));
        Assert.Equal("response.incomplete",error.ProviderEvent);
        Assert.Equal(expected,error.ProviderReason);
        Assert.DoesNotContain("PRIVATE",error.ToString());
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
