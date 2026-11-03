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

    private static SamSceneDefinition Reviewed(params SamSceneSeed[] seeds)=>
        Scene(seeds) with{BodyReferences=seeds.Select(s=>
            new AssemblyBodyReferenceQuality(s.Frame,true,false,false)).ToArray()};

    [Theory]
    [InlineData(false,false,false)]
    [InlineData(true,true,false)]
    [InlineData(true,false,true)]
    public void PartialOccludedOrClippedEarlyBoxCannotSeedNewRequests(bool whole,bool occluded,bool clipped)
    {
        var scene=Reviewed(new(0,"object",100,200,150,280),
            new(200,"object",200,200,250,280),new(600,"object",300,200,350,280));
        scene=scene with{BodyReferences=[new(0,whole,occluded,clipped),
            new(200,true,false,false),new(600,true,false,false)]};
        var reference=SamPrincipalReferencePolicy.Select(scene,Whole);
        Assert.Equal(SamPrincipalReferencePolicy.Protocol,reference.Protocol);
        Assert.Equal(scene.Seeds[1],reference.Seed);Assert.Equal(3,reference.CandidateCount);
        Assert.Equal(new[]{0},reference.RejectedFrameIndices);
        var prompt=SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,reference);
        Assert.Equal("<200f>1<|box;x1=200;y1=200;x2=250;y2=280;w=1080;h=1920|>",prompt);
        Assert.Equal(3,scene.Seeds.Length); // Preserve all observations, including the rejected one.
    }

    [Fact]
    public void CompleteBodyAssertionDoesNotBypassGeometricChecks()
    {
        var scene=Reviewed(new(0,"object",10,20,900,1800),
            new(20,"object",100,200,150,280),new(30,"object",200,200,250,280));
        var reference=SamPrincipalReferencePolicy.Select(scene,Whole);
        Assert.Equal(20,reference.Seed.Frame);Assert.Equal(new[]{0},reference.RejectedFrameIndices);
    }

    [Fact]
    public void NewReviewsRequireTwoCredibleVisibleBodiesInTheWindow()
    {
        var scene=Reviewed(new(0,"object",100,200,150,280),new(200,"object",200,200,250,280));
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.Select(
            scene with{BodyReferences=[new(0,false,true,false),new(200,true,false,false)]},Whole));
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.Select(
            scene with{Seeds=[scene.Seeds[0],new(200,"object",10,20,900,1800)]},Whole));
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.Select(
            scene,new SamAdaptiveWindow(0,0,100,0,100)));
    }

    [Fact]
    public void NewReviewsRejectMissingDuplicateAndUnknownQualityEntries()
    {
        var scene=Reviewed(new(0,"object",100,200,150,280),new(200,"object",200,200,250,280));
        AssemblyBodyReferenceQuality[][] malformed=[[],[new(0,true,false,false)],
            [new(0,true,false,false),new(0,true,false,false)],
            [new(0,true,false,false),new(999,true,false,false)],
            [new(0,true,false,false),null!]];
        foreach(var qualities in malformed)
            Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.Select(
                scene with{BodyReferences=qualities},Whole));
    }

    [Fact]
    public void NewReferenceCannotBeReplayedAsLegacyOrAfterQualityChanges()
    {
        var scene=Reviewed(new(0,"object",100,200,150,280),new(200,"object",200,200,250,280),
            new(600,"object",300,200,350,280));
        var reference=SamPrincipalReferencePolicy.Select(scene,Whole);
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,
            reference with{Protocol=SamPrincipalReferencePolicy.LegacyProtocol}));
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.BuildPrompt(
            scene with{BodyReferences=[new(0,true,true,false),new(200,true,false,false),new(600,true,false,false)]},
            Whole,reference));
    }

    [Fact]
    public void HistoricalSceneKeepsExactV1BehaviorWithoutInventingQuality()
    {
        var scene=Scene(new SamSceneSeed(0,"object",100,200,150,280));
        var reference=SamPrincipalReferencePolicy.Select(scene,Whole);
        Assert.Equal("SAM_SINGLE_PRINCIPAL_REFERENCE_V1",reference.Protocol);
        Assert.Equal(scene.Seeds[0],reference.Seed);
        var json=JsonSerializer.Serialize(scene);
        Assert.DoesNotContain("BodyReferences",json);
        var restored=JsonSerializer.Deserialize<SamSceneDefinition>(json)!;
        Assert.Null(restored.BodyReferences);
        Assert.Equal(SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,reference),
            SamPrincipalReferencePolicy.BuildPrompt(restored,Whole,reference));
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
