using System.Text.Json;
using ProofLab.Api;

namespace ProofLab.Tests;

public sealed class SamPrincipalReferenceTests
{
    private static readonly string Sha=new('a',64);
    private static SamSceneDefinition Scene(params SamSceneSeed[] seeds)=>
        new(Sha,Sha,Sha,Sha,1080,1920,"simple_pendulum",[new("1","object")],seeds,null,null);
    private static readonly SamAdaptiveWindow Whole=new(0,0,1000,0,1000);

    [Fact]
    public void EarliestCredibleSourceBoxIsUsedExactlyOnceAndOtherObservationsRemainIntact()
    {
        var scene=Scene(new(600,"object",400,700,450,780),
            new(0,"object",100,300,150,380),new(200,"object",300,500,350,580));
        var reference=SamPrincipalReferencePolicy.Select(scene,Whole);
        Assert.Equal(scene.Seeds[1],reference.Seed);Assert.Equal(3,reference.CandidateCount);
        Assert.Empty(reference.RejectedFrameIndices);
        var prompt=SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,reference);
        Assert.Equal("<0f>1<|box;x1=100;y1=300;x2=150;y2=380;w=1080;h=1920|>",prompt);
        Assert.Equal(3,scene.Seeds.Length);Assert.Equal(600,scene.Seeds[0].Frame);
        Assert.Contains("<600f>",SamAdaptiveGroundedPlan.BuildPrompt(scene,Whole));
    }

    [Fact]
    public void TinyAreaAndExtremeAspectReferencesDoNotReplaceLaterCoherentBox()
    {
        var scene=Scene(new(0,"object",10,20,12,30),new(1,"object",10,20,900,1800),
            new(2,"object",10,20,14,220),new(3,"object",100,300,120,320),
            new(4,"object",120,300,140,320),new(5,"object",140,300,160,320));
        var reference=SamPrincipalReferencePolicy.Select(scene,Whole);
        Assert.Equal(3,reference.Seed.Frame);Assert.Equal(new[]{0,1,2},reference.RejectedFrameIndices);
    }

    [Fact]
    public void RotatingObjectMayChangeAspectWithinBroadBoundsWithoutInventingCoordinates()
    {
        var scene=Scene(new(10,"object",10,20,50,40),new(20,"object",10,20,30,60),
            new(30,"object",10,20,40,50));
        var reference=SamPrincipalReferencePolicy.Select(scene,Whole);
        Assert.Equal(scene.Seeds[0],reference.Seed);Assert.Empty(reference.RejectedFrameIndices);
    }

    [Fact]
    public void MissingOrDegenerateCandidatesFailWithoutMultiReferenceFallback()
    {
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.Select(Scene(),Whole));
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.Select(
            Scene(new(0,"object",0,0,0,10),new(1,"object",5,5,6,6)),Whole));
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.Select(
            Scene(new SamSceneSeed(2000,"object",10,20,30,40)),Whole));
    }

    [Fact]
    public void SavedReferenceCannotBeMovedToAnotherFrameOrReplacedWithAnotherPolicy()
    {
        var scene=Scene(new(0,"object",100,200,130,240),new(500,"object",300,200,330,240));
        var reference=SamPrincipalReferencePolicy.Select(scene,Whole);
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,
            reference with{Seed=scene.Seeds[1]}));
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,
            reference with{Protocol="UNRECOGNIZED"}));
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,
            reference with{RejectedFrameIndices=[500]}));
    }

    [Fact]
    public void HistoricalContextWithoutPolicyRoundTripsWithoutAnAddedJsonField()
    {
        var old=new SamAdaptiveBatchContext(Scene(new SamSceneSeed(0,"object",10,20,40,60)),53,1);
        var serialized=JsonSerializer.Serialize(old);
        Assert.DoesNotContain("PrincipalReference",serialized);
        Assert.Null(JsonSerializer.Deserialize<SamAdaptiveBatchContext>(serialized)!.PrincipalReference);
    }
}
