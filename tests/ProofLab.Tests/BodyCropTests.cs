
using System.Text.Json;using ProofLab.Api;
namespace ProofLab.Tests;
public class BodyCropTests {
 static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 static readonly ObjectReviewImage[] Images=Enumerable.Range(0,3).Select(i=>new ObjectReviewImage(i,new(i,i,1080,1920,new string('a',64)),new(i,"object",true,400,600,450,700,.9),new string('b',64),null,new(200,600,432,768,new string('c',64)))).ToArray();
 static readonly AssemblyEvidence Draft=new("key",new string('a',64),AssemblyPolicy.Model,AssemblyPolicy.Protocol,Images.Select(i=>i.Frame).ToArray(),new string('d',64),"old",DateTimeOffset.UtcNow,new("simple_pendulum",[],new("UNCERTAIN_MANUAL_SELECTION_REQUIRED",null,null,0,[])),1,1);
 static string Answer(double x1=250,double x2=750)=>JsonSerializer.Serialize(new {observations=Images.Select(r=>new {frame=r.Ordinal,decision="CORRECTED",visible=true,x1,y1=250,x2,y2=750,confidence=.9,visual_reason="Body independently located"}),target=new {status="CLEAR_SINGLE_OBJECT",justification=new {es="Objeto",en="Body"}}},Json);
 [Fact]public void MapsBodyCornersFromPortraitCropToOriginalPixels(){
  var result=ObjectReviewPolicy.Parse(Answer(),Draft,Images);
  Assert.All(result.Observations,b=>{Assert.Equal(308/1080d*1000,b.X1,8);Assert.Equal(524/1080d*1000,b.X2,8);Assert.Equal(792/1920d*1000,b.Y1,8);Assert.Equal(1176/1920d*1000,b.Y2,8);});
 }
 [Theory][InlineData(0,750)][InlineData(250,1000)]public void ClippedBodyIsNotASamSeed(double x1,double x2){
  var result=ObjectReviewPolicy.Parse(Answer(x1,x2),Draft,Images);
  Assert.All(result.Observations,b=>{Assert.False(b.Visible);Assert.Equal(0,b.Confidence);});
  Assert.Null(AssemblyPolicy.Ground(Draft with {Proposal=result,Protocol=AssemblyPolicy.GroundingProtocol,CacheKey=AssemblyPolicy.GroundingKey(Draft.VideoSha256)}));
 }
 [Fact]public void RejectsCropOutsideSource(){Assert.Throws<FormatException>(()=>ObjectReviewPolicy.Parse(Answer(),Draft,Images.Select(i=>i with {BodyCrop=i.BodyCrop! with {Left=1080}}).ToArray()));}
 [Fact]public void BroadSearchIncludesFullCoarseBoxWithoutColorOrMaterialAssumption(){
  var f=new KnotFrame(0,0,1080,1920,"");var c=ObjectReviewPolicy.BodyRegion(f,new(0,"object",true,400,200,450,800,.9));
  Assert.Equal(1920,c.Height);Assert.True(c.Left<=432);Assert.True(c.Left+c.Width>=486);
 }

 [Fact]public void CoarseCordBoxRetainsVerticalSearchContext(){
  var c=ObjectReviewPolicy.BodyRegion(new(0,0,1080,1920,""),new(0,"object",true,390,580,504,676,.9));
  Assert.True(c.Height>=1920*.6);Assert.True(c.Top+c.Height>1700);Assert.True(c.Top>=0);Assert.True(c.Top+c.Height<=1920);
 }
 [Fact]public void V3DerivationBindsActualResponseAndRetainsDraft(){
  var raw=JsonSerializer.Serialize(new {id="test",model=AssemblyPolicy.Model,status="completed",usage=new {input_tokens=1,output_tokens=1},output=new[]{new {type="message",content=new[]{new {type="output_text",text=Answer()}}}}});
  var e=ObjectReviewPolicy.Derive(Draft,new(raw,"test",1,1),Images);
  Assert.Equal(ObjectReviewPolicy.Protocol,e.Derivation);Assert.Equal(KnotPolicy.Hash(ObjectReviewPolicy.Protocol+"\n"+Draft.ResponseSha256+"\n"+KnotPolicy.Hash(raw)),e.ResponseSha256);Assert.Empty(Draft.Proposal.Observations);
 }
}
