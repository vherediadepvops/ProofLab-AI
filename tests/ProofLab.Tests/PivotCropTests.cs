
using System.Text.Json;using ProofLab.Api;
namespace ProofLab.Tests;
public class PivotCropTests {
 static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 static readonly KnotFrame[] Frames=Enumerable.Range(0,3).Select(i=>new KnotFrame(i, i,1080,1920,new string('a',64))).ToArray();
 static readonly ObjectReviewImage[] Images=Frames.Select((f,i)=>new ObjectReviewImage(i,f,new(i,"object",true,400,700,450,800,.9),new string('b',64),new(300,100,400,600,new string('c',64)))).ToArray();
 static readonly AssemblyEvidence Draft=new("key",new string('a',64),AssemblyPolicy.Model,AssemblyPolicy.Protocol,Frames,new string('d',64),"old",DateTimeOffset.UtcNow,
  new("simple_pendulum",[],new("UNCERTAIN_MANUAL_SELECTION_REQUIRED",null,null,0,[])),1,1);
 static string Answer(string camera="STATIC",double x=500,bool visible=true,int last=2)=>JsonSerializer.Serialize(new {
  observations=Images.Select(r=>new {frame=r.Ordinal,decision="CONFIRMED",visible=true,x1=400,y1=700,x2=450,y2=800,confidence=.9,visual_reason="Hanging body"}),
  target=new {status="CLEAR_SINGLE_OBJECT",justification=new {es="Objeto",en="Body"}},
  suspension=new {camera,points=Images.Select((r,i)=>new {frame=i==2?last:i,visible=visible||i>0,x=visible||i>0?x:0,y=visible||i>0?250:0,confidence=visible||i>0?.95:0,visual_reason="Visible free cord exit"})}},Json);
 [Fact]public void MapsCropCoordinatesBackToPortraitOriginalWithoutAxisOrScaleConfusion(){
  var p=ObjectReviewPolicy.Parse(Answer(),Draft,Images).Suspension;
  Assert.Equal(500/1080d*1000,p.X!.Value,8);Assert.Equal(250/1920d*1000,p.Y!.Value,8);
  Assert.Equal(0,p.SpreadPixels);Assert.All(p.Points,x=>Assert.True(x.Visible));
 }
 [Theory][InlineData("MOVING")][InlineData("UNCLEAR")]
 public void CameraMotionCannotBeReplacedWithCoarseOldPivot(string camera){
  var p=ObjectReviewPolicy.Parse(Answer(camera),Draft,Images).Suspension;
  Assert.Null(p.X);Assert.All(p.Points,x=>Assert.False(x.Visible));
 }
 [Fact]public void AnOccludedAnchorRemainsInconclusive(){Assert.Null(ObjectReviewPolicy.Parse(Answer(visible:false),Draft,Images).Suspension.X);}
 [Fact]public void RejectsCoordinatesOutsideCrop(){Assert.Throws<FormatException>(()=>ObjectReviewPolicy.Parse(Answer(x:1000),Draft,Images));}
 [Fact]public void RejectsDuplicateFrameAssociation(){Assert.Throws<FormatException>(()=>ObjectReviewPolicy.Parse(Answer(last:1),Draft,Images));}
 [Fact]public void ChoosesInteriorSourceTimesWithoutLookingAtAnswers(){
  var images=Enumerable.Range(0,11).Select(i=>Images[0] with {Ordinal=i,Frame=Frames[0] with {Index=i*60,TimeSeconds=i}}).ToArray();
  Assert.Equal(new[]{2,5,8},ObjectReviewPolicy.SuspensionOrdinals(images));
 }
 [Fact]public void ClippedTopCropRetainsExactDimensions(){var c=ObjectReviewPolicy.Crop(new(0,0,640,480,""),new(0,true,544,45,.9));Assert.Equal(new SuspensionCrop(220,0,256,192,""),c);}
 [Fact]public void LegacyReviewDerivationIsStable(){
  var images=Images.Select(r=>r with {SuspensionCrop=null}).ToArray();var text=Answer();
  var raw=JsonSerializer.Serialize(new {id="test",model=AssemblyPolicy.Model,status="completed",usage=new {input_tokens=1,output_tokens=1},output=new[]{new {type="message",content=new[]{new {type="output_text",text}}}}});
  var evidence=ObjectReviewPolicy.Derive(Draft,new(raw,"test",1,1),images);
  Assert.Equal(ObjectReviewPolicy.LegacyProtocol,evidence.Derivation);
  Assert.Equal(KnotPolicy.Hash(ObjectReviewPolicy.LegacyProtocol+"\n"+Draft.ResponseSha256+"\n"+KnotPolicy.Hash(raw)),evidence.ResponseSha256);
  Assert.Equal(Draft.Proposal.Suspension,evidence.Proposal.Suspension);
 }
}
