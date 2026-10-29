using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using ProofLab.Api;
using ProofLab.Science;

namespace ProofLab.Tests;

/// <summary>Synthetic provider fixtures only. No HTTP client, API key or Meta transport is compiled here.</summary>
public sealed class PendulumSamCacheIntegrationTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string Raw = """{"object":"response","model":"sam-3.1","status":"completed","output":[]}""";

    private sealed class Runtime : ISamVideoRuntime
    {
        public required SamVideoSource Source { get; init; }
        public bool LiveCallsPermitted { get; set; } = true;
        public int Reservations { get; private set; }
        public bool ManualTokenAllowed { get; set; }
        public string? ReservedToken { get; private set; }
        public bool CanRunWithToken(string? token) => LiveCallsPermitted ||
            (ManualTokenAllowed && token is not null && SamManualApiTokenPolicy.Accepts(token));
        public void ReserveOrThrow(string? token) {
            if (!CanRunWithToken(token)) throw new InvalidOperationException("Test gates closed.");
            ReservedToken = token; Reservations++;
        }
        public Task<SamVideoSource> GetVerifiedSourceAsync(string studyId, string role, CancellationToken ct) =>
            Task.FromResult(Source);
        public Task<double[]> ReadFramePtsAsync(string path, CancellationToken ct) =>
            Task.FromResult(Enumerable.Range(0, 60).Select(i => i / 30d).ToArray());
        public Task CreateMp4Async(string source, string destination, CancellationToken ct) =>
            File.WriteAllTextAsync(destination, "SYNTHETIC TEST MEDIA", ct);
        public void ReserveOrThrow()
        {
            if (!LiveCallsPermitted) throw new InvalidOperationException("Test billing gates closed.");
            Reservations++;
        }
    }
    private sealed class Transport : ISamVideoTransport
    {
        public int Calls { get; private set; }
        public string ResultRaw { get; set; } = Raw;
        public string? LastConcept { get; private set; }
        public Func<CancellationToken, Task>? OnCall { get; set; }
        public async Task<SamApiCompletedResult> UploadAndSegmentAsync(string originalPath, string concept,
            Func<SamVideoSseProgress, ValueTask>? progress, CancellationToken ct, string? oneShotMetaToken = null)
        {
            Calls++; LastConcept = concept;
            if (OnCall is not null) await OnCall(ct);
            return new(ResultRaw, "SYNTHETIC-NOT-A-PROVIDER-RECEIPT", 60);
        }
    }
    private sealed class Environment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Offline tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(AppContext.BaseDirectory, "sam-test-data", Guid.NewGuid().ToString("N"));
        public Runtime Runtime { get; }
        public Transport Transport { get; } = new();
        public string Key => PendulumSamCache.CacheKey(Runtime.Source.Sha256);
        public string CacheRoot => Path.Combine(Root, ".private", "physics", "sam-cache", Key);
        public string Db => Path.Combine(Root, ".private", "physics", "sam-video-evidence.sqlite");
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Runtime = new() { Source = new(new string('a',64), "fixture-video",
                Path.Combine(Root,"original.mp4"), "h264", 640, 480, 2) };
        }
        public PendulumSamCache Open(IAssemblyScenes? scenes=null) => new(new Environment(Root), Runtime, Transport,
            new SamVideoEvidenceIndex(Root), NullLogger<PendulumSamCache>.Instance, scenes);
        public SamVideoRunRequest Request(string? mode = "api-meta") =>
            new(Runtime.Source.Sha256, true, true, 0.50, Mode: mode);
        public string Run(SamVideoArtifact artifact) => Path.Combine(CacheRoot, "runs", artifact.State.AnalysisId!);
        public async Task LegacyMarker(bool retry = false)
        {
            Directory.CreateDirectory(CacheRoot);
            var mp4 = Path.Combine(CacheRoot, "sam-upload.mp4");
            await File.WriteAllTextAsync(mp4, "SYNTHETIC TEST MEDIA");
            var a = new SamOriginalAttempt(PendulumSamCache.Protocol, Runtime.Source.Sha256,
                SamVideoArchive.Hash("SYNTHETIC TEST MEDIA"), 60, 0.012, DateTimeOffset.UtcNow,
                retry ? "SECOND_ATTEMPT_MAY_BE_BILLABLE_NO_AUTOMATIC_RETRY" : "MAY_BE_BILLABLE_NO_AUTOMATIC_RETRY");
            await SamVideoArchive.WriteExclusiveAsync(Path.Combine(CacheRoot,
                retry ? "manual-retry-started.json" : "attempt-started.json"), a, Ct);
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    [Fact]
    public async Task ManualTokenFlowsThroughAdmissionAndReservationWithoutBackendKey()
    {
        using var f = new Fixture();
        f.Runtime.LiveCallsPermitted = false;
        f.Runtime.ManualTokenAllowed = true;
        const string token = "LLM_FAKE_OFFLINE_TEST_ONLY_1234567890123456";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Open().RunOnceAsync("study", "primary", f.Request(), Ct));
        var result = await f.Open().RunOnceAsync("study", "primary", f.Request(), Ct, token);
        Assert.False(result.State.CacheHit);
        Assert.Equal(token, f.Runtime.ReservedToken);
        Assert.Equal(1, f.Runtime.Reservations);
        Assert.Equal(1, f.Transport.Calls);
        f.Runtime.ManualTokenAllowed = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Open().RunOnceAsync("study", "primary", f.Request(), Ct, token));
        Assert.Equal(1, f.Transport.Calls);
    }

    [Fact]
    public async Task DefaultCacheHit_ReopensSQLite_WithoutTransportOrBudgetOrConsent()
    {
        using var f = new Fixture();
        var first = await f.Open().RunOnceAsync("study", "primary", f.Request(), Ct);
        f.Runtime.LiveCallsPermitted = false;
        var restarted = f.Open();
        var hit = await restarted.RunOnceAsync("study", "primary",
            new(f.Runtime.Source.Sha256, false, false, 0), Ct);
        Assert.True(hit.State.CacheHit);
        Assert.Equal("sqlite", hit.State.Storage);
        Assert.Equal(first.State.AnalysisId, hit.State.AnalysisId);
        Assert.Equal(Raw, hit.RawResponseJson);
        Assert.Equal(1, f.Transport.Calls);
        Assert.Equal(1, f.Runtime.Reservations);
        Assert.True((await restarted.StatusAsync("study", "primary", Ct)).CacheHit);
    }

    [Fact]
    public async Task TwoFreshAnalyses_KeepDistinctImmutableArchives_ForIdenticalProviderOutput()
    {
        using var f = new Fixture();
        var api = f.Open();
        var first = await api.RunOnceAsync("study", "primary", f.Request(), Ct);
        var originalReceipt = await File.ReadAllTextAsync(Path.Combine(f.Run(first), "receipt.json"));
        var second = await api.RunOnceAsync("study", "primary", f.Request(), Ct);
        Assert.False(first.State.CacheHit);
        Assert.False(second.State.CacheHit);
        Assert.Equal(PendulumSamCache.FreshMode, second.State.Mode);
        Assert.NotEqual(first.State.AnalysisId, second.State.AnalysisId);
        Assert.Equal(first.RawResponseJson, second.RawResponseJson);
        Assert.Equal(originalReceipt, await File.ReadAllTextAsync(Path.Combine(f.Run(first), "receipt.json")));
        Assert.Equal(second.State.AnalysisId, (await f.Open().ReadArchiveAsync("study", "primary", Ct)).State.AnalysisId);
        Assert.Equal(2, f.Transport.Calls);
        using var db = new SqliteConnection("Data Source=" + f.Db + ";Pooling=False");
        db.Open();
        using var sql = db.CreateCommand();
        sql.CommandText = "SELECT count(*) FROM sam_video_evidence";
        Assert.Equal(2L, sql.ExecuteScalar());
    }

    [Fact]
    public async Task FreshMode_WithCachedEvidence_DoesNotBypassClosedGates()
    {
        using var f = new Fixture();
        var api = f.Open();
        await api.RunOnceAsync("study", "primary", f.Request(), Ct);
        f.Runtime.LiveCallsPermitted = false;
        var state = await api.StatusAsync("study", "primary", Ct, "api-meta");
        Assert.Equal("READY_FOR_FRESH_META_ANALYSIS", state.Status);
        Assert.False(state.CanRunLive);
        Assert.False(state.CacheHit);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            api.RunOnceAsync("study", "primary", f.Request(), Ct));
        Assert.Equal(1, f.Transport.Calls);
        Assert.Equal(1, f.Runtime.Reservations);
    }

    [Fact]
    public async Task StaleSourceConfirmation_CannotReceiveAHit()
    {
        using var f = new Fixture();
        var api = f.Open();
        await api.RunOnceAsync("study", "primary", f.Request(), Ct);
        await Assert.ThrowsAsync<ArgumentException>(() => api.RunOnceAsync("study", "primary",
            f.Request("api-meta-cache") with { VideoSha256 = new string('b',64) }, Ct));
        Assert.Equal(1, f.Transport.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TamperedResponseOrReceipt_FailsClosedEvenInFreshMode(bool receipt)
    {
        using var f = new Fixture();
        var api = f.Open();
        var first = await api.RunOnceAsync("study", "primary", f.Request(), Ct);
        var file = Path.Combine(f.Run(first), receipt ? "receipt.json" : "response.json");
        if (receipt)
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(first.Receipt with { EstimatedCostUsd = 0.02 }, Json));
        else await File.AppendAllTextAsync(file, " ");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Open().RunOnceAsync("study", "primary", f.Request(), Ct));
        Assert.Equal(1, f.Transport.Calls);
        Assert.Equal(1, f.Runtime.Reservations);
    }

    [Fact]
    public async Task BrowserCancellation_AfterProviderCompletion_DoesNotDiscardPaidResult()
    {
        using var f = new Fixture();
        using var browser = new CancellationTokenSource();
        f.Transport.OnCall = token => {
            browser.Cancel();
            Assert.False(token.IsCancellationRequested);
            return Task.CompletedTask;
        };
        var result = await f.Open().RunOnceAsync("study", "primary", f.Request(), browser.Token);
        Assert.True(browser.IsCancellationRequested);
        Assert.NotNull(result.State.AnalysisId);
        Assert.Equal(result.State.AnalysisId, (await f.Open().ReadArchiveAsync("study", "primary", Ct)).State.AnalysisId);
        Assert.Equal(1, f.Transport.Calls);
    }

    [Fact]
    public async Task CompletedJournal_RecoversInterruptedFileCommit_WithoutAnotherCall()
    {
        using var f = new Fixture();
        var first = await f.Open().RunOnceAsync("study", "primary", f.Request(), Ct);
        var run = f.Run(first);
        await File.WriteAllTextAsync(Path.Combine(run, "completed-result.json"),
            JsonSerializer.Serialize(new SamVideoArchive.CompletedBundle(first.Receipt, first.RawResponseJson), Json));
        // Simulate a process stopping after response but before receipt/index.
        File.Delete(Path.Combine(run, "receipt.json"));
        File.Delete(f.Db);
        f.Runtime.LiveCallsPermitted = false;
        var hit = await f.Open().RunOnceAsync("study", "primary", f.Request("api-meta-cache"), Ct);
        Assert.True(hit.State.CacheHit);
        Assert.Equal(first.State.AnalysisId, hit.State.AnalysisId);
        Assert.True(File.Exists(Path.Combine(run, "receipt.json")));
        Assert.Equal(1, f.Transport.Calls);
    }

    [Fact]
    public async Task PartialLegacyResponseWithoutReceipt_IsNeverInferredOrChargedAgain()
    {
        using var f = new Fixture();
        Directory.CreateDirectory(f.CacheRoot);
        await File.WriteAllTextAsync(Path.Combine(f.CacheRoot, "response.json"), Raw);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Open().RunOnceAsync("study", "primary", f.Request(), Ct));
        Assert.Equal(0, f.Transport.Calls);
        Assert.Equal(0, f.Runtime.Reservations);
    }

    [Fact]
    public async Task OlderCache_CannotHideNewerUncertainAttempt()
    {
        using var f = new Fixture();
        var api = f.Open();
        var first = await api.RunOnceAsync("study", "primary", f.Request(), Ct);
        f.Transport.OnCall = _ => throw new HttpRequestException("SYNTHETIC FAILURE");
        await Assert.ThrowsAsync<HttpRequestException>(() => api.RunOnceAsync("study", "primary", f.Request(), Ct));
        var restarted = f.Open();
        var status = await restarted.StatusAsync("study", "primary", Ct, "api-meta");
        Assert.Equal("NEW_ANALYSIS_REQUIRES_MANUAL_RECONCILIATION", status.Status);
        Assert.False(status.CanRunLive);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            restarted.RunOnceAsync("study", "primary", f.Request(), Ct));
        var hit = await restarted.RunOnceAsync("study", "primary", f.Request("api-meta-cache"), Ct);
        Assert.True(hit.State.CacheHit);
        Assert.Equal(first.State.AnalysisId, hit.State.AnalysisId);
        Assert.Equal(2, f.Transport.Calls);
        Assert.Equal(2, f.Runtime.Reservations);
    }

    [Fact]
    public async Task LegacyReconciliation_RetainsExactConsent_AndPermanentThirdAttemptLock()
    {
        using var f = new Fixture();
        await f.LegacyMarker();
        var api = f.Open();
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.RunOnceAsync("study", "primary", f.Request(), Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.RunOnceAsync("study", "primary",
            f.Request() with { OwnerApprovesOneReconciledRetry = true, PreviousAttemptBillingEvidence = "unverified" }, Ct));
        Assert.Equal(0, f.Runtime.Reservations);
        var result = await api.RunOnceAsync("study", "primary", f.Request() with {
            OwnerApprovesOneReconciledRetry = true,
            PreviousAttemptBillingEvidence = ManualRetryReconciliationPolicy.OwnerAuthorization
        }, Ct);
        Assert.True(File.Exists(Path.Combine(f.CacheRoot, "attempt-started.json")));
        Assert.True(File.Exists(Path.Combine(f.CacheRoot, "manual-retry-started.json")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Open().RunOnceAsync("study", "primary", f.Request(), Ct));
        var hit = await f.Open().RunOnceAsync("study", "primary", f.Request("api-meta-cache"), Ct);
        Assert.Equal(result.State.AnalysisId, hit.State.AnalysisId);
        Assert.Equal(1, f.Transport.Calls);
    }

    [Fact]
    public async Task IndependentServiceInstances_CannotReserveConcurrentPaidAttempts()
    {
        using var f = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport.OnCall = async _ => { entered.SetResult(); await release.Task; };
        var first = f.Open().RunOnceAsync("study", "primary", f.Request(), Ct);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                f.Open().RunOnceAsync("study", "primary", f.Request(), Ct));
            Assert.Equal(1, f.Runtime.Reservations);
        }
        finally { release.SetResult(); }
        await first;
        Assert.Equal(1, f.Transport.Calls);
    }

    [Fact]
    public async Task InvalidModeAndMissingConsent_StopBeforeTransport()
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            f.Open().RunOnceAsync("study", "primary", f.Request("legacy-live"), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Open().RunOnceAsync("study", "primary",
            f.Request() with { ConfirmOneBillableSamCall = false }, Ct));
        Assert.Equal(0, f.Transport.Calls);
        Assert.Equal(0, f.Runtime.Reservations);
    }

    [Fact]
    public async Task PreindexedLegacyArchive_PreservesItsExistingAnalysisIdentity()
    {
        using var f = new Fixture();
        var first = await f.Open().RunOnceAsync("study", "primary", f.Request(), Ct);
        var legacy = first.Receipt with { AnalysisId = null };
        var receiptText = JsonSerializer.Serialize(legacy, Json);
        await File.WriteAllTextAsync(Path.Combine(f.CacheRoot, "receipt.json"), receiptText);
        await File.WriteAllTextAsync(Path.Combine(f.CacheRoot, "response.json"), Raw);
        Directory.Delete(f.Run(first), true);
        File.Delete(f.Db);
        var arbitraryId = "ffffffffffffffffffffffffffffffff";
        var index = new SamVideoEvidenceIndex(f.Root);
        await index.RegisterVerifiedAsync(new(arbitraryId, f.Runtime.Source.Sha256, f.Key,
            PendulumSamCache.SamModel, PendulumSamCache.Protocol, SamVideoArchive.Hash(PendulumSamCache.Prompt),
            SamVideoArchive.Hash(Raw), SamVideoArchive.Hash(receiptText), f.Key, 60,
            legacy.CapturedAtUtc.ToUniversalTime().ToString("O"), SamVideoEvidenceIndex.Provenance), Ct);
        f.Runtime.LiveCallsPermitted = false;
        var hit = await f.Open().RunOnceAsync("study", "primary", f.Request("api-meta-cache"), Ct);
        Assert.True(hit.State.CacheHit);
        Assert.Equal(arbitraryId, hit.State.AnalysisId);
        Assert.Equal(1, f.Transport.Calls);
    }

    [Fact]
    public void OfflineInspectorOrNonMonotonicPts_CannotBecomeVerifiedEvidence()
    {
        using var f = new Fixture();
        var pts = Enumerable.Range(0,60).Select(i => i / 30d).ToArray();
        var receipt = new SamVideoReceipt(f.Key, f.Runtime.Source.Sha256, "video",
            PendulumSamCache.SamModel, PendulumSamCache.Prompt, PendulumSamCache.Protocol,
            DateTimeOffset.UtcNow, SamVideoArchive.Hash(Raw), "synthetic", 60, 60, pts,
            new string('b',64), "USER_PASTED_JSON", 0.012);
        Assert.Throws<InvalidDataException>(() => SamVideoArchive.Validate(receipt, f.Runtime.Source, f.Key, Raw, null));
        pts[2] = pts[1];
        Assert.Throws<InvalidDataException>(() => SamVideoArchive.Validate(receipt with {
            ProviderEvidenceStatus = "COMPLETED_PROVIDER_RESPONSE_REQUIRES_VISUAL_REVIEW"
        }, f.Runtime.Source, f.Key, Raw, null));
    }

    private const string MaskWire = "<0f>0<|box;x1=211;y1=228;x2=270;y2=254;w=320;h=334|>" +
        "<|mask;x=0;y=0;data=27,60,~!!!!M!0c[0o91w?q1!pIH4pPRVp2B3'7`e.ioeAf6-k/#Xd8%dX9x(|>";
    private static string MaskResponse => JsonSerializer.Serialize(new {
        model="sam-3.1",status="completed",
        output=new[]{new {type="message",content=new[]{new {type="output_text",text=MaskWire}}}}
    });

    private static async Task<SamVideoArchive.Stored> WriteOldTarget(Fixture f, string raw)
    {
        var key=PendulumSamCache.LegacyCacheKey(f.Runtime.Source.Sha256);
        var archive=new SamVideoArchive(f.Root,new SamVideoEvidenceIndex(f.Root),legacyDirect:true);
        var id=Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(archive.RunFolder(key,id));
        var receipt=new SamVideoReceipt(key,f.Runtime.Source.Sha256,"legacy-video",
            PendulumSamCache.SamModel,PendulumSamCache.LegacyPrompt,PendulumSamCache.LegacyProtocol,
            DateTimeOffset.UtcNow,SamVideoArchive.Hash(raw),"SYNTHETIC-LEGACY",60,60,
            Enumerable.Range(0,60).Select(i=>i/30d).ToArray(),new string('b',64),
            "COMPLETED_PROVIDER_RESPONSE_REQUIRES_VISUAL_REVIEW",.012,id);
        return await archive.CommitAsync(f.Runtime.Source,key,receipt,raw);
    }

    [Fact]
    public async Task NewTargetUsesMaterialIndependentPromptAndDistinctProtocol()
    {
        using var f=new Fixture();
        var result=await f.Open().RunOnceAsync("study","primary",f.Request(),Ct);
        Assert.Equal("pendulum bob",f.Transport.LastConcept);
        Assert.Equal("pendulum bob",result.Receipt.Concept);
        Assert.Equal("PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V3_GENERIC_BOB",result.Receipt.RequestProtocol);
        Assert.NotEqual(PendulumSamCache.LegacyCacheKey(f.Runtime.Source.Sha256),result.Receipt.CacheKey);
    }

    [Fact]
    public async Task LegacyMasksRemainAFreeHitWithTheirOriginalIdentityAndPrompt()
    {
        using var f=new Fixture();
        var old=await WriteOldTarget(f,MaskResponse);
        f.Runtime.LiveCallsPermitted=false;
        var api=f.Open();
        var status=await api.StatusAsync("study","primary",Ct);
        Assert.True(status.CacheHit);
        var hit=await api.RunOnceAsync("study","primary",new(f.Runtime.Source.Sha256,false,false,0),Ct);
        Assert.Equal(old.Entry.AnalysisId,hit.State.AnalysisId);
        Assert.Equal(JsonSerializer.Serialize(old.Receipt,Json),JsonSerializer.Serialize(hit.Receipt,Json));
        Assert.Equal(old.Raw,hit.RawResponseJson);
        Assert.Equal(PendulumSamCache.LegacyPrompt,hit.Receipt.Concept);
        Assert.Equal(old.Entry.AnalysisId,(await api.ReadArchiveAsync("study","primary",Ct)).State.AnalysisId);
        Assert.Equal(0,f.Transport.Calls);
        Assert.Equal(0,f.Runtime.Reservations);
    }

    [Fact]
    public async Task FreshAfterLegacyMasksUsesGenericTargetAndPreservesOldFiles()
    {
        using var f=new Fixture();
        var old=await WriteOldTarget(f,MaskResponse);
        var fresh=await f.Open().RunOnceAsync("study","primary",f.Request(),Ct);
        Assert.Equal(PendulumSamCache.Prompt,fresh.Receipt.Concept);
        Assert.NotEqual(old.Entry.AnalysisId,fresh.State.AnalysisId);
        var oldArchive=new SamVideoArchive(f.Root,new SamVideoEvidenceIndex(f.Root),legacyDirect:true);
        var restored=await oldArchive.LoadAsync(f.Runtime.Source,old.Receipt.CacheKey,Ct);
        Assert.Equal(old.Raw,Assert.Single(restored).Raw);
        Assert.Equal(fresh.State.AnalysisId,(await f.Open().StatusAsync("study","primary",Ct)).AnalysisId);
        Assert.Equal(1,f.Transport.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<0f>no matches")]
    [InlineData("<0f>0<|box;x1=0;y1=0;x2=5;y2=5;w=10;h=10|>")]
    [InlineData("<0f>0<|box;x1=0;y1=0;x2=5;y2=5;w=10;h=10|><|mask;x=0;y=0;data=6,6,|>")]
    public async Task LegacyZeroMatchesOrNoMaskDoesNotHideTheNewTarget(string text)
    {
        using var f=new Fixture();
        var raw=JsonSerializer.Serialize(new {model="sam-3.1",status="completed",
            output=new[]{new {content=new[]{new {type="output_text",text}}}}});
        await WriteOldTarget(f,raw);
        var api=f.Open();
        var status=await api.StatusAsync("study","primary",Ct);
        Assert.False(status.CacheHit);
        Assert.True(status.CanRunLive);
        Assert.Equal(PendulumSamCache.Prompt,status.Concept);
        await Assert.ThrowsAsync<FileNotFoundException>(()=>api.ReadArchiveAsync("study","primary",Ct));
        var result=await api.RunOnceAsync("study","primary",f.Request("api-meta-cache"),Ct);
        Assert.False(result.State.CacheHit);
        Assert.Equal(PendulumSamCache.Prompt,result.Receipt.Concept);
        Assert.Equal(1,f.Transport.Calls);
    }

    [Fact]
    public async Task ChangingPromptCannotBypassAnUncertainLegacyCharge()
    {
        using var f=new Fixture();
        var key=PendulumSamCache.LegacyCacheKey(f.Runtime.Source.Sha256);
        var folder=Path.Combine(f.Root,".private","physics","sam-cache",key);
        Directory.CreateDirectory(folder);
        await SamVideoArchive.WriteExclusiveAsync(Path.Combine(folder,"attempt-started.json"),
            new SamOriginalAttempt(PendulumSamCache.LegacyProtocol,f.Runtime.Source.Sha256,new string('b',64),
                60,.012,DateTimeOffset.UtcNow,"MAY_BE_BILLABLE_NO_AUTOMATIC_RETRY"),Ct);
        var status=await f.Open().StatusAsync("study","primary",Ct);
        Assert.False(status.CanRunLive);
        Assert.Contains("RECONCILIATION",status.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Open().RunOnceAsync("study","primary",f.Request(),Ct));
        Assert.Equal(0,f.Runtime.Reservations);
    }

    [Fact]
    public async Task OldProcessLockPreventsConcurrentGenericCharge()
    {
        using var f=new Fixture();
        var key=PendulumSamCache.LegacyCacheKey(f.Runtime.Source.Sha256);
        var folder=Path.Combine(f.Root,".private","physics","sam-cache",key);
        Directory.CreateDirectory(folder);
        using(var held=new FileStream(Path.Combine(folder,".run.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Open().RunOnceAsync("study","primary",f.Request(),Ct));
        Assert.Equal(0,f.Transport.Calls);
        await f.Open().RunOnceAsync("study","primary",f.Request(),Ct);
        Assert.Equal(1,f.Transport.Calls);
    }

    [Fact]
    public async Task LegacyReceiptCannotBeRelabeledAsGeneric()
    {
        using var f=new Fixture();
        var old=await WriteOldTarget(f,MaskResponse);
        Assert.Throws<InvalidDataException>(()=>SamVideoArchive.Validate(old.Receipt,
            f.Runtime.Source,old.Receipt.CacheKey,old.Raw,old.Entry.AnalysisId));
        Assert.Throws<InvalidDataException>(()=>SamVideoArchive.Validate(old.Receipt with {Concept=PendulumSamCache.Prompt},
            f.Runtime.Source,old.Receipt.CacheKey,old.Raw,old.Entry.AnalysisId,legacyDirect:true));
    }

    private sealed class MissingGrounding : IAssemblyScenes
    {
        public Task<SamSceneDefinition?> ApprovedAsync(string id,string role,CancellationToken ct) =>
            Task.FromResult<SamSceneDefinition?>(null);
    }

    [Fact]
    public async Task CompletedEmptyTextSearchRequiresGroundingInsteadOfAFreeEmptyHit()
    {
        using var f=new Fixture();
        var old=await f.Open().RunOnceAsync("study","primary",f.Request(),Ct);
        var before=await File.ReadAllTextAsync(Path.Combine(f.Run(old),"receipt.json"));
        var service=f.Open(new MissingGrounding());
        var status=await service.StatusAsync("study","primary",Ct);
        Assert.False(status.CacheHit);Assert.True(status.RecognitionRequired);
        Assert.Equal("NOT_ANALYZED_WITH_SAM",status.Status);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>
            service.RunOnceAsync("study","primary",f.Request("api-meta-cache"),Ct));
        Assert.Equal("SPARK_TARGET_REQUIRED",error.Message);
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Runtime.Reservations);
        Assert.Equal(before,await File.ReadAllTextAsync(Path.Combine(f.Run(old),"receipt.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingDirectMasksRemainFreeWithoutSparkAndStillBlockFreshUngroundedCalls(bool legacy)
    {
        using var f=new Fixture();
        string analysis;
        if(legacy)analysis=(await WriteOldTarget(f,MaskResponse)).Entry.AnalysisId;
        else {
            f.Transport.ResultRaw=MaskResponse;
            analysis=(await f.Open().RunOnceAsync("study","primary",f.Request(),Ct)).State.AnalysisId!;
        }
        var calls=f.Transport.Calls;var reservations=f.Runtime.Reservations;
        f.Runtime.LiveCallsPermitted=false;
        var service=f.Open(new MissingGrounding());
        var status=await service.StatusAsync("study","primary",Ct);
        Assert.True(status.CacheHit);Assert.False(status.RecognitionRequired);
        var hit=await service.RunOnceAsync("study","primary",new(f.Runtime.Source.Sha256,false,false,0),Ct);
        Assert.Equal(analysis,hit.State.AnalysisId);
        Assert.Equal(analysis,(await service.ReadArchiveAsync("study","primary",Ct)).State.AnalysisId);
        Assert.Equal(calls,f.Transport.Calls);Assert.Equal(reservations,f.Runtime.Reservations);
        var fresh=await service.StatusAsync("study","primary",Ct,"api-meta");
        Assert.False(fresh.CacheHit);Assert.True(fresh.RecognitionRequired);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            service.RunOnceAsync("study","primary",f.Request(),Ct));
        Assert.Equal(calls,f.Transport.Calls);
    }

    private sealed class SceneHistory : IAssemblyScenes
    {
        public required SamSceneDefinition Current { get; set; }
        public required SamSceneDefinition[] History { get; set; }
        public Task<SamSceneDefinition?> ApprovedAsync(string id,string role,CancellationToken ct) =>
            Task.FromResult<SamSceneDefinition?>(Current);
        public Task<SamSceneDefinition[]> TrackingHistoryAsync(string id,string role,CancellationToken ct,bool quote=false) =>
            Task.FromResult(History);
    }
    private static SamSceneDefinition HistoricalScene(string sha,char identity,int offset)
    {
        var scene=new SamSceneDefinition("",sha,new string(identity,64),new string('b',64),
            640,480,"simple_pendulum",[new("1","object")],
            [new(6,"object",100+offset,100,160+offset,180),new(30,"object",110+offset,110,170+offset,190)],
            null,null,AssemblyPolicy.GroundingOrigin);
        return scene with {Key=AssemblyPolicy.DefinitionKey(scene)};
    }

    [Fact]
    public async Task SceneRefreshKeepsOlderUsableMasksAndTheirExactReceipt()
    {
        using var f=new Fixture();
        var a=HistoricalScene(f.Runtime.Source.Sha256,'c',0);
        var b=HistoricalScene(f.Runtime.Source.Sha256,'d',20);
        var scenes=new SceneHistory {Current=a,History=[a]};
        var api=f.Open(scenes);
        f.Transport.ResultRaw=MaskResponse;
        var first=await api.RunOnceAsync("study","primary",f.Request() with {SceneKey=a.Key},Ct);
        scenes.Current=b;scenes.History=[a,b];
        f.Transport.ResultRaw=Raw;
        var empty=await api.RunOnceAsync("study","primary",f.Request() with {SceneKey=b.Key},Ct);
        Assert.NotEqual(first.State.AnalysisId,empty.State.AnalysisId);
        var before=f.Runtime.Reservations;
        var status=await api.StatusAsync("study","primary",Ct);
        Assert.True(status.CacheHit);
        Assert.Equal(first.State.AnalysisId,status.AnalysisId);
        var hit=await api.RunOnceAsync("study","primary",f.Request("api-meta-cache"),Ct);
        Assert.Equal(JsonSerializer.Serialize(first.Receipt,Json),JsonSerializer.Serialize(hit.Receipt,Json));
        Assert.Equal(first.State.AnalysisId,(await api.ReadArchiveAsync("study","primary",Ct)).State.AnalysisId);
        Assert.Equal(before,f.Runtime.Reservations);
    }

    [Fact]
    public async Task NewSparkSceneCannotBypassAnUnresolvedSamAttempt()
    {
        using var f=new Fixture();
        var a=HistoricalScene(f.Runtime.Source.Sha256,'c',0);
        var b=HistoricalScene(f.Runtime.Source.Sha256,'d',20);
        var scenes=new SceneHistory {Current=a,History=[a]};
        var api=f.Open(scenes);
        f.Transport.OnCall=_=>throw new HttpRequestException("Synthetic interrupted inference");
        await Assert.ThrowsAsync<HttpRequestException>(()=>
            api.RunOnceAsync("study","primary",f.Request() with {SceneKey=a.Key},Ct));
        scenes.Current=b;scenes.History=[a,b];
        f.Transport.OnCall=null;
        var status=await api.StatusAsync("study","primary",Ct,"api-meta");
        Assert.Contains("RECONCILIATION",status.Status);
        Assert.False(status.CanRunLive);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            api.RunOnceAsync("study","primary",f.Request() with {SceneKey=b.Key},Ct));
        Assert.Equal(1,f.Runtime.Reservations);Assert.Equal(1,f.Transport.Calls);
    }

    [Fact]
    public async Task FullVideoSceneWithoutSamMasksIsNotAUsefulCacheHit()
    {
        using var f=new Fixture();
        var a=HistoricalScene(f.Runtime.Source.Sha256,'c',0);
        var scenes=new SceneHistory {Current=a,History=[a]};
        var api=f.Open(scenes);
        await api.RunOnceAsync("study","primary",f.Request() with {SceneKey=a.Key},Ct);
        var status=await api.StatusAsync("study","primary",Ct);
        Assert.False(status.CacheHit);Assert.False(status.RecognitionRequired);
        Assert.Equal("NOT_ANALYZED_WITH_SAM",status.Status);
        Assert.NotNull((await api.ReadArchiveAsync("study","primary",Ct)).Receipt);
    }

    [Fact]
    public async Task NewSceneCannotReplaceTheExplicitSceneAuthorizedForSam()
    {
        using var f=new Fixture();
        var a=HistoricalScene(f.Runtime.Source.Sha256,'c',0);
        var b=HistoricalScene(f.Runtime.Source.Sha256,'d',20);
        var scenes=new SceneHistory {Current=b,History=[a,b]};
        await Assert.ThrowsAsync<ArgumentException>(()=>
            f.Open(scenes).RunOnceAsync("study","primary",f.Request() with {SceneKey=a.Key},Ct));
        Assert.Equal(0,f.Runtime.Reservations);Assert.Equal(0,f.Transport.Calls);
    }
}
