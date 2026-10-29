using System.Net;
namespace ProofLab.Api;

public static class AssemblyEndpoints
{
    private static bool Local(HttpContext c,bool write=false)
    {
        if(c.Connection.RemoteIpAddress is not IPAddress ip || !IPAddress.IsLoopback(ip) ||
            c.Request.Host.Host is not ("127.0.0.1" or "localhost"))return false;
        if(!write)return true;
        var origin=c.Request.Headers.Origin.ToString();
        return (origin=="http://127.0.0.1:4300" || origin=="http://localhost:4300") &&
            c.Request.Headers["X-ProofLab-Manual-Action"]=="assembly";
    }
    private static async Task<IResult> Safe(Func<Task<IResult>> action)
    {
        try{return await action();}
        catch(FileNotFoundException){return Results.NotFound(new {error="ASSEMBLY_SOURCE_OR_EVIDENCE_MISSING"});}
        catch(ArgumentException){return Results.BadRequest(new {error="ASSEMBLY_INPUT_INVALID"});}
        catch(InvalidDataException){return Results.Conflict(new {error="ASSEMBLY_EVIDENCE_INTEGRITY_FAILED"});}
        catch(AssemblyNoInferenceSentException){return Results.Conflict(new {error="GROUNDING_NO_INFERENCE_SENT_REVIEW_COST"});}
        catch(InvalidOperationException){return Results.Conflict(new {error="ASSEMBLY_GATES_OR_PREVIOUS_ATTEMPT_BLOCKED"});}
        catch(IOException){return Results.Conflict(new {error="ASSEMBLY_BUSY_OR_STORAGE_FAILURE"});}
        catch(HttpRequestException){return Results.Problem("Meta request failed; inspect status before retrying.",statusCode:502);}
        catch(System.Text.Json.JsonException){return Results.Problem("Invalid Meta knot response.",statusCode:502);}
        catch(FormatException){return Results.Problem("Incomplete Meta knot response.",statusCode:502);}
        catch(KeyNotFoundException){return Results.Problem("Incomplete Meta knot response.",statusCode:502);}
        catch(OperationCanceledException){return Results.Problem("Knot operation timed out; inspect status.",statusCode:504);}
    }
    public static void MapAssemblyEndpoints(this WebApplication app)
    {
        const string path="/api/science/director/pendulum/sessions/{id}/assembly/{role}";
        app.MapGet(path+"/grounding/preflight",(string id,string role,string? mode,AssemblyLocator locator,
            HttpContext c,CancellationToken ct)=> {
            c.Response.Headers.CacheControl="no-store";
            return Local(c)?Safe(async()=>Results.Ok(await locator.GroundingQuoteAsync(id,role,ct,mode))):
                Task.FromResult(Results.StatusCode(403));
        });
        app.MapPost(path+"/grounding",(string id,string role,AssemblyGroundingRunRequest input,
            AssemblyLocator locator,HttpContext c,CancellationToken ct)=> {
            c.Response.Headers.CacheControl="no-store";
            if(!Local(c,true))return Task.FromResult(Results.StatusCode(403));
            if(c.Request.ContentLength is >4096)return Task.FromResult(Results.BadRequest());
            return Safe(async()=>Results.Ok(await locator.GroundAsync(id,role,input,ct)));
        });
        app.MapGet(path+"/preflight",(string id,string role,AssemblyLocator knots,
            HttpContext c,CancellationToken ct)=> {
            c.Response.Headers.CacheControl="no-store";
            return Local(c)?Safe(async()=>Results.Ok(await knots.QuoteAsync(id,role,ct))):
                Task.FromResult(Results.StatusCode(403));
        });
        app.MapPost(path+"/locate",(string id,string role,KnotRunRequest input,
            AssemblyLocator knots,HttpContext c,CancellationToken ct)=> {
            c.Response.Headers.CacheControl="no-store";
            if(!Local(c,true))return Task.FromResult(Results.StatusCode(403));
            if(c.Request.ContentLength is >4096)return Task.FromResult(Results.BadRequest());
            return Safe(async()=>Results.Ok(await knots.RunAsync(id,role,input,ct)));
        });
        app.MapGet(path+"/frame/{index:int}",(string id,string role,int index,string key,
            AssemblyLocator knots,HttpContext c,CancellationToken ct)=> {
            c.Response.Headers.CacheControl="private, no-store";
            if(!Local(c))return Task.FromResult(Results.StatusCode(403));
            return Safe(async()=> {
                var image=await knots.ImageAsync(id,role,key,index,ct);
                return Results.File(image.Bytes,"image/jpeg");
            });
        });
        app.MapPost(path+"/confirm",(string id,string role,AssemblyConfirmRequest input,
            AssemblyLocator scenes,HttpContext c,CancellationToken ct)=> {
            c.Response.Headers.CacheControl="no-store";
            if(!Local(c,true))return Task.FromResult(Results.StatusCode(403));
            if(c.Request.ContentLength is >4096)return Task.FromResult(Results.BadRequest());
            return Safe(async()=>Results.Ok(await scenes.ConfirmAsync(id,role,input,ct)));
        });
    }
}
