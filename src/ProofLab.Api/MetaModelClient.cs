using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ProofLab.Science;

namespace ProofLab.Api;

public sealed record HypothesisRequest(string Experiment, string ResearchQuestion);
public sealed record MathResearchPlanRequest(string ProblemId);
public sealed record SegmentationRequest(string Concept, string ImageDataUrl);

public sealed class MetaModelClient(IHttpClientFactory clients, IConfiguration configuration)
{
    public bool Enabled =>
        string.Equals(configuration["Meta:AllowPaidRequests"], "true",
            StringComparison.OrdinalIgnoreCase);

    public bool HasKey => !string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable("MODEL_API_KEY"));

    public async Task<string> CreateHypothesisAsync(
        HypothesisRequest input, CancellationToken cancellationToken)
    {
        if (!Enabled || !HasKey) throw new InvalidOperationException("Meta API is disabled.");
        if (string.IsNullOrWhiteSpace(input.ResearchQuestion) ||
            input.ResearchQuestion.Length > 700)
            throw new ArgumentException("Research question must be 1-700 characters.");

        var payload = new
        {
            model = "muse-spark-1.3",
            messages = new[]
            {
                new {
                    role = "developer",
                    content = "You are a scientific experiment planner for adult researchers. " +
                        "State one falsifiable hypothesis, controlled variables, a safe and measurable " +
                        "procedure, a possible null outcome and uncertainty sources. " +
                        "Never invent measurements or claim that an experiment has occurred."
                },
                new {
                    role = "user",
                    content = $"Experiment: {input.Experiment}. Question: {input.ResearchQuestion}"
                }
            },
            reasoning_effort = "low",
            max_completion_tokens = 1000
        };
        using var request = Request("chat/completions", payload);
        using var response = await clients.CreateClient("meta")
            .SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Meta planning returned HTTP {(int)response.StatusCode}.");

        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return result.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString()
                ?? throw new FormatException("Meta response did not contain text.");
    }

    public async Task<string> CreateMathematicalStrategyAsync(
        MathProblem problem, CancellationToken cancellationToken)
    {
        if (!Enabled || !HasKey) throw new InvalidOperationException("Meta API is disabled.");

        // Muse Spark suggests research directions only. Never execute model output.
        var payload = new
        {
            model = "muse-spark-1.3",
            messages = new[]
            {
                new {
                    role = "developer",
                    content = "Act as a careful mathematical research planner. " +
                        "The input is an ALREADY KNOWN theorem, not a new discovery. " +
                        "Suggest up to three possible Lean 4 proof tactics or mathematical approaches. " +
                        "Clearly distinguish hypotheses from kernel-verified proofs. " +
                        "Never claim the problem has been solved by this session. " +
                        "Never request running arbitrary scripts, commands, or imported code. " +
                        "Use only rfl, omega, or simp as tactics for the current constrained evaluator."
                },
                new {
                    role = "user",
                    content = "Curated known theorem: " + problem.Id +
                        ". Statement: " + problem.StatementEnglish +
                        ". Lean goal: " + problem.LeanDeclaration
                }
            },
            reasoning_effort = "low",
            max_completion_tokens = 650
        };

        using var request = Request("chat/completions", payload);
        using var response = await clients.CreateClient("meta")
            .SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Meta math planning returned HTTP {(int)response.StatusCode}.");

        using var result = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        return result.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString()
                ?? throw new FormatException("Meta response did not contain text.");
    }

    public async Task<string> SegmentImageAsync(
        SegmentationRequest input, CancellationToken cancellationToken)
    {
        if (!Enabled || !HasKey) throw new InvalidOperationException("Meta API is disabled.");
        if (string.IsNullOrWhiteSpace(input.Concept) || input.Concept.Length > 90 ||
            input.Concept.Any(c => char.IsControl(c)))
            throw new ArgumentException("Provide a short object description.");
        var uri = input.ImageDataUrl;
        if (uri is null || uri.Length > 3_500_000 ||
            !(uri.StartsWith("data:image/png;base64,", StringComparison.Ordinal) ||
              uri.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) ||
              uri.StartsWith("data:image/webp;base64,", StringComparison.Ordinal)))
            throw new ArgumentException("Provide a small PNG/JPEG/WebP data URI.");

        var payload = new
        {
            model = "sam-3.1",
            input = new object[]
            {
                new
                {
                    type = "message",
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = input.Concept },
                        new { type = "input_image", image_url = uri }
                    }
                }
            },
            stream = false
        };
        using var request = Request("responses", payload);
        using var response = await clients.CreateClient("meta")
            .SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Meta segmentation returned HTTP {(int)response.StatusCode}.");

        // Raw SAM output is a special-token grammar. Do NOT pretend it is a decoded mask.
        // Mask decoding, identity tracking and visible overlay are separate future milestones.
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (text.Length > 1_000_000) throw new InvalidOperationException("Segmentation response too large.");
        return text;
    }

    private HttpRequestMessage Request(string path, object json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.meta.ai/v1/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", Environment.GetEnvironmentVariable("MODEL_API_KEY"));
        request.Content = JsonContent.Create(json);
        return request;
    }
}
