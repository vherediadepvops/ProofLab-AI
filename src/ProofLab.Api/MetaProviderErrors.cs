using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ProofLab.Api;

/// <summary>Private, bounded diagnostics. Never retain the raw error body or request.</summary>
public static class MetaProviderErrors
{
    private const string DataKey = "ProofLab.MetaDiagnostic";
    private const int MaximumBytes = 32 * 1024;
    public sealed record Diagnostic(string DiagnosticId, DateTimeOffset AtUtc,
        string Phase, int HttpStatus, string? Code, string? Type,
        string? Message, string? ProviderRequestId, string BodyReadState);

    public static Diagnostic? Find(Exception exception)
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
            if (e.Data[DataKey] is Diagnostic diagnostic) return diagnostic;
        return null;
    }

    public static async Task<HttpRequestException> CreateAsync(HttpResponseMessage response,
        string token, string phase, ILogger? logger, CancellationToken ct)
    {
        string? code = null, type = null, message = null;
        var state = "UNREAD";
        // Failure reporting must not hang or replace the original HTTP failure.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            while (body.Length <= MaximumBytes)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(0,
                    Math.Min(buffer.Length, MaximumBytes + 1 - (int)body.Length)), deadline.Token);
                if (n == 0) break;
                body.Write(buffer, 0, n);
            }
            if (body.Length > MaximumBytes) state = "TOO_LARGE_OMITTED";
            else
            {
                using var json = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = 12 });
                state = "JSON_FIELDS_ONLY";
                if (json.RootElement.ValueKind == JsonValueKind.Object &&
                    json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    code = Identifier(Field(error, "code"), token);
                    type = Identifier(Field(error, "type"), token);
                    message = SafeMessage(Field(error, "message"), token);
                }
            }
        }
        catch (JsonException) { state = "NON_JSON_OMITTED"; }
        catch (OperationCanceledException) { state = "READ_CANCELLED"; }
        catch (Exception) { state = "READ_FAILED"; }

        string? requestId = null;
        foreach (var name in new[] { "x-request-id", "request-id", "x-meta-request-id" })
            if (response.Headers.TryGetValues(name, out var values))
            { requestId = Identifier(values.FirstOrDefault(), token); if (requestId is not null) break; }
        var diagnostic = new Diagnostic(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            Identifier(phase, token) ?? "provider", (int)response.StatusCode,
            code, type, message, requestId, state);
        logger?.LogWarning("Meta failure {DiagnosticId}: phase={Phase} HTTP={Status} code={Code} type={Type} request={RequestId} body={BodyState} message={Message}",
            diagnostic.DiagnosticId, diagnostic.Phase, diagnostic.HttpStatus, diagnostic.Code,
            diagnostic.Type, diagnostic.ProviderRequestId, diagnostic.BodyReadState, diagnostic.Message);
        var exception = new HttpRequestException("Meta request failed. Diagnostic: " + diagnostic.DiagnosticId,
            null, response.StatusCode);
        exception.Data[DataKey] = diagnostic;
        return exception;
    }

    private static string? Field(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) ? value.ValueKind switch
        { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), _ => null } : null;

    private static string? Identifier(string? value, string token) =>
        value is { Length: > 0 and <= 200 } &&
        (string.IsNullOrEmpty(token) || !value.Contains(token, StringComparison.Ordinal)) &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '/') ? value : null;

    private static string? SafeMessage(string? value, string token)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (!string.IsNullOrEmpty(token)) value = value.Replace(token, "[credential omitted]", StringComparison.Ordinal);
        // Omit echoed structured inputs/prompts rather than trying to log their contents.
        if (value.Length > 4096 || value.IndexOfAny(['{', '}', '[', ']', '<', '>']) >= 0)
            return "Provider message omitted (structured or oversized content).";
        value = Regex.Replace(value, @"(?i)data:[^\s]+|https?://[^\s]+|\bBearer\s+[^\s]+|\b(?:LLM_|sk-)[A-Za-z0-9_-]+|[A-Za-z0-9+/=_-]{80,}", "[redacted]");
        value = Regex.Replace(value, @"(?i)\b(?:token|api[_ -]?key|authorization|password)\s*[:=]\s*\S+", "[credential omitted]");
        value = Regex.Replace(value, @"[\r\n\t\p{C}]+", " ");
        return value.Length > 1200 ? value[..1200] + "…" : value;
    }
}
