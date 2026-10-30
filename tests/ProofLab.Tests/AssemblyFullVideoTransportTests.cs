using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProofLab.Api;

namespace ProofLab.Tests;

/// <summary>Production full-video transport, with every HTTP operation intercepted in memory.</summary>
public sealed class AssemblyFullVideoTransportTests
{
    private const string Token = "LLM_SYNTHETIC_FULL_VIDEO_TEST_ONLY";
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly string ProviderRaw = JsonSerializer.Serialize(new {
        id = "resp-synthetic-full-video", model = AssemblyPolicy.Model, status = "completed",
        output = new[] { new { type = "message", content = new[] {
            new { type = "output_text", text = "{\"target\":{\"status\":\"AMBIGUOUS\"}}" }
        }}},
        usage = new { input_tokens = 1000, output_tokens = 100 }
    });

    private sealed class Clip : IDisposable
    {
        private readonly string parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "assembly-full-video-test-data"));
        public string Root { get; }
        public string PathName { get; }
        public byte[] Bytes { get; }
        public byte[][] Images { get; }
        public KnotFrame[] Anchors { get; }
        public AssemblyVideoInput Input { get; }
        public Clip(int count=3)
        {
            Root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            PathName = Path.Combine(Root, "private-original-name.mp4");
            Bytes = Enumerable.Range(0, 131073).Select(i => (byte)(i * 17 % 251)).ToArray();
            Encoding.ASCII.GetBytes("SYNTHETIC_MP4_NOT_A_REAL_VIDEO").CopyTo(Bytes, 0);
            File.WriteAllBytes(PathName, Bytes);
            Images = Enumerable.Range(0, count).Select(i => Enumerable.Repeat((byte)(i + 21), 200 + i * 17).ToArray()).ToArray();
            Anchors = new[] {
                new KnotFrame(30, .5, 1080, 1920, Hash(Images[0])),
                new KnotFrame(300, 5, 1080, 1920, Hash(Images[1])),
                new KnotFrame(570, 9.5, 1080, 1920, Hash(Images[2]))
            };
            if(count==50)Anchors=Enumerable.Range(0,50).Select(i=>new KnotFrame(i*12,i*.2,1080,1920,Hash(Images[i]))).ToArray();
            Input = new(new string('a', 64), Hash(Bytes), Bytes.LongLength, "", 600, new string('b', 64),count==50?"FULL_VIDEO_REMUX_WITH_50_NATIVE_ANCHORS":"FULL_VIDEO_REMUX_WITH_3_NATIVE_ANCHORS");
        }
        private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
            if (!resolved.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe test cleanup path.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }

    private sealed record RequestSnapshot(string Method, string Path, string? Json);
    private sealed class FakeHttp : HttpMessageHandler, IHttpClientFactory
    {
        public readonly List<RequestSnapshot> Requests = [];
        public readonly List<string> Events = [];
        public readonly List<AssemblyVideoInput> Uploaded = [];
        public byte[]? UploadedBytes;
        public string? UploadMime;
        public string? UploadPurpose;
        public string? UploadFilename;
        public int CountTokens = 1000;
        public string? FailPath;
        public HttpStatusCode FailCode = HttpStatusCode.ServiceUnavailable;
        public bool FailCallback;
        public string Raw = ProviderRaw;
        public HttpClient CreateClient(string name) => new(this, false);
        public Task UploadedCallback(AssemblyVideoInput input)
        {
            Events.Add("uploaded-callback");
            Uploaded.Add(input);
            if (FailCallback) throw new IOException("Synthetic receipt persistence failure.");
            return Task.CompletedTask;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Assert.Equal("https", request.RequestUri.Scheme);
            Assert.Equal("api.meta.ai", request.RequestUri.Host);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(Token, request.Headers.Authorization.Parameter);
            string? body = null;
            if (request.Content is MultipartFormDataContent multipart)
            {
                foreach (var part in multipart)
                {
                    var name = part.Headers.ContentDisposition!.Name!.Trim('"');
                    if (name == "purpose") UploadPurpose = await part.ReadAsStringAsync(ct);
                    if (name == "file")
                    {
                        UploadedBytes = await part.ReadAsByteArrayAsync(ct);
                        UploadMime = part.Headers.ContentType?.MediaType;
                        UploadFilename = part.Headers.ContentDisposition.FileName?.Trim('"');
                    }
                }
            }
            else if (request.Content is not null) body = await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new(request.Method.Method, path, body));
            Events.Add(request.Method.Method + " " + path);
            if (path == FailPath && request.Method != HttpMethod.Delete)
                return new(FailCode) { Content = new StringContent("Synthetic upstream body must not leak " + Token) };
            if (request.Method == HttpMethod.Delete)
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"deleted\":true}") };
            if (path == "/v1/files")
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"file-synthetic-full-video\"}") };
            if (path == "/v1/responses/input_tokens")
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { input_tokens = CountTokens })) };
            if (path == "/v1/responses")
                return new(HttpStatusCode.OK) { Content = new StringContent(Raw) };
            throw new InvalidOperationException("Unexpected fake endpoint: " + path);
        }
    }

    private static Task<AssemblyVideoProviderResult> Send(Clip clip, FakeHttp http, double budget = .25) =>
        new AssemblyMetaTransport(http).LocateVideoGroundingAsync(clip.PathName, clip.Input,
            clip.Anchors, clip.Images, Token, budget, http.UploadedCallback, Ct);

    [Fact]
    public async Task SendsEntireMp4AndThreeNativeAnchorsWithOriginalIndices()
    {
        using var clip = new Clip(); using var http = new FakeHttp();
        var result = await Send(clip, http);
        Assert.Equal(clip.Bytes, http.UploadedBytes);
        Assert.Equal("video/mp4", http.UploadMime);
        Assert.Equal("user_data", http.UploadPurpose);
        Assert.DoesNotContain("private-original-name", http.UploadFilename ?? "");
        var uploaded = Assert.Single(http.Uploaded);
        Assert.Equal("file-synthetic-full-video", uploaded.ProviderFileId);
        Assert.Equal(clip.Input with { ProviderFileId = uploaded.ProviderFileId }, uploaded);
        Assert.Equal(uploaded, result.VideoInput);
        var inference = Assert.Single(http.Requests, r => r.Method == "POST" && r.Path == "/v1/responses");
        using var doc = JsonDocument.Parse(inference.Json!);
        var parts = doc.RootElement.GetProperty("input")[0].GetProperty("content").EnumerateArray().ToArray();
        var video = Assert.Single(parts, p => p.GetProperty("type").GetString() == "input_file");
        Assert.Equal(uploaded.ProviderFileId, video.GetProperty("file_id").GetString());
        Assert.DoesNotContain(parts, p => p.TryGetProperty("file_data", out _));
        var pictures = parts.Where(p => p.GetProperty("type").GetString() == "input_image").ToArray();
        Assert.Equal(3, pictures.Length);
        for (var i = 0; i < 3; i++)
        {
            var url = pictures[i].GetProperty("image_url").GetString()!;
            Assert.Equal("data:image/jpeg;base64," + Convert.ToBase64String(clip.Images[i]), url);
            var pictureIndex = Array.FindIndex(parts, p => p.GetRawText() == pictures[i].GetRawText());
            Assert.True(pictureIndex > 0);
            var label = parts[pictureIndex - 1];
            Assert.Equal("input_text", label.GetProperty("type").GetString());
            var text = label.GetProperty("text").GetString()!;
            Assert.Contains(clip.Anchors[i].Index.ToString(CultureInfo.InvariantCulture), text);
            Assert.Contains(clip.Anchors[i].TimeSeconds.ToString(CultureInfo.InvariantCulture), text);
            Assert.Contains("1080", text); Assert.Contains("1920", text);
        }
        Assert.Equal(ProviderRaw, result.Response.Raw);
        AssemblyMetaTransport.ValidateReceipt(result.Response.Raw, result.Response.ProviderResponseId,
            result.Response.InputTokens, result.Response.OutputTokens);
        Assert.DoesNotContain(Token, JsonSerializer.Serialize(result));
        Assert.DoesNotContain(clip.PathName, inference.Json!);
        Assert.True(http.Events.IndexOf("uploaded-callback") < http.Events.IndexOf("POST /v1/responses"));
    }

    [Fact]
    public async Task SkipsOptionalTokenCountAndRetainsUploadedFileWithStrictSchema()
    {
        using var clip = new Clip(); using var http = new FakeHttp();
        await Send(clip, http);
        Assert.DoesNotContain(http.Requests, r => r.Path == "/v1/responses/input_tokens" || r.Method=="DELETE");
        var infer = Assert.Single(http.Requests, r => r.Method == "POST" && r.Path == "/v1/responses");
        using var doc = JsonDocument.Parse(infer.Json!); var root = doc.RootElement;
        Assert.Equal(AssemblyPolicy.Model, root.GetProperty("model").GetString());
        Assert.Equal(AssemblyPolicy.MaximumOutputTokens, root.GetProperty("max_output_tokens").GetInt32());
        Assert.False(root.TryGetProperty("tools", out _));
        Assert.False(root.TryGetProperty("previous_response_id", out _));
        var format = root.GetProperty("text").GetProperty("format");
        Assert.True(format.GetProperty("strict").GetBoolean());
        var schema = format.GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains(schema.GetProperty("required").EnumerateArray(), p => p.GetString() == "target");
        Assert.Equal(AssemblyPolicy.VideoGroundingPrompt,
            root.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendsFiftyLabeledImagesAndFullVideoWithoutCountOrDelete() {
        using var clip=new Clip(50);using var http=new FakeHttp();
        await Send(clip,http);
        Assert.Equal(new[]{"POST /v1/files","uploaded-callback","POST /v1/responses"},http.Events);
        using var doc=JsonDocument.Parse(http.Requests.Single(r=>r.Path=="/v1/responses").Json!);
        var input=doc.RootElement.GetProperty("input")[0].GetProperty("content").EnumerateArray().ToArray();
        Assert.Equal(50,input.Count(x=>x.GetProperty("type").GetString()=="input_image"));
        Assert.Single(input,x=>x.GetProperty("type").GetString()=="input_file");
        Assert.Equal("low",doc.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.True(doc.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
    }

    [Theory]
    [InlineData("/v1/files")]
    public async Task UploadFailureIsExplicitlyNotSubmittedAndNeverRetries(string path)
    {
        using var clip = new Clip(); using var http = new FakeHttp { FailPath = path };
        var error = await Assert.ThrowsAsync<AssemblyNoInferenceSentException>(() => Send(clip, http));
        Assert.DoesNotContain(Token, error.ToString());
        Assert.Single(http.Requests, r => r.Method == "POST" && r.Path == path);
        Assert.DoesNotContain(http.Requests, r => r.Path == "/v1/responses");
    }

    [Fact]
    public async Task CannotSubmitInferenceBeforeUploadedReceiptIsPersisted()
    {
        using var clip = new Clip(); using var http = new FakeHttp { FailCallback = true };
        await Assert.ThrowsAsync<AssemblyNoInferenceSentException>(() => Send(clip, http));
        Assert.Single(http.Uploaded);
        Assert.DoesNotContain(http.Requests, r => r.Path.StartsWith("/v1/responses", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InferenceFailureRemainsUncertainAndIsNotAutomaticallyRetried()
    {
        using var clip = new Clip(); using var http = new FakeHttp { FailPath = "/v1/responses" };
        var error = await Assert.ThrowsAnyAsync<HttpRequestException>(() => Send(clip, http));
        Assert.DoesNotContain(Token, error.ToString());
        Assert.Single(http.Requests, r => r.Method == "POST" && r.Path == "/v1/responses");
        Assert.DoesNotContain(http.Requests, r => r.Path == "/v1/responses/input_tokens");
    }
}
