using System.Text.Json;
using System.Text.Json.Nodes;
using ProofLab.Api;

namespace ProofLab.Tests;

public sealed class BodyInteriorPointTests
{
    private static readonly string Sha=new('a',64);
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static (AssemblyEvidence Draft,ObjectReviewImage[] Images) Fixture()
    {
        var frames=Enumerable.Range(0,50).Select(i=>new KnotFrame(i*20,i*.2,1080,1920,Sha)).ToArray();
        var points=Enumerable.Range(0,50).Select(i=>new KnotPoint(i,true,500,150,.95)).ToArray();
        var consensus=DenseLocalizationPolicy.Resolve(frames,points,"STATIC");
        var observations=Enumerable.Range(0,50).Select(i=>new AssemblyObservation(i,"object",true,400,700,450,800,.95)).ToArray();
        var draft=new AssemblyEvidence(AssemblyPolicy.GroundingKey(Sha),Sha,AssemblyPolicy.Model,
            AssemblyPolicy.GroundingProtocol,frames,Sha,"offline",DateTimeOffset.UnixEpoch,
            new("simple_pendulum",observations,DenseLocalizationPolicy.Suspension(consensus,points),
                new("CLEAR_SINGLE_OBJECT",new("Objeto visible","Visible object")),"STATIC",consensus),
            100,100,Derivation:DenseLocalizationPolicy.OriginalProtocol);
        var images=SuspensionAuditPolicy.RepresentativeOrdinals().Select(i=>new ObjectReviewImage(
            i,frames[i],observations[i],Sha,BodyCrop:new(200,600,432,768,Sha),SuspensionCandidate:points[i])).ToArray();
        return (draft,images);
    }
    private static JsonObject Payload(bool withPoint=true,bool legacy=false)
    {
        var rows=SuspensionAuditPolicy.RepresentativeOrdinals();
        var root=JsonSerializer.SerializeToNode(new {
            observations=rows.Select(i=>new {frame=i,decision=i==0?"UNCERTAIN":"CORRECTED",visible=i!=0,
                x1=i==0?0:250,y1=i==0?0:250,x2=i==0?0:750,y2=i==0?0:750,confidence=i==0?0:.95,
                visual_reason=i==0?"Hand hides the upper body outline.":"Whole experimental body visible.",
                whole_body_visible=i!=0,occluded=i==0,clipped=false}),
            target=new {status="CLEAR_SINGLE_OBJECT",justification=new {es="Mismo objeto",en="Same visible body"}},
            suspension_audit=new {camera="STATIC",points=rows.Select(i=>new {frame=i,reference_kind="FIXED_CORD_CONTACT",
                visible=true,x=500,y=150,confidence=.95,visual_reason="Independent loaded cord contact."})}
        },Json)!.AsObject();
        if(legacy)foreach(var p in root["suspension_audit"]!["points"]!.AsArray())p!["decision"]="CONFIRMED";
        if(withPoint)root["body_interior_point"]=JsonSerializer.SerializeToNode(new {
            frame=0,visible=true,x=250,y=750,confidence=.95,visual_reason="Visible lower material of the body, away from fingers."
        },Json);
        return root;
    }
    private static AssemblyProposal Parse(JsonObject payload,AssemblyEvidence draft,ObjectReviewImage[] images,
        string protocol=ObjectReviewPolicy.InitialBodyPointProtocol)=>ObjectReviewPolicy.Parse(payload.ToJsonString(),draft,images,protocol);

    [Fact] public void VisibleInteriorPointDoesNotPromoteAnOccludedBoxOrMassCenter()
    {
        var (draft,images)=Fixture();var before=JsonSerializer.Serialize(draft);
        var parsed=Parse(Payload(),draft,images);var point=parsed.BodyInteriorPoint!;
        Assert.True(point.Visible);Assert.Equal(0,point.Frame);
        Assert.Equal(308/1080d*1000,point.X,8);Assert.Equal(1176/1920d*1000,point.Y,8);
        Assert.False(parsed.Observations[0].Visible);Assert.Equal(0,parsed.Observations[0].Confidence);
        Assert.False(parsed.BodyReferences![0].WholeBodyVisible);Assert.True(parsed.BodyReferences[0].Occluded);
        Assert.Equal(SuspensionAuditPolicy.Protocol,parsed.SuspensionAudit!.Protocol);
        Assert.Equal(before,JsonSerializer.Serialize(draft));
    }
    [Theory]
    [InlineData("x",-1)] [InlineData("x",1000)] [InlineData("y",-1)] [InlineData("y",1000)]
    [InlineData("confidence",-1)] [InlineData("confidence",1.1)] [InlineData("frame",6)]
    public void RejectsCoordinatesOutsideExactCropAndWrongOrdinal(string field,double value)
    {
        var (draft,images)=Fixture();var payload=Payload();
        if(field=="frame")payload["body_interior_point"]![field]=(int)value;
        else payload["body_interior_point"]![field]=value;
        Assert.Throws<FormatException>(()=>Parse(payload,draft,images));
    }
    [Fact] public void FirstSampleCannotMasqueradeAsFirstNativeFrame()
    {
        var (draft,images)=Fixture();images[0]=images[0] with {Frame=images[0].Frame with {Index=10}};
        Assert.Throws<FormatException>(()=>Parse(Payload(),draft,images));
    }
    [Fact] public void InvisiblePointIsExplicitlyRetainedWithoutInventedCoordinates()
    {
        var (draft,images)=Fixture();var payload=Payload();var point=payload["body_interior_point"]!;
        point["visible"]=false;point["x"]=0;point["y"]=0;point["confidence"]=0;
        var parsed=Parse(payload,draft,images).BodyInteriorPoint!;
        Assert.False(parsed.Visible);Assert.Equal(0,parsed.X);Assert.Equal(0,parsed.Y);Assert.Equal(0,parsed.Confidence);
        point["x"]=1;Assert.Throws<FormatException>(()=>Parse(payload,draft,images));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void HistoricalReviewCannotAcquireAnInjectedPoint(bool legacy)
    {
        var (draft,images)=Fixture();
        Assert.Throws<FormatException>(()=>Parse(Payload(true,legacy),draft,images,
            legacy?SuspensionAuditPolicy.LegacyProtocol:SuspensionAuditPolicy.Protocol));
    }
    [Fact] public void NewReviewRequiresAnExplicitPointDecision()
    {
        var (draft,images)=Fixture();
        Assert.ThrowsAny<Exception>(()=>Parse(Payload(false),draft,images));
    }
    [Theory] [InlineData("extra")] [InlineData("reason")] [InlineData("missing")]
    public void RejectsUnboundPointFields(string change)
    {
        var (draft,images)=Fixture();var payload=Payload();var point=payload["body_interior_point"]!.AsObject();
        if(change=="extra")point["source"]="human";
        else if(change=="reason")point["visual_reason"]="";
        else point.Remove("confidence");
        Assert.Throws<FormatException>(()=>Parse(payload,draft,images));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void HistoricalDerivationHashAndSerializationKeepTheirOriginalProtocol(bool legacy)
    {
        var (draft,images)=Fixture();var protocol=legacy?SuspensionAuditPolicy.LegacyProtocol:SuspensionAuditPolicy.Protocol;
        var answer=Payload(false,legacy).ToJsonString();
        var raw=JsonSerializer.Serialize(new {id="offline",model=AssemblyPolicy.Model,status="completed",
            usage=new {input_tokens=2,output_tokens=3},
            output=new[]{new {type="message",content=new[]{new {type="output_text",text=answer}}}}});
        var evidence=ObjectReviewPolicy.Derive(draft,new(raw,"offline",2,3),images,protocol);
        Assert.Equal(protocol,evidence.Derivation);Assert.Null(evidence.Proposal.BodyInteriorPoint);
        Assert.DoesNotContain("BodyInteriorPoint",JsonSerializer.Serialize(evidence));
        Assert.DoesNotContain("bodyInteriorPoint",JsonSerializer.Serialize(evidence,Json));
        Assert.Equal(KnotPolicy.Hash(protocol+"\n"+draft.ResponseSha256+"\n"+KnotPolicy.Hash(raw)),evidence.ResponseSha256);
    }
    [Theory] [InlineData(false,0)] [InlineData(true,.79)]
    public void NewReviewDoesNotSilentlyReturnToALateReference(bool visible,double confidence)
    {
        var (draft,images)=Fixture();var payload=Payload();var point=payload["body_interior_point"]!;
        point["visible"]=visible;point["confidence"]=confidence;
        if(!visible){point["x"]=0;point["y"]=0;}
        var evidence=draft with {Proposal=Parse(payload,draft,images),Derivation=ObjectReviewPolicy.InitialBodyPointProtocol};
        Assert.Equal("INITIAL_REFERENCE_UNRESOLVED",AssemblyPolicy.GroundingStatus(evidence));
    }
    [Fact] public void HistoricalV3CanStillUseItsClearLaterBoxesWithoutAnInitialPoint()
    {
        var (draft,images)=Fixture();
        var evidence=draft with {Proposal=Parse(Payload(false),draft,images,SuspensionAuditPolicy.Protocol),
            Derivation=SuspensionAuditPolicy.Protocol};
        Assert.Equal("READY",AssemblyPolicy.GroundingStatus(evidence));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void NewReviewAcceptsAnObservedInitialPointOrAnUnobstructedInitialBody(bool wholeBody)
    {
        var (draft,images)=Fixture();var payload=Payload();
        if(wholeBody){
            var point=payload["body_interior_point"]!;point["visible"]=false;point["x"]=0;point["y"]=0;point["confidence"]=0;
            var body=payload["observations"]![0]!;body["decision"]="CORRECTED";body["visible"]=true;
            body["x1"]=250;body["y1"]=250;body["x2"]=750;body["y2"]=750;body["confidence"]=.95;
            body["whole_body_visible"]=true;body["occluded"]=false;
        }
        var evidence=draft with {Proposal=Parse(payload,draft,images),Derivation=ObjectReviewPolicy.InitialBodyPointProtocol};
        Assert.Equal("READY",AssemblyPolicy.GroundingStatus(evidence));
    }

    [Fact] public void NewDerivationBindsPointToRawResponseAndKeepsSuspensionV3()
    {
        var (draft,images)=Fixture();var answer=Payload().ToJsonString();
        var raw=JsonSerializer.Serialize(new {id="offline",model=AssemblyPolicy.Model,status="completed",
            usage=new {input_tokens=2,output_tokens=3},
            output=new[]{new {type="message",content=new[]{new {type="output_text",text=answer}}}}});
        var evidence=ObjectReviewPolicy.Derive(draft,new(raw,"offline",2,3),images,ObjectReviewPolicy.InitialBodyPointProtocol);
        Assert.Equal(ObjectReviewPolicy.InitialBodyPointProtocol,evidence.Derivation);
        Assert.NotNull(evidence.Proposal.BodyInteriorPoint);
        Assert.Equal(SuspensionAuditPolicy.Protocol,evidence.Proposal.SuspensionAudit!.Protocol);
        Assert.Equal(KnotPolicy.Hash(ObjectReviewPolicy.InitialBodyPointProtocol+"\n"+draft.ResponseSha256+"\n"+KnotPolicy.Hash(raw)),evidence.ResponseSha256);
    }
}
