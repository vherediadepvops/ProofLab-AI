using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using ProofLab.Api;
using ProofLab.Science;
using Xunit;

namespace ProofLab.Workflow.Tests;

// Real service/store/archives; all media, measurements and provider responses are
// synthetic. No network transport or real credential is constructed.
public sealed class MuseReviewWorkflowTests
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private const string Study="11111111111111111111111111111111";
    private const string Token="LLM_FAKE_OFFLINE_TEST_ONLY_1234567890123456";
    private static readonly CancellationToken Ct=CancellationToken.None;
    private sealed class Env(string root):IWebHostEnvironment {
        public string ApplicationName {get;set;}="Offline";
        public string EnvironmentName {get;set;}="Testing";
        public string ContentRootPath {get;set;}=root;
        public string WebRootPath {get;set;}=root;
        public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();
        public IFileProvider WebRootFileProvider {get;set;}=new NullFileProvider();
    }
    private sealed class Runtime:ISamVideoRuntime {
        public SamVideoSource Source {get;}=new(new string('a',64),"fixture-video","synthetic.mp4","h264",640,480,2);
        public bool LiveCallsPermitted=>true;
        public void ReserveOrThrow(){}
        public Task<SamVideoSource> GetVerifiedSourceAsync(string studyId,string role,CancellationToken ct)=>Task.FromResult(Source);
        public Task<double[]> ReadFramePtsAsync(string path,CancellationToken ct)=>Task.FromResult(Enumerable.Range(0,60).Select(i=>i/30d).ToArray());
        public Task CreateMp4Async(string source,string destination,CancellationToken ct)=>File.WriteAllTextAsync(destination,"SYNTHETIC TEST MEDIA",ct);
    }
    private sealed class SamTransport:ISamVideoTransport {
        public Task<SamApiCompletedResult> UploadAndSegmentAsync(string path,string concept,
            Func<SamVideoSseProgress,ValueTask>? progress,CancellationToken ct,string? oneShotMetaToken=null)
            =>Task.FromResult(new SamApiCompletedResult("""{"object":"response","model":"sam-3.1","status":"completed","output":[]}""","SYNTHETIC-NOT-PROVIDER",60));
    }
    private sealed class Worker:ISamPeriodWorker {
        public int Calls;
        public Task<string> MeasureAsync(object input,CancellationToken ct) {
            Calls++;
            var value=JsonSerializer.SerializeToElement(input,Json);
            var analysis=value.GetProperty("archive").GetProperty("receipt").GetProperty("analysisId").GetString();
            return Task.FromResult(JsonSerializer.Serialize(new {
                source=new {analysisId=analysis,objectId="0",studyId=value.GetProperty("studyId"),role="baseline",
                    originalVideoSha256=value.GetProperty("videoSha256"),pivotRevisionId=value.GetProperty("pivotRevisionId"),
                    providerResponseSha256=value.GetProperty("archive").GetProperty("receipt").GetProperty("responseJsonSha256")},
                input=new {framePtsSeconds=new[]{0d,.1,.2},centroids=new[]{new{frameIndex=0,x=100d,y=100d}},
                    sourceWidth=640,sourceHeight=480,imageWidth=640,imageHeight=480,pivot=new{x=.5,y=.1}},
                assessment=new {status="CANDIDATE",periodSeconds=1.415,statisticalStandardUncertaintySeconds=.001,
                    detectedFrames=3,totalFrames=3,completeCycles=8,usedCycles=8},
                limitations=new {es="Evidencia sintética, sin certificación.",en="Synthetic evidence, not certification."}
            },Json));
        }
    }
    private sealed class MuseTransport:IMuseExperimentTransport {
        public int Calls;
        public Task<KnotProviderResult> EvaluateAsync(string input,string token,CancellationToken ct) {
            Calls++;Assert.Equal(Token,token);
            var text=new MuseText("Hipótesis condicional.","Conditional hypothesis.");
            using var supplied=JsonDocument.Parse(input);
            var option=supplied.RootElement.GetProperty("experimentOptions")[0];
            var answer=new MuseInvestigatorAnswer("PROPOSE_EXPERIMENT",text,text,text,text,text,
                option.GetProperty("nextLengthMeters").GetDouble(),option.GetProperty("periodSeconds").GetDouble(),
                4,new("PENDULUM_COMPATIBLE",text));
            var raw=JsonSerializer.Serialize(new {id="synthetic-muse",model=MuseExperimentPolicy.Model,status="completed",
                output=new[]{new{type="message",content=new[]{new{type="output_text",text=JsonSerializer.Serialize(answer,Json)}}}},
                usage=new{input_tokens=1000,output_tokens=100}},Json);
            return Task.FromResult(new KnotProviderResult(raw,"synthetic-muse",1000,100));
        }
    }
    private sealed class Fixture:IDisposable {
        public string Root {get;}=Path.Combine(AppContext.BaseDirectory,"muse-workflow-tests",Guid.NewGuid().ToString("N"));
        public MuseExperimentStore Store {get;}
        public Worker Worker {get;}=new();
        public MuseTransport Transport {get;}=new();
        public PendulumDirectorWorkflow Director {get;}
        public MuseExperimentService Service {get;}
        public MusePrepareRequest Request {get;private set;}=null!;
        private readonly Runtime runtime=new();
        private readonly PendulumSamCache sam;
        public Fixture() {
            var env=new Env(Root);Store=new(env);Director=new(env,null!,Store);
            sam=new(env,runtime,new SamTransport(),new SamVideoEvidenceIndex(Root),NullLogger<PendulumSamCache>.Instance);
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["Meta:AllowPaidRequests"]="true",["Meta:CreditsConfirmedByOwner"]="true",["Meta:OwnerApprovedPaidRequests"]="true"
            }).Build();
            Service=new(Director,sam,null!,Worker,Store,new MetaLiveAccess(config,new MetaCallSafetyBudget()),Transport,null!);
        }
        public string StudyFile(string name)=>Path.Combine(Root,".private","physics","director",Study,name);
        public async Task Initialize() {
            Directory.CreateDirectory(Path.GetDirectoryName(StudyFile("session.json"))!);
            var session=new DirectorSession(Study,.5,DateTimeOffset.UtcNow.AddMinutes(-2),"READY","OFFLINE",null,null,null,null,null,15,280);
            await File.WriteAllTextAsync(StudyFile("session.json"),JsonSerializer.Serialize(session,Json));
            var baseline=new DirectorVideoEvidence("baseline","fixture-video","synthetic-pendulum.mp4",100000,
                runtime.Source.Sha256,DateTimeOffset.UtcNow.AddMinutes(-1),new("h264",640,480,2,30,"mp4"),null,null,false,false,"DEFERRED");
            await File.WriteAllTextAsync(StudyFile("baseline.json"),JsonSerializer.Serialize(baseline,Json));
            var result=await sam.RunOnceAsync(Study,"baseline",new(runtime.Source.Sha256,true,true,.5,Mode:"api-meta"),Ct);
            Request=new(runtime.Source.Sha256,result.Receipt.AnalysisId!,null);
        }
        public async Task Review() {
            var review=new DirectorHumanReview(DateTimeOffset.UtcNow,runtime.Source.Sha256,true,"SYNTHETIC_SAVED_HUMAN_REVIEW");
            await File.WriteAllTextAsync(StudyFile("human-review.json"),JsonSerializer.Serialize(review,Json));
        }
        public async Task<MuseExperimentState> Evaluate(MusePrepareRequest? request=null) {
            var observation=await Service.PrepareAsync(Study,request??Request,Ct);
            return await Service.EvaluateAsync(Study,new(observation.Id,Token,true,true,.05),Ct);
        }
        public long ObservationCount() {
            using var db=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=Path.Combine(Root,".private","physics","muse-experiments.sqlite"),Pooling=false}.ToString());
            db.Open();using var command=db.CreateCommand();command.CommandText="SELECT count(*) FROM muse_observations";
            return (long)command.ExecuteScalar()!;
        }
        public void Dispose(){if(Directory.Exists(Root))Directory.Delete(Root,true);}
    }

    [Fact]
    public async Task LegacyInterpretationRequiresExplicitChoiceButAnAlreadyFrozenLegacyAnswerSurvivesReload() {
        using var f=new Fixture();await f.Initialize();var before=await f.Evaluate();
        Assert.Null(before.Prediction);
        var original=before.Observation!;
        Assert.False(JsonDocument.Parse(original.PayloadJson).RootElement.GetProperty("humanObjectReviewed").GetBoolean());
        await f.Review();
        Assert.Equal(original,await f.Service.PrepareAsync(Study,f.Request,Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.FreezeAsync(Study,new(original.Id,before.Evaluation!.ResponseId),Ct));
        // Simulate an immutable prediction stored by the previous release.
        f.Store.Freeze(original,before.Evaluation!);
        var frozen=await f.Service.FreezeAsync(Study,new(original.Id,before.Evaluation!.ResponseId),Ct);
        Assert.Equal(original,frozen.Observation);Assert.Equal(before.Evaluation,frozen.Evaluation);
        Assert.NotNull(frozen.Prediction);Assert.Equal(1,f.ObservationCount());Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Worker.Calls);
        Assert.Equal(frozen.Prediction,f.Service.State(Study).Prediction);
        Assert.Equal(frozen.Prediction,new MuseExperimentStore(new Env(f.Root)).Prediction(Study));
        var again=await f.Service.FreezeAsync(Study,new(original.Id,before.Evaluation.ResponseId),Ct);
        Assert.Equal(frozen.Prediction,again.Prediction);
        Assert.Equal(original.PayloadJson,f.Store.Observation(original.Id)!.PayloadJson);
    }

    [Fact]
    public async Task FreezeWithoutSavedReviewRemainsBlocked() {
        using var f=new Fixture();await f.Initialize();var before=await f.Evaluate(f.Request with {Purpose="prediction",PlannedFollowupLengthMeters=.8});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.FreezeAsync(Study,
            new(before.Observation!.Id,before.Evaluation!.ResponseId),Ct));
        Assert.Null(f.Service.State(Study).Prediction);Assert.Equal(1,f.ObservationCount());
    }

    [Fact]
    public async Task ChangedParametersCannotFreezeAndDoNotAppendABlankObservation() {
        using var f=new Fixture();await f.Initialize();var before=await f.Evaluate(f.Request with {Purpose="prediction",PlannedFollowupLengthMeters=.8});await f.Review();
        var node=JsonNode.Parse(await File.ReadAllTextAsync(f.StudyFile("session.json")))!;
        node["baselineLengthMeters"]=.6;await File.WriteAllTextAsync(f.StudyFile("session.json"),node.ToJsonString());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.FreezeAsync(Study,
            new(before.Observation!.Id,before.Evaluation!.ResponseId),Ct));
        Assert.Equal(1,f.ObservationCount());Assert.Equal(1,f.Worker.Calls);Assert.Equal(1,f.Transport.Calls);
        Assert.Equal(before.Evaluation,f.Service.State(Study,"prediction").Evaluation);Assert.Null(f.Service.State(Study).Prediction);
    }

    [Fact]
    public async Task LegacyReviewOnlyBlankSnapshotRecoversCompletedOriginalWithoutMutatingHistory() {
        using var f=new Fixture();await f.Initialize();var before=await f.Evaluate();await f.Review();
        var original=before.Observation!;var node=JsonNode.Parse(original.PayloadJson)!;
        node["identity"]!["objectReview"]=true;node["humanObjectReviewed"]=true;
        var payload=node.ToJsonString(Json);var key=KnotPolicy.Hash(node["identity"]!.ToJsonString(Json));
        f.Store.SaveObservation(original with {Id=key,CreatedAtUtc=DateTimeOffset.UtcNow,PayloadJson=payload,PayloadSha256=KnotPolicy.Hash(payload)});
        Assert.Equal(key,f.Store.LatestObservation(Study)!.Id);
        var recovered=f.Service.State(Study);Assert.Equal(original,recovered.Observation);Assert.Equal(before.Evaluation,recovered.Evaluation);
        Assert.Equal(original,await f.Service.PrepareAsync(Study,f.Request,Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.FreezeAsync(Study,new(original.Id,before.Evaluation!.ResponseId),Ct));
        // Simulate an immutable prediction stored by the previous release.
        f.Store.Freeze(original,before.Evaluation!);
        var frozen=await f.Service.FreezeAsync(Study,new(original.Id,before.Evaluation!.ResponseId),Ct);
        Assert.Equal(original.Id,frozen.Prediction!.ObservationId);Assert.Equal(2,f.ObservationCount());
        Assert.Equal(1,f.Transport.Calls);Assert.Equal(1,f.Worker.Calls);Assert.NotNull(f.Store.Observation(key));
    }

    [Theory]
    [InlineData("analysisId")][InlineData("pivotRevisionId")][InlineData("workerHash")]
    [InlineData("videoSha256")][InlineData("responseSha256")][InlineData("baselineLengthMeters")]
    public async Task RecoveryNeverSubstitutesEvaluationForChangedEvidence(string field) {
        using var f=new Fixture();await f.Initialize();var before=await f.Evaluate();
        var original=before.Observation!;var node=JsonNode.Parse(original.PayloadJson)!;
        node["identity"]!["objectReview"]=true;
        node["identity"]![field]=field=="baselineLengthMeters"?JsonValue.Create(.6):JsonValue.Create("different");
        var payload=node.ToJsonString(Json);var key=KnotPolicy.Hash(node["identity"]!.ToJsonString(Json));
        var changed=f.Store.SaveObservation(original with {Id=key,CreatedAtUtc=DateTimeOffset.UtcNow,PayloadJson=payload,PayloadSha256=KnotPolicy.Hash(payload)});
        Assert.Equal(changed,f.Service.State(Study).Observation);Assert.Null(f.Service.State(Study).Evaluation);
        Assert.Equal(1,f.Transport.Calls);
    }

    [Theory]
    [InlineData("IN_FLIGHT_NO_RETRY")]
    [InlineData("FAILED_REQUIRES_RECONCILIATION")]
    public async Task RecoveryDoesNotHideAnAttemptForTheNewObservation(string attempt)
    {
        using var f=new Fixture();await f.Initialize();var before=await f.Evaluate();
        var original=before.Observation!;var node=JsonNode.Parse(original.PayloadJson)!;
        node["identity"]!["objectReview"]=true;node["humanObjectReviewed"]=true;
        var payload=node.ToJsonString(Json);var key=KnotPolicy.Hash(node["identity"]!.ToJsonString(Json));
        var current=f.Store.SaveObservation(original with {Id=key,CreatedAtUtc=DateTimeOffset.UtcNow,
            PayloadJson=payload,PayloadSha256=KnotPolicy.Hash(payload)});
        f.Store.BeginAttempt(current);
        if(attempt!="IN_FLIGHT_NO_RETRY")f.Store.Fail(key,attempt);
        var state=f.Service.State(Study);
        Assert.Equal(current,state.Observation);Assert.Equal(attempt,state.AttemptStatus);Assert.Null(state.Evaluation);
    }

    [Fact]
    public async Task ReviewCompatibilityRequiresBooleanForwardTransition()
    {
        using var f=new Fixture();await f.Initialize();var original=await f.Service.PrepareAsync(Study,f.Request,Ct);
        var payload=JsonNode.Parse(original.PayloadJson)!;
        var current=payload["identity"]!.DeepClone();
        current["objectReview"]=true;
        Assert.True(MuseObservationIdentity.MatchesCurrent(original,JsonSerializer.SerializeToElement(current,Json)));
        current["objectReview"]="true";
        Assert.False(MuseObservationIdentity.MatchesCurrent(original,JsonSerializer.SerializeToElement(current,Json)));
        current.AsObject().Remove("objectReview");
        Assert.False(MuseObservationIdentity.MatchesCurrent(original,JsonSerializer.SerializeToElement(current,Json)));
        current["objectReview"]=false;payload["identity"]!["objectReview"]=true;
        Assert.False(MuseObservationIdentity.MatchesCurrent(original with {PayloadJson=payload.ToJsonString(Json)},
            JsonSerializer.SerializeToElement(current,Json)));
    }
    [Fact]
    public async Task ExplicitPredictionKeepsOriginalInterpretationAndFreezesOnlyChosenLength() {
        using var f=new Fixture();await f.Initialize();var interpretation=await f.Evaluate();
        Assert.Null(f.Service.State(Study,"prediction").Observation);
        var chosen=await f.Service.PrepareAsync(Study,f.Request with {Purpose="prediction",PlannedFollowupLengthMeters=.6},Ct);
        Assert.NotEqual(interpretation.Observation!.Id,chosen.Id);
        var evaluated=await f.Service.EvaluateAsync(Study,new(chosen.Id,Token,true,true,.05),Ct);
        Assert.Equal(.6,evaluated.Evaluation!.Answer.NextLengthMeters);
        Assert.Equal(interpretation.Evaluation,f.Service.State(Study).Evaluation);
        Assert.Equal(chosen,f.Service.State(Study,"prediction").Observation);
        await f.Review();
        var frozen=await f.Service.FreezeAsync(Study,new(chosen.Id,evaluated.Evaluation.ResponseId),Ct);
        Assert.Equal(chosen.Id,frozen.Prediction!.ObservationId);
        using var payload=JsonDocument.Parse(frozen.Prediction.PayloadJson);
        Assert.Equal(.6,payload.RootElement.GetProperty("prediction").GetProperty("nextLengthMeters").GetDouble());
        Assert.Equal(interpretation.Observation,f.Service.State(Study).Observation);
        Assert.Equal(interpretation.Evaluation,f.Service.State(Study).Evaluation);
        Assert.Equal(frozen.Prediction,f.Service.State(Study).Prediction);
        Assert.Equal(frozen.Prediction,f.Service.State(Study,"prediction").Prediction);
        Assert.Equal(chosen,new MuseExperimentStore(new Env(f.Root)).PredictionDraft(Study));
        var reviewed=await f.Service.ConfirmTrackingReviewAsync(Study,new(true,f.Request),Ct);
        Assert.True(reviewed.HumanReview!.BobConfirmed);
        Assert.Equal(interpretation.Evaluation,f.Service.State(Study).Evaluation);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.PrepareAsync(Study,
            f.Request with {Purpose="prediction",PlannedFollowupLengthMeters=.9},Ct));
        Assert.Equal(2,f.Transport.Calls);
    }

    [Fact]
    public async Task ChangingChosenLengthDoesNotReuseAnAnswerForAnotherLengthAndCanRecoverExactCachedChoice() {
        using var f=new Fixture();await f.Initialize();await f.Review();
        var first=await f.Service.PrepareAsync(Study,f.Request with {Purpose="prediction",PlannedFollowupLengthMeters=.6},Ct);
        var evaluated=await f.Service.EvaluateAsync(Study,new(first.Id,Token,true,true,.05),Ct);
        var second=await f.Service.PrepareAsync(Study,f.Request with {Purpose="prediction",PlannedFollowupLengthMeters=.8},Ct);
        Assert.NotEqual(first.Id,second.Id);Assert.Null(f.Service.State(Study,"prediction").Evaluation);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.FreezeAsync(Study,new(first.Id,evaluated.Evaluation!.ResponseId),Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.EvaluateAsync(Study,new(first.Id,Token,true,true,.05),Ct));
        var recovered=await f.Service.PrepareAsync(Study,f.Request with {Purpose="prediction",PlannedFollowupLengthMeters=.6},Ct);
        Assert.Equal(first,recovered);Assert.Equal(evaluated.Evaluation,f.Service.State(Study,"prediction").Evaluation);
        Assert.Equal(first,new MuseExperimentStore(new Env(f.Root)).PredictionDraft(Study));
        var reused=await f.Service.EvaluateAsync(Study,new(first.Id,Token,true,true,.05),Ct);
        Assert.Equal(evaluated.Evaluation,reused.Evaluation);Assert.Equal(1,f.Transport.Calls);
        Assert.Equal(2,f.ObservationCount());
    }

    [Fact]
    public async Task ChosenSeventyToEightyCentimetersSupportsLessThanFifteenPercentChange() {
        using var f=new Fixture();await f.Initialize();
        var session=JsonNode.Parse(await File.ReadAllTextAsync(f.StudyFile("session.json")))!;
        session["baselineLengthMeters"]=.7;await File.WriteAllTextAsync(f.StudyFile("session.json"),session.ToJsonString());
        var choice=await f.Service.PrepareAsync(Study,f.Request with {Purpose="prediction",PlannedFollowupLengthMeters=.8},Ct);
        var evaluated=await f.Service.EvaluateAsync(Study,new(choice.Id,Token,true,true,.05),Ct);
        Assert.Equal(.8,evaluated.Evaluation!.Answer.NextLengthMeters);
        Assert.Equal(1.415*Math.Sqrt(.8/.7),evaluated.Evaluation.Answer.PeriodSeconds!.Value,10);
    }

    [Theory]
    [InlineData(null)][InlineData(.09)][InlineData(2.01)][InlineData(.5)]
    public async Task MissingOutOfRangeOrUnchangedLengthCannotPreparePrediction(double? length) {
        using var f=new Fixture();await f.Initialize();
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.PrepareAsync(Study,
            f.Request with {Purpose="prediction",PlannedFollowupLengthMeters=length},Ct));
        Assert.Null(f.Service.State(Study,"prediction").Observation);
        Assert.Equal(0,f.Worker.Calls);Assert.Equal(0,f.Transport.Calls);
    }

    [Fact]
    public async Task ChoiceRequiresExplicitPurposeAndCannotBeChangedAfterVideoTwo() {
        using var f=new Fixture();await f.Initialize();
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.PrepareAsync(Study,
            f.Request with {PlannedFollowupLengthMeters=.8},Ct));
        Assert.Throws<ArgumentException>(()=>f.Service.State(Study,"unknown"));
        var prepared=await f.Service.PrepareAsync(Study,f.Request with {Purpose="prediction",PlannedFollowupLengthMeters=.8},Ct);
        var evaluated=await f.Service.EvaluateAsync(Study,new(prepared.Id,Token,true,true,.05),Ct);
        await f.Review();await f.Service.FreezeAsync(Study,new(prepared.Id,evaluated.Evaluation!.ResponseId),Ct);
        var followup=JsonNode.Parse(await File.ReadAllTextAsync(f.StudyFile("baseline.json")))!;
        followup["role"]="followup";followup["sha256"]=new string('b',64);followup["uploadedAtUtc"]=DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(f.StudyFile("followup.json"),followup.ToJsonString());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.PrepareAsync(Study,
            f.Request with {Purpose="prediction",PlannedFollowupLengthMeters=.8},Ct));
        Assert.Equal(1,f.Transport.Calls);
    }

    [Fact]
    public async Task ChosenLengthIsAuthenticatedAndCannotBeSubstitutedByTheModel() {
        using var f=new Fixture();await f.Initialize();
        var choice=await f.Service.PrepareAsync(Study,f.Request with {Purpose="prediction",PlannedFollowupLengthMeters=.6},Ct);
        using var payload=JsonDocument.Parse(choice.PayloadJson);
        Assert.Equal(.6,payload.RootElement.GetProperty("identity").GetProperty("plannedFollowupLengthMeters").GetDouble());
        using var input=JsonDocument.Parse(MuseExperimentPolicy.BuildInput(choice));
        Assert.Equal(.6,input.RootElement.GetProperty("operatorSelectedLengthMeters").GetDouble());
        Assert.Equal(1,input.RootElement.GetProperty("experimentOptions").GetArrayLength());
        var text=new MuseText("Hipótesis condicional.","Conditional hypothesis.");
        var wrong=new MuseInvestigatorAnswer("PROPOSE_EXPERIMENT",text,text,text,text,text,
            .8,1.415*Math.Sqrt(1.6),4,new("PENDULUM_COMPATIBLE",text));
        Assert.Throws<FormatException>(()=>MuseExperimentPolicy.Parse(JsonSerializer.Serialize(wrong,Json),payload.RootElement));
    }

}
