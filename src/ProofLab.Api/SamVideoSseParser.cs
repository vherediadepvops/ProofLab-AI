using System.Text;
using System.Text.Json;

namespace ProofLab.Api;

/// <summary>
/// Sanitized, bounded information about SAM streaming progress. These fields
/// contain NO source video, masks, prompts, provider IDs or credentials.
/// </summary>
public sealed record SamVideoSseProgress(
    string Stage, int Events, int DeltaEvents, int OutputChars);

public sealed record SamVideoSseResult(
    string OutputText, string ProviderResponseId, int? BilledFrames,
    int Events, int DeltaEvents);

public sealed class SamVideoSseException(string code) : FormatException(code)
{
    public string Code { get; } = code;
}

/// <summary>
/// Strict SSE envelope reader for Meta SAM video, independent of HTTP.
/// One call to ReadAsync consumes an already established HTTP response.
/// Testing this class never contacts the provider or uses tokens.
/// </summary>
public static class SamVideoSseParser
{
    public const int MaximumOutputChars = 85_000_000;
    private const int MaximumEventChars = 90_000_000;

    public static async Task<SamVideoSseResult> ReadAsync(
        TextReader reader, Func<SamVideoSseProgress, ValueTask>? progress,
        CancellationToken ct, int maxOutputChars = MaximumOutputChars)
    {
        if (maxOutputChars <= 0 || maxOutputChars > MaximumOutputChars)
            throw new ArgumentOutOfRangeException(nameof(maxOutputChars));

        var output = new StringBuilder();
        var dataLines = new StringBuilder();
        var events = 0;
        var deltas = 0;
        var completed = false;
        var providerResponseId = "PROVIDER_RESPONSE_ID_UNAVAILABLE";
        int? billedFrames = null;

        async ValueTask Report(string stage)
        {
            if (progress is not null)
                await progress(new SamVideoSseProgress(
                    stage, events, deltas, output.Length));
        }

        void AppendSafe(string value)
        {
            if (value.Length > maxOutputChars - output.Length)
                throw new SamVideoSseException("SAM_SSE_OUTPUT_LIMIT_EXCEEDED");
            output.Append(value);
        }

        async ValueTask<bool> ConsumeEvent()
        {
            if (dataLines.Length == 0)
                return false;
            var payload = dataLines.ToString();
            dataLines.Clear();
            if (payload == "[DONE]")
            {
                await Report("SSE_DONE");
                return true;
            }

            if (payload.Length > MaximumEventChars)
                throw new SamVideoSseException("SAM_SSE_EVENT_LIMIT_EXCEEDED");

            JsonDocument parsed;
            try { parsed = JsonDocument.Parse(payload); }
            catch (JsonException)
            {
                await Report("SSE_INVALID_JSON");
                throw new SamVideoSseException("SAM_SSE_INVALID_JSON");
            }
            using (parsed)
            {
                events++;
                var message = parsed.RootElement;
                if (message.ValueKind != JsonValueKind.Object ||
                    !message.TryGetProperty("type", out var type) ||
                    type.ValueKind != JsonValueKind.String)
                    throw new SamVideoSseException("SAM_SSE_INVALID_EVENT");
                switch (type.GetString())
                {
                    case "response.output_text.delta":
                        if (!message.TryGetProperty("delta", out var delta) ||
                            delta.ValueKind != JsonValueKind.String)
                            throw new SamVideoSseException("SAM_SSE_DELTA_MISSING");
                        AppendSafe(delta.GetString() ?? "");
                        deltas++;
                        if (deltas == 1 || deltas % 25 == 0)
                            await Report("SSE_DELTA_PROGRESS");
                        break;

                    case "response.output_text.done":
                        // Fallback only: some providers return all output in the
                        // done event and no deltas. NEVER duplicate prior deltas.
                        if (output.Length == 0 &&
                            message.TryGetProperty("text", out var doneText) &&
                            doneText.ValueKind == JsonValueKind.String)
                            AppendSafe(doneText.GetString() ?? "");
                        await Report("SSE_TEXT_DONE");
                        break;

                    case "response.completed":
                        if (!message.TryGetProperty("response", out var response) ||
                            response.ValueKind != JsonValueKind.Object ||
                            !response.TryGetProperty("status", out var status) ||
                            status.ValueKind != JsonValueKind.String ||
                            status.GetString() != "completed")
                            throw new SamVideoSseException("SAM_SSE_BAD_COMPLETION");
                        if (response.TryGetProperty("id", out var id) &&
                            id.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrWhiteSpace(id.GetString()))
                            providerResponseId = id.GetString()!;
                        if (response.TryGetProperty("usage", out var usage) &&
                            usage.ValueKind == JsonValueKind.Object &&
                            usage.TryGetProperty("video_frames_processed", out var frames) &&
                            frames.ValueKind == JsonValueKind.Number &&
                            frames.TryGetInt32(out var count) && count >= 0)
                            billedFrames = count;
                        if (output.Length == 0 &&
                            response.TryGetProperty("output", out var messages) &&
                            messages.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in messages.EnumerateArray())
                            {
                                if (!item.TryGetProperty("content", out var contents) ||
                                    contents.ValueKind != JsonValueKind.Array)
                                    continue;
                                foreach (var part in contents.EnumerateArray())
                                {
                                    if (part.TryGetProperty("type", out var partType) &&
                                        partType.ValueKind == JsonValueKind.String &&
                                        partType.GetString() == "output_text" &&
                                        part.TryGetProperty("text", out var text) &&
                                        text.ValueKind == JsonValueKind.String)
                                        AppendSafe(text.GetString() ?? "");
                                }
                            }
                        }
                        completed = true;
                        await Report(output.Length == 0
                            ? "SSE_COMPLETED_NO_MATCH" : "SSE_COMPLETED");
                        return true;

                    case "response.failed":
                    case "response.incomplete":
                    case "error":
                        // Raw provider errors may include sensitive data.
                        // Their category is sufficient for a private audit.
                        await Report("SSE_PROVIDER_TERMINATED");
                        throw new SamVideoSseException("SAM_SSE_PROVIDER_TERMINATED");

                    default:
                        if (events == 1 || events % 100 == 0)
                            await Report("SSE_CONTROL_EVENT");
                        break;
                }
            }
            return false;
        }

        await Report("SSE_WAITING");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                if (await ConsumeEvent()) break;
                break;
            }
            if (line.Length == 0)
            {
                if (await ConsumeEvent()) break;
                continue;
            }
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue; // SSE comments, event:, id:, retry:, keepalives
            var content = line.AsSpan(5);
            if (content.Length > 0 && content[0] == ' ')
                content = content[1..];
            if (dataLines.Length > 0)
                dataLines.Append('\n');
            if (content.Length > MaximumEventChars - dataLines.Length)
                throw new SamVideoSseException("SAM_SSE_EVENT_LIMIT_EXCEEDED");
            dataLines.Append(content);
        }
        if (!completed)
        {
            await Report("SSE_EOF_WITHOUT_COMPLETION");
            throw new SamVideoSseException("SAM_SSE_MISSING_COMPLETION");
        }

        // Meta explicitly allows zero matches: completed with empty lane.
        return new SamVideoSseResult(
            output.ToString(), providerResponseId, billedFrames, events, deltas);
    }
}
