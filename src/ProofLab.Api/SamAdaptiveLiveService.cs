using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

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
    ISamAdaptiveClipBuilder clipBuilder,MetaLiveAccess access,
    IWebHostEnvironment environment,IConfiguration config)
{
    public const string Protocol="PROOFLAB_SAM_ADAPTIVE_SSE_SOURCE_PTS_V1";
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
        // A read-only upper-bound quote must work before the first Spark call.
        // Exact grounded parts are quoted again after localization, before SAM.
        var source=await runtime.GetQuoteSourceAsync(studyId,role,ct);
        if(source.Codec is not ("h264" or "hevc" or "mpeg4") ||
           maxInFlight is <1 or >MaximumLiveConcurrentRequests)
            throw new ArgumentException("Unsupported video or concurrency.");
        var pts=await runtime.ReadSourceFramePtsAsync(source,ct);
        var scene=await scenes.TrackingForQuoteAsync(studyId,role,ct);
        var ideal=SamAdaptiveSegmentPlan.Create(pts.Length,targetCoreFrames,maxInFlight,
            alignToTwoFrameIntraKeyframes:true);
        SamAdaptiveGroundingDecision? grounded=null;
        if(scene is not null) {
            AssemblyPolicy.Validate(scene,source.Sha256,pts.Length);
            grounded=SamAdaptiveGroundedPlan.Select(pts.Length,scene,targetCoreFrames,maxInFlight);
        }
        var plan=grounded?.Plan??ideal;
        var ptsSha=plan.VerifySourcePts(pts);
        return new(Protocol,source.Sha256.ToLowerInvariant(),scene?.Key??"",
            ptsSha,PlanKey(source.Sha256,scene?.Key??"PENDING_SPARK",ptsSha,plan),
            Guid.NewGuid().ToString("N"),pts.Length,plan.PartCount,
            plan.MaxInFlight,plan.SubmittedFrames,plan.BilledFrames,
            plan.EstimatedSamCostUsd,0.50,null,access.ManualTokenCallsPermitted,
            scene is not null,scene is null,
            scene is null?"SAM_UPPER_BOUND_PENDING_SPARK_NO_INFERENCE_SENT":
                "VERIFIED_SPARK_SEEDS; SOURCE_FRAMES; NO_AUTOMATIC_RETRY",
            grounded?.IdealParts??ideal.PartCount,grounded?.VerifiedSparkSeedCount??0,
            grounded?.ReducedForSparseSparkSeeds??false);
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
               request.MaximumSamCostUsd>0.500000001)
                throw new InvalidOperationException(
                    "Source, scene or approved batch quote changed.");
            // Prepare and VERIFY ALL clips before any billable call or trial slot.
            var batchId=request.ClientBatchId;
            var folder=BatchDir(batchId);
            if(Directory.Exists(folder))
                throw new InvalidOperationException(
                    "Batch ID was used before; no automatic or duplicate retry.");
            Directory.CreateDirectory(folder);
            var created=DateTimeOffset.UtcNow;
            await WriteJson(Path.Combine(folder,"batch-preparing.json"),new{
                batchId,createdAtUtc=created,expectedParts=plan.PartCount
            },CancellationToken.None);
            var clipDir=Path.Combine(folder,"clips");
            SamAdaptiveClipManifest manifests;
            try {
                manifests=await clipBuilder.PrepareAsync(source,pts,plan,clipDir,ct);
            } catch {
                await WriteJson(Path.Combine(folder,"preparation-failed.json"),
                    new{finishedAtUtc=DateTimeOffset.UtcNow},CancellationToken.None);
                throw;
            }
            await WriteJson(Path.Combine(folder,"preparation-completed.json"),new{
                finishedAtUtc=DateTimeOffset.UtcNow,
                elapsedMs=(long)(DateTimeOffset.UtcNow-created).TotalMilliseconds
            },CancellationToken.None);
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
            await WriteJson(Path.Combine(folder,"batch-context.json"),
                new SamAdaptiveBatchContext(scene,request.TargetCoreFrames,request.MaxInFlight),
                CancellationToken.None);
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
            await WriteJson(Path.Combine(folder,"validation-started.json"),
                new{startedAtUtc=DateTimeOffset.UtcNow},CancellationToken.None);
            if(completed==plan.PartCount)
            {
                try
                {
                    var parts=results.Select((r,i)=>new SamAdaptivePartOutput(
                        i,plan.Windows[i].InputStart,plan.Windows[i].InputEndExclusive,
                        plan.Windows[i].CoreStart,plan.Windows[i].CoreEndExclusive,
                        r!.Receipt!.ResponseSha256,r.Provider!.FullResponseJson)).ToArray();
                    derived=await SamAdaptiveMaskStitcher.StitchAsync(plan,parts,config,CancellationToken.None);
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
                catch(Exception e) when(e is InvalidDataException or OperationCanceledException or System.ComponentModel.Win32Exception)
                {
                    status="PROVIDER_PARTS_COMPLETE_DERIVED_EVIDENCE_REJECTED";
                    await WriteJson(Path.Combine(folder,"validation-failed.json"),
                        new{reason=e is InvalidDataException ? "MASK_OR_INDEX_VALIDATION_FAILED" :
                            "LOCAL_MASK_WORKER_UNAVAILABLE",recordedAtUtc=DateTimeOffset.UtcNow},
                        CancellationToken.None);
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

    public async Task<SamVideoArtifact> ReadPlaybackAsync(
        string studyId,string role,string batchId,CancellationToken ct)
    {
        var report=await ReadReportAsync(batchId,ct) ??
            throw new FileNotFoundException("Batch report unavailable.");
        if(report.Status!="PROVIDER_PARTS_COMPLETE_DERIVED_MASKS_PENDING_HUMAN_REVIEW" ||
           report.DerivedSeamsVerified!=true || report.CompletedParts!=report.Parts)
            throw new InvalidDataException("Incomplete or inconclusive batch cannot drive overlays.");
        var source=await runtime.GetVerifiedSourceAsync(studyId,role,ct);
        if(source.Sha256!=report.SourceSha256)throw new InvalidDataException("Batch source mismatch.");
        var folder=BatchDir(batchId);
        var context=await SamVideoArchive.ReadJsonAsync<SamAdaptiveBatchContext>(
            Path.Combine(folder,"batch-context.json"),ct);
        var scene=context.Scene;
        var pts=await runtime.ReadSourceFramePtsAsync(source,ct);
        AssemblyPolicy.Validate(scene,source.Sha256,pts.Length);
        var plan=SamAdaptiveGroundedPlan.Select(pts.Length,scene,
            context.TargetCoreFrames,context.MaxInFlight).Plan;
        var ptsSha=plan.VerifySourcePts(pts);
        if(scene.Key!=report.SparkSceneKey || plan.PartCount!=report.Parts ||
           PlanKey(source.Sha256,scene.Key,ptsSha,plan)!=report.PlanSha256)
            throw new InvalidDataException("Batch plan identity mismatch.");
        var outputs=new List<SamAdaptivePartOutput>();
        foreach(var w in plan.Windows) {
            var part=Path.Combine(folder,"part-"+w.Part.ToString("D2",CultureInfo.InvariantCulture));
            var receipt=await SamVideoArchive.ReadJsonAsync<SamAdaptivePartReceipt>(
                Path.Combine(part,"receipt.json"),ct);
            var raw=await File.ReadAllTextAsync(Path.Combine(part,"response.json"),ct);
            if(receipt.Part!=w.Part || receipt.OriginalVideoSha256!=source.Sha256 ||
               receipt.SparkSceneKey!=scene.Key || receipt.SourcePtsSha256!=ptsSha ||
               receipt.InputStart!=w.InputStart || receipt.InputEndExclusive!=w.InputEndExclusive ||
               receipt.CoreStart!=w.CoreStart || receipt.CoreEndExclusive!=w.CoreEndExclusive ||
               receipt.ResponseSha256!=Hash(raw) || receipt.PromptSha256!=Hash(Prompt(scene,w)) ||
               receipt.Status!="COMPLETED_PROVIDER_STREAM_PENDING_HUMAN_VISUAL_REVIEW")
                throw new InvalidDataException("Part receipt mismatch.");
            outputs.Add(new(w.Part,w.InputStart,w.InputEndExclusive,
                w.CoreStart,w.CoreEndExclusive,receipt.ResponseSha256,raw));
        }
        var derived=await SamAdaptiveMaskStitcher.StitchAsync(plan,outputs,config,ct);
        var saved=await File.ReadAllTextAsync(Path.Combine(folder,"derived-response.json"),ct);
        if(!derived.SeamsVerified || derived.FramesWithMasks<=0 ||
           derived.DerivedResponseSha256!=report.DerivedResponseSha256 ||
           Hash(saved)!=derived.DerivedResponseSha256)
            throw new InvalidDataException("Derived masks failed verification.");
        var state=new SamVideoState("VERIFIED_META_SAM_SEGMENTS_REQUIRES_VISUAL_REVIEW",
            "sam-3.1","pendulum object",source.Sha256,report.PlanSha256,pts.Length,
            report.EstimatedSamUsd,derived.DerivedResponseSha256,null,false,
            "DERIVED_FROM_AUTHENTICATED_META_SEGMENTS_NOT_A_NATIVE_SINGLE_RESPONSE",
            batchId,"api-meta-adaptive",false,"private-segment-archive",scene.Key);
        var receiptAll=new SamVideoReceipt(report.PlanSha256,source.Sha256,source.VideoId,
            "sam-3.1","pendulum object",Protocol,report.RecordedAtUtc,
            derived.DerivedResponseSha256,"",null,pts.Length,pts,"",
            "COMPLETED_PROVIDER_PARTS_DERIVED_REQUIRES_VISUAL_REVIEW",
            report.EstimatedSamUsd,batchId,scene);
        return new(state,receiptAll,saved,await scenes.SuspensionAsync(studyId,role,scene,ct),
            Derivation:new(batchId,false,true,ptsSha,outputs.Select(x=>x.ResponseJsonSha256).ToArray()));
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
        var attempt=Path.Combine(folder,"batch-preparing.json");
        if(!File.Exists(attempt))attempt=Path.Combine(folder,"batch-started.json");
        if(!File.Exists(attempt))
            throw new FileNotFoundException("Unknown adaptive batch.");
        using var data=JsonDocument.Parse(await File.ReadAllTextAsync(attempt,ct));
        var total=data.RootElement.GetProperty("expectedParts").GetInt32();
        var created=data.RootElement.GetProperty("createdAtUtc").GetDateTimeOffset();
        var report=await ReadReportAsync(batchId,ct);
        var end=report?.RecordedAtUtc??DateTimeOffset.UtcNow;
        var preparationMs=(long)(end-created).TotalMilliseconds;
        var prep=Path.Combine(folder,"preparation-completed.json");
        var phase=File.Exists(Path.Combine(folder,"preparation-failed.json"))?"FAILED":"PREPARING";
        if(File.Exists(prep)) {
            using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(prep,ct));
            preparationMs=doc.RootElement.GetProperty("elapsedMs").GetInt64();phase="SAM";
        } else if(File.Exists(Path.Combine(folder,"batch-started.json"))) {preparationMs=0;phase="SAM";}
        long validationMs=0;
        var validation=Path.Combine(folder,"validation-started.json");
        if(File.Exists(validation)) {
            using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(validation,ct));
            validationMs=(long)(end-doc.RootElement.GetProperty("startedAtUtc").GetDateTimeOffset()).TotalMilliseconds;
            phase="VALIDATING";
        }
        if(report is not null)phase=report.DerivedSeamsVerified==true?"COMPLETED":"FAILED";
        var rows=new List<SamAdaptivePartStatus>(total);
        for(var part=0;part<total;part++)
        {
            var path=Path.Combine(folder,"part-"+part.ToString("D2",CultureInfo.InvariantCulture));
            var state=File.Exists(Path.Combine(path,"receipt.json"))?"COMPLETED":
                File.Exists(Path.Combine(path,"failure.json"))?"FAILED":
                File.Exists(Path.Combine(path,"attempt-started.json"))?"IN_PROGRESS":"NOT_STARTED";
            var events=0;var deltas=0;var chars=0;long elapsedMs=0;
            var telemetry=Path.Combine(path,"progress.jsonl");
            if(File.Exists(telemetry))
            {
                try
                {
                    foreach(var line in File.ReadLines(telemetry).Where(x=>!string.IsNullOrWhiteSpace(x))) {
                        try {
                            using var doc=JsonDocument.Parse(line);var entry=doc.RootElement;
                            if(entry.TryGetProperty("Events",out var ev))events=Math.Max(events,ev.GetInt32());
                            if(entry.TryGetProperty("DeltaEvents",out var de))deltas=Math.Max(deltas,de.GetInt32());
                            if(entry.TryGetProperty("OutputChars",out var oc))chars=Math.Max(chars,oc.GetInt32());
                            if(entry.TryGetProperty("elapsedMs",out var ms))elapsedMs=Math.Max(elapsedMs,ms.GetInt64());
                        }catch(JsonException){} // active writer may have a partial final line
                    }
                }
                catch(IOException){} // reader can collide with an active writer
                catch(JsonException){} // a partially flushed last line is not evidence
            }
            var dispatch=Path.Combine(path,"provider-dispatch.json");
            if(state=="IN_PROGRESS" && File.Exists(dispatch)) {
                using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(dispatch,ct));
                elapsedMs=(long)(DateTimeOffset.UtcNow-doc.RootElement.GetProperty("dispatchedAtUtc").GetDateTimeOffset()).TotalMilliseconds;
            }
            rows.Add(new(part,state,events,deltas,chars,Math.Max(0,elapsedMs)));
        }
        return new(batchId,total,rows.Count(x=>x.State!="NOT_STARTED"),
            rows.Count(x=>x.State=="COMPLETED"),
            rows.Count(x=>x.State=="FAILED"),rows,phase,Math.Max(0,preparationMs),Math.Max(0,validationMs),Math.Max(0,(long)(end-created).TotalMilliseconds));
    }
}
