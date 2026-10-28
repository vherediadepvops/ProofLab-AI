using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ProofLab.Api;

/// <summary>
/// HTTPS-only, operator-gated transport for a single SAM 3.1 VIDEO request.
/// Never construct or send a model call from the Angular browser.
/// This is NOT a substitute for owner's credits/usage verification.
/// </summary>
public sealed class SamVideoMetaTransport(IHttpClientFactory clients)
{
    private static readonly Uri Base = new("https://api.meta.ai/v1/");
    private const int MaximumWireChars = 85_000_000;

    private static HttpRequestMessage Create(
        HttpMethod method, string relative, string token)
    {
        var request = new HttpRequestMessage(method,new Uri(Base,relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",token);
        return request;
    }

    public async Task<SamApiCompletedResult> UploadAndSegmentAsync(
        string originalPath, string concept, CancellationToken ct)
    {
        if (concept != PendulumSamCache.Prompt)
            throw new ArgumentException("Only a curated one-object scientific prompt is allowed.");
        var token = Environment.GetEnvironmentVariable("MODEL_API_KEY");
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Meta backend key is not configured.");
        var http = clients.CreateClient("sam-video");
        string providerFileId;

        // Files API is a THIRD-PARTY video transfer. The upstream cache
        // orchestrator has already checked explicit user approval and a
        // payment gate, and stored an attempt marker before calling us.
        using (var upload=Create(HttpMethod.Post,"files",token))
        using (var multipart=new MultipartFormDataContent())
        {
            multipart.Add(new StringContent("user_data"),"purpose");
            multipart.Add(new StringContent("created_at"),"expires_after[anchor]");
            multipart.Add(new StringContent("86400"),"expires_after[seconds]");
            await using var input=new FileStream(originalPath,FileMode.Open,
                FileAccess.Read,FileShare.Read,128*1024,useAsync:true);
            using(var filePart=new StreamContent(input))
            {
                filePart.Headers.ContentType = new MediaTypeHeaderValue(
                    Path.GetExtension(originalPath).Equals(".mov",
                        StringComparison.OrdinalIgnoreCase)
                        ? "video/quicktime" : "video/mp4");
                multipart.Add(filePart,"file","pendulum"+Path.GetExtension(originalPath).ToLowerInvariant());
                upload.Content=multipart;
                using var response=await http.SendAsync(upload,
                    HttpCompletionOption.ResponseHeadersRead,ct);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException("Meta video upload failed.",null,response.StatusCode);
                var raw=await response.Content.ReadAsStringAsync(ct);
                if(raw.Length>16_384)throw new FormatException("Invalid upload receipt.");
                using var doc=JsonDocument.Parse(raw);
                var root=doc.RootElement;
                providerFileId=root.GetProperty("id").GetString() ??
                    throw new FormatException("Meta video upload had no file ID.");
                if(!providerFileId.StartsWith("file-",StringComparison.Ordinal) ||
                   providerFileId.Length>128 ||
                   providerFileId.Any(ch=>!char.IsAsciiLetterOrDigit(ch) && ch is not ('-' or '_')))
                    throw new FormatException("Unexpected Meta file ID.");
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
        using var inference=Create(HttpMethod.Post,"responses",token);
        inference.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        inference.Content=new StringContent(JsonSerializer.Serialize(payload),
            Encoding.UTF8,"application/json");
        using var reply=await http.SendAsync(inference,
            HttpCompletionOption.ResponseHeadersRead,ct);
        if(!reply.IsSuccessStatusCode)
            throw new HttpRequestException("Meta SAM video inference failed.",null,reply.StatusCode);

        var buffer=new StringBuilder(65_536);
        string? responseId=null;
        int? billedFrames=null;
        bool completed=false;
        await using var stream=await reply.Content.ReadAsStreamAsync(ct);
        using var reader=new StreamReader(stream,Encoding.UTF8,detectEncodingFromByteOrderMarks:true);
        while(true)
        {
            ct.ThrowIfCancellationRequested();
            var line=await reader.ReadLineAsync(ct);
            if(line is null)break;
            if(!line.StartsWith("data: ",StringComparison.Ordinal))continue;
            var data=line[6..];
            if(data=="[DONE]")break;
            if(data.Length>4_000_000)
                throw new FormatException("SAM event exceeds configured size limit.");
            using var eventJson=JsonDocument.Parse(data);
            var item=eventJson.RootElement;
            var type=item.TryGetProperty("type",out var typ) ? typ.GetString():null;
            if(type=="response.output_text.delta")
            {
                if(item.TryGetProperty("delta",out var delta) &&
                   delta.ValueKind==JsonValueKind.String)
                {
                    var next=delta.GetString() ?? "";
                    if(buffer.Length+next.Length>MaximumWireChars)
                        throw new FormatException("SAM video output exceeds safety limit.");
                    buffer.Append(next);
                }
            }
            else if(type=="response.completed")
            {
                if(!item.TryGetProperty("response",out var final) ||
                   final.ValueKind!=JsonValueKind.Object ||
                   !final.TryGetProperty("status",out var state) ||
                   state.GetString()!="completed")
                    throw new FormatException("Meta SAM completion state not verified.");
                completed=true;
                if(final.TryGetProperty("id",out var id) && id.ValueKind==JsonValueKind.String)
                    responseId=id.GetString();
                if(final.TryGetProperty("usage",out var usage) &&
                   usage.ValueKind==JsonValueKind.Object &&
                   usage.TryGetProperty("video_frames_processed",out var frames) &&
                   frames.TryGetInt32(out var n) && n>=0)
                    billedFrames=n;
                // Some non-streaming-compatible providers include the full
                // output only in final.response. Never append it to deltas.
                if(buffer.Length==0 && final.TryGetProperty("output",out var outputs) &&
                   outputs.ValueKind==JsonValueKind.Array)
                {
                    foreach(var outItem in outputs.EnumerateArray())
                    {
                        if(!outItem.TryGetProperty("content",out var parts) ||
                           parts.ValueKind!=JsonValueKind.Array)continue;
                        foreach(var part in parts.EnumerateArray())
                        {
                            if(part.TryGetProperty("type",out var partType) &&
                               partType.GetString()=="output_text" &&
                               part.TryGetProperty("text",out var textPart) &&
                               textPart.ValueKind==JsonValueKind.String)
                                buffer.Append(textPart.GetString());
                        }
                    }
                }
            }
            else if(type is "response.failed" or "response.incomplete" or "error")
                throw new FormatException("Meta SAM video result was incomplete.");
        }
        if(!completed || buffer.Length==0 || buffer.Length>MaximumWireChars)
            throw new FormatException("No complete segmentation received from Meta.");

        // This JSON envelope is constructed solely from authenticated HTTPS
        // provider SSE frames, not from a user-supplied replay.
        var completedResponse = JsonSerializer.Serialize(new {
            @object="response", model="sam-3.1", status="completed",
            output=new[]{new {type="message",content=new[]{
                new {type="output_text",text=buffer.ToString()}}}}
        });
        return new SamApiCompletedResult(
            completedResponse,responseId ?? "PROVIDER_RESPONSE_ID_UNAVAILABLE",billedFrames);
    }
}
