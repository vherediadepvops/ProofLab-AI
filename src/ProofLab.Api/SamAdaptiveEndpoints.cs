using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ProofLab.Api;

/// <summary>
/// New operator-only, loopback experiment route. The existing SAM endpoints,
/// paid gates and SQLite evidence archive are not modified or bypassed.
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
        app.MapGet(Route+"/preflight",async (
            string id,string role,int? targetCoreFrames,int? maxInFlight,
            SamAdaptiveLiveService service,HttpContext ctx,CancellationToken ct)=>
        {
            if(!Loopback(ctx))return Results.StatusCode(403);
            try
            {
                return Results.Ok(await service.PreflightAsync(
                    id,role,targetCoreFrames??53,maxInFlight??0,ct));
            }
            catch(ArgumentException){return Results.BadRequest(new{
                error="Unsafe adaptive plan parameters."});}
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
            string id,string role,AssemblyLocator locator,HttpContext ctx,
            CancellationToken ct)=>
        {
            if(!Loopback(ctx))return Results.StatusCode(403);
            try{return Results.Ok(await locator.GroundingQuoteAsync(
                id,role,ct,"api-meta-cache"));}
            catch(ArgumentException){return Results.BadRequest();}
            catch(FileNotFoundException){return Results.NotFound();}
            catch(InvalidDataException){return Results.Conflict();}
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
                    request with{Mode="api-meta-cache"},ct));
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
            catch(ArgumentException){return Results.BadRequest(new{
                error="Invalid or unapproved frame splitting parameters."});}
            catch(FileNotFoundException){return Results.NotFound(new{
                error="Original recording not found."});}
            catch(InvalidDataException){return Results.Conflict(new{
                error="Video frames, timestamps or Spark source are inconsistent."});}
            catch(InvalidOperationException){return Results.Conflict(new{
                error="Adaptive request blocked: budget, credit, prior attempt, source or owner approval."});}
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
