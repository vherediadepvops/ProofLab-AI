using System.Text;
using System.Text.Json;
using ProofLab.Api;

namespace ProofLab.Tests;

/// <summary>Wire format fixtures only, not genuine Meta provider evidence.</summary>
public sealed class SamFourWayMaskAssemblerTests
{
    private static readonly string Video=new('a',64);
    private static readonly string Scene=new('b',64);
    private static readonly double[] Pts=Enumerable.Range(0,1000).Select(i=>i/60d).ToArray();

    private static string FakeEnvelope(string wire)=>JsonSerializer.Serialize(new {
        model="sam-3.1",status="completed",
        output=new[]{new{content=new[]{new{type="output_text",text=wire}}}}
    });

    private static (SamFourWayPlan Plan,SamFourWayMerge Merge,
        IReadOnlyList<SamFourWayRawPart> Parts,string Reference)
        Fixture()
    {
        var plan=SamFourWayPlan.Create(Pts);
        var input=new StringBuilder();
        for(var i=0;i<Pts.Length;i++)
            input.Append('<').Append(i).Append("f>0<|box;x1=20;y1=30;x2=40;y2=50;w=1080;h=1920|>")
                .Append("<|mask;x=20;y=30;data=21,21,FAKE_ONLY|>").Append('\n');
        var original=FakeEnvelope(input.ToString());
        var parts=SamFourWayMaskAssembler.PartitionReferenceFixture(
            plan,Video,Scene,original);
        var observations=plan.Windows.Select(w=>new SamFourWaySegment(
            w.Part,Video,Scene,true,
            Enumerable.Range(w.InputStart,w.InputFrames).Select(i=>
                new SamFourWayDetection(i-w.InputStart,"p"+w.Part,
                    30,40,20,30,41,51)).ToArray())).ToArray();
        var merge=SamFourWayStitcher.Merge(plan,Pts,Video,Scene,observations);
        return (plan,merge,parts,original);
    }

    [Fact]
    public void FourFakeParts_ReconstructEveryMaskByteAndGlobalFrame()
    {
        var (plan,merged,parts,original)=Fixture();
        var result=SamFourWayMaskAssembler.Assemble(plan,merged,parts);
        Assert.Equal(1000,result.MaskFrames);
        Assert.True(result.SeamsVerified);
        Assert.Equal(plan.SourcePtsSha256,result.SourcePtsSha256);
        Assert.Equal(SamFourWayMaskAssembler.OutputWire(original),result.WireOutputText);
        Assert.Contains("DERIVED_NOT_A_NATIVE_PROVIDER_RESPONSE",result.Provenance);
        Assert.DoesNotContain("p1",result.WireOutputText);
    }

    [Fact]
    public void FalseProviderCompletionCannotProduceAComposite()
    {
        var (plan,merged,parts,_)=Fixture();
        var corrupted=parts.ToArray();
        corrupted[1]=corrupted[1] with {ProviderCompleted=false};
        Assert.Throws<InvalidDataException>(()=>
            SamFourWayMaskAssembler.Assemble(plan,merged,corrupted));
    }

    [Fact]
    public void MissingMasksAreNeverReconstructedFromCentroids()
    {
        var (plan,merged,parts,_)=Fixture();
        var changed=parts.ToArray();
        changed[1]=changed[1] with {
            RawResponseJson=FakeEnvelope("")
        };
        Assert.Throws<InvalidDataException>(()=>
            SamFourWayMaskAssembler.Assemble(plan,merged,changed));
    }

    [Fact]
    public void RejectsSharedMaskPayloadMismatchEvenWhenBoxesAndCentroidsAgree()
    {
        var (plan,merged,parts,_)=Fixture();
        var changed=parts.ToArray();
        var part1=SamFourWayMaskAssembler.OutputWire(changed[1].RawResponseJson);
        const string original="<0f>p1<|box;x1=20;y1=30;x2=40;y2=50;w=1080;h=1920|>"+
            "<|mask;x=20;y=30;data=21,21,FAKE_ONLY|>";
        const string mutated="<0f>p1<|box;x1=20;y1=30;x2=40;y2=50;w=1080;h=1920|>"+
            "<|mask;x=20;y=30;data=21,21,ALTERED_ONLY|>";
        Assert.Contains(original,part1);
        changed[1]=changed[1] with {
            RawResponseJson=FakeEnvelope(
                part1.Replace(original,mutated,StringComparison.Ordinal))
        };
        // Overlap frame coordinates and boxes remain identical; the RAW
        // mask has changed, so a derived union must NOT be marked valid.
        Assert.Throws<InvalidDataException>(()=>
            SamFourWayMaskAssembler.Assemble(plan,merged,changed));
    }

    [Fact]
    public void RejectsUnverifiedSeamEvenWithFourCompletedRawParts()
    {
        var (plan,merged,parts,_)=Fixture();
        var unverified=merged with {Seams=merged.Seams.Select((s,i)=>
            i==0?s with {VerifiedCommonFrames=0,IdentityVerified=false}:s).ToArray()};
        Assert.Throws<InvalidDataException>(()=>
            SamFourWayMaskAssembler.Assemble(plan,unverified,parts));
    }

    [Fact]
    public void RejectsFrameTamperingInOneOfFourWireFragments()
    {
        var (plan,merged,parts,_)=Fixture();
        var changed=parts.ToArray();
        var raw=changed[0].RawResponseJson;
        var json=SamFourWayMaskAssembler.OutputWire(raw);
        // Change the first local frame index without corresponding geometric record.
        var replaced=json.Replace("<0f>p0","<999f>p0",StringComparison.Ordinal);
        changed[0]=changed[0] with {RawResponseJson=FakeEnvelope(replaced)};
        Assert.Throws<InvalidDataException>(()=>
            SamFourWayMaskAssembler.Assemble(plan,merged,changed));
    }

    [Fact]
    public void RejectsCorruptProviderEnvelope()
    {
        Assert.Throws<InvalidDataException>(()=>
            SamFourWayMaskAssembler.OutputWire(FakeEnvelope("")+
                "MALFORMED"));
        Assert.Throws<InvalidDataException>(()=>
            SamFourWayMaskAssembler.OutputWire(
                "{\"model\":\"sam-3.1\",\"status\":\"incomplete\",\"output\":[]}"));
    }
}
