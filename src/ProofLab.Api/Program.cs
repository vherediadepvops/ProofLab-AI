using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using ProofLab.Api;
using ProofLab.Science;

var builder = WebApplication.CreateBuilder(args);
// Long SAM video streams may exceed two minutes before completion.
// This is a timeout, NOT permission to make any additional paid requests.
// Model calls still require explicit owner approval and budget gates.
builder.Services.AddHttpClient("meta", client =>
    client.Timeout = TimeSpan.FromMinutes(3));
builder.Services.AddHttpClient("sam-video", client =>
    client.Timeout = TimeSpan.FromMinutes(30));
builder.Services.AddSingleton<MetaCallSafetyBudget>();
builder.Services.AddSingleton<MetaLiveAccess>();
builder.Services.AddScoped<MetaModelClient>();
builder.Services.AddSingleton<LeanProofVerifier>();
builder.Services.AddSingleton<ResearchWorkflowStore>();
builder.Services.AddSingleton<GuidedResearchDrafts>();
builder.Services.AddScoped<MuseResearchDrafts>();
builder.Services.AddSingleton<ScientificMemoryStore>();
builder.Services.AddSingleton<MathEvidenceWorkflow>();
builder.Services.AddSingleton<MemoryGuidedMathFlow>();
builder.Services.AddSingleton<OpenConjectureEvidenceWorkflow>();
builder.Services.AddSingleton<OfflineMathResearchFlow>();
builder.Services.AddSingleton<PendulumResearchStore>();
builder.Services.AddSingleton<VideoMediaDecoder>();
builder.Services.AddSingleton<PendulumVideoWorkflow>();
builder.Services.AddSingleton<PendulumDirectorWorkflow>();
builder.Services.AddSingleton<PendulumDirectorKinematicsStore>();
builder.Services.AddSingleton<SamVideoMetaTransport>();
builder.Services.AddSingleton<PendulumSamCache>();
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

app.MapGet("/api/meta/readiness", (MetaLiveAccess meta) =>
{
    var x = meta.Status();
    return Results.Ok(new {
        provider = "Meta Model API",
        museModel = "muse-spark-1.3",
        samModel = "sam-3.1",
        apiBaseUrl = "https://api.meta.ai/v1",
        apiKeyConfigured = x.ApiKeyConfigured,
        creditsConfirmedByOwner = x.CreditsConfirmedByOwner,
        ownerApprovedPaidRequests = x.OwnerApprovedPaidRequests,
        paidRequestsSwitchEnabled = x.PaidRequestsSwitchEnabled,
        liveCallsPermitted = x.LiveCallsPermitted,
        usedRequests = x.RequestsUsed,
        remainingRequests = x.RequestsRemaining,
        maxRequestsPerProcess = x.MaxRequestsPerProcess,
        promotionalBalanceVerifiedAutomatically = false,
        museInferenceValidatedLive = false,
        samSegmentationValidatedLive = false,
        keyLocation = "Backend-only MODEL_API_KEY environment variable"
    });
});
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
// Read-only server-curated draft preview. Never saves or approves the report.
app.MapGet("/api/research/{id}/stages/{stage:int}/guided-draft", async (
    string id, int stage, string? lang,
    GuidedResearchDrafts suggestions, CancellationToken ct) =>
{
    try { return Results.Ok(await suggestions.PrepareAsync(id, stage, lang ?? "es", ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Research not found." }); }
});
// This explicit user-triggered endpoint is billable ONLY after four independent
// safety gates; it returns an unverified draft without saving or approving it.
app.MapPost("/api/research/{id}/stages/{stage:int}/muse-draft", async (
    string id, int stage, string? lang, MuseResearchDrafts muse,
    MetaLiveAccess access, CancellationToken ct) =>
{
    if (!access.Status().LiveCallsPermitted)
        return Results.Conflict(new {
            error = "Meta live calls are blocked pending key, owner-confirmed credits, permission and request budget."
        });
    try { return Results.Ok(await muse.PrepareAsync(id, stage, lang ?? "es", ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Research not found." }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (HttpRequestException ex) { return MetaSafeDiagnostics.ProviderFailure(ex, "muse-spark-1.3"); }
    catch (FormatException) { return Results.Problem("Meta returned invalid research draft JSON.", statusCode: 502); }
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
    OfflineMathResearchFlow offline, MemoryGuidedMathFlow guided,
    CancellationToken ct) =>
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
            markdown += await guided.RenderMarkdownAsync(id, spanish, ct);
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
    OfflineMathResearchFlow offline, MemoryGuidedMathFlow guided,
    CancellationToken ct) =>
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
            markdown += await guided.RenderMarkdownAsync(id, spanish, ct);
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
app.MapGet("/api/math/memory/{problemId}", async (
    string problemId, ScientificMemoryStore store, CancellationToken ct) =>
{
    try { return Results.Ok(await store.RecallAsync(problemId, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapGet("/api/research/{id}/math/memory-cycles", async (
    string id, MemoryGuidedMathFlow guided, CancellationToken ct) =>
{
    try { return Results.Ok(await guided.ListAsync(id, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Research not found." }); }
});
app.MapPost("/api/research/{id}/math/memory-cycle", async (
    string id, MemoryCycleRequest input, MemoryGuidedMathFlow guided,
    CancellationToken ct) =>
{
    try { return Results.Ok(await guided.RunAsync(id, input, ct)); }
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
    catch (HttpRequestException ex) { return MetaSafeDiagnostics.ProviderFailure(ex, "muse-spark-1.3"); }
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

// Interactive theoretical workbench (zero Meta tokens). It never consumes
// observational video coordinates, so predicted and measured curves stay independent.
app.MapPost("/api/science/pendulum/mathematical-model",
    (PendulumMathematicalRequest request) =>
{
    try { return Results.Ok(PendulumMathematicalModel.Predict(request)); }
    catch(ArgumentException ex){return Results.BadRequest(new {error=ex.Message});}
});

// No model calls: register the 50→80 cm theoretical prediction before submitting
// either coordinate series. Not evidence that the videos were filmed afterwards.
// Main flagship flow: one initial video is analysed BEFORE a locally proposed
// follow-up is timestamped, then a second video is uploaded for comparison.
// No Meta model calls here; never present local control logic as autonomous AI.
app.MapPost("/api/science/director/pendulum/sessions", async (
    DirectorStartRequest request, PendulumDirectorWorkflow director, CancellationToken ct) =>
{
    try { return Results.Ok(await director.StartAsync(request,ct)); }
    catch(ArgumentException ex){return Results.BadRequest(new {error=ex.Message});}
});
app.MapGet("/api/science/director/pendulum/sessions/{id}", async (
    string id, PendulumDirectorWorkflow director, CancellationToken ct) =>
{
    try { return Results.Ok(await director.ReadAsync(id,ct)); }
    catch(ArgumentException ex){return Results.BadRequest(new {error=ex.Message});}
    catch(FileNotFoundException){return Results.NotFound(new {error="Session not found."});}
    catch(InvalidDataException){return Results.Conflict(new {error="Research integrity mismatch."});}
});
app.MapPost("/api/science/director/pendulum/sessions/{id}/video/{role}",
    async (string id,string role,string? filename,HttpRequest request,
        PendulumDirectorWorkflow director,CancellationToken ct) =>
{
    if(request.ContentLength is null)
        return Results.BadRequest(new {error="Content-Length required."});
    try { return Results.Ok(await director.UploadAsync(id,role,filename ?? "",
        request.Body,request.ContentLength.Value,ct)); }
    catch(ArgumentException ex){return Results.BadRequest(new {error=ex.Message});}
    catch(FileNotFoundException){return Results.NotFound(new {error="Session not found."});}
    catch(InvalidOperationException ex){return Results.Problem(ex.Message,statusCode:503);}
})
.WithMetadata(new RequestSizeLimitAttribute(250_000_000));
app.MapPost("/api/science/director/pendulum/sessions/{id}/review-baseline",
    async (string id,DirectorHumanReviewRequest request,
        PendulumDirectorWorkflow director,CancellationToken ct) =>
{
    try {return Results.Ok(await director.ConfirmBaselineAsync(id,
        request.BobConfirmed,ct));}
    catch(ArgumentException ex){return Results.BadRequest(new {error=ex.Message});}
    catch(FileNotFoundException){return Results.NotFound(new {error="Session not found."});}
});
app.MapPost("/api/science/director/pendulum/sessions/{id}/propose-local-control",
    async (string id,PendulumDirectorWorkflow director,CancellationToken ct) =>
{
    try {return Results.Ok(await director.ProposeLocalControlAsync(id,ct));}
    catch(ArgumentException ex){return Results.BadRequest(new {error=ex.Message});}
    catch(FileNotFoundException){return Results.NotFound(new {error="Session not found."});}
});
app.MapPost("/api/science/director/pendulum/sessions/{id}/compare",
    async (string id,PendulumDirectorWorkflow director,CancellationToken ct) =>
{
    try {return Results.Ok(await director.CompareAsync(id,ct));}
    catch(ArgumentException ex){return Results.BadRequest(new {error=ex.Message});}
    catch(FileNotFoundException){return Results.NotFound(new {error="Session not found."});}
});
app.MapPost("/api/science/director/pendulum/sessions/{id}/compare-single",
    async(string id,PendulumDirectorWorkflow director,CancellationToken ct) =>
{
    try {return Results.Ok(await director.CompareSingleAsync(id,ct));}
    catch(ArgumentException ex){return Results.BadRequest(new {error=ex.Message});}
    catch(FileNotFoundException){return Results.NotFound(new {error="Session not found."});}
});
// Pivot selection and derived angular measurements are LOCAL and stored as
// append-only private revisions tied to the original video SHA-256.
app.MapPost("/api/science/director/pendulum/sessions/{id}/kinematics/{role}",
    async (string id, string role, DirectorPivotRequest request,
        PendulumDirectorKinematicsStore kinematics, CancellationToken ct) =>
{
    try { return Results.Ok(await kinematics.AnalyzeAsync(id, role, request, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Video or study missing." }); }
    catch (InvalidDataException) { return Results.Conflict(new { error = "Evidence provenance mismatch." }); }
});
app.MapGet("/api/science/director/pendulum/sessions/{id}/kinematics/{role}",
    async (string id, string role, PendulumDirectorKinematicsStore kinematics,
        CancellationToken ct) =>
{
    try { return Results.Ok(await kinematics.ReadLatestAsync(id, role, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "No valid pivot revision found." }); }
    catch (InvalidDataException) { return Results.Conflict(new { error = "Evidence provenance mismatch." }); }
});

// Stage 3: provider-specific SAM observation archive. A cache HIT never
// reserves credits or calls Meta. Only an explicit localhost POST can ask for
// a new inference, after checking the backend payment gates and source SHA.
// Imported JSON from the offline viewer is never accepted as provider truth.
app.MapGet("/api/science/director/pendulum/sessions/{id}/sam/{role}/status",
    async (string id,string role,PendulumSamCache sam,
        HttpContext context,CancellationToken ct) =>
{
    if(context.Connection.RemoteIpAddress is not System.Net.IPAddress address ||
       !System.Net.IPAddress.IsLoopback(address))
        return Results.StatusCode(403);
    try {return Results.Ok(await sam.StatusAsync(id,role,ct));}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
    catch(FileNotFoundException){return Results.NotFound(new{error="Original video unavailable."});}
    catch(InvalidDataException){return Results.Conflict(new{error="Source or cache integrity mismatch."});}
});
app.MapGet("/api/science/director/pendulum/sessions/{id}/sam/{role}/preflight",
    async (string id,string role,PendulumSamCache sam,
        HttpContext context,CancellationToken ct) =>
{
    if(context.Connection.RemoteIpAddress is not System.Net.IPAddress address ||
       !System.Net.IPAddress.IsLoopback(address))
        return Results.StatusCode(403);
    try {return Results.Ok(await sam.PreflightAsync(id,role,ct));}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
    catch(FileNotFoundException){return Results.NotFound(new{error="Original video unavailable."});}
    catch(InvalidDataException){return Results.Conflict(new{error="Video frame timing integrity mismatch."});}
    catch(InvalidOperationException ex){return Results.Problem(ex.Message,statusCode:503);}
});
app.MapGet("/api/science/director/pendulum/sessions/{id}/sam/{role}/evidence",
    async (string id,string role,PendulumSamCache sam,
        HttpContext context,CancellationToken ct) =>
{
    if(context.Connection.RemoteIpAddress is not System.Net.IPAddress address ||
       !System.Net.IPAddress.IsLoopback(address))
        return Results.StatusCode(403);
    try {return Results.Ok(await sam.ReadArchiveAsync(id,role,ct));}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
    catch(FileNotFoundException){return Results.NotFound(new{error="No authentic cached SAM evidence."});}
    catch(InvalidDataException){return Results.Conflict(new{error="SAM cache integrity mismatch."});}
});
app.MapPost("/api/science/director/pendulum/sessions/{id}/sam/{role}/analyze",
    async (string id,string role,SamVideoRunRequest input,
        PendulumSamCache sam,HttpContext context,CancellationToken ct) =>
{
    if(context.Connection.RemoteIpAddress is not System.Net.IPAddress address ||
       !System.Net.IPAddress.IsLoopback(address))
        return Results.StatusCode(403);
    try {
        var result=await sam.RunOnceAsync(id,role,input,ct);
        return Results.Ok(result.State);
    }
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
    catch(FileNotFoundException){return Results.NotFound(new{error="Original video unavailable."});}
    catch(InvalidDataException){return Results.Conflict(new{error="SAM scientific evidence integrity mismatch."});}
    catch(InvalidOperationException ex){return Results.Conflict(new{error=ex.Message});}
    catch(HttpRequestException ex){return MetaSafeDiagnostics.ProviderFailure(ex,"sam-3.1");}
    catch(FormatException){return Results.Problem("Unexpected or incomplete SAM provider output.",statusCode:502);}
});
// Owner-facing one-shot token entry. It is NOT a payment-gate override.
// The browser password field is sent once to this loopback-only backend
// for an already-authorized process; the token is never persisted or logged.
// A browser-supplied key alone cannot enable Meta:AllowPaidRequests.
app.MapPost("/api/science/director/pendulum/sessions/{id}/sam/{role}/manual-one-shot",
    async (string id,string role,SamManualOneShotRequest input,
        PendulumSamCache sam,MetaLiveAccess meta,HttpContext context,
        CancellationToken ct) =>
{
    var remote=context.Connection.RemoteIpAddress;
    var host=context.Request.Host.Host;
    var origin=context.Request.Headers.Origin.ToString();
    if(remote is null || !System.Net.IPAddress.IsLoopback(remote) ||
       (host!="127.0.0.1" && host!="localhost") ||
       (origin!="http://127.0.0.1:4300" &&
        origin!="http://localhost:4300") ||
       context.Request.Headers["X-ProofLab-Manual-Action"]!="sam-one-shot")
        return Results.StatusCode(403);
    context.Response.Headers.CacheControl="no-store";
    context.Response.Headers.Pragma="no-cache";
    if(context.Request.ContentLength is > 4096)
        return Results.BadRequest(new{error="One-shot request exceeded limit."});
    if(!SamManualApiTokenPolicy.Accepts(input.ModelApiToken))
        return Results.BadRequest(new{error="Invalid Meta key format. Key not stored."});
    // All original credit/consent, process budget and previous-attempt
    // locks remain mandatory; never set config switches here.
    if(!meta.Status().LiveCallsPermitted)
        return Results.Conflict(new{error="Backend paid-access gates remain disabled."});
    try {
        var result=await sam.RunOnceAsync(id,role,
            input.ToSafeRunRequest(),ct,input.ModelApiToken);
        return Results.Ok(result.State);
    }
    catch(ArgumentException){return Results.BadRequest(new{error="Invalid local SAM request."});}
    catch(FileNotFoundException){return Results.NotFound(new{error="Original video unavailable."});}
    catch(InvalidDataException){return Results.Conflict(new{error="Video or evidence integrity mismatch."});}
    catch(InvalidOperationException){return Results.Conflict(new{error="Manual attempt blocked by safety gates or prior attempt."});}
    catch(HttpRequestException ex){return MetaSafeDiagnostics.ProviderFailure(ex,"sam-3.1");}
    catch(FormatException){return Results.Problem("Incomplete or malformed provider output.",statusCode:502);}
});
// This is private browser review media. Real SAM uses the SHA-checked original.
app.MapGet("/api/science/director/pendulum/sessions/{id}/poster/{role}",
    async(string id,string role,PendulumDirectorWorkflow director,CancellationToken ct) =>
{
    try{return Results.File(await director.PosterAsync(id,role,ct),"image/jpeg");}
    catch(ArgumentException ex){return Results.BadRequest(new {error=ex.Message});}
    catch(FileNotFoundException){return Results.NotFound(new {error="Poster not found."});}
});

app.MapGet("/api/science/director/pendulum/sessions/{id}/preview/{role}",
    async(string id,string role,PendulumDirectorWorkflow director,CancellationToken ct) =>
{
    try {
        var proxy=await director.PreviewAsync(id,role,ct);
        return Results.File(proxy,"video/mp4",enableRangeProcessing:true);
    }
    catch(ArgumentException ex){return Results.BadRequest(new {error=ex.Message});}
    catch(FileNotFoundException){return Results.NotFound(new {error="Review proxy unavailable."});}
});

app.MapPost("/api/science/pendulum/preregister", async (
    PendulumPreregisterRequest request, PendulumResearchStore study,
    CancellationToken ct) =>
{
    try { return Results.Ok(await study.PreregisterAsync(request, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapGet("/api/science/pendulum/preregistrations/{id}", async (
    string id, PendulumResearchStore study, CancellationToken ct) =>
{
    try { return Results.Ok(await study.GetAsync(id, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Study not found." }); }
    catch (InvalidDataException) { return Results.Conflict(new { error = "Integrity check failed." }); }
});
app.MapPost("/api/science/pendulum/preregistrations/{id}/compare", async (
    string id, PendulumCompareRequest request, PendulumResearchStore study,
    CancellationToken ct) =>
{
    try { return Results.Ok(await study.CompareAsync(id, request, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Study not found." }); }
    catch (InvalidDataException) { return Results.Conflict(new { error = "Integrity check failed." }); }
});

// Local upload of original phone media. The binary contents stay private and
// never reach Muse/SAM or Git. This prototype is local-only, not a public
// multi-user upload service. Uploaded footage must not contain people.
app.MapGet("/api/science/pendulum/video-formats", (PendulumVideoWorkflow workflow) =>
    Results.Ok(new {
        decoderAvailable = workflow.DecoderAvailable,
        supportedExtensions = VideoClipPolicy.AllowedExtensions.OrderBy(x => x),
        supportedCodecs = VideoClipPolicy.AllowedCodecs.OrderBy(x => x),
        maximumVideoBytes = VideoClipPolicy.MaximumBytes,
        maximumDurationSeconds = VideoClipPolicy.MaximumDurationSeconds,
        maxTakesPerCondition = 3,
        processingMethod = "LOCAL_FFMPEG_AND_EXPERIMENTAL_TRACKER_NOT_SAM",
        metaCalls = 0,
        requiresHumanVideoVerification = true
    }));
app.MapPost("/api/science/pendulum/preregistrations/{id}/video/{condition}/{take:int}",
    async (string id, string condition, int take, string? filename,
        HttpRequest request, PendulumVideoWorkflow workflow, CancellationToken ct) =>
{
    if (request.ContentLength is null)
        return Results.BadRequest(new { error = "Content-Length is required." });
    try {
        return Results.Ok(await workflow.UploadAsync(id, condition, take,
            filename ?? "", request.Body, request.ContentLength.Value, ct));
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Study not found." }); }
    catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: 503); }
    catch (InvalidDataException) { return Results.BadRequest(new { error = "Invalid or corrupt video file." }); }
})
.WithMetadata(new RequestSizeLimitAttribute(250_000_000));
app.MapGet("/api/science/pendulum/preregistrations/{id}/videos",
    async (string id, PendulumVideoWorkflow workflow, CancellationToken ct) =>
{
    try { return Results.Ok(await workflow.ListAsync(id, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Study not found." }); }
    catch (InvalidDataException) { return Results.Conflict(new { error = "Video ledger check failed." }); }
});
// Recompute a robust period from saved coordinates, with append-only
// spectral-v1 metadata. Never changes the uploaded original or calls Meta.
app.MapPost("/api/science/pendulum/preregistrations/{id}/videos/{videoId}/reanalyze",
    async (string id, string videoId, PendulumVideoWorkflow workflow,
        CancellationToken ct) =>
{
    try { return Results.Ok(await workflow.ReanalyzeAsync(id, videoId, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Video not found." }); }
    catch (InvalidDataException) { return Results.Conflict(new { error = "Review provenance check failed." }); }
});
app.MapGet("/api/science/pendulum/preregistrations/{id}/videos/{videoId}/poster",
    async (string id, string videoId, PendulumVideoWorkflow workflow, CancellationToken ct) =>
{
    try {
        var jpeg = await workflow.ReadPosterAsync(id, videoId, ct);
        return Results.File(jpeg, "image/jpeg");
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Poster not available." }); }
});
app.MapPost("/api/science/pendulum/preregistrations/{id}/compare-videos",
    async (string id, PendulumVideoComparisonRequest request,
        PendulumVideoWorkflow workflow, CancellationToken ct) =>
{
    try { return Results.Ok(await workflow.CompareAsync(id, request, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "Study not found." }); }
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
    catch (HttpRequestException ex) { return MetaSafeDiagnostics.ProviderFailure(ex, "muse-spark-1.3"); }
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
    catch (HttpRequestException ex) { return MetaSafeDiagnostics.ProviderFailure(ex, "sam-3.1"); }
});

app.Run();
