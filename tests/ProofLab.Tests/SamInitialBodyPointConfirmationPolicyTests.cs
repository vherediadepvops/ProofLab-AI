using System.Text.Json;
using ProofLab.Api;
namespace ProofLab.Tests;

/// <summary>New submission safety must not rewrite the meaning of historical SAM receipts.</summary>
public sealed class SamInitialBodyPointConfirmationPolicyTests
{
    private static readonly string Sha=new('a',64);
    private static readonly SamAdaptiveWindow Whole=new(0,0,1757,0,1757);
    private static SamSceneDefinition Scene()=>new(Sha,Sha,Sha,Sha,1080,1920,"simple_pendulum",
        [new("1","object")],[new(191,"object",860,1684,942,1793),new(356,"object",203,1644,285,1759)],null,null,
        Origin:AssemblyPolicy.GroundingOrigin,BodyReferences:[new(191,true,false,false),new(356,true,false,false)],
        InitialBodyPoint:new(0,220,1740),InitialPointReviewProtocol:ObjectReviewPolicy.VisibleBodyPointProtocol);

    private static SamInitialBodyPointConfirmation Confirmation(SamSceneDefinition scene)
    {
        var review=new SamInitialBodyPointConfirmation(new('b',32),SamInitialBodyPointPolicy.Protocol,
            new('c',32),"baseline",new('d',32),scene.VideoSha256,new('e',32),scene.Key,scene.ResponseSha256,
            new('f',64),scene.Width,scene.Height,new(0,250,1745),DateTimeOffset.Parse("2026-11-04T13:00:00Z"),"");
        return review with{Sha256=SamInitialBodyPointPolicy.Digest(review)};
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"x\":250}")]
    [InlineData("{\"y\":1745}")]
    public void ExplicitSelectionCannotDefaultOmittedCoordinatesToZero(string json)
    {
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<SamInitialBodyPointConfirmationRequest>(json,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void ConfirmedPointChangesOnlyTheNewPromptAndPreservesSparkEvidence()
    {
        var scene=Scene();var before=JsonSerializer.Serialize(scene);var review=Confirmation(scene);
        var reference=SamPrincipalReferencePolicy.Confirm(scene,Whole,review);
        Assert.Equal("<0f>1<|point;x=250;y=1745;w=1080;h=1920|>",SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,reference));
        Assert.NotEqual(SamPrincipalReferencePolicy.InitialPointProtocol,reference.Protocol);
        Assert.Equal(review,reference.HumanConfirmation);
        Assert.Equal(before,JsonSerializer.Serialize(scene));
        Assert.Equal(new SamScenePoint(0,220,1740),scene.InitialBodyPoint);
        var restored=JsonSerializer.Deserialize<SamPrincipalReference>(JsonSerializer.Serialize(reference))!;
        Assert.Equal(SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,reference),SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,restored));
    }

    [Fact]
    public void HumanReferenceCannotDropOrReplaceItsConfirmationOrNativePoint()
    {
        var scene=Scene();var review=Confirmation(scene);
        var reference=SamPrincipalReferencePolicy.Confirm(scene,Whole,review);
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,reference with{HumanConfirmation=null}));
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,reference with{InitialBodyPoint=new(0,251,1745)}));
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,reference with{HumanConfirmation=review with{Point=new(0,251,1745)}}));
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,reference with{Protocol=SamPrincipalReferencePolicy.InitialPointProtocol}));
    }

    [Fact]
    public void ConfirmationDigestBindsItsReviewProvenance()
    {
        var scene=Scene();var review=Confirmation(scene);
        SamInitialBodyPointPolicy.Validate(review,scene);
        foreach(var changed in new[]{
            review with{Id=new('1',32)},review with{StudyId=new('2',32)},review with{VideoRole="followup"},
            review with{SourceVideoId=new('3',32)},review with{SceneRunId=new('4',32)},review with{Frame0Sha256=new('5',64)},
            review with{ConfirmedAtUtc=review.ConfirmedAtUtc.AddSeconds(1)},review with{Point=new(0,251,1745)}})
            Assert.Throws<InvalidDataException>(()=>SamInitialBodyPointPolicy.Validate(changed,scene));
    }

    [Fact]
    public void EvenSelfConsistentSnapshotCannotTargetAnotherSceneOrDimensions()
    {
        var scene=Scene();var review=Confirmation(scene);
        foreach(var changed in new[]{review with{VideoSha256=new('1',64)},review with{SceneKey=new('2',64)},
            review with{SparkResponseSha256=new('3',64)},review with{Width=720},review with{Height=1280},
            review with{Point=new(1,250,1745)},review with{Point=new(0,-1,1745)},
            review with{Point=new(0,1080,1745)},review with{Point=new(0,250,1920)}})
        {
            var signed=changed with{Sha256=SamInitialBodyPointPolicy.Digest(changed)};
            Assert.Throws<InvalidDataException>(()=>SamInitialBodyPointPolicy.Validate(signed,scene));
        }
    }

    [Fact]
    public void MissingFirstFrameQualityRequiresConfirmationDespiteClearLaterReferences()
    {
        Assert.True(SamInitialBodyPointPolicy.RequiresConfirmation(Scene()));
        Assert.True(SamInitialBodyPointPolicy.RequiresConfirmation(Scene() with{BodyReferences=null}));
        Assert.True(SamInitialBodyPointPolicy.RequiresConfirmation(Scene() with{BodyReferences=[]}));
    }

    [Theory]
    [InlineData(false,false,false)]
    [InlineData(false,false,true)]
    [InlineData(false,true,false)]
    [InlineData(false,true,true)]
    [InlineData(true,false,true)]
    [InlineData(true,true,false)]
    [InlineData(true,true,true)]
    public void AnyFirstFrameOcclusionClippingOrIncompleteOutlineRequiresConfirmation(bool whole,bool occluded,bool clipped)
    {
        var scene=Scene();
        scene=scene with{Seeds=[new(0,"object",200,1650,285,1760),..scene.Seeds],
            BodyReferences=[new(0,whole,occluded,clipped),..scene.BodyReferences!]};
        Assert.True(SamInitialBodyPointPolicy.RequiresConfirmation(scene));
    }

    [Fact]
    public void ClearCompleteFirstFrameKeepsAutomaticInitialPointEligible()
    {
        var scene=Scene();
        scene=scene with{Seeds=[new(0,"object",200,1650,285,1760),..scene.Seeds],
            BodyReferences=[new(0,true,false,false),..scene.BodyReferences!]};
        Assert.False(SamInitialBodyPointPolicy.RequiresConfirmation(scene));
    }

    [Fact]
    public void NoPointAndNoSceneDoNotInventAConfirmationRequirement()
    {
        Assert.False(SamInitialBodyPointPolicy.RequiresConfirmation(null));
        Assert.False(SamInitialBodyPointPolicy.RequiresConfirmation(Scene() with{InitialBodyPoint=null,InitialPointReviewProtocol=null}));
    }

    [Fact]
    public void RiskGateDoesNotRelabelOrInvalidateHistoricalPointReceipt()
    {
        var scene=Scene();
        var key=AssemblyPolicy.DefinitionKey(scene);
        var reference=SamPrincipalReferencePolicy.Select(scene,Whole);
        Assert.True(SamInitialBodyPointPolicy.RequiresConfirmation(scene));
        Assert.Equal(SamPrincipalReferencePolicy.InitialPointProtocol,reference.Protocol);
        Assert.DoesNotContain("HumanConfirmation",JsonSerializer.Serialize(reference));
        Assert.Equal("<0f>1<|point;x=220;y=1740;w=1080;h=1920|>",SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,reference));
        var restored=JsonSerializer.Deserialize<SamPrincipalReference>(JsonSerializer.Serialize(reference))!;
        Assert.Equal(SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,reference),SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,restored));
        Assert.Equal(key,AssemblyPolicy.DefinitionKey(scene));
    }

    [Fact]
    public void OldBoxOnlyReceiptRetainsItsEarliestClearAnchorAndSerializedShape()
    {
        var scene=Scene() with{InitialBodyPoint=null,InitialPointReviewProtocol=null};
        var reference=SamPrincipalReferencePolicy.Select(scene,Whole);
        Assert.Equal(SamPrincipalReferencePolicy.Protocol,reference.Protocol);
        Assert.DoesNotContain("HumanConfirmation",JsonSerializer.Serialize(reference));
        Assert.DoesNotContain("InitialBodyPoint",JsonSerializer.Serialize(reference));
        Assert.Equal("<191f>1<|box;x1=860;y1=1684;x2=942;y2=1793;w=1080;h=1920|>",SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,reference));
    }
}
