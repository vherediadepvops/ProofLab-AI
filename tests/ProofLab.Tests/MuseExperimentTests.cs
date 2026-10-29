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
    private static MuseInvestigatorAnswer Answer(string decision="PROPOSE_EXPERIMENT")=>
        new(decision,Text,Text,Text,Text,Text,decision=="PROPOSE_EXPERIMENT"?.8:null,
            decision=="PROPOSE_EXPERIMENT"?1.415*Math.Sqrt(.8/.5):null,decision=="PROPOSE_EXPERIMENT"?4:null);
    private static MuseObservation Observation(string status="CANDIDATE") {
        var identity=JsonSerializer.SerializeToElement(new {protocol=MuseExperimentPolicy.Version,studyId=Study,videoSha256=new string('a',64),analysisId="synthetic-sam",status},Json);
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
}