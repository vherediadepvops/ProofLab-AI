using System.Net;
using System.Text.Json;
using ProofLab.Api;

namespace ProofLab.Tests;

public class MetaProviderErrorsTests
{
    private const string Token = "LLM_TEST_DIAGNOSTICS_NEVER_REAL";

    [Fact]
    public async Task Preserves413StatusAndSafeProviderFieldsThroughWrapper()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge) {
            Content = new StringContent("{\"error\":{\"code\":\"payload_too_large\",\"type\":\"invalid_request_error\",\"message\":\"Response storage exceeds the limit.\"}}")
        };
        response.Headers.Add("x-request-id", "req-test-413");
        var error = await MetaProviderErrors.CreateAsync(response, Token, "responses", null, default);
        var detail = MetaProviderErrors.Find(new InvalidOperationException("outer", error));
        Assert.NotNull(detail);
        Assert.Equal(413, detail.HttpStatus);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, error.StatusCode);
        Assert.Equal("payload_too_large", detail.Code);
        Assert.Equal("invalid_request_error", detail.Type);
        Assert.Equal("req-test-413", detail.ProviderRequestId);
        Assert.Equal("Response storage exceeds the limit.", detail.Message);
        Assert.Equal("JSON_FIELDS_ONLY", detail.BodyReadState);
        Assert.Contains(detail.DiagnosticId, error.Message);
    }

    [Theory]
    [InlineData("Credential LLM_TEST_DIAGNOSTICS_NEVER_REAL was rejected")]
    [InlineData("Invalid data:image/jpeg;base64,aGVsbG8= image")]
    [InlineData("Invalid https://example.test/private?token=secret-value request")]
    [InlineData("Request {\"prompt\":\"private science data\"} is invalid")]
    public async Task OmitsCredentialsImagesUrlsAndStructuredEchoes(string message)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest) {
            Content = new StringContent(JsonSerializer.Serialize(new { error = new {
                code = Token, type = "invalid_request_error", message,
                request = "private science data", api_key = Token
            }}))
        };
        response.Headers.Add("x-request-id", Token);
        var error = await MetaProviderErrors.CreateAsync(response, Token, "responses", null, default);
        var serialized = JsonSerializer.Serialize(MetaProviderErrors.Find(error));
        foreach (var secret in new[] { Token, "aGVsbG8", "example.test", "private science data", "secret-value" })
            Assert.DoesNotContain(secret, serialized);
        Assert.DoesNotContain(Token, error.ToString());
        Assert.Null(MetaProviderErrors.Find(error)!.Code);
        Assert.Null(MetaProviderErrors.Find(error)!.ProviderRequestId);
    }

    [Fact]
    public async Task DoesNotStoreHtmlOrOversizedErrors()
    {
        foreach (var (body, state) in new[] {
            ("<html>private gateway data</html>", "NON_JSON_OMITTED"),
            (new string('x', 40000), "TOO_LARGE_OMITTED")
        }) {
            using var response = new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent(body) };
            var error = await MetaProviderErrors.CreateAsync(response, Token, "files", null, default);
            var detail = MetaProviderErrors.Find(error)!;
            Assert.Equal(state, detail.BodyReadState);
            Assert.Null(detail.Message);
            Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        }
    }
}
