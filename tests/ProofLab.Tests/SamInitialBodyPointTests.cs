using System.Text.Json;
using ProofLab.Api;
namespace ProofLab.Tests;

public sealed class SamInitialBodyPointTests
{
    private static readonly string Sha=new('a',64);
    private static readonly SamAdaptiveWindow Whole=new(0,0,1757,0,1757);
    private static SamSceneDefinition Scene()=>new(Sha,Sha,Sha,Sha,1080,1920,"simple_pendulum",
        [new("1","object")],[new(191,"object",860,1684,942,1793),new(356,"object",203,1644,285,1759)],null,null,
        Origin:AssemblyPolicy.GroundingOrigin,BodyReferences:[new(191,true,false,false),new(356,true,false,false)],
        InitialBodyPoint:new(0,220,1740));

    [Fact]
    public void ObservedInitialPointDoesNotRequireInventedWholeBodyBoxInOccludedFrame()
    {
        var scene=Scene();var reference=SamPrincipalReferencePolicy.Select(scene,Whole);
        Assert.Equal(SamPrincipalReferencePolicy.InitialPointProtocol,reference.Protocol);
        Assert.Equal(scene.Seeds[0],reference.Seed); // Clear later observation retained for audit.
        Assert.Equal(scene.InitialBodyPoint,reference.InitialBodyPoint);
        var prompt=SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,reference);
        Assert.Equal("<0f>1<|point;x=220;y=1740;w=1080;h=1920|>",prompt);
        Assert.True(AssemblyPolicy.IsSamPrompt(prompt));
        Assert.Equal(new[]{191,356},scene.Seeds.Select(s=>s.Frame));
    }
    [Theory]
    [InlineData(1,220,1740)] [InlineData(0,-1,1740)] [InlineData(0,1080,1740)]
    [InlineData(0,220,-1)] [InlineData(0,220,1920)]
    public void InvalidInitialPointCannotBeSubmitted(int frame,int x,int y)=>
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.Select(Scene() with{InitialBodyPoint=new(frame,x,y)},Whole));
    [Fact]
    public void PointDoesNotBypassIndependentClearObjectReferences()
    {
        var scene=Scene();
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.Select(scene with{BodyReferences=null},Whole));
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.Select(scene with{Origin=null},Whole));
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.Select(scene with{BodyReferences=[new(191,true,false,false),new(356,false,true,false)]},Whole));
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.Select(scene,new(0,100,1757,100,1757)));
    }
    [Fact]
    public void PersistedPointCannotChangeWhileReusingReceiptOrPlan()
    {
        var scene=Scene();var reference=SamPrincipalReferencePolicy.Select(scene,Whole);
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,reference with{InitialBodyPoint=new(0,230,1740)}));
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,reference with{InitialBodyPoint=null}));
        Assert.Throws<InvalidDataException>(()=>SamPrincipalReferencePolicy.BuildPrompt(scene with{InitialBodyPoint=new(0,230,1740)},Whole,reference));
        Assert.NotEqual(AssemblyPolicy.DefinitionKey(scene),AssemblyPolicy.DefinitionKey(scene with{InitialBodyPoint=new(0,230,1740)}));
    }
    [Fact]
    public void HistoricalV2JsonKeepsNoPointAndOriginalBoxPrompt()
    {
        var scene=Scene() with{InitialBodyPoint=null};var reference=SamPrincipalReferencePolicy.Select(scene,Whole);
        Assert.Equal(SamPrincipalReferencePolicy.Protocol,reference.Protocol);
        Assert.DoesNotContain("InitialBodyPoint",JsonSerializer.Serialize(reference));
        Assert.DoesNotContain("InitialBodyPoint",JsonSerializer.Serialize(scene));
        Assert.Equal("<191f>1<|box;x1=860;y1=1684;x2=942;y2=1793;w=1080;h=1920|>",SamPrincipalReferencePolicy.BuildPrompt(scene,Whole,reference));
    }
    [Fact]
    public void PointOnlySyntaxDoesNotOpenArbitraryProviderInstructions()
    {
        Assert.False(AssemblyPolicy.IsSamPrompt("red body <0f>1<|point;x=220;y=1740;w=1080;h=1920|>"));
        Assert.False(AssemblyPolicy.IsSamPrompt("<1f>1<|point;x=220;y=1740;w=1080;h=1920|>"));
        Assert.False(AssemblyPolicy.IsSamPrompt("<0f>1<|point;x=-1;y=1740;w=1080;h=1920|>"));
    }
}
