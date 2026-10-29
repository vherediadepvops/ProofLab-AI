using System.Net;
using System.Text.Json;

// Isolated READ-ONLY QA server. No Meta services, keys or model calls.
// Never deploy this test host to the main API process.
var reportPath=Environment.GetEnvironmentVariable("PROOFLAB_SAM_PARALLEL4_REPORT");
var builder=WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5407");
var app=builder.Build();
app.Use(async(context,next)=>{
    if(context.Connection.RemoteIpAddress is not { } address ||
       !IPAddress.IsLoopback(address))
    {
        context.Response.StatusCode=403;
        return;
    }
    context.Response.Headers.CacheControl="no-store";
    await next();
});
app.MapGet("/",()=>Results.Redirect("/api/qa/report"));
app.MapGet("/api/qa/health",()=>Results.Ok(new{
    service="ProofLab SAM Parallel4 OFFLINE QA",
    mode="READ_ONLY_REFERENCE_REPARTITION",
    realMetaCalls=0,
    paidInferenceAvailable=false,
    source="feature/sam-parallel4-streaming"
}));
app.MapGet("/api/qa/report",async(CancellationToken ct)=>{
    if(string.IsNullOrWhiteSpace(reportPath) ||
       !reportPath.Split(Path.DirectorySeparatorChar)
                  .Contains(".private",StringComparer.OrdinalIgnoreCase) ||
       !File.Exists(reportPath))
        return Results.Problem("Private QA result not available.",statusCode:503);
    using var stream=File.OpenRead(reportPath);
    using var source=await JsonDocument.ParseAsync(stream,cancellationToken:ct);
    var data=source.RootElement;
    if(data.GetProperty("status").GetString()!="OFFLINE_REFERENCE_REPARTITION_VALIDATED" ||
       data.GetProperty("providerCalls").GetInt32()!=0 ||
       data.GetProperty("liveFourPartInference").GetBoolean())
        return Results.Problem("Unverified QA data.",statusCode:409);
    var sealedReport=JsonSerializer.Deserialize<JsonElement>(data.GetRawText());
    return Results.Ok(new{
        experimental=true,
        noPaidProviderCalls=true,
        interpretation="The four parts reuse one previously completed SAM response. Speed and quality of four independent Meta calls have NOT been measured.",
        offlineReport=sealedReport
    });
});
// Feature-only HTTP streaming smoke: interleaved events from four LOCAL
// synthetic producers. This tests incremental SSE delivery, not Meta output.
// Never substitute these events for SAM provider evidence.
app.MapGet("/api/qa/stream",async(HttpContext ctx)=>{
    ctx.Response.ContentType="text/event-stream";
    ctx.Response.Headers["X-ProofLab-QA-Mode"]="OFFLINE_SIMULATED_NO_META";
    using var writeGate=new SemaphoreSlim(1,1);
    async Task Emit(string eventType,object body)
    {
        await writeGate.WaitAsync(ctx.RequestAborted);
        try
        {
            await ctx.Response.WriteAsync("event: "+eventType+"\n"+
                "data: "+JsonSerializer.Serialize(body)+"\n\n",
                ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }
        finally {writeGate.Release();}
    }
    await Emit("mode",new {offline=true,providerCalls=0,parts=4});
    var workers=Enumerable.Range(0,4).Select(async part=>{
        for(var step=1;step<=4;step++)
        {
            await Task.Delay(70+part*10,ctx.RequestAborted);
            await Emit("part-progress",new{
                part,step,
                stage="LOCAL_SIMULATED_SSE_PROGRESS",
                notActualMetaStream=true
            });
        }
    });
    await Task.WhenAll(workers);
    await Emit("finished",new{
        fourPartsDone=true,
        realMetaCalls=0,
        evidence="OFFLINE_REFERENCE_ONLY"
    });
});
app.Run();
