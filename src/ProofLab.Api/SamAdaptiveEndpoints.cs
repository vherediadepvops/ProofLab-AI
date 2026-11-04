using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ProofLab.Api;

/// <summary>
/// Explicit manual analysis routes. Direct Meta uses a new exact Spark run;
/// combined mode may reuse verified evidence. Paid gates apply to both.
/// </summary>
public static class SamAdaptiveEndpoints
{
    private static bool Loopback(HttpContext ctx) => ApiRequestAccess.Host(ctx);

    internal static bool AuthorizedOrigin(string origin) => origin.Length==0 ||
        origin is "http://127.0.0.1:4300" or "http://localhost:4300" or
            "http://127.0.0.1:4327" or "http://localhost:4327";

    private const string Route=
        "/api/science/director/pendulum/sessions/{id}/sam-adaptive/{role}";

    public static IEndpointRouteBuilder MapSamAdaptiveEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapSamInitialReferenceEndpoints();
        app.MapGet(Route+"/preflight",async (
            string id,string role,int? targetCoreFrames,int? maxInFlight,string? mode,string? sceneRunId,string? initialBodyPointConfirmationId,
            SamAdaptiveLiveService service,HttpContext ctx,CancellationToken ct)=>
        {
            if(!Loopback(ctx))return Results.StatusCode(403);
            try
            {
                return Results.Ok(await service.PreflightAsync(
                    id,role,targetCoreFrames??53,maxInFlight??0,ct,mode,sceneRunId,initialBodyPointConfirmationId));
            }
            catch(ArgumentException ex) when(ex is SamSingleVideoFrameLimitException ||
                ex.Message=="SAM video requires at least 50 source frames.")
            {return Results.BadRequest(new{
                error="SAM_SINGLE_VIDEO_FRAME_LIMIT",
                minimumFrames=SamAdaptiveSegmentPlan.MinimumBilledFrames,
                maximumFrames=SamAdaptiveSegmentPlan.MaximumSourceFrames,
                automaticSplitting=false});}
            catch(ArgumentException){return Results.BadRequest(new{
                error="Unsafe single-video plan parameters."});}
            catch(FileNotFoundException){return Results.NotFound(new{
                error="Original recording not found."});}
            catch(InvalidDataException){return Results.Conflict(new{
                error="Source PTS or Spark evidence not verified."});}
            catch(InvalidOperationException){return Results.Conflict(new{
                error="A verified Spark scene and spending plan are required."});}
        });

        // Reuse the existing guarded Spark implementation to localize the
        // full ORIGINAL video once, when no authenticated scene exists.
        // This is independently billable and requires its own exact quote,
        // explicit transfer/payment consent and backend credit gates.
        app.MapGet(Route+"/spark-quote",async(
            string id,string role,string? mode,AssemblyLocator locator,HttpContext ctx,
            CancellationToken ct)=>
        {
            if(!Loopback(ctx))return Results.StatusCode(403);
            try{return Results.Ok(await locator.GroundingQuoteAsync(
                id,role,ct,mode));}
            catch(ArgumentException){return Results.BadRequest();}
            catch(FileNotFoundException){return Results.NotFound();}
            catch(InvalidDataException){return Results.Conflict();}
            catch(InvalidOperationException){return Results.Conflict(new{
                error="Source is not ready for analysis. Complete preparation first."});}
        });

        app.MapPost(Route+"/spark-once",async (
            string id,string role,AssemblyGroundingRunRequest request,
            AssemblyLocator locator,MetaLiveAccess meta,HttpContext ctx,
            CancellationToken ct)=>
        {
            var origin=ctx.Request.Headers.Origin.ToString();
            if(!Loopback(ctx) || !ApiRequestAccess.Origin(ctx,true) ||
               ctx.Request.Headers["X-ProofLab-Manual-Action"]!="sam-adaptive-spark" ||
               ctx.Request.ContentLength is >4096)
                return Results.StatusCode(403);
            ctx.Response.Headers.CacheControl="no-store";
            if(!SamManualApiTokenPolicy.Accepts(request.ModelApiToken) ||
               !meta.Status(request.ModelApiToken).LiveCallsPermitted ||
               !request.ConfirmImageTransfer || !request.ConfirmVideoTransfer ||
               !request.ConfirmPaidCall)
                return Results.Conflict(new{
                    error="Explicit Spark transfer/credit/consent gates not satisfied."});
            try
            {
                return Results.Ok(await locator.GroundAsync(id,role,
                    request,ct));
            }
            catch(ArgumentException){return Results.BadRequest();}
            catch(FileNotFoundException){return Results.NotFound();}
            catch(InvalidDataException){return Results.Conflict(new{
                error="Spark full-video provenance mismatch."});}
            catch(InvalidOperationException){return Results.Conflict(new{
                error="Prior Spark attempt or backend credit gate blocks this request."});}
            catch(HttpRequestException){return Results.Problem(
                "Spark provider request failed; no automatic retry.",statusCode:502);}
            catch(FormatException){return Results.Problem(
                "Spark provider output could not be validated.",statusCode:502);}
        });

        app.MapPost(Route+"/manual-batch",async (
            string id,string role,SamAdaptiveLiveRunRequest request,
            SamAdaptiveLiveService service,HttpContext ctx,CancellationToken ct)=>
        {
            var origin=ctx.Request.Headers.Origin.ToString();
            var authorizedOrigin=ApiRequestAccess.Origin(ctx,true);
            if(!Loopback(ctx) || !authorizedOrigin ||
               ctx.Request.Headers["X-ProofLab-Manual-Action"]!="sam-adaptive-batch" ||
               ctx.Request.ContentLength is >4096)
                return Results.StatusCode(403);
            ctx.Response.Headers.CacheControl="no-store";
            ctx.Response.Headers.Pragma="no-cache";
            if(!SamManualApiTokenPolicy.Accepts(request.ModelApiToken))
                return Results.BadRequest(new{error="Manual Meta key format invalid."});
            try
            {
                // After the durable batch trial marker, a disconnected
                // browser must not truncate any potentially billable result.
                var result=await service.RunWithOneRetryAsync(id,role,request,ct);
                return Results.Ok(result);
            }
            catch(ArgumentException ex) when(ex is SamSingleVideoFrameLimitException ||
                ex.Message=="SAM video requires at least 50 source frames.")
            {return Results.BadRequest(new{
                error="SAM_SINGLE_VIDEO_FRAME_LIMIT",
                minimumFrames=SamAdaptiveSegmentPlan.MinimumBilledFrames,
                maximumFrames=SamAdaptiveSegmentPlan.MaximumSourceFrames,
                automaticSplitting=false});}
            catch(ArgumentException){return Results.BadRequest(new{
                error="Invalid or unapproved single-video parameters."});}
            catch(FileNotFoundException){return Results.NotFound(new{
                error="Original recording not found."});}
            catch(InvalidDataException){return Results.Conflict(new{
                error="Video frames, timestamps or Spark source are inconsistent."});}
            catch(SamInitialBodyPointConfirmationRequiredException ex){return Results.Conflict(new{code="SAM_INITIAL_BODY_POINT_CONFIRMATION_REQUIRED",sceneRunId=ex.SceneRunId,failedBatchId=ex.FailedBatchId,newBatchId=ex.NewBatchId});}
            catch(SamInitialReferenceReviewRequiredException){return Results.Conflict(new{code="SAM_INITIAL_REFERENCE_REVIEW_REQUIRED"});}
            catch(InvalidOperationException){return Results.Conflict(new{
                error="Adaptive request blocked: budget, credit, prior attempt, source or owner approval."});}
        });

        // A separate, owner-initiated retry from the failed SAM stage only.
        // A read-only quote never repeats Spark or opens a billable request.
        app.MapGet(Route+"/manual-stage-retry/quote",async (
            string id,string role,string? initialBodyPointConfirmationId,SamAdaptiveLiveService service,
            HttpContext ctx,CancellationToken ct)=>
        {
            if(!Loopback(ctx))return Results.StatusCode(403);
            ctx.Response.Headers.CacheControl="no-store";
            try{
                var quote=await service.StageRetryQuoteAsync(id,role,ct,initialBodyPointConfirmationId);
                return quote is null?Results.NotFound():Results.Ok(quote);
            }
            catch(SamInitialBodyPointConfirmationRequiredException ex){return Results.Conflict(new{code="SAM_INITIAL_BODY_POINT_CONFIRMATION_REQUIRED",sceneRunId=ex.SceneRunId,failedBatchId=ex.FailedBatchId,newBatchId=ex.NewBatchId});}
            catch(SamInitialReferenceReviewRequiredException){return Results.Conflict(new{code="SAM_INITIAL_REFERENCE_REVIEW_REQUIRED"});}
            catch(ArgumentException){return Results.BadRequest();}
            catch(FileNotFoundException){return Results.NotFound();}
            catch(InvalidDataException){return Results.Conflict();}
            catch(InvalidOperationException){return Results.Conflict();}
        });

        app.MapPost(Route+"/manual-stage-retry",async (
            string id,string role,SamStageRetryRequest request,
            SamAdaptiveLiveService service,HttpContext ctx,CancellationToken ct)=>
        {
            if(!Loopback(ctx) || !ApiRequestAccess.Origin(ctx,true) ||
               ctx.Request.Headers["X-ProofLab-Manual-Action"]!="sam-adaptive-stage-retry" ||
               ctx.Request.ContentLength is >4096)
                return Results.StatusCode(403);
            ctx.Response.Headers.CacheControl="no-store";
            if(!SamManualApiTokenPolicy.Accepts(request.ModelApiToken))
                return Results.BadRequest(new{error="Manual Meta key invalid."});
            try{return Results.Ok(await service.RunStageRetryAsync(id,role,request,ct));}
            catch(SamInitialBodyPointConfirmationRequiredException ex){return Results.Conflict(new{code="SAM_INITIAL_BODY_POINT_CONFIRMATION_REQUIRED",sceneRunId=ex.SceneRunId,failedBatchId=ex.FailedBatchId,newBatchId=ex.NewBatchId});}
            catch(SamInitialReferenceReviewRequiredException){return Results.Conflict(new{code="SAM_INITIAL_REFERENCE_REVIEW_REQUIRED"});}
            catch(ArgumentException){return Results.BadRequest();}
            catch(FileNotFoundException){return Results.NotFound();}
            catch(InvalidDataException){return Results.Conflict(new{error="Stored SAM clip or source is inconsistent."});}
            catch(InvalidOperationException){return Results.Conflict(new{error="No verified failed run or explicit retry authorization available."});}
            catch(IOException){return Results.Conflict(new{error="Retry claimed; inspect its result, do not submit twice."});}
        });

        app.MapGet(Route+"/batches/{batchId}/progress",async(
            string batchId,SamAdaptiveLiveService service,
            HttpContext ctx,CancellationToken ct)=>
        {
            if(!Loopback(ctx))return Results.StatusCode(403);
            try{return Results.Ok(await service.ReadProgressAsync(batchId,ct));}
            catch(ArgumentException){return Results.BadRequest();}
            catch(FileNotFoundException){return Results.NotFound();}
            catch(InvalidDataException){return Results.Conflict();}
        });

        app.MapGet(Route+"/batches/{batchId}/report",async(
            string batchId,SamAdaptiveLiveService service,
            HttpContext ctx,CancellationToken ct)=>
        {
            if(!Loopback(ctx))return Results.StatusCode(403);
            try
            {
                var report=await service.ReadReportAsync(batchId,ct);
                return report is null?Results.NotFound():Results.Ok(report);
            }
            catch(ArgumentException){return Results.BadRequest();}
            catch(InvalidDataException){return Results.Conflict();}
        });
        app.MapGet(Route+"/batches/{batchId}/evidence",async(
            string id,string role,string batchId,SamAdaptiveLiveService service,
            HttpContext ctx,CancellationToken ct)=>{
            if(!Loopback(ctx))return Results.StatusCode(403);
            ctx.Response.Headers.CacheControl="no-store";
            try{return Results.Ok(await service.ReadPlaybackAsync(id,role,batchId,ct));}
            catch(ArgumentException){return Results.BadRequest();}
            catch(FileNotFoundException){return Results.NotFound();}
            catch(InvalidDataException){return Results.Conflict(new{error="Segment evidence is incomplete or inconsistent."});}
        });
        return app;
    }
}
