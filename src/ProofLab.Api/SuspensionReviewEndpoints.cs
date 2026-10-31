
namespace ProofLab.Api;
public static class SuspensionReviewEndpoints {
    public static void MapSuspensionReviewEndpoints(this WebApplication app) {
        const string path="/api/science/director/pendulum/sessions/{id}/sam/{role}/suspension-review";
        app.MapGet(path,async(string id,string role,SuspensionReviewService service,HttpContext context,CancellationToken ct)=>{
            if(context.Connection.RemoteIpAddress is not {} remote || !System.Net.IPAddress.IsLoopback(remote))
                return Results.StatusCode(403);
            context.Response.Headers.CacheControl="no-store";
            try {return Results.Ok(await service.StateAsync(id,role,ct));}
            catch(FileNotFoundException){return Results.NotFound(new{error="SAM evidence missing."});}
            catch(Exception ex) when(ex is ArgumentException or InvalidDataException or InvalidOperationException) {
                return Results.Conflict(new{error="Suspension review unavailable; evidence must be checked."});
            }
        });
        app.MapPost(path,async(string id,string role,SuspensionReviewRequest input,SuspensionReviewService service,
            HttpContext context,CancellationToken ct)=>{
            if(!ApiRequestAccess.Manual(context,"suspension-review"))return Results.StatusCode(403);
            context.Response.Headers.CacheControl="no-store";
            if(context.Request.ContentLength is >4096)return Results.BadRequest(new{error="Request too large."});
            try {return Results.Ok(await service.RunAsync(id,role,input,ct));}
            catch(FileNotFoundException){return Results.NotFound(new{error="SAM evidence missing."});}
            catch(AssemblyNoInferenceSentException){return Results.Conflict(new{error="REVIEW_NO_INFERENCE_SENT"});}
            catch(Exception ex) when(ex is ArgumentException or InvalidDataException or InvalidOperationException) {
                return Results.Conflict(new{error="REVIEW_BLOCKED_CHECK_EVIDENCE"});
            }
            catch(HttpRequestException ex){return MetaSafeDiagnostics.ProviderFailure(ex,AssemblyPolicy.Model);}
            catch(FormatException){return Results.Problem("Malformed suspension review.",statusCode:502);}
        });
    }
}
