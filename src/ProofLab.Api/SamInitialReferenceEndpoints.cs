using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ProofLab.Api;

public static class SamInitialReferenceEndpoints
{
    private const string Route="/api/science/director/pendulum/sessions/{id}/sam-adaptive/{role}/initial-reference";

    public static IEndpointRouteBuilder MapSamInitialReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Route,async(string id,string role,string? mode,string sceneRunId,
            string? initialBodyPointConfirmationId,SamAdaptiveLiveService service,HttpContext ctx,CancellationToken ct)=>{
            if(!ApiRequestAccess.Host(ctx))return Results.StatusCode(403);
            ctx.Response.Headers.CacheControl="no-store";
            try{return Results.Ok(await service.InitialReferenceQuoteAsync(id,role,mode,sceneRunId,initialBodyPointConfirmationId,ct));}
            catch(ArgumentException){return Results.BadRequest(new{code="INITIAL_REFERENCE_REQUEST_INVALID"});}
            catch(FileNotFoundException){return Results.NotFound(new{code="INITIAL_REFERENCE_NOT_FOUND"});}
            catch(InvalidDataException){return Results.Conflict(new{code="INITIAL_REFERENCE_CHANGED"});}
            catch(InvalidOperationException){return Results.Conflict(new{code="INITIAL_REFERENCE_UNAVAILABLE"});}
        });
        app.MapGet(Route+"/image/{frame:int}",async(string id,string role,int frame,
            string sceneRunId,string videoSha256,string sceneKey,SamAdaptiveLiveService service,HttpContext ctx,CancellationToken ct)=>{
            if(!ApiRequestAccess.Host(ctx))return Results.StatusCode(403);
            ctx.Response.Headers.CacheControl="no-store";
            try{return Results.File(await service.InitialReferenceImageAsync(id,role,sceneRunId,videoSha256,sceneKey,frame,ct),"image/jpeg");}
            catch(ArgumentException){return Results.BadRequest();}
            catch(FileNotFoundException){return Results.NotFound();}
            catch(InvalidDataException){return Results.Conflict();}
            catch(InvalidOperationException){return Results.Conflict();}
        });
        app.MapPost(Route,async(string id,string role,SamInitialBodyPointConfirmationRequest request,
            SamAdaptiveLiveService service,HttpContext ctx,CancellationToken ct)=>{
            if(!ApiRequestAccess.Host(ctx) || !ApiRequestAccess.Origin(ctx,true) ||
               ctx.Request.Headers["X-ProofLab-Manual-Action"]!="sam-initial-reference" ||
               ctx.Request.ContentLength is >4096)return Results.StatusCode(403);
            ctx.Response.Headers.CacheControl="no-store";
            try{return Results.Ok(await service.ConfirmInitialBodyPointAsync(id,role,request,ct));}
            catch(ArgumentException){return Results.BadRequest(new{code="INITIAL_REFERENCE_REQUEST_INVALID"});}
            catch(FileNotFoundException){return Results.NotFound(new{code="INITIAL_REFERENCE_NOT_FOUND"});}
            catch(InvalidDataException){return Results.Conflict(new{code="INITIAL_REFERENCE_CHANGED"});}
            catch(InvalidOperationException){return Results.Conflict(new{code="INITIAL_REFERENCE_UNAVAILABLE"});}
        });
        return app;
    }
}
