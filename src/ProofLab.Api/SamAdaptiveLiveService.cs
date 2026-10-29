using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace ProofLab.Api;

/// <summary>
/// Owner-approved, bounded real SAM 3.1 batch experiment.
/// Spark's existing authenticated scene is reused ONCE across the source;
/// every SAM fragment has independent HTTPS evidence, billable attempt,
/// provider response, digest and immutable receipt. No automatic retries.
/// No synthetic composite is indexed as a native single Meta response.
/// </summary>
public sealed class SamAdaptiveLiveService(
    ISamVideoRuntime runtime,IAssemblyScenes scenes,ISamVideoTransport transport,
    SamAdaptiveClipBuilder clipBuilder,MetaLiveAccess access,
    IWebHostEnvironment environment,IConfiguration config)
{
    public const string Protocol="PROOFLAB_SAM_ADAPTIVE_SSE_SOURCE_PTS_V1";
    public const int MaximumApprovedTrials=3;
    public const int MaximumLiveConcurrentRequests=4;
    private readonly SemaphoreSlim gate=new(1,1);
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web)
        {WriteIndented=true};

    private string StoreRoot
    {
        get
        {
            var configured=config["SamAdaptive:PrivateRoot"];
            var value=Path.GetFullPath(string.IsNullOrEmpty(configured)
                ?Path.Combine(environment.ContentRootPath,".private","physics","sam-adaptive")
                :configured);
            if(!value.Split(Path.DirectorySeparatorChar)
                .Contains(".private",StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Adaptive receipts must stay in private storage.");
            return value;
        }
    }
    private static string Hash(string s)=>SamVideoArchive.Hash(s);
    private static bool ValidSha(string? s) =>
        s is {Length:64} && s.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool ValidBatchId(string? s)=>
        s is {Length:32} && s.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private string BatchDir(string id)=>Path.Combine(StoreRoot,"batches",id);
    private string CampaignDir=>Path.Combine(StoreRoot,"campaign-2026-10-29");
    private static async Task WriteImmutable(string path,string contents,CancellationToken ct)
    {
        await using var fs=new FileStream(path,FileMode.CreateNew,
            FileAccess.Write,FileShare.None,64*1024,FileOptions.WriteThrough);
        var bytes=Encoding.UTF8.GetBytes(contents);
        await fs.WriteAsync(bytes,ct);
        await fs.FlushAsync(ct);
        fs.Flush(flushToDisk:true);
    }
    private static Task WriteJson(string path,object value,CancellationToken ct)=>
        WriteImmutable(path,JsonSerializer.Serialize(value,JsonOptions),ct);
    private int ExistingTrials()
    {
        var folder=CampaignDir;
        return Directory.Exists(folder)?
            Directory.EnumerateFiles(folder,"trial-*.json").Count():0;
    }
    private static string PlanKey(string sourceSha,string sceneKey,string ptsSha,
        SamAdaptiveSegmentPlan plan)=>Hash(
            Protocol+"\n"+sourceSha+"\n"+sceneKey+"\n"+ptsSha+"\n"+
            plan.RequestedTargetCoreFrames.ToString(CultureInfo.InvariantCulture)+"\n"+
            plan.MaxInFlight.ToString(CultureInfo.InvariantCulture)+"\n"+
            plan.PartCount.ToString(CultureInfo.InvariantCulture)+"\n"+
            plan.BilledFrames.ToString(CultureInfo.InvariantCulture)+"\n"+
            plan.EstimatedSamCostUsd.ToString("R",CultureInfo.InvariantCulture));

    private async Task<(SamVideoSource Source,SamSceneDefinition Scene,
        double[] Pts,SamAdaptiveSegmentPlan Plan,string Key)> Context(
        string studyId,string role,int target,int inFlight,CancellationToken ct)
    {
        if(inFlight is <1 or >MaximumLiveConcurrentRequests)
            throw new ArgumentException("Live SAM batches begin with at most four parallel calls.");
        var source=await runtime.GetVerifiedSourceAsync(studyId,role,ct);
        if(source.Codec is not ("h264" or "hevc" or "mpeg4"))
            throw new ArgumentException("Unrecognized source video codec.");
        var scene=await scenes.TrackingAsync(studyId,role,ct) ??
            throw new InvalidOperationException(
                "One completed Spark scene must exist BEFORE adaptive SAM.");
        var pts=await runtime.ReadSourceFramePtsAsync(source,ct);
        AssemblyPolicy.Validate(scene,source.Sha256,pts.Length);
        if(scene.Roles.Length!=1 || scene.Roles[0].Role!="object" ||
           scene.Roles[0].Id!="1")
            throw new InvalidOperationException(
                "Adaptive SAM v1 supports only one authenticated moving object.");
        var decision=SamAdaptiveGroundedPlan.Select(
            pts.Length,scene,target,inFlight,budgetUsd:0.50);
        var plan=decision.Plan;
        var ptsSha=plan.VerifySourcePts(pts);
        return (source,scene,pts,plan,
            PlanKey(source.Sha256.ToLowerInvariant(),scene.Key,ptsSha,plan));
    }

    public async Task<SamAdaptiveLiveQuote> PreflightAsync(
        string studyId,string role,int targetCoreFrames,int maxInFlight,CancellationToken ct)
    {
        var (source,scene,pts,plan,key)=await Context(
            studyId,role,targetCoreFrames,maxInFlight,ct);
        var grounded=SamAdaptiveGroundedPlan.Select(
            pts.Length,scene,targetCoreFrames,maxInFlight,budgetUsd:0.50);
        return new(Protocol,source.Sha256.ToLowerInvariant(),scene.Key,
            plan.VerifySourcePts(pts),key,Guid.NewGuid().ToString("N"),
            pts.Length,plan.PartCount,
            plan.MaxInFlight,plan.SubmittedFrames,plan.BilledFrames,
            plan.EstimatedSamCostUsd,0.50,
            Math.Max(0,MaximumApprovedTrials-ExistingTrials()),
            access.ManualTokenCallsPermitted,true,false,
            "ONE_REUSED_SPARK_SCENE; ONLY_SOURCE_FRAMES_WITH_AUTHENTIC_SPARK_BOXES; NO_AUTOMATIC_RETRY",
            grounded.IdealParts,grounded.VerifiedSparkSeedCount,
            grounded.ReducedForSparseSparkSeeds);
    }

    private int ReserveTrialSlot(string batchId,SamAdaptiveLiveQuote quote)
    {
        Directory.CreateDirectory(CampaignDir);
        using var guard=new FileStream(Path.Combine(CampaignDir,".trial.lock"),
            FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var n=ExistingTrials();
        if(n>=MaximumApprovedTrials)
            throw new InvalidOperationException("Three authorized real adaptive trials already consumed.");
        var slot=n+1;
        var path=Path.Combine(CampaignDir,"trial-"+slot.ToString("D2",
            CultureInfo.InvariantCulture)+".json");
        WriteJson(path,new{
            slot,batchId,quote.SourceSha256,quote.SparkSceneKey,quote.PlanSha256,
            maximumEstimatedSamUsd=quote.TotalSamEstimatedUsd,
            startedAtUtc=DateTimeOffset.UtcNow,
            status="OWNER_AUTHORIZED_TEST_MAY_BE_BILLABLE_NO_RETRY"
        },CancellationToken.None).GetAwaiter().GetResult();
        return slot;
    }

    private static string Prompt(SamSceneDefinition scene,SamAdaptiveWindow window) =>
        SamAdaptiveGroundedPlan.BuildPrompt(scene,window);

    public async Task<SamAdaptiveLiveReport> RunOnceAsync(
        string studyId,string role,SamAdaptiveLiveRunRequest request,CancellationToken ct)
    {
        if(!request.ConfirmPrivateVideoTransfer ||
           !request.ConfirmUpToManyBillableSamCalls ||
           !request.ConfirmNoAutomaticRetry ||
           !ValidSha(request.VideoSha256) ||
           !ValidSha(request.SceneKey) ||
           !ValidSha(request.PlanSha256) ||
           !ValidBatchId(request.ClientBatchId) ||
           !SamManualApiTokenPolicy.Accepts(request.ModelApiToken) ||
           !access.Status(request.ModelApiToken).LiveCallsPermitted)
            throw new InvalidOperationException("Owner consent, token, or backend paid gates missing.");
        await gate.WaitAsync(ct);
        try
        {
            var (source,scene,pts,plan,key)=await Context(
                studyId,role,request.TargetCoreFrames,request.MaxInFlight,ct);
            var exact=await PreflightAsync(studyId,role,
                request.TargetCoreFrames,request.MaxInFlight,ct);
            if(request.VideoSha256!=source.Sha256.ToLowerInvariant() ||
               request.SceneKey!=scene.Key ||
               request.PlanSha256!=key ||
               !double.IsFinite(request.MaximumSamCostUsd) ||
               request.MaximumSamCostUsd<exact.TotalSamEstimatedUsd ||
               request.MaximumSamCostUsd>0.500000001 ||
               exact.RemainingTrialSlots==0)
                throw new InvalidOperationException(
                    "Source/scene/quote/three-test spending limit changed.");
            // Prepare and VERIFY ALL clips before any billable call or trial slot.
            var batchId=request.ClientBatchId;
            var folder=BatchDir(batchId);
            if(Directory.Exists(folder))
                throw new InvalidOperationException(
                    "Batch ID was used before; no automatic or duplicate retry.");
            Directory.CreateDirectory(folder);
            var clipDir=Path.Combine(folder,"clips");
            var manifests=await clipBuilder.PrepareAsync(
                source,pts,plan,clipDir,ct);
            if(manifests.BilledFrames!=plan.BilledFrames ||
               manifests.OriginalPtsSha256!=plan.VerifySourcePts(pts) ||
               manifests.Clips.Count!=plan.PartCount)
                throw new InvalidDataException("Local clip preparation and quoted plan differ.");
            // Resolve and validate EVERY Spark-grounded prompt BEFORE
            // consuming a trial slot or reserving any potentially billable
            // provider request. A missing source-frame seed aborts the batch.
            var prompts=plan.Windows.Select(window=>Prompt(scene,window))
                .ToArray();
            ct.ThrowIfCancellationRequested();
            _=ReserveTrialSlot(batchId,exact);
            var created=DateTimeOffset.UtcNow;
            await WriteJson(Path.Combine(folder,"batch-started.json"),new{
                batchId,createdAtUtc=created,sourceSha256=source.Sha256,
                sceneKey=scene.Key,planSha256=key,
                protocol=Protocol,expectedParts=plan.PartCount,
                quotedMaximumUsd=exact.TotalSamEstimatedUsd,
                approvedByOwner=true,model="sam-3.1",
                noAutomaticRetry=true
            },CancellationToken.None);
            var results=new PartOutcome[plan.PartCount];
            using var slots=new SemaphoreSlim(plan.MaxInFlight);
            using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(30));
            var stopped=0;
            async Task Execute(SamAdaptiveWindow window)
            {
                var taken=false;
                try
                {
                    await slots.WaitAsync(deadline.Token);
                    taken=true;
                    if(Volatile.Read(ref stopped)!=0)
                    {
                        results[window.Part]=new("SKIPPED_AFTER_PART_FAILURE",null,null);
                        return;
                    }
                    var partDir=Path.Combine(folder,"part-"+window.Part.ToString("D2",
                        CultureInfo.InvariantCulture));
                    Directory.CreateDirectory(partDir);
                    var clip=manifests.Clips.Single(c=>c.Part==window.Part);
                    var prompt=prompts[window.Part];
                    // Write an immutable intended-attempt marker BEFORE
                    // reserving a provider budget or crossing the network.
                    // A crash is treated conservatively, never auto-retried.
                    await WriteJson(Path.Combine(partDir,"attempt-started.json"),new{
                        batchId,part=window.Part,sourceSha256=source.Sha256,
                        sceneKey=scene.Key,quotedPlanSha256=key,
                        clipSha256=clip.VideoSha256,clipFrames=clip.Frames,
                        promptSha256=Hash(prompt),
                        estimatedCostUsd=clip.BilledFrames*0.0002,
                        requestedAtUtc=DateTimeOffset.UtcNow,
                        status="ATTEMPT_PREPARED_NO_NETWORK_SENT_YET"
                    },CancellationToken.None);
                    // Existing Meta backend budget is reserved ONCE per part,
                    // never bypassed by the batch or manual browser token.
                    runtime.ReserveOrThrow(request.ModelApiToken);
                    await WriteJson(Path.Combine(partDir,"budget-reserved.json"),new{
                        part=window.Part,reservedAtUtc=DateTimeOffset.UtcNow,
                        noAutomaticRetry=true
                    },CancellationToken.None);
                    var t=Stopwatch.StartNew();
                    var progressLog=Path.Combine(partDir,"progress.jsonl");
                    async ValueTask Progress(SamVideoSseProgress p)
                    {
                        var line=JsonSerializer.Serialize(new{
                            timeUtc=DateTimeOffset.UtcNow,part=window.Part,
                            p.Stage,p.Events,p.DeltaEvents,p.OutputChars,
                            elapsedMs=t.ElapsedMilliseconds
                        })+"\n";
                        await File.AppendAllTextAsync(
                            progressLog,line,CancellationToken.None);
                    }
                    await WriteJson(Path.Combine(partDir,"provider-dispatch.json"),new{
                        part=window.Part,mayBeBillable=true,
                        dispatchedAtUtc=DateTimeOffset.UtcNow,
                        noAutomaticRetry=true
                    },CancellationToken.None);
                    await Progress(new("PART_PROVIDER_REQUEST_STARTED",0,0,0));
                    var provider=await transport.UploadAndSegmentAsync(
                        Path.Combine(clipDir,clip.RelativePath),prompt,
                        Progress,deadline.Token,request.ModelApiToken);
                    if(provider.FullResponseJson.Length is <15 or >85_000_000)
                        throw new InvalidDataException("SAM part response size invalid.");
                    // Strict provider envelope completed, prior to archiving.
                    _=SamFourWayMaskAssembler.OutputWire(provider.FullResponseJson);
                    var receipt=new SamAdaptivePartReceipt(
                        window.Part,window.InputStart,window.InputEndExclusive,
                        window.CoreStart,window.CoreEndExclusive,
                        clip.Frames,clip.BilledFrames,
                        source.Sha256.ToLowerInvariant(),scene.Key,
                        plan.VerifySourcePts(pts),clip.VideoSha256,
                        Hash(prompt),Hash(provider.FullResponseJson),
                        provider.ProviderResponseId,provider.BilledFrames,
                        DateTimeOffset.UtcNow,
                        "COMPLETED_PROVIDER_STREAM_PENDING_HUMAN_VISUAL_REVIEW");
                    await WriteImmutable(Path.Combine(partDir,"response.json"),
                        provider.FullResponseJson,CancellationToken.None);
                    await WriteJson(Path.Combine(partDir,"receipt.json"),
                        receipt,CancellationToken.None);
                    await Progress(new("PROVIDER_PART_ARCHIVED",0,0,
                        provider.FullResponseJson.Length));
                    results[window.Part]=new("COMPLETED",provider,receipt);
                }
                catch(Exception e)
                {
                    Interlocked.Exchange(ref stopped,1);
                    var failure=e switch{
                        OperationCanceledException => "SAM_PART_DEADLINE_OR_CANCELLED",
                        HttpRequestException => "SAM_PART_PROVIDER_HTTP_FAILURE",
                        SamVideoSseException => "SAM_PART_PROVIDER_SSE_FAILURE",
                        InvalidDataException => "SAM_PART_EVIDENCE_INVALID",
                        _ => "SAM_PART_FAILED_NO_AUTOMATIC_RETRY"
                    };
                    results[window.Part]=new(failure,null,null);
                    var path=Path.Combine(folder,"part-"+window.Part.ToString("D2",
                        CultureInfo.InvariantCulture));
                    Directory.CreateDirectory(path);
                    try{await WriteJson(Path.Combine(path,"failure.json"),
                        new{part=window.Part,status=failure,
                            recordedAtUtc=DateTimeOffset.UtcNow,
                            noAutomaticRetry=true},CancellationToken.None);}
                    catch(IOException){} // no second inference
                }
                finally{if(taken)slots.Release();}
            }
            // Task.WhenAll drains already-started, potentially billable
            // operations even when another part fails. No hidden retries.
            await Task.WhenAll(plan.Windows.Select(Execute));
            var completed=results.Count(r=>r?.State=="COMPLETED");
            var failures=results.Count(r=>r is not null &&
                r.State!="COMPLETED" && r.State!="SKIPPED_AFTER_PART_FAILURE");
            var skipped=results.Count(r=>r?.State=="SKIPPED_AFTER_PART_FAILURE");
            // Only attempts with a durable marker can represent actual
            // potentially billable calls. Queued/not-admitted parts do not.
            var started=Enumerable.Range(0,plan.PartCount).Count(i=>
                File.Exists(Path.Combine(folder,
                    "part-"+i.ToString("D2",CultureInfo.InvariantCulture),
                    "provider-dispatch.json")));
            var status="PARTIAL_PROVIDER_ATTEMPTS_REQUIRE_MANUAL_RECONCILIATION";
            SamAdaptiveMaskResult? derived=null;
            if(completed==plan.PartCount)
            {
                try
                {
                    var parts=results.Select((r,i)=>new SamAdaptivePartOutput(
                        i,plan.Windows[i].InputStart,plan.Windows[i].InputEndExclusive,
                        plan.Windows[i].CoreStart,plan.Windows[i].CoreEndExclusive,
                        r!.Receipt!.ResponseSha256,r.Provider!.FullResponseJson)).ToArray();
                    derived=SamAdaptiveMaskStitcher.Stitch(plan,parts);
                    await WriteJson(Path.Combine(folder,"derived-report.json"),
                        new{derived.Provenance,derived.SourceFrames,
                            derived.FramesWithMasks,derived.MissingCoreFrames,
                            derived.SeamsVerified,derived.Seams,
                            derived.DerivedResponseSha256,
                            nativeProviderResult=false},CancellationToken.None);
                    if(derived.SeamsVerified && derived.FramesWithMasks>0)
                    {
                        await WriteImmutable(Path.Combine(folder,"derived-response.json"),
                            derived.DerivedResponseJson,CancellationToken.None);
                        status="PROVIDER_PARTS_COMPLETE_DERIVED_MASKS_PENDING_HUMAN_REVIEW";
                    }
                    else status="PROVIDER_PARTS_COMPLETE_SEAM_OR_DETECTION_INCONCLUSIVE";
                }
                catch(InvalidDataException)
                {
                    status="PROVIDER_PARTS_COMPLETE_DERIVED_EVIDENCE_REJECTED";
                }
            }
            var report=new SamAdaptiveLiveReport(batchId,status,
                source.Sha256.ToLowerInvariant(),scene.Key,key,plan.PartCount,
                started,completed,failures,skipped,
                plan.SourceFrames,plan.BilledFrames,plan.EstimatedSamCostUsd,
                derived?.DerivedResponseSha256,
                derived?.FramesWithMasks,derived?.SeamsVerified,
                created,DateTimeOffset.UtcNow,
                "Each SAM part was independently submitted and archived; no automatic retry or fictitious human/scientific approval.");
            await WriteJson(Path.Combine(folder,"batch-report.json"),
                report,CancellationToken.None);
            return report;
        }
        finally{gate.Release();}
    }

    private sealed record PartOutcome(string State,
        SamApiCompletedResult? Provider,SamAdaptivePartReceipt? Receipt);

    public async Task<SamAdaptiveLiveReport?> ReadReportAsync(
        string batchId,CancellationToken ct)
    {
        if(!ValidBatchId(batchId))throw new ArgumentException("Invalid batch ID.");
        var path=Path.Combine(BatchDir(batchId),"batch-report.json");
        return File.Exists(path)?
            await SamVideoArchive.ReadJsonAsync<SamAdaptiveLiveReport>(path,ct):
            null;
    }
    public async Task<SamAdaptiveProgress> ReadProgressAsync(
        string batchId,CancellationToken ct)
    {
        if(!ValidBatchId(batchId))throw new ArgumentException("Invalid batch ID.");
        var folder=BatchDir(batchId);
        var attempt=Path.Combine(folder,"batch-started.json");
        if(!File.Exists(attempt))
            throw new FileNotFoundException("Unknown adaptive batch.");
        using var data=JsonDocument.Parse(await File.ReadAllTextAsync(attempt,ct));
        var total=data.RootElement.GetProperty("expectedParts").GetInt32();
        var rows=new List<SamAdaptivePartStatus>(total);
        for(var part=0;part<total;part++)
        {
            var path=Path.Combine(folder,"part-"+part.ToString("D2",CultureInfo.InvariantCulture));
            var state=File.Exists(Path.Combine(path,"receipt.json"))?"COMPLETED":
                File.Exists(Path.Combine(path,"failure.json"))?"FAILED":
                File.Exists(Path.Combine(path,"attempt-started.json"))?"IN_PROGRESS":"NOT_STARTED";
            var events=0;var deltas=0;var chars=0;
            var telemetry=Path.Combine(path,"progress.jsonl");
            if(File.Exists(telemetry))
            {
                try
                {
                    var last=File.ReadLines(telemetry).LastOrDefault(
                        line=>!string.IsNullOrWhiteSpace(line));
                    if(last is not null)
                    {
                        using var doc=JsonDocument.Parse(last);
                        var entry=doc.RootElement;
                        if(entry.TryGetProperty("Events",out var ev))
                            events=ev.GetInt32();
                        if(entry.TryGetProperty("DeltaEvents",out var de))
                            deltas=de.GetInt32();
                        if(entry.TryGetProperty("OutputChars",out var oc))
                            chars=oc.GetInt32();
                    }
                }
                catch(IOException){} // reader can collide with an active writer
                catch(JsonException){} // a partially flushed last line is not evidence
            }
            rows.Add(new(part,state,events,deltas,chars));
        }
        return new(batchId,total,rows.Count(x=>x.State!="NOT_STARTED"),
            rows.Count(x=>x.State=="COMPLETED"),
            rows.Count(x=>x.State=="FAILED"),rows);
    }
}
