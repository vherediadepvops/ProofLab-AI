using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ProofLab.Api;

public static class ResearchRecoveryEndpoints
{
    private static readonly Regex PaidRoute = new(@"^/api/science/director/pendulum/sessions/(?<id>[a-f0-9]{32})/(?:(?<stage>assembly|sam-adaptive|sam|knot)/(?<role>baseline|followup)/(?<action>grounding|spark-once|manual-batch|manual-one-shot|analyze|run|locate|suspension-review)|(?<stage>muse)/(?<action>evaluate|contrast/evaluate))$", RegexOptions.Compiled);
    public static void UseResearchRecovery(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var match = PaidRoute.Match(context.Request.Path.Value ?? "");
            if (context.Request.Method != "POST" || !match.Success || !Local(context)) { await next(context); return; }
            var study = match.Groups["id"].Value; var role = match.Groups["role"].Success ? match.Groups["role"].Value :
                match.Groups["action"].Value.Contains("contrast") ? "followup" : "baseline";
            await ResearchOperationExecution.RunAsync(context, () => next(context),
                context.RequestServices.GetRequiredService<ResearchOperationJournal>(), study, role,
                match.Groups["stage"].Value + "/" + match.Groups["action"].Value, app.Lifetime.ApplicationStopping);
        });
        app.MapGet("/api/science/director/pendulum/sessions/{id}/recovery", async (string id,
            PendulumDirectorWorkflow director, ResearchOperationJournal journal, PendulumSamCache sam,
            SamAdaptiveLiveService adaptive, MuseExperimentService muse, HttpContext context, CancellationToken ct) =>
        {
            if (!Local(context)) return Results.StatusCode(403);
            context.Response.Headers.CacheControl = "no-store";
            if (!ResearchOperationJournal.ValidId(id)) return Results.BadRequest();
            try
            {
                var session = await director.ReadAsync(id, ct); var operations = await journal.ReadAsync(id, ct);
                var selections = new List<object>(); var unavailable = new List<string>();
                // No expensive archive scan while an operation is still writing.
                if (!operations.Any(x => x.State == "RUNNING")) foreach (var role in new[] { "baseline", "followup" })
                {
                    var clip = role == "baseline" ? session.Baseline : session.Followup; if (clip is null) continue;
                    // A frozen prediction pins its input analysis. A later processing of the
                    // same source must never silently replace the baseline evidence.
                    if (role == "baseline" && session.MusePrediction is not null)
                    {
                        try
                        {
                            var frozen = ResearchFrozenSource.Resolve(muse.State(id), id, clip.Sha256)
                                ?? throw new InvalidDataException("Frozen source missing.");
                            var artifact = frozen.AdaptiveBatchId is { } frozenBatch
                                ? await adaptive.ReadPlaybackAsync(id, role, frozenBatch, ct)
                                : await sam.ReadArchiveAsync(id, role, ct);
                            if (artifact.Receipt.AnalysisId != frozen.AnalysisId || artifact.Receipt.OriginalVideoSha256 != clip.Sha256)
                                throw new InvalidDataException("Frozen evidence unavailable.");
                            selections.Add(new
                            {
                                studyId = id,
                                videoRole = role,
                                sourceSha256 = clip.Sha256,
                                analysisId = frozen.AnalysisId,
                                adaptiveBatchId = frozen.AdaptiveBatchId
                            });
                        }
                        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or JsonException or KeyNotFoundException)
                        {
                            unavailable.Add(role);
                        }
                        continue;
                    }
                    var batch = await adaptive.LatestRecoverableResultAsync(clip.Sha256, ct);
                    SamVideoArtifact? archived = null;
                    try { archived = await sam.ReadArchiveAsync(id, role, ct); }
                    catch (InvalidDataException) { unavailable.Add(role); }
                    catch (InvalidOperationException) { unavailable.Add(role); }
                    catch (FileNotFoundException) { }
                    if (batch is not null && (archived is null || batch.RecordedAtUtc >= archived.Receipt.CapturedAtUtc))
                        selections.Add(new { studyId = id, videoRole = role, sourceSha256 = clip.Sha256, analysisId = batch.BatchId, adaptiveBatchId = batch.BatchId });
                    else if (archived?.Receipt.AnalysisId is { } analysisId)
                        selections.Add(new { studyId = id, videoRole = role, sourceSha256 = clip.Sha256, analysisId, adaptiveBatchId = (string?)null });
                }
                return Results.Ok(new { session, operations, selections, unavailable, canResumeWithoutProvider = true });
            }
            catch (FileNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException) { return Results.BadRequest(); }
        });
    }
    private static bool Local(HttpContext context) => ApiRequestAccess.Host(context);
}
