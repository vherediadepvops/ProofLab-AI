using System.Text.Json.Serialization;
using ProofLab.Api;
using ProofLab.Science;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient("meta", client =>
    client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddScoped<MetaModelClient>();
builder.Services.AddSingleton<LeanProofVerifier>();
builder.Services.AddSingleton<ResearchWorkflowStore>();
builder.Services.AddSingleton<MathEvidenceWorkflow>();
builder.Services.AddSingleton<OpenConjectureEvidenceWorkflow>();
builder.Services.AddSingleton<OfflineMathResearchFlow>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(options => options.AddPolicy("HackathonLocal", policy =>
    policy.WithOrigins("http://localhost:4300", "http://127.0.0.1:4300")
          .AllowAnyMethod().AllowAnyHeader()));
var app = builder.Build();

app.UseCors("HackathonLocal");
app.MapGet("/api/health", (MetaModelClient meta) =>
    Results.Ok(new {
        project = "ProofLab AI — competition edition",
        startedLocal = "2026-10-26",
        appVersion = "2026.10.26-alpha",
        metaModelApiEnabled = meta.Enabled && meta.HasKey,
        science = new[] { "pendulum", "rolling-sphere" }
    }));

app.MapGet("/api/research/stages", () =>
    Results.Ok(ScientificMethodWorkflow.Stages));
app.MapPost("/api/research", async (
    ResearchCreateRequest input, ResearchWorkflowStore store, CancellationToken ct) =>
{
    try { return Results.Ok(await store.CreateAsync(input, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapGet("/api/research/{id}", async (
    string id, ResearchWorkflowStore store, CancellationToken ct) =>
{
    try { return Results.Ok(await store.GetAsync(id, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Research not found." }); }
});
app.MapPost("/api/research/{id}/stages/{stage:int}/draft", async (
    string id, int stage, ResearchDraftRequest input,
    ResearchWorkflowStore store, CancellationToken ct) =>
{
    try { return Results.Ok(await store.SaveDraftAsync(id, stage, input, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Research not found." }); }
});
app.MapPost("/api/research/{id}/stages/{stage:int}/approve", async (
    string id, int stage, ResearchApprovalRequest input,
    ResearchWorkflowStore store, MathEvidenceWorkflow evidence,
    CancellationToken ct) =>
{
    try {
        var project = await store.GetAsync(id, ct);
        await evidence.ValidateStageEvidenceAsync(project, stage, ct);
        return Results.Ok(await store.ApproveAsync(id, stage, input, ct));
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Research not found." }); }
});
app.MapGet("/api/research/{id}/reports/{stage:int}", async (
    string id, int stage, string? lang,
    ResearchWorkflowStore store, MathEvidenceWorkflow evidence,
    OpenConjectureEvidenceWorkflow finiteEvidence,
    OfflineMathResearchFlow offline, CancellationToken ct) =>
{
    try {
        var markdown = await store.ReportAsync(id, stage, lang ?? "es", ct);
        var project = await store.GetAsync(id, ct);
        if (project.Discipline == "Mathematics" && stage >= 4)
        {
            var spanish = !string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase);
            markdown += await evidence.RenderLedgerMarkdownAsync(id, spanish, ct);
            markdown += await finiteEvidence.RenderLedgerMarkdownAsync(id, spanish, ct);
            markdown += await offline.RenderLedgerMarkdownAsync(id, spanish, ct);
        }
        return Results.Text(markdown, "text/markdown; charset=utf-8");
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Research not found." }); }
});

app.MapGet("/api/research/{id}/math/attempts", async (
    string id, MathEvidenceWorkflow evidence, CancellationToken ct) =>
{
    try { return Results.Ok(await evidence.ListAsync(id, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Research not found." }); }
});
app.MapPost("/api/research/{id}/math/attempts", async (
    string id, MathVerificationRequest input, MathEvidenceWorkflow evidence,
    CancellationToken ct) =>
{
    try { return Results.Ok(await evidence.RunAttemptAsync(id, input, "MANUAL_CONTROL", ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Research not found." }); }
});
app.MapPost("/api/research/{id}/math/cycle", async (
    string id, MathCycleRequest input, MathEvidenceWorkflow evidence, CancellationToken ct) =>
{
    try { return Results.Ok(await evidence.RunDeterministicCycleAsync(id, input, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Research not found." }); }
});

app.MapGet("/api/research/{id}/paper", async (
    string id, string? lang, ResearchWorkflowStore store,
    MathEvidenceWorkflow evidence, OpenConjectureEvidenceWorkflow finiteEvidence,
    OfflineMathResearchFlow offline, CancellationToken ct) =>
{
    try {
        var markdown = await store.ScientificPaperAsync(id, lang ?? "es", ct);
        var project = await store.GetAsync(id, ct);
        if (project.Discipline == "Mathematics")
        {
            var spanish = !string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase);
            markdown += await evidence.RenderLedgerMarkdownAsync(id, spanish, ct);
            markdown += await finiteEvidence.RenderLedgerMarkdownAsync(id, spanish, ct);
            markdown += await offline.RenderLedgerMarkdownAsync(id, spanish, ct);
        }
        return Results.Text(markdown, "text/markdown; charset=utf-8");
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Research not found." }); }
});

app.MapGet("/api/math/problems", () =>
    Results.Ok(MathResearchCatalog.GetProblems()));

app.MapGet("/api/math/conjectures", () =>
    Results.Ok(OpenConjectureCatalog.GetAll()));
app.MapGet("/api/math/conjectures/{id}", (string id) =>
{
    try { return Results.Ok(OpenConjectureCatalog.Find(id)); }
    catch (ArgumentException ex) { return Results.NotFound(new { error = ex.Message }); }
});
app.MapGet("/api/research/{id}/math/finite-checks", async (
    string id, OpenConjectureEvidenceWorkflow evidence, CancellationToken ct) =>
{
    try { return Results.Ok(await evidence.ListAsync(id, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Research not found." }); }
});
app.MapPost("/api/research/{id}/math/conjectures/{conjectureId}/finite-check", async (
    string id, string conjectureId, GoldbachFiniteRequest input,
    OpenConjectureEvidenceWorkflow evidence, CancellationToken ct) =>
{
    try { return Results.Ok(await evidence.RunAsync(id, conjectureId, input, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Research not found." }); }
});
app.MapGet("/api/research/{id}/math/offline-events", async (
    string id, OfflineMathResearchFlow offline, CancellationToken ct) =>
{
    try { return Results.Ok(await offline.ListAsync(id, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Research not found." }); }
});
app.MapPost("/api/research/{id}/math/offline-cycle", async (
    string id, OfflineMathResearchFlow offline, CancellationToken ct) =>
{
    try { return Results.Ok(await offline.RunAsync(id, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Research not found." }); }
});
app.MapGet("/api/math/agent/capabilities", () =>
    Results.Ok(new {
        mode = "SCRIPTED_OFFLINE_VALIDATION_AVAILABLE_NO_MODEL_INFERENCE",
        offlineRehearsalAvailable = true,
        allowedTools = ResearchAgentToolPolicy.AllowedTools,
        maxToolCallsPerCycle = ResearchAgentToolPolicy.MaxToolCallsPerCycle,
        museLiveValidated = false,
        arbitraryLeanExecutionEnabled = false,
        paidRequestsRequireExplicitAuthorization = true
    }));
app.MapGet("/api/math/status", (LeanProofVerifier verifier) =>
    Results.Ok(new {
        mission = "Mathematical Discovery",
        phase = "CURATED_OPEN_CONJECTURE_FINITE_EXPLORATION",
        curatedOpenConjectures = OpenConjectureCatalog.GetAll().Count,
        arbitraryLeanSandboxValidated = false,
        leanAvailable = verifier.FindLean() is not null,
        allowsArbitraryLean = false,
        knownOpenProblemsAttempted = 0,
        novelProofsVerified = 0
    }));
app.MapPost("/api/math/verify", async (
    MathVerificationRequest input,
    LeanProofVerifier verifier,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await verifier.VerifyAsync(input, cancellationToken);
        await verifier.RecordAsync(result, cancellationToken);
        return Results.Ok(result);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/math/plan", async (
    MathResearchPlanRequest input, MetaModelClient meta,
    CancellationToken cancellationToken) =>
{
    if (!meta.Enabled || !meta.HasKey)
        return Results.Conflict(new { error = "Meta API disabled pending credit verification." });
    try
    {
        var problem = MathResearchCatalog.Find(input.ProblemId);
        var suggestion = await meta.CreateMathematicalStrategyAsync(problem, input.Language, cancellationToken);
        return Results.Ok(new { problemId = problem.Id, model = "muse-spark-1.3",
            suggestedApproaches = suggestion, verifiedProof = false,
            provenance = "META_MODEL_API_LIVE_OUTPUT_UNVERIFIED" });
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (HttpRequestException) { return Results.Problem("Meta math planning is unavailable.", statusCode: 502); }
    catch (FormatException) { return Results.Problem("Unexpected Meta response.", statusCode: 502); }
});

app.MapGet("/api/science/experiments", () =>
    Results.Ok(new[] {
        new { id = "Pendulum", label = "Péndulo", needs = "lengthMeters",
              metric = "period_seconds" },
        new { id = "RollingSphere", label = "Rampa con esfera", needs = "angleDegrees",
              metric = "acceleration_m_s2" }
    }));

app.MapPost("/api/science/predict", (ExperimentSetup input) =>
{
    try { return Results.Ok(ScientificModels.Predict(input)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/science/evaluate", (EvidenceRequest input) =>
{
    try { return Results.Ok(EvidenceAnalyzer.Analyze(input)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/science/synthetic/{kind}", (string kind) =>
{
    if (!Enum.TryParse<ExperimentKind>(kind, true, out var experiment))
        return Results.BadRequest(new { error = "Unknown experiment." });
    var spec = experiment == ExperimentKind.Pendulum
        ? new ExperimentSetup(experiment, LengthMeters: .5)
        : new ExperimentSetup(experiment, AngleDegrees: 15);
    var expected = ScientificModels.Predict(spec).ExpectedValue;
    Observation[] series;
    if (experiment == ExperimentKind.Pendulum)
        series = Enumerable.Range(0, 1100).Select(i =>
        {
            var t = i / 60.0;
            return new Observation(t,
                80 * Math.Cos(2 * Math.PI * t / (1.025 * expected)));
        }).ToArray();
    else
        series = Enumerable.Range(0, 180).Select(i =>
        {
            var t = i / 120.0;
            return new Observation(t,
                0.03 + 0.04 * t + 0.5 * (0.96 * expected) * t * t);
        }).ToArray();

    return Results.Ok(EvidenceAnalyzer.Analyze(
        new EvidenceRequest(spec, series, EvidenceOrigin.Simulated, "DEMO-ONLY")));
});

app.MapPost("/api/ai/plan", async (
    HypothesisRequest input, MetaModelClient meta, CancellationToken cancellationToken) =>
{
    if (!meta.Enabled || !meta.HasKey)
        return Results.Conflict(new { error = "Meta API disabled pending credit verification." });
    try
    {
        var output = await meta.CreateHypothesisAsync(input, cancellationToken);
        return Results.Ok(new { model = "muse-spark-1.3", generatedPlan = output,
            provenance = "META_MODEL_API_LIVE_OUTPUT" });
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (HttpRequestException) { return Results.Problem("Meta planning is unavailable.", statusCode: 502); }
    catch (FormatException) { return Results.Problem("Unexpected model output.", statusCode: 502); }
});

app.MapPost("/api/ai/segment-frame", async (
    SegmentationRequest input, MetaModelClient meta, CancellationToken cancellationToken) =>
{
    if (!meta.Enabled || !meta.HasKey)
        return Results.Conflict(new { error = "SAM API disabled pending credit verification." });
    try
    {
        var raw = await meta.SegmentImageAsync(input, cancellationToken);
        return Results.Ok(new { model = "sam-3.1", rawResponseJson = raw,
            decodedMasks = false, provenance = "META_MODEL_API_RAW_OUTPUT" });
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (HttpRequestException) { return Results.Problem("SAM 3.1 is unavailable.", statusCode: 502); }
});

app.Run();
