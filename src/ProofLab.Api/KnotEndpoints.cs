using System.Net;
namespace ProofLab.Api;

public static class KnotEndpoints
{
    private static bool Local(HttpContext c,bool write=false)
    {
        if(c.Connection.RemoteIpAddress is not IPAddress ip || !IPAddress.IsLoopback(ip) ||
            c.Request.Host.Host is not ("127.0.0.1" or "localhost"))return false;
        if(!write)return true;
        var origin=c.Request.Headers.Origin.ToString();
        return (origin=="http://127.0.0.1:4300" || origin=="http://localhost:4300") &&
            c.Request.Headers["X-ProofLab-Manual-Action"]=="knot";
    }
    private static async Task<IResult> Safe(Func<Task<IResult>> action)
    {
        try{return await action();}
        catch(FileNotFoundException){return Results.NotFound(new {error="KNOT_SOURCE_OR_EVIDENCE_MISSING"});}
        catch(ArgumentException){return Results.BadRequest(new {error="KNOT_INPUT_INVALID"});}
        catch(InvalidDataException){return Results.Conflict(new {error="KNOT_EVIDENCE_INTEGRITY_FAILED"});}
        catch(InvalidOperationException){return Results.Conflict(new {error="KNOT_GATES_OR_PREVIOUS_ATTEMPT_BLOCKED"});}
        catch(IOException){return Results.Conflict(new {error="KNOT_BUSY_OR_STORAGE_FAILURE"});}
        catch(HttpRequestException){return Results.Problem("Meta request failed; inspect status before retrying.",statusCode:502);}
        catch(System.Text.Json.JsonException){return Results.Problem("Invalid Meta knot response.",statusCode:502);}
        catch(FormatException){return Results.Problem("Incomplete Meta knot response.",statusCode:502);}
        catch(KeyNotFoundException){return Results.Problem("Incomplete Meta knot response.",statusCode:502);}
        catch(OperationCanceledException){return Results.Problem("Knot operation timed out; inspect status.",statusCode:504);}
    }
    public static void MapKnotEndpoints(this WebApplication app)
    {
        const string path="/api/science/director/pendulum/sessions/{id}/knot/{role}";
        app.MapGet(path+"/preflight",(string id,string role,PendulumKnotLocator knots,
            HttpContext c,CancellationToken ct)=> {
            c.Response.Headers.CacheControl="no-store";
            return Local(c)?Safe(async()=>Results.Ok(await knots.QuoteAsync(id,role,ct))):
                Task.FromResult(Results.StatusCode(403));
        });
        app.MapPost(path+"/locate",(string id,string role,KnotRunRequest input,
            PendulumKnotLocator knots,HttpContext c,CancellationToken ct)=> {
            c.Response.Headers.CacheControl="no-store";
            if(!Local(c,true))return Task.FromResult(Results.StatusCode(403));
            if(c.Request.ContentLength is >4096)return Task.FromResult(Results.BadRequest());
            return Safe(async()=>Results.Ok(await knots.RunAsync(id,role,input,ct)));
        });
        app.MapGet(path+"/frame/{index:int}",(string id,string role,int index,string key,
            PendulumKnotLocator knots,HttpContext c,CancellationToken ct)=> {
            c.Response.Headers.CacheControl="private, no-store";
            if(!Local(c))return Task.FromResult(Results.StatusCode(403));
            return Safe(async()=> {
                var image=await knots.ImageAsync(id,role,key,index,ct);
                return Results.File(image.Bytes,"image/jpeg");
            });
        });
        app.MapPost(path+"/confirm",(string id,string role,KnotConfirmRequest input,
            PendulumKnotLocator knots,PendulumDirectorKinematicsStore store,HttpContext c,CancellationToken ct)=> {
            c.Response.Headers.CacheControl="no-store";
            if(!Local(c,true))return Task.FromResult(Results.StatusCode(403));
            return Safe(async()=> {
                var evidence=await knots.ReadAsync(id,role,input.CacheKey,ct);
                if(input.VideoSha256!=evidence.VideoSha256 || !input.ConfirmRopeExit ||
                    !double.IsFinite(input.X) || !double.IsFinite(input.Y) ||
                    input.X is <0 or >=1000 || input.Y is <0 or >=1000 ||
                    evidence.Proposal.Status!="CANDIDATE_REQUIRES_HUMAN_CONFIRMATION")
                    throw new ArgumentException("Review required.");
                var adjusted=Math.Abs(input.X-evidence.Proposal.X!.Value)>0.01 ||
                    Math.Abs(input.Y-evidence.Proposal.Y!.Value)>0.01;
                return Results.Ok(await store.ConfirmMetaAsync(id,role,new(
                    input.X*480/1000,input.Y*270/1000,input.VideoSha256),input.CacheKey,adjusted,ct));
            });
        });
    }
}
