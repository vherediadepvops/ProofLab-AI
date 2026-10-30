using System.Text.Json;
using ProofLab.Api;
namespace ProofLab.Tests;
public sealed class MuseContrastTests
{
    private static JsonElement Frozen=>JsonSerializer.SerializeToElement(new{prediction=new{nextLengthMeters=.8,periodSeconds=1.8,lowerSeconds=1.71,upperSeconds=1.89}});
    [Theory][InlineData(1.8,.8,"CANDIDATE","WITHIN_PREDECLARED_BAND")]
    [InlineData(1.71,.8,"CANDIDATE","WITHIN_PREDECLARED_BAND")][InlineData(1.89,.8,"CANDIDATE","WITHIN_PREDECLARED_BAND")]
    [InlineData(1.9,.8,"CANDIDATE","OUTSIDE_PREDECLARED_BAND")][InlineData(1.7,.8,"CANDIDATE","OUTSIDE_PREDECLARED_BAND")]
    [InlineData(1.8,.5,"CANDIDATE","INCONCLUSIVE")][InlineData(1.8,.8,"INCONCLUSIVE","INCONCLUSIVE")]
    public void FrozenBandAndActualLengthControlContrast(double t,double l,string status,string expected)
        =>Assert.Equal(expected,MuseContrastPolicy.NumericalVerdict(Frozen,JsonSerializer.SerializeToElement(new{status,periodSeconds=t}),l));
    [Fact] public void MissingPeriodIsInconclusive()=>Assert.Equal("INCONCLUSIVE",MuseContrastPolicy.NumericalVerdict(Frozen,
        JsonSerializer.SerializeToElement(new{status="CANDIDATE",periodSeconds=(double?)null}),.8));
    [Fact] public void SparkCannotOverrideNumericalDiscrepancyOrInventConfirmation()
    {
        var text=new MuseText("Condicional","Conditional");
        string Answer(string verdict)=>JsonSerializer.Serialize(new MuseContrastAnswer(verdict,text,text,text),MuseExperimentPolicy.Json);
        Assert.Throws<FormatException>(()=>MuseContrastPolicy.Parse(Answer("WITHIN_PREDECLARED_BAND"),"OUTSIDE_PREDECLARED_BAND"));
        Assert.Throws<FormatException>(()=>MuseContrastPolicy.Parse(Answer("CONFIRMED"),"WITHIN_PREDECLARED_BAND"));
        Assert.Equal("INCONCLUSIVE",MuseContrastPolicy.Parse(Answer("INCONCLUSIVE"),"OUTSIDE_PREDECLARED_BAND").Verdict);
    }
    [Fact] public void ExplicitConfiguredLengthIsRespectedWithoutChangingHistoricalDefaults()
    {
        JsonElement O(double? length)=>JsonSerializer.SerializeToElement(new{identity=new{protocol=MuseExperimentPolicy.Version},
            baselineLengthMeters=.5,plannedFollowupLengthMeters=length,measurement=new{assessment=new{status="CANDIDATE",periodSeconds=1.4}}});
        Assert.Equal(3,MuseExperimentPolicy.ExperimentOptions(O(null)).Length);
        var chosen=Assert.Single(MuseExperimentPolicy.ExperimentOptions(O(.8)));Assert.Equal(.8,chosen.NextLengthMeters);Assert.Equal(1.4*Math.Sqrt(1.6),chosen.PeriodSeconds);
        Assert.Empty(MuseExperimentPolicy.ExperimentOptions(O(.51)));
    }
}
