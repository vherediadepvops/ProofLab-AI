using System.Net;
namespace ProofLab.Api;
public static class MuseExperimentEndpoints
{
    private static bool Local(HttpContext c,bool write=false)=>
        c.Connection.RemoteIpAddress is IPAddress ip && IPAddress.IsLoopback(ip) &&
        c.Request.Host.Host is "127.0.0.1" or "localhost" &&
        (!write || ((c.Request.Headers.Origin=="http://127.0.0.1:4300" ||
          c.Request.Headers.Origin=="http://localhost:4300") &&
          c.Request.Headers["X-ProofLab-Manual-Action"]=="muse-research"));
    private static async Task<IResult> Safe(Func<Task<IResult>> work) {
        try {return await work();}
        catch(FileNotFoundException){return Results.NotFound(new {error="MUSE_EVIDENCE_OR_WORKER_MISSING"});}
        catch(ArgumentException){return Results.BadRequest(new {error="MUSE_SOURCE_OR_REQUEST_CHANGED"});}
        catch(InvalidDataException){return Results.Conflict(new {error="MUSE_EVIDENCE_INTEGRITY_FAILED"});}
        catch(InvalidOperationException){return Results.Conflict(new {error="MUSE_GATES_OR_PREVIOUS_ATTEMPT_BLOCKED"});}
        catch(IOException){return Results.Conflict(new {error="MUSE_BUSY_OR_STORAGE_FAILED"});}
        catch(Microsoft.Data.Sqlite.SqliteException){return Results.Conflict(new {error="MUSE_STORAGE_CONFLICT"});}
        catch(HttpRequestException){return Results.Problem("Meta request failed; inspect the recorded attempt.",statusCode:502);}
        catch(Exception e) when(e is System.Text.Json.JsonException or FormatException or KeyNotFoundException) {
            return Results.Problem("Meta result is incomplete or inconsistent; no prediction was approved.",statusCode:502);
        }
        catch(OperationCanceledException){return Results.Problem("Research operation timed out; inspect status.",statusCode:504);}
    }
    public static void MapMuseExperimentEndpoints(this WebApplication app)
    {
        const string path="/api/science/director/pendulum/sessions/{id}/muse";
        app.MapGet(path,(string id,MuseExperimentService service,HttpContext c)=>{
            c.Response.Headers.CacheControl="no-store";
            return Local(c)?Safe(()=>Task.FromResult(Results.Ok(service.State(id)) as IResult)):
                Task.FromResult(Results.StatusCode(403));
        });
        app.MapPost(path+"/prepare",(string id,MusePrepareRequest request,MuseExperimentService service,HttpContext c,CancellationToken ct)=>{
            c.Response.Headers.CacheControl="no-store";
            if(!Local(c,true))return Task.FromResult(Results.StatusCode(403));
            if(c.Request.ContentLength is >4096)return Task.FromResult(Results.BadRequest());
            return Safe(async()=>{await service.PrepareAsync(id,request,ct);return Results.Ok(service.State(id));});
        });
        app.MapPost(path+"/evaluate",(string id,MuseExperimentRunRequest request,MuseExperimentService service,HttpContext c,CancellationToken ct)=>{
            c.Response.Headers.CacheControl="no-store";
            if(!Local(c,true))return Task.FromResult(Results.StatusCode(403));
            if(c.Request.ContentLength is >4096)return Task.FromResult(Results.BadRequest());
            return Safe(async()=>Results.Ok(await service.EvaluateAsync(id,request,ct)));
        });
        app.MapGet(path+"/report",(string id,MuseExperimentService service,HttpContext c)=>{
            c.Response.Headers.CacheControl="private, no-store";
            return Local(c)?Safe(()=>{
                var data=service.State(id);
                return Task.FromResult(Results.File(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(data,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
                    "application/json","prooflab-muse-"+id+".json") as IResult);
            }):Task.FromResult(Results.StatusCode(403));
        });
    }
}
