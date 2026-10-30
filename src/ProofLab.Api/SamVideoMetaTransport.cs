using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ProofLab.Api;

/// <summary>
/// HTTPS-only, operator-gated SAM 3.1 video transport. Never uploads media
/// except after PendulumSamCache has reserved ONE approved request and written
/// an attempt marker. Logs never contain masks, source pixels, keys or IDs.
/// </summary>
public sealed class SamVideoMetaTransport(
    IHttpClientFactory clients, ILogger<SamVideoMetaTransport> logger) : ISamVideoTransport
{
    private static readonly Uri Base = new("https://api.meta.ai/v1/");

    private static HttpRequestMessage Create(
        HttpMethod method, string relative, string token)
    {
        var request = new HttpRequestMessage(method, new Uri(Base, relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public async Task<SamApiCompletedResult> UploadAndSegmentAsync(
        string originalPath, string concept,
        Func<SamVideoSseProgress, ValueTask>? progress, CancellationToken ct,
        string? oneShotMetaToken = null)
    {
        if (!AssemblyPolicy.IsSamPrompt(concept))
            throw new ArgumentException("Only curated SAM prompt is allowed.");

        // HttpClient.Timeout with ResponseHeadersRead protects HTTP headers,
        // NOT the subsequent SSE content. A linked 30-minute deadline also
        // protects ReadLineAsync during the video segmentation stream.
        // The caller supplies an independent operation deadline after billing
        // reservation, so browser disconnect cannot discard a completed result.
        using var operationDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        operationDeadline.CancelAfter(TimeSpan.FromMinutes(30));
        ct = operationDeadline.Token;

        // The key in the single browser POST is used only for this authorized
        // backend HTTPS request. The environment key is the normal fallback;
        // neither value is put into logs, receipts, SQLite or source files.
        var token = oneShotMetaToken ??
            Environment.GetEnvironmentVariable("MODEL_API_KEY");
        if (string.IsNullOrWhiteSpace(token) ||
            (oneShotMetaToken is not null &&
             !SamManualApiTokenPolicy.Accepts(oneShotMetaToken)))
            throw new InvalidOperationException("Valid Meta key required.");

        var elapsed = Stopwatch.StartNew();
        async ValueTask Audit(string stage, int events = 0,
            int deltas = 0, int outputChars = 0)
        {
            logger.LogInformation(
                "SAM transport stage={Stage} elapsedMs={ElapsedMs} events={Events} deltas={Deltas} outputChars={Chars}",
                stage, elapsed.ElapsedMilliseconds, events, deltas, outputChars);
            if (progress != null)
                await progress(new SamVideoSseProgress(stage, events, deltas, outputChars));
        }

        var http = clients.CreateClient("sam-video");
        await Audit("UPLOAD_STARTED");
        string providerFileId;
        using (var upload = Create(HttpMethod.Post, "files", token))
        using (var multipart = new MultipartFormDataContent())
        {
            multipart.Add(new StringContent("user_data"), "purpose");
            await using var input = new FileStream(originalPath, FileMode.Open,
                FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
            using (var part = new StreamContent(input))
            {
                part.Headers.ContentType = new MediaTypeHeaderValue(
                    Path.GetExtension(originalPath).Equals(".mov",
                        StringComparison.OrdinalIgnoreCase)
                        ? "video/quicktime" : "video/mp4");
                multipart.Add(part, "file",
                    "pendulum" + Path.GetExtension(originalPath).ToLowerInvariant());
                upload.Content = multipart;
                using var response = await http.SendAsync(
                    upload, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode)
                {
                    await Audit("UPLOAD_REJECTED");
                    throw new HttpRequestException(
                        "Meta video upload failed.", null, response.StatusCode);
                }
                await Audit("UPLOAD_HTTP_ACCEPTED");
                var raw = await response.Content.ReadAsStringAsync(ct);
                if (raw.Length > 16_384)
                    throw new FormatException("SAM_UPLOAD_RECEIPT_TOO_LARGE");
                using var doc = JsonDocument.Parse(raw);
                providerFileId = doc.RootElement.GetProperty("id").GetString() ??
                    throw new FormatException("SAM_UPLOAD_MISSING_FILE_ID");
                if (!providerFileId.StartsWith("file-", StringComparison.Ordinal) ||
                    providerFileId.Length > 128 ||
                    providerFileId.Any(ch => !char.IsAsciiLetterOrDigit(ch) &&
                                             ch is not ('-' or '_')))
                    throw new FormatException("SAM_UPLOAD_INVALID_FILE_ID");
            }
        }

        var payload = new
        {
            model = "sam-3.1",
            input = new[] { new {
                type = "message", role = "user",
                content = new object[] {
                    new { type = "input_text", text = concept },
                    new { type = "input_video", file_id = providerFileId }
                }
            }},
            stream = true
        };

        await Audit("INFERENCE_REQUEST_STARTED");
        using var inference = Create(HttpMethod.Post, "responses", token);
        inference.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        inference.Content = new StringContent(JsonSerializer.Serialize(payload),
            Encoding.UTF8, "application/json");
        using var reply = await http.SendAsync(
            inference, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!reply.IsSuccessStatusCode)
        {
            await Audit("INFERENCE_REJECTED");
            throw new HttpRequestException(
                "Meta SAM video inference failed.", null, reply.StatusCode);
        }
        await Audit("INFERENCE_HTTP_ACCEPTED");

        await using var stream = await reply.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        var parsed = await SamVideoSseParser.ReadAsync(reader, progress, ct);
        // A completed zero-match result is valid: never fabricate a mask.
        await Audit(parsed.OutputText.Length > 0
            ? "PROVIDER_RESPONSE_COMPLETED" : "PROVIDER_COMPLETED_ZERO_MATCH",
            parsed.Events, parsed.DeltaEvents, parsed.OutputText.Length);

        // Construct a narrow envelope from authenticated TLS/SSE events,
        // not from user-uploaded JSON in the offline inspector.
        var complete = JsonSerializer.Serialize(new {
            @object = "response", model = "sam-3.1", status = "completed",
            output = new[] { new { type = "message", content = new[] {
                new { type = "output_text", text = parsed.OutputText } } } }
        });
        return new SamApiCompletedResult(complete, parsed.ProviderResponseId,
            parsed.BilledFrames);
    }
}
