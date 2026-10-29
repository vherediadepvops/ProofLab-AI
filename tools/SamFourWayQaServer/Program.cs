using System.Net;
using System.Text.Json;
using ProofLab.Api;

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
// Read-only adaptive window preview. No file access, no Meta credentials,
// no automatic provider calls and no change to production endpoints.
app.MapGet("/api/qa/dynamic-plan",
    (int frames,int? concurrency,int? targetCoreFrames)=>{
    try
    {
        var plan=SamAdaptiveSegmentPlan.Create(frames,
            targetCoreFrames??SamAdaptiveSegmentPlan.TargetCoreFrames,
            concurrency??4);
        return Results.Ok(new {
            mode="OFFLINE_DYNAMIC_PLANNER_NO_META",
            frames=plan.SourceFrames,
            count=plan.PartCount,
            maxInFlight=plan.MaxInFlight,
            targetCoreFrames=plan.RequestedTargetCoreFrames,
            minimumBillableFramesPerRequest=SamAdaptiveSegmentPlan.MinimumBilledFrames,
            totalSubmittedFrames=plan.SubmittedFrames,
            totalBilledFrames=plan.BilledFrames,
            overlapExtraFrames=plan.OverlapExtraFrames,
            baselineSingleCallUsd=plan.UnsplitSamCostUsd,
            parallelSamEstimateUsd=plan.EstimatedSamCostUsd,
            estimatedExtraCostUsd=plan.EstimatedSamCostUsd-plan.UnsplitSamCostUsd,
            budgetCapUsd=plan.BudgetCapUsd,
            budgetReducedPartCount=plan.BudgetReducedPartCount,
            providerCalls=0,
            actualLatencyAndQualityUnmeasured=true,
            windows=plan.Windows
        });
    }
    catch(ArgumentException)
    {
        return Results.BadRequest(new {code="INVALID_DYNAMIC_SAM_PLAN"});
    }
    catch(InvalidOperationException)
    {
        return Results.BadRequest(new {code="DYNAMIC_SAM_BUDGET_EXCEEDED"});
    }
});

// Simulates exactly the selected amount of segmented streams, through the
// bounded OFFLINE scheduler. It emits a REAL HTTP SSE stream carrying
// SYNTHETIC progress; it never invokes the Meta inference transport.
app.MapGet("/api/qa/dynamic-stream",
    async(HttpContext ctx,int frames,int? concurrency)=>{
    SamAdaptiveSegmentPlan plan;
    try{plan=SamAdaptiveSegmentPlan.Create(frames,
        maxInFlight:concurrency??4);}
    catch(ArgumentException)
    {
        ctx.Response.StatusCode=400;
        return;
    }
    ctx.Response.ContentType="text/event-stream";
    ctx.Response.Headers["X-ProofLab-QA-Mode"]="OFFLINE_SYNTHETIC_NO_META";
    using var emitGate=new SemaphoreSlim(1,1);
    async ValueTask Emit(string eventName,object payload)
    {
        await emitGate.WaitAsync(ctx.RequestAborted);
        try
        {
            await ctx.Response.WriteAsync("event: "+eventName+"\n"+
                "data: "+JsonSerializer.Serialize(payload)+"\n\n",
                ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }
        finally{emitGate.Release();}
    }
    await Emit("plan",new {
        mode="OFFLINE_SYNTHETIC_NO_PROVIDER",
        parts=plan.PartCount,parallelLimit=plan.MaxInFlight,
        expectedBillableFrames=plan.BilledFrames,
        providerCalls=0
    });
    var active=0;
    var peak=0;
    var result=await SamAdaptiveOfflineScheduler.RunAsync(
        plan,async(window,publish,ct)=>{
            var now=Interlocked.Increment(ref active);
            while(true)
            {
                var before=Volatile.Read(ref peak);
                if(now<=before || Interlocked.CompareExchange(
                    ref peak,now,before)==before)break;
            }
            try
            {
                for(var tick=1;tick<=3;tick++)
                {
                    await Task.Delay(20+(window.Part%4)*5,ct);
                    await publish(new("SSE_DELTA_PROGRESS",
                        tick*5,tick*4,tick*1024));
                }
                return window.Part;
            }
            finally{Interlocked.Decrement(ref active);}
        },
        p=>Emit("simulated-part-progress",new{
            p.Part,p.Stage,p.Events,p.Deltas,p.OutputChars,
            synthetic=true,providerCalls=0
        }),ctx.RequestAborted);
    await Emit("finished",new {
        parts=plan.PartCount,completed=result.Completed,
        failed=result.Failed,skipped=result.Skipped,
        actualPeakLocalConcurrency=peak,
        allCompleted=result.AllCompleted,
        providerCalls=0,
        independentProviderMasksProduced=false
    });
});
app.Run();
