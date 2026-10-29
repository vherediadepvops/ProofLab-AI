using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using ProofLab.Api;
using ProofLab.Science;

namespace ProofLab.Tests;
public sealed class MuseExperimentTests
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private const string Study="11111111111111111111111111111111";
    private const string Token="LLM_SYNTHETIC_OFFLINE_TEST_ONLY";
    private static MuseText Text=>new("Hipótesis condicional, sin incertidumbre total.","Conditional hypothesis, without total uncertainty.");
    private static MuseInvestigatorAnswer Answer(string decision="PROPOSE_EXPERIMENT",bool classified=true)=>
        new(decision,Text,Text,Text,Text,Text,decision=="PROPOSE_EXPERIMENT"?.8:null,
            decision=="PROPOSE_EXPERIMENT"?1.415*Math.Sqrt(.8/.5):null,decision=="PROPOSE_EXPERIMENT"?4:null,
            classified?new MusePhenomenon(decision=="PROPOSE_EXPERIMENT"?"PENDULUM_COMPATIBLE":"INCONCLUSIVE",Text):null);
    private static MuseObservation Observation(string status="CANDIDATE",string? version=null) {
        var identity=JsonSerializer.SerializeToElement(new {protocol=version??MuseExperimentPolicy.Version,studyId=Study,videoSha256=new string('a',64),analysisId="synthetic-sam",status},Json);
        var payload=JsonSerializer.Serialize(new {identity,baselineLengthMeters=.5,
            measurement=new {assessment=new {status,periodSeconds=1.415,statisticalStandardUncertaintySeconds=.001}}},Json);
        return new(KnotPolicy.Hash(identity.GetRawText()),Study,new string('a',64),"synthetic-sam",DateTimeOffset.UtcNow,payload,KnotPolicy.Hash(payload));
    }
    private static string Input() {
        using var observation=JsonDocument.Parse(Observation().PayloadJson);
        return JsonSerializer.Serialize(new {experimentOptions=MuseExperimentPolicy.ExperimentOptions(observation.RootElement)},Json);
    }
    [Fact] public void PrecomputedOptionsDoNotConfuseBaselineWithPrediction() {
        using var doc=JsonDocument.Parse(Observation().PayloadJson);
        var options=MuseExperimentPolicy.ExperimentOptions(doc.RootElement);
        Assert.Equal(3,options.Length);
        Assert.All(options,p=>Assert.Equal(1.415*Math.Sqrt(p.NextLengthMeters/.5),p.PeriodSeconds,12));
        Assert.DoesNotContain(options,p=>p.PeriodSeconds==1.415);
        Assert.Throws<FormatException>(()=>Parse(Answer() with {NextLengthMeters=.75,PeriodSeconds=1.415}));
        Assert.Throws<FormatException>(()=>Parse(Answer() with {NextLengthMeters=.9,PeriodSeconds=1.415*Math.Sqrt(1.8)}));
    }

    [Theory]
    [InlineData("INCONCLUSIVE","SAM_PIVOT_ARC_VS_LINE_V1")]
    [InlineData("COMPATIBLE_WITH_SIMPLE_PENDULUM","UNKNOWN")]
    public void LegacyV2PeriodicityWithoutVerifiedPendularConsistencyCannotFreeze(string motionStatus,string method) {
        using var original=JsonDocument.Parse(Observation(version:"MUSE_SAM_INVESTIGATOR_V2").PayloadJson);
        var payload=JsonSerializer.SerializeToElement(new {
            identity=original.RootElement.GetProperty("identity"),baselineLengthMeters=.5,
            measurement=new {assessment=new {status="CANDIDATE",periodSeconds=1.415,
                statisticalStandardUncertaintySeconds=.001,motion=new {method,status=motionStatus}}}
        },Json);
        Assert.Empty(MuseExperimentPolicy.ExperimentOptions(payload));
        Assert.Throws<FormatException>(()=>MuseExperimentPolicy.Parse(JsonSerializer.Serialize(Answer(),Json),payload));
        Assert.Null(MuseExperimentPolicy.Parse(JsonSerializer.Serialize(Answer("REPEAT_RECORDING"),Json),payload).PeriodSeconds);
        Assert.Null(MuseExperimentPolicy.Parse(JsonSerializer.Serialize(Answer("INCONCLUSIVE"),Json),payload).PeriodSeconds);
    }
    [Fact] public void LegacyV2CompatibleMotionSupportsOnlyTheExistingConditionalPrediction() {
        using var original=JsonDocument.Parse(Observation(version:"MUSE_SAM_INVESTIGATOR_V2").PayloadJson);
        var payload=JsonSerializer.SerializeToElement(new {
            identity=original.RootElement.GetProperty("identity"),baselineLengthMeters=.5,
            measurement=new {assessment=new {status="CANDIDATE",periodSeconds=1.415,
                statisticalStandardUncertaintySeconds=.001,
                motion=new {method="SAM_PIVOT_ARC_VS_LINE_V1",status="COMPATIBLE_WITH_SIMPLE_PENDULUM"}}}
        },Json);
        Assert.Equal(3,MuseExperimentPolicy.ExperimentOptions(payload).Length);
        Assert.Equal(Answer(),MuseExperimentPolicy.Parse(JsonSerializer.Serialize(Answer(),Json),payload));
    }
    [Fact] public void LegacyPredictionWithoutMotionKeepsItsExactPayloadAndHashAfterRestart() {
        using var f=new Fixture();var store=f.Open();var o=store.SaveObservation(Observation(version:"MUSE_SAM_INVESTIGATOR_V2"));var a=Answer(classified:false);
        Assert.DoesNotContain("\"motion\"",o.PayloadJson);
        store.BeginAttempt(o);store.SaveRaw(o.Id,Envelope(a));store.Complete(o,Evaluation(o,a));
        var before=store.Prediction(Study)!;
        var newIdentity=JsonSerializer.SerializeToElement(new {protocol=MuseExperimentPolicy.Version,
            studyId=Study,workerHash="new-worker-with-motion"},Json);
        var nextPayload=JsonSerializer.Serialize(new {identity=newIdentity,baselineLengthMeters=.5,
            measurement=new {assessment=new {status="CANDIDATE",periodSeconds=1.415,
                motion=new {method="SAM_PIVOT_ARC_VS_LINE_V1",status="INCONCLUSIVE"}}}},Json);
        store.SaveObservation(new(KnotPolicy.Hash(newIdentity.GetRawText()),Study,o.VideoSha256,o.AnalysisId,
            DateTimeOffset.UtcNow,nextPayload,KnotPolicy.Hash(nextPayload)));
        var after=f.Open().Prediction(Study)!;
        Assert.Equal(before.PayloadJson,after.PayloadJson);Assert.Equal(before.Sha256,after.Sha256);
        Assert.Equal(o.Id,after.ObservationId);
    }
    [Fact] public void ExplicitBudgetExtensionPreservesSpentRequestsAndConcurrencyLimit() {
        var budget=new MetaCallSafetyBudget(2,4);
        Assert.Equal(2,budget.Used);
        Assert.Equal(2,budget.Snapshot(true,true,true,true).RequestsRemaining);
        var allowed=0;
        Parallel.For(0,20,_=>{if(budget.TryReserve(true,true,true,true))Interlocked.Increment(ref allowed);});
        Assert.Equal(2,allowed);Assert.Equal(4,budget.Used);
        Assert.Equal(0,budget.Snapshot(true,true,true,true).RequestsRemaining);
        Assert.Equal(0,new MetaCallSafetyBudget(2,1).Snapshot(true,true,true,true).RequestsRemaining);
        Assert.Throws<ArgumentOutOfRangeException>(()=>new MetaCallSafetyBudget(0,6));
        Assert.Equal(2,new MetaCallSafetyBudget().Snapshot(true,true,true,true).MaxRequestsPerProcess);
    }
    private static string Envelope(MuseInvestigatorAnswer answer)=>JsonSerializer.Serialize(new {
        id="synthetic-not-provider",model=MuseExperimentPolicy.Model,status="completed",
        output=new[]{new {type="message",content=new[]{new {type="output_text",text=JsonSerializer.Serialize(answer,Json)}}}},
        usage=new {input_tokens=1000,output_tokens=100}},Json);
    private static MuseInvestigatorAnswer Parse(MuseInvestigatorAnswer a,string status="CANDIDATE") {
        using var doc=JsonDocument.Parse(Observation(status).PayloadJson);
        return MuseExperimentPolicy.Parse(JsonSerializer.Serialize(a,Json),doc.RootElement);
    }
    [Fact] public void ProposalUsesMeasuredPeriodAndExplicitPracticalBand() {
        var a=Parse(Answer());var o=Observation();var raw=Envelope(a);
        var e=new MuseEvaluation(Study,o.Id,"synthetic-not-provider",MuseExperimentPolicy.Model,
            KnotPolicy.Hash(raw),a,1000,100,DateTimeOffset.UtcNow,.001675);
        var p=JsonSerializer.SerializeToElement(MuseExperimentPolicy.FreezePayload(o,e,DateTimeOffset.UtcNow),Json).GetProperty("prediction");
        Assert.Equal(1.415*Math.Sqrt(1.6),p.GetProperty("periodSeconds").GetDouble(),10);
        Assert.Equal(.001*Math.Sqrt(1.6),p.GetProperty("statisticalUncertaintySeconds").GetDouble(),10);
        Assert.Contains("NOT_CONFIDENCE_INTERVAL",p.GetProperty("criterion").GetString());
    }
    [Fact] public void FabricatedOrUnusablePredictionsAreRejected() {
        Assert.Throws<FormatException>(()=>Parse(Answer(),"INCONCLUSIVE"));
        Assert.Throws<FormatException>(()=>Parse(Answer() with {PeriodSeconds=9}));
        Assert.Throws<FormatException>(()=>Parse(Answer() with {NextLengthMeters=.51}));
        Assert.Throws<FormatException>(()=>Parse(Answer() with {TolerancePercent=50}));
        Assert.Throws<FormatException>(()=>Parse(Answer() with {Limitations=new("","English")}));
        Assert.Throws<FormatException>(()=>Parse(Answer("INCONCLUSIVE") with {PeriodSeconds=1.8}));
    }
    [Theory][InlineData("INCONCLUSIVE")][InlineData("REPEAT_RECORDING")]
    public void InsufficientEvidenceCanRemainWithoutPrediction(string decision) {
        Assert.Null(Parse(Answer(decision),"INCONCLUSIVE").PeriodSeconds);
    }
    [Fact] public void UnknownJsonFieldsAreNotAcceptedAsScientificEvidence() {
        using var doc=JsonDocument.Parse(Observation().PayloadJson);
        var json=JsonSerializer.Serialize(Answer(),Json).TrimEnd('}')+",\"inventedConfidence\":0.99}";
        Assert.Throws<JsonException>(()=>MuseExperimentPolicy.Parse(json,doc.RootElement));
    }
    private sealed class Env(string root):IWebHostEnvironment {
        public string ApplicationName {get;set;}="Offline";
        public string EnvironmentName {get;set;}="Testing";
        public string ContentRootPath {get;set;}=root;
        public string WebRootPath {get;set;}=root;
        public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();
        public IFileProvider WebRootFileProvider {get;set;}=new NullFileProvider();
    }
    private sealed class Fixture:IDisposable {
        public readonly string Root=Path.Combine(AppContext.BaseDirectory,"muse-test-data",Guid.NewGuid().ToString("N"));
        public MuseExperimentStore Open()=>new(new Env(Root));
        public SqliteConnection Database() {
            var c=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=Path.Combine(Root,".private","physics","muse-experiments.sqlite"),Pooling=false}.ToString());c.Open();return c;
        }
        public void Dispose(){if(Directory.Exists(Root))Directory.Delete(Root,true);}
    }
    private static MuseEvaluation Evaluation(MuseObservation o,MuseInvestigatorAnswer a)=>new(Study,o.Id,
        "synthetic-not-provider",MuseExperimentPolicy.Model,KnotPolicy.Hash(Envelope(a)),a,1000,100,DateTimeOffset.UtcNow,.001675);
    [Fact] public void FrozenPredictionSurvivesRestartAndCannotBeRewritten() {
        using var f=new Fixture();var store=f.Open();var o=store.SaveObservation(Observation());var a=Answer();
        store.BeginAttempt(o);store.SaveRaw(o.Id,Envelope(a));store.Complete(o,Evaluation(o,a));
        var p=store.Prediction(Study)!;Assert.Equal(p,f.Open().Prediction(Study));
        Assert.Equal("COMPLETED",store.AttemptStatus(o.Id));Assert.DoesNotContain(Token,p.PayloadJson);
        using var db=f.Database();using var cmd=db.CreateCommand();
        cmd.CommandText="UPDATE muse_predictions SET payload='{}'";
        Assert.Throws<SqliteException>(()=>cmd.ExecuteNonQuery());
        cmd.CommandText="DELETE FROM muse_observations";
        Assert.Throws<SqliteException>(()=>cmd.ExecuteNonQuery());
        Assert.Throws<SqliteException>(()=>store.BeginAttempt(o));
    }
    [Fact] public void FailedCommitDoesNotLeaveUnlockedPrediction() {
        using var f=new Fixture();var s=f.Open();var o=s.SaveObservation(Observation());s.BeginAttempt(o);
        s.SaveRaw(o.Id,Envelope(Answer()));
        Assert.Throws<InvalidDataException>(()=>s.Complete(o,Evaluation(o,Answer()) with {RawResponseSha256=new string('b',64)}));
        Assert.Null(s.Prediction(Study));Assert.Null(s.Evaluation(o.Id));
        s.Fail(o.Id,"FAILED_REQUIRES_RECONCILIATION");
        Assert.Throws<SqliteException>(()=>s.BeginAttempt(o));
        using var db=f.Database();using var cmd=db.CreateCommand();cmd.CommandText="SELECT raw_response FROM muse_attempts";
        Assert.Equal(Envelope(Answer()),cmd.ExecuteScalar());
    }
    [Fact] public void ReceiptTamperingIsDetectedEvenWhenAnswerIsUnchanged() {
        using var f=new Fixture();var s=f.Open();var o=s.SaveObservation(Observation());var a=Answer();
        s.BeginAttempt(o);s.SaveRaw(o.Id,Envelope(a));s.Complete(o,Evaluation(o,a));
        using var db=f.Database();using var cmd=db.CreateCommand();
        cmd.CommandText="UPDATE muse_attempts SET evaluation=$e";
        cmd.Parameters.AddWithValue("$e",JsonSerializer.Serialize(Evaluation(o,a) with {ResponseId="fake-id"},Json));cmd.ExecuteNonQuery();
        Assert.Throws<InvalidDataException>(()=>s.Prediction(Study));
    }
    [Fact] public void InconclusiveEvaluationIsRecordedWithoutUnlockingVideoTwo() {
        using var f=new Fixture();var s=f.Open();var o=s.SaveObservation(Observation());var a=Answer("INCONCLUSIVE");
        s.BeginAttempt(o);s.SaveRaw(o.Id,Envelope(a));s.Complete(o,Evaluation(o,a));
        Assert.NotNull(s.Evaluation(o.Id));Assert.Null(s.Prediction(Study));
    }
    [Fact] public void CrossProcessLockExcludesConcurrentFreezeAndUpload() {
        using var f=new Fixture();var s=f.Open();using var held=s.LockStudy(Study);
        Assert.Throws<IOException>(()=>f.Open().LockStudy(Study));
    }
    private sealed class Handler(int count=1000,bool fail=false):HttpMessageHandler {
        public readonly List<string> Paths=[];public readonly List<string> Payloads=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            Paths.Add(request.RequestUri!.AbsolutePath);Payloads.Add(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(Token,request.Headers.Authorization!.Parameter);
            return new(fail?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK) {
                Content=new StringContent(Paths.Count==1?JsonSerializer.Serialize(new {input_tokens=count}):Envelope(Answer()),Encoding.UTF8,"application/json")};
        }
    }
    private sealed class Factory(Handler handler):IHttpClientFactory {public HttpClient CreateClient(string name)=>new(handler,false);}
    [Fact] public async Task TransportCountsThenSendsOneStrictInferenceWithoutTools() {
        var h=new Handler();var r=await new MuseExperimentTransport(new Factory(h)).EvaluateAsync(Input(),Token,CancellationToken.None);
        Assert.Equal(new[]{"/v1/responses/input_tokens","/v1/responses"},h.Paths);
        using var p=JsonDocument.Parse(h.Payloads[1]);
        Assert.True(p.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        Assert.DoesNotContain(p.RootElement.GetProperty("text").GetProperty("format").GetProperty("schema").GetProperty("properties")
            .GetProperty("periodSeconds").GetProperty("enum").EnumerateArray(),x=>x.ValueKind==JsonValueKind.Number && x.GetDouble()==1.415);
        var schema=p.RootElement.GetProperty("text").GetProperty("format").GetProperty("schema");
        Assert.Contains(schema.GetProperty("required").EnumerateArray(),v=>v.GetString()=="phenomenon");
        var phenomenon=schema.GetProperty("properties").GetProperty("phenomenon");
        Assert.False(phenomenon.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[]{"PENDULUM_COMPATIBLE","OTHER_MOTION","INCONCLUSIVE"},
            phenomenon.GetProperty("properties").GetProperty("classification").GetProperty("enum")
                .EnumerateArray().Select(v=>v.GetString()).ToArray());
        Assert.False(p.RootElement.TryGetProperty("tools",out _));Assert.Equal(4096,p.RootElement.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("synthetic-not-provider",MuseExperimentTransport.Validate(r.Raw).ProviderResponseId);
    }
    [Theory][InlineData(25000,false)][InlineData(1000,true)]
    public async Task OverBudgetOrFailedCountDoesNotInferOrRetry(int count,bool fail) {
        var h=new Handler(count,fail);
        await Assert.ThrowsAnyAsync<Exception>(()=>new MuseExperimentTransport(new Factory(h)).EvaluateAsync(Input(),Token,CancellationToken.None));
        Assert.Single(h.Paths);
    }
    [Fact] public void IncompleteResponseIsNeverAccepted() {
        Assert.Throws<FormatException>(()=>MuseExperimentTransport.Validate(Envelope(Answer()).Replace("\"completed\"","\"incomplete\"")));
    }

    [Theory]
    [InlineData("RESOLVED_ARC")]
    [InlineData("INCONCLUSIVE")]
    [InlineData("COMPATIBLE_WITH_SIMPLE_PENDULUM")]
    public void V3LocalMotionLabelDoesNotDecideSparkClassification(string localStatus)
    {
        using var doc=JsonDocument.Parse(DetailedObservation(localStatus).PayloadJson);
        Assert.Equal(3,MuseExperimentPolicy.ExperimentOptions(doc.RootElement).Length);
        Assert.Equal("PENDULUM_COMPATIBLE",MuseExperimentPolicy.Parse(
            JsonSerializer.Serialize(Answer(),Json),doc.RootElement).Phenomenon!.Classification);
        Assert.Throws<FormatException>(()=>MuseExperimentPolicy.Parse(
            JsonSerializer.Serialize(Answer() with {Phenomenon=new("OTHER_MOTION",Text)},Json),doc.RootElement));
    }

    [Fact] public void V3RequiresRealBilingualSparkClassification()
    {
        Assert.Throws<FormatException>(()=>Parse(Answer(classified:false)));
        Assert.Throws<FormatException>(()=>Parse(Answer() with {Phenomenon=new("PENDULUM",Text)}));
        Assert.Throws<FormatException>(()=>Parse(Answer() with {Phenomenon=new("PENDULUM_COMPATIBLE",new("","English"))}));
        Assert.Throws<FormatException>(()=>Parse(Answer() with {Phenomenon=new("INCONCLUSIVE",Text)}));
        Assert.Null(Parse(Answer("REPEAT_RECORDING") with {Phenomenon=new("OTHER_MOTION",Text)}).PeriodSeconds);
    }

    [Theory]
    [InlineData("MUSE_SAM_INVESTIGATOR_V1")]
    [InlineData("MUSE_SAM_INVESTIGATOR_V2")]
    public void HistoricalAnswerSerializationAndFreezeExcludeTheNewField(string version)
    {
        var old=Answer(classified:false);
        var previousJson=JsonSerializer.Serialize(new {
            old.Decision,old.Justification,old.Limitations,old.Protocol,old.Hypothesis,old.Falsification,
            old.NextLengthMeters,old.PeriodSeconds,old.TolerancePercent
        },Json);
        Assert.Equal(previousJson,JsonSerializer.Serialize(old,Json));
        using var doc=JsonDocument.Parse(Observation(version:version).PayloadJson);
        Assert.Null(MuseExperimentPolicy.Parse(previousJson,doc.RootElement).Phenomenon);
        using var f=new Fixture();var store=f.Open();var o=store.SaveObservation(Observation(version:version));
        store.BeginAttempt(o);store.SaveRaw(o.Id,Envelope(old));store.Complete(o,Evaluation(o,old));
        var frozen=store.Prediction(Study)!;
        Assert.DoesNotContain("phenomenon",frozen.PayloadJson);
        Assert.Equal(frozen.PayloadJson,f.Open().Prediction(Study)!.PayloadJson);
        Assert.Equal(frozen.Sha256,KnotPolicy.Hash(frozen.PayloadJson));
    }

    private static MuseObservation DetailedObservation(string motionStatus="RESOLVED_ARC",bool empty=false)
    {
        var count=empty?0:100;
        var identity=JsonSerializer.SerializeToElement(new {protocol=MuseExperimentPolicy.Version,studyId=Study},Json);
        var payload=JsonSerializer.Serialize(new {
            identity,baselineLengthMeters=.5,lengthProvenance="USER_CONFIGURED",
            nominalReleaseAngleDegrees=15,humanObjectReviewed=false,
            measurement=new {
                source=new {objectId="0"},
                input=new {
                    framePtsSeconds=Enumerable.Range(0,220).Select(i=>i/59.94).ToArray(),
                    centroids=Enumerable.Range(0,count).Select(i=>new {frameIndex=i*2,x=150+20*Math.Sin(i/6d),y=200d}).ToArray(),
                    sourceWidth=320,sourceHeight=334,imageWidth=320,imageHeight=334,pivot=new {x=.5,y=.1}
                },
                assessment=new {
                    status=empty?"INCONCLUSIVE":"CANDIDATE",
                    issues=empty?new[]{"NO_DETECTIONS"}:Array.Empty<string>(),
                    periodSeconds=empty?(double?)null:1.415,statisticalStandardUncertaintySeconds=.001,
                    detectedFrames=count,detectionCoverage=count/220d,completeCycles=20,usedCycles=10,
                    motion=new {method="SAM_PIVOT_ARC_VS_LINE_V1",status=motionStatus,
                        radialRmsPixels=1d,lineRmsPixels=4d,arcSagittaPixels=6d,minimumResolvableSagittaPixels=2d},
                    cycles=Enumerable.Range(0,20).Select(i=>new {startFrame=i*10,endFrame=i*10+9,periodSeconds=1.415}).ToArray()
                },
                limitations=new {es="Sin incertidumbre total.",en="No total uncertainty."}
            }
        },Json);
        return new(KnotPolicy.Hash(identity.GetRawText()),Study,new string('a',64),"synthetic-sam",
            DateTimeOffset.UtcNow,payload,KnotPolicy.Hash(payload));
    }

    [Fact] public void SparkInputContainsBoundedOriginalSamPtsAndMetricsWithoutLocalVerdict()
    {
        var o=DetailedObservation("COMPATIBLE_WITH_SIMPLE_PENDULUM");
        var input=MuseExperimentPolicy.BuildInput(o);
        using var doc=JsonDocument.Parse(input);
        var root=doc.RootElement;
        Assert.DoesNotContain("COMPATIBLE_WITH_SIMPLE_PENDULUM",input);
        Assert.False(root.GetProperty("assessment").TryGetProperty("motion",out _));
        Assert.False(root.GetProperty("geometryDiagnostics").TryGetProperty("status",out _));
        Assert.Equal(1,root.GetProperty("geometryDiagnostics").GetProperty("radialRmsPixels").GetDouble());
        var samples=root.GetProperty("trajectory").GetProperty("samples").EnumerateArray().ToArray();
        Assert.Equal(48,samples.Length);
        Assert.Equal(0,samples[0].GetProperty("frameIndex").GetInt32());
        Assert.Equal(198,samples[^1].GetProperty("frameIndex").GetInt32());
        foreach(var p in samples) {
            var frame=p.GetProperty("frameIndex").GetInt32();
            Assert.Equal(0,frame%2);
            Assert.Equal(frame/59.94,p.GetProperty("timeSeconds").GetDouble());
            Assert.Equal(150+20*Math.Sin((frame/2)/6d),p.GetProperty("x").GetDouble());
        }
        Assert.Equal(12,root.GetProperty("sampledCycles").GetArrayLength());
        Assert.Equal(20,root.GetProperty("totalCycles").GetInt32());
        Assert.Contains("alias",root.GetProperty("trajectory").GetProperty("limitation").GetString());
        Assert.True(input.Length<=16000);
    }

    [Fact] public void EmptySamEvidenceReachesSparkButCannotCertifyAnotherPhenomenon()
    {
        var o=DetailedObservation(empty:true);
        using var input=JsonDocument.Parse(MuseExperimentPolicy.BuildInput(o));
        Assert.Equal(0,input.RootElement.GetProperty("trajectory").GetProperty("samples").GetArrayLength());
        Assert.Equal(0,input.RootElement.GetProperty("experimentOptions").GetArrayLength());
        using var observation=JsonDocument.Parse(o.PayloadJson);
        var a=MuseExperimentPolicy.Parse(JsonSerializer.Serialize(Answer("INCONCLUSIVE"),Json),observation.RootElement);
        Assert.Equal("INCONCLUSIVE",a.Phenomenon!.Classification);
        foreach(var classification in new[]{"OTHER_MOTION","PENDULUM_COMPATIBLE"})
            Assert.Throws<FormatException>(()=>MuseExperimentPolicy.Parse(JsonSerializer.Serialize(
                Answer("INCONCLUSIVE") with {Phenomenon=new(classification,Text)},Json),observation.RootElement));
    }

    [Fact] public void EvidenceSummaryRejectsInvalidIndicesAndOversizeBeforeTransport()
    {
        var o=DetailedObservation();
        var bad=o.PayloadJson.Replace("\"frameIndex\":198","\"frameIndex\":99999");
        Assert.Throws<ArgumentException>(()=>MuseExperimentPolicy.BuildInput(o with {PayloadJson=bad}));
        var large=o.PayloadJson.Replace("Sin incertidumbre total.",new string('X',16001));
        Assert.Throws<ArgumentException>(()=>MuseExperimentPolicy.BuildInput(o with {PayloadJson=large}));
    }

    [Theory]
    [InlineData("OTHER_MOTION")]
    [InlineData("INCONCLUSIVE")]
    public void SparkNonPendularEvaluationIsRecordedWithoutFreezing(string classification)
    {
        using var f=new Fixture();var s=f.Open();var o=s.SaveObservation(Observation());
        var a=Parse(Answer("INCONCLUSIVE") with {Phenomenon=new(classification,Text)});
        s.BeginAttempt(o);s.SaveRaw(o.Id,Envelope(a));s.Complete(o,Evaluation(o,a));
        Assert.Equal(classification,s.Evaluation(o.Id)!.Answer.Phenomenon!.Classification);
        Assert.Null(s.Prediction(Study));
    }

    [Fact] public void LegacyObservationWithoutProtocolUsesThePreviousV2FreezeFallback()
    {
        var old=Observation(version:"MUSE_SAM_INVESTIGATOR_V2");
        var payload=old.PayloadJson.Replace("\"protocol\":\"MUSE_SAM_INVESTIGATOR_V2\",","");
        var observation=old with {PayloadJson=payload,PayloadSha256=KnotPolicy.Hash(payload)};
        var answer=Answer(classified:false);
        using var doc=JsonDocument.Parse(payload);
        Assert.Null(MuseExperimentPolicy.Parse(JsonSerializer.Serialize(answer,Json),doc.RootElement).Phenomenon);
        var freeze=JsonSerializer.SerializeToElement(MuseExperimentPolicy.FreezePayload(
            observation,Evaluation(observation,answer),DateTimeOffset.UtcNow),Json);
        Assert.Equal("MUSE_SAM_INVESTIGATOR_V2",freeze.GetProperty("protocol").GetString());
        Assert.False(freeze.GetProperty("answer").TryGetProperty("phenomenon",out _));
    }
}
