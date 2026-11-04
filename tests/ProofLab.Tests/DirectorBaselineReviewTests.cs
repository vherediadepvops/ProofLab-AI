using System.Text.Json;
using System.Text.Json.Nodes;
using ProofLab.Api;
using ProofLab.Science;

namespace ProofLab.Tests;

public sealed class DirectorBaselineReviewTests
{
    private const string Study="11111111111111111111111111111111";
    private static readonly string Video=new('a',64),Response=new('b',64);
    private static readonly MusePrepareRequest Request=new(Video,"sam-baseline",null,"batch-baseline");
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    private static MuseObservation Observation(Action<JsonObject>? change=null)
    {
        var payload=JsonSerializer.SerializeToNode(new {
            identity=new {studyId=Study,videoSha256=Video,analysisId=Request.AnalysisId,
                responseSha256=Response,pivotRevisionId=(string?)null,adaptiveBatchId=Request.AdaptiveBatchId},
            measurement=new {
                source=new {studyId=Study,role="baseline",originalVideoSha256=Video,
                    analysisId=Request.AnalysisId,providerResponseSha256=Response,pivotRevisionId=(string?)null},
                assessment=new {status="CANDIDATE",periodSeconds=1.1461770982721713,
                    usedCycles=13,completeCycles=26,totalFrames=1910,detectedFrames=1910,
                    motion=new {status="INCONCLUSIVE",issues=new[]{"RADIAL_RESIDUAL_TOO_LARGE"}}}
            }
        },Json)!.AsObject();
        change?.Invoke(payload);
        var json=payload.ToJsonString();
        using var parsed=JsonDocument.Parse(json);
        return new(KnotPolicy.Hash(parsed.RootElement.GetProperty("identity").GetRawText()),Study,
            Video,Request.AnalysisId,DateTimeOffset.UtcNow,json,KnotPolicy.Hash(json));
    }

    [Fact] public void ValidSamCandidateCanBeReviewedWhenLegacySignalHasNoPeriod()
    {
        var old=PendulumSpectralEstimator.Estimate([],0);
        Assert.Null(old.PeriodSeconds);
        Assert.Throws<ArgumentException>(()=>DirectorBaselineReviewPolicy.RequireLocalPeriod(old));
        var evidence=DirectorBaselineReviewPolicy.FromSamObservation(Study,Request,Observation());
        Assert.Equal(Study,evidence.StudyId);Assert.Equal(Video,evidence.VideoSha256);
        Assert.Equal(Request.AnalysisId,evidence.AnalysisId);
    }

    [Theory]
    [InlineData("INCONCLUSIVE",1.146)]
    [InlineData("CANDIDATE",0d)]
    [InlineData("CANDIDATE",-1d)]
    [InlineData("CANDIDATE",null)]
    public void UnusableSamMeasurementCannotAcknowledgeTheBaseline(string status,double? period)
    {
        var observation=Observation(p=>{
            p["measurement"]!["assessment"]!["status"]=status;
            p["measurement"]!["assessment"]!["periodSeconds"]=period;
        });
        Assert.Throws<ArgumentException>(()=>DirectorBaselineReviewPolicy.FromSamObservation(Study,Request,observation));
    }

    [Fact] public void NonFiniteOrStringPeriodDoesNotBecomeValidEvidence()
    {
        var original=Observation();
        var json=original.PayloadJson.Replace("1.1461770982721713","1e400",StringComparison.Ordinal);
        var nonFinite=original with {PayloadJson=json,PayloadSha256=KnotPolicy.Hash(json)};
        Assert.Throws<ArgumentException>(()=>DirectorBaselineReviewPolicy.FromSamObservation(Study,Request,nonFinite));
        Assert.Throws<ArgumentException>(()=>DirectorBaselineReviewPolicy.FromSamObservation(Study,Request,
            Observation(p=>p["measurement"]!["assessment"]!["periodSeconds"]="1.146")));
    }

    [Theory]
    [InlineData("studyId","22222222222222222222222222222222")]
    [InlineData("role","followup")]
    [InlineData("originalVideoSha256","other-video")]
    [InlineData("analysisId","other-analysis")]
    [InlineData("providerResponseSha256","other-provider-response")]
    [InlineData("pivotRevisionId","new-pivot")]
    public void MeasurementFromAnotherStudyRoleOrAnalysisCannotBeUsed(string field,string value)
    {
        var observation=Observation(p=>p["measurement"]!["source"]![field]=value);
        Assert.Throws<InvalidDataException>(()=>DirectorBaselineReviewPolicy.FromSamObservation(Study,Request,observation));
    }

    [Fact] public void StaleSourceSelectionOrMetadataCannotBeUsed()
    {
        var observation=Observation();
        foreach(var stale in new[]{
            Request with {VideoSha256=new string('c',64)},Request with {AnalysisId="new-analysis"},
            Request with {PivotRevisionId="new-pivot"},Request with {AdaptiveBatchId="new-batch"}})
            Assert.Throws<InvalidDataException>(()=>DirectorBaselineReviewPolicy.FromSamObservation(Study,stale,observation));
        Assert.Throws<InvalidDataException>(()=>DirectorBaselineReviewPolicy.FromSamObservation(
            "22222222222222222222222222222222",Request,observation));
        Assert.Throws<InvalidDataException>(()=>DirectorBaselineReviewPolicy.FromSamObservation(Study,Request,
            observation with {AnalysisId="different-analysis"}));
        Assert.Throws<InvalidDataException>(()=>DirectorBaselineReviewPolicy.FromSamObservation(Study,Request,
            observation with {PayloadSha256=new string('c',64)}));
    }

    [Fact] public void MissingCheckboxIsRejectedWithOrWithoutSamEvidence()
    {
        var legacy=JsonSerializer.Deserialize<DirectorHumanReviewRequest>("{}",Json)!;
        var sam=JsonSerializer.Deserialize<DirectorHumanReviewRequest>(
            JsonSerializer.Serialize(new {samEvidence=Request},Json),Json)!;
        Assert.Throws<ArgumentException>(()=>DirectorBaselineReviewPolicy.RequireHuman(legacy.BobConfirmed));
        Assert.Throws<ArgumentException>(()=>DirectorBaselineReviewPolicy.RequireHuman(sam.BobConfirmed));
        DirectorBaselineReviewPolicy.RequireHuman(true);
    }

    [Fact] public void LegacyRequestAndLocalPeriodValidationRemainUnchanged()
    {
        var request=JsonSerializer.Deserialize<DirectorHumanReviewRequest>("{\"bobConfirmed\":true}",Json)!;
        Assert.True(request.BobConfirmed);Assert.Null(request.SamEvidence);
        var local=new PendulumSpectralAssessment(PendulumSpectralEstimator.Version,1.2,.99,50,
            1.2,1.2,0,360,2,PendulumSpectralEstimator.Candidate,"Local only.");
        DirectorBaselineReviewPolicy.RequireLocalPeriod(local);
        Assert.Throws<ArgumentException>(()=>DirectorBaselineReviewPolicy.RequireLocalPeriod(null));
        Assert.Throws<ArgumentException>(()=>DirectorBaselineReviewPolicy.RequireLocalPeriod(
            local with {Status=PendulumSpectralEstimator.Inconclusive}));
    }
}
