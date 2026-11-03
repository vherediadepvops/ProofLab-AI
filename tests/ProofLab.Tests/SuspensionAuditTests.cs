using System.Text.Json;
using ProofLab.Api;
namespace ProofLab.Tests;

public sealed class SuspensionAuditTests
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static readonly string Sha=new('a',64);
    private static AssemblyEvidence Draft()
    {
        var frames=Enumerable.Range(0,50).Select(i=>new KnotFrame(i*20,i*.2,1080,1920,Sha)).ToArray();
        var points=Enumerable.Range(0,50).Select(i=>new KnotPoint(i,true,500,150,.95)).ToArray();
        var consensus=DenseLocalizationPolicy.Resolve(frames,points,"STATIC");
        var proposal=new AssemblyProposal("simple_pendulum",
            Enumerable.Range(0,50).Select(i=>new AssemblyObservation(i,"object",true,400,700,450,800,.95)).ToArray(),
            DenseLocalizationPolicy.Suspension(consensus,points),new("CLEAR_SINGLE_OBJECT",new("Objeto visible","Visible object")),"STATIC",consensus);
        return new(AssemblyPolicy.GroundingKey(Sha),Sha,AssemblyPolicy.Model,AssemblyPolicy.GroundingProtocol,frames,Sha,"offline",
            DateTimeOffset.UnixEpoch,proposal,100,100);
    }
    private static SuspensionAuditPoint[] Points(AssemblyEvidence draft,string decision="CORRECTED",double x=550,double y=180)=>
        SuspensionAuditPolicy.RepresentativeOrdinals().Select(i=>new SuspensionAuditPoint(i,decision,"FIXED_CORD_CONTACT",true,x,y,.95,
            "The loaded cord meets its fixed upper support at this visible contact.",draft.Frames[i].TimeSeconds)).ToArray();
    private static SuspensionAuditPoint Uncertain(SuspensionAuditPoint p)=>p with {
        Decision="UNCERTAIN",ReferenceKind="UNCLEAR",Visible=false,X=0,Y=0,Confidence=0,VisualReason="The physical contact is obscured."};
    private static JsonElement Payload(SuspensionAuditPoint[] points,string camera="STATIC")=>JsonSerializer.SerializeToElement(new {
        camera,points=points.Select(p=>new {frame=p.Frame,decision=p.Decision,reference_kind=p.ReferenceKind,visible=p.Visible,
            x=p.X,y=p.Y,confidence=p.Confidence,visual_reason=p.VisualReason})});
    private static SuspensionAuditEvidence Resolve(AssemblyEvidence draft,SuspensionAuditPoint[] points,string camera="STATIC")=>
        SuspensionAuditPolicy.ValidateAndResolve(camera,points,draft,SuspensionAuditPolicy.RepresentativeOrdinals());

    [Fact] public void CorrectedAuditKeepsOriginalFiftyAndUsesOnlyNineRealSourceFrames()
    {
        var draft=Draft();var original=JsonSerializer.Serialize(draft,Json);var points=Points(draft);
        var result=SuspensionAuditPolicy.Parse(Payload(points),draft,SuspensionAuditPolicy.RepresentativeOrdinals());
        Assert.Equal("CONSENSUS_ESTIMATE",result.Consensus.Status);Assert.Equal(9,result.Consensus.Submitted);
        Assert.Equal(9,result.Consensus.Usable);Assert.Equal(550,result.Consensus.X);Assert.Equal(180,result.Consensus.Y);
        Assert.Equal(SuspensionAuditPolicy.RepresentativeOrdinals(),result.Consensus.InlierOrdinals);
        Assert.Equal(points,result.Points);Assert.Equal(original,JsonSerializer.Serialize(draft,Json));
        SuspensionAuditPolicy.Validate(result,draft);
    }
    [Fact] public void ConfirmedAndCorrectedPositionsCanAgreeEvenIfOriginalConfidenceWasLow()
    {
        var draft=Draft();draft=draft with {Proposal=draft.Proposal with {Suspension=draft.Proposal.Suspension with {
            Points=draft.Proposal.Suspension.Points.Select(p=>p with {Confidence=.2}).ToArray()}}};
        var points=Points(draft,"CONFIRMED",500,150);
        for(var i=6;i<9;i++)points[i]=points[i] with {Decision="CORRECTED",X=501};
        Assert.Equal("CONSENSUS_ESTIMATE",Resolve(draft,points).Consensus.Status);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ContradictoryConfirmationIsPreservedButCannotAcceptOrFallBack(bool invisibleOriginal)
    {
        var draft=Draft();var points=Points(draft,"CONFIRMED",500,150);
        if(invisibleOriginal)draft=draft with {Proposal=draft.Proposal with {Suspension=draft.Proposal.Suspension with {
            Points=draft.Proposal.Suspension.Points.Select(p=>p.Frame==points[0].Frame?p with {Visible=false,X=0,Y=0,Confidence=0}:p).ToArray()}}};
        else points[0]=points[0] with {X=530};
        var audit=Resolve(draft,points);
        Assert.Equal("CONTRADICTORY_CONFIRMATION",audit.Consensus.Status);Assert.Null(audit.Consensus.X);
        Assert.Empty(audit.Consensus.InlierOrdinals);Assert.Equal(points,audit.Points);
    }
    [Fact] public void UncertainReviewCannotReuseTheConfidentInitialConsensus()
    {
        var draft=Draft();var points=Points(draft).Select(Uncertain).ToArray();var result=Resolve(draft,points);
        Assert.Equal("CONSENSUS_ESTIMATE",draft.Proposal.Consensus!.Status);
        Assert.Equal("INSUFFICIENT_OBSERVATIONS",result.Consensus.Status);Assert.Equal(0,result.Consensus.Usable);
        Assert.Null(result.Consensus.X);Assert.Empty(result.Consensus.InlierOrdinals);
    }
    [Theory] [InlineData(5,"INSUFFICIENT_OBSERVATIONS")] [InlineData(6,"CONSENSUS_ESTIMATE")]
    public void AtLeastSixOfNineUsefulObservationsAcrossTheTimelineAreRequired(int useful,string status)
    {
        var draft=Draft();var points=Points(draft);var slots=new[]{0,1,3,5,7,8}.Take(useful).ToHashSet();
        for(var i=0;i<points.Length;i++)if(!slots.Contains(i))points[i]=Uncertain(points[i]);
        var result=Resolve(draft,points);Assert.Equal(status,result.Consensus.Status);Assert.Equal(useful,result.Consensus.Usable);
    }
    [Fact] public void AnyExplicitMovingContactBlocksTheEntireAudit()
    {
        var draft=Draft();var points=Points(draft);points[4]=Uncertain(points[4]) with {ReferenceKind="MOVING_CONTACT"};
        var result=Resolve(draft,points);Assert.Equal("MOVING_CORD_CONTACT",result.Consensus.Status);
        Assert.Null(result.Consensus.X);Assert.Empty(result.Consensus.InlierOrdinals);
    }
    [Theory] [InlineData("MOVING")] [InlineData("UNCLEAR")]
    public void AStaticContactClaimCannotOverrideAnUnresolvedCamera(string camera)
    {
        var draft=Draft();Assert.Equal("CAMERA_UNRESOLVED",Resolve(draft,Points(draft),camera).Consensus.Status);
    }
    [Fact] public void AStrongRivalClusterIsAmbiguousEvenWithSixConsistentPositions()
    {
        var draft=Draft();var points=Points(draft);
        for(var i=6;i<9;i++)points[i]=points[i] with {X=650};
        Assert.Equal("NO_DOMINANT_CLUSTER",Resolve(draft,points).Consensus.Status);
    }
    [Fact] public void CoverageUsesOriginalVideoSpanAndRequiresThreeTimeBins()
    {
        var draft=Draft();draft=draft with {Frames=draft.Frames.Select((f,i)=>i==49?f with {TimeSeconds=100}:f).ToArray()};
        Assert.Equal("INSUFFICIENT_TIME_COVERAGE",Resolve(draft,Points(draft)).Consensus.Status);
    }
    [Fact] public void ParseDerivesTimesAndRejectsProviderSuppliedTimes()
    {
        var draft=Draft();var payload=System.Text.Json.Nodes.JsonNode.Parse(Payload(Points(draft)).GetRawText())!;
        payload["points"]![0]!["timeSeconds"]=99;
        using var document=JsonDocument.Parse(payload.ToJsonString());
        Assert.Throws<FormatException>(()=>SuspensionAuditPolicy.Parse(document.RootElement,draft,SuspensionAuditPolicy.RepresentativeOrdinals()));
    }
    [Theory] [InlineData("duplicate")] [InlineData("reordered")] [InlineData("missing")] [InlineData("fifty")]
    public void AuditRowsCannotBeDuplicatedReorderedOmittedOrExpandedToFifty(string kind)
    {
        var draft=Draft();var points=Points(draft);
        if(kind=="duplicate")points[1]=points[0];
        if(kind=="reordered")(points[0],points[1])=(points[1],points[0]);
        if(kind=="missing")points=points[..8];
        if(kind=="fifty")points=Enumerable.Range(0,50).Select(i=>points[i%9]).ToArray();
        Assert.Throws<FormatException>(()=>SuspensionAuditPolicy.Parse(Payload(points),draft,SuspensionAuditPolicy.RepresentativeOrdinals()));
    }
    [Theory] [InlineData("confidence")] [InlineData("coordinate")] [InlineData("reason")] [InlineData("uncertain")] [InlineData("time")]
    public void InvalidCoordinatesExplanationsAndSourceTimesAreRejected(string kind)
    {
        var draft=Draft();var points=Points(draft);
        points[0]=kind switch {
            "confidence"=>points[0] with {Confidence=1.1},"coordinate"=>points[0] with {X=1000},
            "reason"=>points[0] with {VisualReason=" "},"uncertain"=>points[0] with {Decision="UNCERTAIN"},
            _=>points[0] with {TimeSeconds=.001}};
        Assert.Throws<FormatException>(()=>Resolve(draft,points));
    }
    [Fact] public void ReferenceSelectionCannotSubstituteNineDifferentFrames()
    {
        var draft=Draft();Assert.Throws<InvalidDataException>(()=>SuspensionAuditPolicy.Parse(Payload(Points(draft)),draft,Enumerable.Range(0,9).ToArray()));
    }
    [Theory] [InlineData("center")] [InlineData("submitted")] [InlineData("ordinal")]
    public void AStoredConsensusMustRebuildExactlyFromItsNineAuditedPositions(string field)
    {
        var draft=Draft();var audit=Resolve(draft,Points(draft));
        audit=audit with {Consensus=field switch {
            "center"=>audit.Consensus with {X=500},"submitted"=>audit.Consensus with {Submitted=50},
            _=>audit.Consensus with {InlierOrdinals=Enumerable.Range(0,9).ToArray()}}};
        Assert.Throws<InvalidDataException>(()=>SuspensionAuditPolicy.Validate(audit,draft));
    }
    [Fact] public void NewProtocolRequiresAuditWhileHistoricalJsonRemainsByteCompatible()
    {
        var draft=Draft();Assert.DoesNotContain("suspensionAudit",JsonSerializer.Serialize(draft.Proposal,Json));
        var historical=new SamSuspensionEvidence(Sha,"scene","evidence",Sha,AssemblyPolicy.Model,1080,1920,draft.Proposal.Suspension.Points);
        Assert.DoesNotContain("audit",JsonSerializer.Serialize(historical,Json));
        Assert.Throws<InvalidDataException>(()=>AssemblyPolicy.Ground(draft with {Derivation=SuspensionAuditPolicy.LegacyProtocol}));
        var audit=Resolve(draft,Points(draft));var reviewed=draft with {Derivation=SuspensionAuditPolicy.LegacyProtocol,
            Proposal=draft.Proposal with {SuspensionAudit=audit,Target=new("AMBIGUOUS",new("Objeto ambiguo","Ambiguous object"))}};
        Assert.Null(AssemblyPolicy.Ground(reviewed));
        Assert.Throws<InvalidDataException>(()=>AssemblyPolicy.Ground(reviewed with {Derivation=DenseLocalizationPolicy.OriginalBodyProtocol}));
        var saved=historical with {Audit=audit,Refinement=SuspensionAuditPolicy.LegacyProtocol};
        Assert.Equal(JsonSerializer.Serialize(saved,Json),JsonSerializer.Serialize(JsonSerializer.Deserialize<SamSuspensionEvidence>(JsonSerializer.Serialize(saved,Json),Json),Json));
    }
    private static JsonElement IndependentPayload(SuspensionAuditPoint[] points,string camera="STATIC")=>JsonSerializer.SerializeToElement(new {
        camera,points=points.Select(p=>new {frame=p.Frame,reference_kind=p.ReferenceKind,visible=p.Visible,
            x=p.X,y=p.Y,confidence=p.Confidence,visual_reason=p.VisualReason})});
    private static SuspensionAuditEvidence Independent(AssemblyEvidence draft,SuspensionAuditPoint[] points,string camera="STATIC")=>
        SuspensionAuditPolicy.Parse(IndependentPayload(points,camera),draft,SuspensionAuditPolicy.RepresentativeOrdinals(),SuspensionAuditPolicy.Protocol);

    [Fact] public void V3DerivesCorrectionForIndependentContactTwentySevenPixelsFromTheCandidate()
    {
        var draft=Draft();var original=JsonSerializer.Serialize(draft,Json);
        // Provider only supplies independently located positions; it cannot contradict
        // those coordinates by incorrectly calling a moved marker CONFIRMED.
        var result=Independent(draft,Points(draft,"CONFIRMED",500,164));
        Assert.Equal(SuspensionAuditPolicy.Protocol,result.Protocol);
        Assert.All(result.Points,p=>Assert.Equal("CORRECTED",p.Decision));
        Assert.Equal("CONSENSUS_ESTIMATE",result.Consensus.Status);Assert.Equal(164,result.Consensus.Y);
        Assert.Equal(original,JsonSerializer.Serialize(draft,Json));
        SuspensionAuditPolicy.Validate(result,draft);
        var legacy=Resolve(draft,Points(draft,"CONFIRMED",500,164));
        Assert.Equal("CONTRADICTORY_CONFIRMATION",legacy.Consensus.Status);Assert.Null(legacy.Protocol);
    }
    [Fact] public void V3DerivesConfirmationOnlyWithinTheSameNativePixelTolerance()
    {
        var draft=Draft();var points=Points(draft,"CORRECTED",500,150);
        points[0]=points[0] with {X=505}; // 5.4px, exactly the inclusive limit.
        points[1]=points[1] with {X=505.001};
        var result=Independent(draft,points);
        Assert.Equal("CONFIRMED",result.Points[0].Decision);Assert.Equal("CORRECTED",result.Points[1].Decision);
        Assert.All(result.Points.Skip(2),p=>Assert.Equal("CONFIRMED",p.Decision));
        draft=draft with {Proposal=draft.Proposal with {Suspension=draft.Proposal.Suspension with {
            Points=draft.Proposal.Suspension.Points.Select(p=>p with {Visible=false,X=0,Y=0,Confidence=0}).ToArray()}}};
        Assert.All(Independent(draft,points).Points,p=>Assert.Equal("CORRECTED",p.Decision));
    }
    [Fact] public void V3RejectsProviderDecisionAndUnknownProtocolInsteadOfReinterpretingThem()
    {
        var draft=Draft();var points=Points(draft);
        Assert.Throws<FormatException>(()=>SuspensionAuditPolicy.Parse(Payload(points),draft,
            SuspensionAuditPolicy.RepresentativeOrdinals(),SuspensionAuditPolicy.Protocol));
        Assert.Throws<FormatException>(()=>SuspensionAuditPolicy.Parse(IndependentPayload(points),draft,
            SuspensionAuditPolicy.RepresentativeOrdinals(),"UNKNOWN"));
        Assert.Throws<FormatException>(()=>SuspensionAuditPolicy.Parse(IndependentPayload(points),draft,
            SuspensionAuditPolicy.RepresentativeOrdinals(),SuspensionAuditPolicy.LegacyProtocol));
    }
    [Fact] public void V3StillRejectsMovingUnresolvedAndSpatiallyUnstableContactEvidence()
    {
        var draft=Draft();var moving=Points(draft);moving[3]=Uncertain(moving[3]) with {ReferenceKind="MOVING_CONTACT"};
        Assert.Equal("MOVING_CORD_CONTACT",Independent(draft,moving).Consensus.Status);
        Assert.Equal("INSUFFICIENT_OBSERVATIONS",Independent(draft,Points(draft).Select(Uncertain).ToArray()).Consensus.Status);
        var unstable=Points(draft).Select((p,i)=>p with {X=400+i*20}).ToArray();
        Assert.Equal("NO_DOMINANT_CLUSTER",Independent(draft,unstable).Consensus.Status);
        Assert.Equal("CAMERA_UNRESOLVED",Independent(draft,Points(draft),"MOVING").Consensus.Status);
    }
    [Fact] public void V3StoredDecisionsCannotBeChangedOrRemovedWithoutInvalidatingTheAudit()
    {
        var draft=Draft();var audit=Independent(draft,Points(draft,"CONFIRMED",500,150));
        var changed=audit.Points.ToArray();changed[0]=changed[0] with {Decision="CORRECTED"};
        Assert.Throws<FormatException>(()=>SuspensionAuditPolicy.Validate(audit with {Points=changed},draft));
        Assert.Throws<FormatException>(()=>SuspensionAuditPolicy.Validate(audit with {Protocol="UNKNOWN"},draft));
        var serialized=JsonSerializer.Serialize(audit,Json);
        Assert.Contains("\"protocol\":\""+SuspensionAuditPolicy.Protocol+"\"",serialized);
        var restored=JsonSerializer.Deserialize<SuspensionAuditEvidence>(serialized,Json)!;
        SuspensionAuditPolicy.Validate(restored,draft);Assert.Equal(serialized,JsonSerializer.Serialize(restored,Json));
        var legacy=Resolve(draft,Points(draft));var old=JsonSerializer.Serialize(new {camera=legacy.Camera,points=legacy.Points,consensus=legacy.Consensus},Json);
        Assert.Equal(old,JsonSerializer.Serialize(legacy,Json));
    }

}
