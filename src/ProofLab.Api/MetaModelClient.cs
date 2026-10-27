using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ProofLab.Science;

namespace ProofLab.Api;

public sealed record HypothesisRequest(string Experiment, string ResearchQuestion, string Language = "es");
public sealed record MathResearchPlanRequest(string ProblemId, string Language = "es");
public sealed record SegmentationRequest(string Concept, string ImageDataUrl);
public sealed record MetaStageDraftModelOutput(
    Dictionary<string, string> Fields, int? InputTokens, int? OutputTokens);

public sealed class MetaModelClient(IHttpClientFactory clients, MetaLiveAccess access)
{
    // Keep human-readable scientific prose in the UI-selected language.
    // Canonical math symbols, Lean tactics and API identifiers remain unchanged.
    public static string LocaleInstruction(string? language) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
            ? "Write the entire human-readable response in English. Keep theorem symbols and Lean tactics unchanged."
            : "Responde íntegramente en español. Conserva los símbolos matemáticos y las tácticas de Lean sin traducir.";

    public bool Enabled => access.Status().LiveCallsPermitted;
    public bool HasKey => access.KeyConfigured;

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
                        "Never invent measurements or claim that an experiment has occurred. " +
                        LocaleInstruction(input.Language)
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
        MathProblem problem, string language, CancellationToken cancellationToken)
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
                        "Use only rfl, omega, or simp as tactics for the current constrained evaluator. " +
                        LocaleInstruction(language)
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

    /// <summary>
    /// One gated paid call returning JSON-schema-constrained stage prose.
    /// It never executes tools, writes reports, or approves research.
    /// </summary>
    public async Task<MetaStageDraftModelOutput> CreateResearchDraftAsync(
        int stage, string language, ResearchField[] definitions,
        IReadOnlyDictionary<string, string> curatedEvidence,
        CancellationToken cancellationToken)
    {
        if (!Enabled || !HasKey)
            throw new InvalidOperationException("Meta Model API is not authorized for live calls.");
        if (stage is < 1 or > 5 || language is not ("es" or "en") ||
            definitions.Length is < 1 or > 6 ||
            definitions.Any(f => !curatedEvidence.ContainsKey(f.Key)))
            throw new ArgumentException("Invalid, ungrounded scientific draft request.");

        var keys = definitions.Select(f => f.Key).ToArray();
        var fieldSchema = keys.ToDictionary(
            key => key, _ => (object)new { type = "string" }, StringComparer.Ordinal);
        var payload = new
        {
            model = "muse-spark-1.3",
            messages = new[] {
                new {
                    role = "developer",
                    content = "You are a careful scientific RESEARCH DRAFTING ASSISTANT, NOT a proof kernel. "
                        + "Given only a curated mathematical catalog and verified observation summaries, "
                        + "draft clear, falsifiable research stage content. "
                        + "Never invent sources, proof IDs, test results or experiments. "
                        + "A bounded Goldbach observation is NOT a proof of the open conjecture. "
                        + "Any Lean proof in the context refers to a DIFFERENT KNOWN theorem. "
                        + "Never claim novel mathematical discovery, peer review, or human approval. "
                        + "Return only JSON fields matching the requested schema; no shell, no code, no tools. "
                        + LocaleInstruction(language)
                },
                new {
                    role = "user",
                    content = JsonSerializer.Serialize(new {
                        stage, language,
                        requiredFields = definitions.Select(x => new {
                            key = x.Key, guidance = language == "es" ? x.GuidanceEs : x.GuidanceEn,
                            minCharacters = x.MinimumCharacters
                        }),
                        verifiedOrCuratedBaseline = curatedEvidence
                    })
                }
            },
            reasoning_effort = "low",
            max_completion_tokens = 1800,
            response_format = new {
                type = "json_schema",
                json_schema = new {
                    name = "prooflab_scientific_stage_draft",
                    strict = true,
                    schema = new {
                        type = "object",
                        properties = fieldSchema,
                        required = keys,
                        additionalProperties = false
                    }
                }
            }
        };
        using var request = Request("chat/completions", payload);
        using var response = await clients.CreateClient("meta")
            .SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Meta scientific drafting returned HTTP {(int)response.StatusCode}.");
        using var raw = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var content = raw.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString()
            ?? throw new FormatException("Meta scientific draft did not contain JSON text.");
        if (content.Length > 26000)
            throw new FormatException("Meta returned an oversized research draft.");
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(content)
            ?? throw new FormatException("Meta returned an empty research draft.");
        if (fields.Count != keys.Length ||
            keys.Any(k => !fields.TryGetValue(k, out var s) || s is null))
            throw new FormatException("Meta returned unexpected research draft fields.");

        int? Token(string name) =>
            raw.RootElement.TryGetProperty("usage", out var u) &&
            u.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : null;
        return new MetaStageDraftModelOutput(fields,
            Token("prompt_tokens"), Token("completion_tokens"));
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
        access.ReserveOrThrow();
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.meta.ai/v1/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", Environment.GetEnvironmentVariable("MODEL_API_KEY"));
        request.Content = JsonContent.Create(json);
        return request;
    }
}
