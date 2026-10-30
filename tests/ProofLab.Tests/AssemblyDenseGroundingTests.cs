using System.Text.Json;
using ProofLab.Api;
namespace ProofLab.Tests;
public sealed class AssemblyDenseGroundingTests {
    private static readonly KnotFrame[] Frames=Enumerable.Range(0,50).Select(i=>new KnotFrame(i*10,i/6d,1080,1920,new string('a',64))).ToArray();
    private static string Payload(int bad=-1)=>JsonSerializer.Serialize(new {
        arrangement="simple_pendulum",
        samples=Enumerable.Range(0,50).Select(i=>new double[]{i==bad?51:i,1,400,600,450,650,.95}),
        points=new[]{new double[]{0,1,500,100,.95},new double[]{25,1,500,100,.95},new double[]{49,1,500,100,.95}},
        target=new {status="CLEAR_SINGLE_OBJECT",justification=new {es="Objeto suspendido visible.",en="Visible suspended object."}}
    });
    [Fact]public void DenseObservationsKeepSourceCorrespondenceAndSuspensionOrdinals(){
        var p=AssemblyPolicy.ParseGrounding(Payload(),Frames);
        Assert.Equal(50,p.Observations.Length);Assert.Equal(new[]{0,25,49},p.Suspension.Points.Select(x=>x.Frame));
        Assert.Equal("CLEAR_SINGLE_OBJECT",p.Target!.Status);
        Assert.Equal(49,p.Observations.Last().Frame);
    }
    [Theory][InlineData(0)][InlineData(25)][InlineData(49)]public void WrongReferenceOrdinalIsRejected(int bad)=>Assert.Throws<FormatException>(()=>AssemblyPolicy.ParseGrounding(Payload(bad),Frames));
    [Fact]public void NativePlanUsesAtMostThreeKeyframeStartsAndCoversEverySourceFrame(){
        var keys=Enumerable.Range(0,34).Select(i=>i*56).Where(i=>i<1845).ToArray();
        var plan=SamAdaptiveSegmentPlan.Native(1845,keys,Enumerable.Range(1,1845).ToArray(),Enumerable.Range(0,50).Select(i=>(int)Math.Round(1844*i/49d)).ToArray(),53,8);
        Assert.Equal(3,plan.PartCount);Assert.Equal(3,plan.MaxInFlight);Assert.True(plan.OriginalPacketCopy);
        Assert.Equal(1845,plan.Windows.Sum(w=>w.CoreFrames));
        foreach(var w in plan.Windows)Assert.Contains(w.InputStart,keys);
        for(int i=1;i<plan.PartCount;i++)Assert.Equal(plan.Windows[i-1].CoreEndExclusive,plan.Windows[i].CoreStart);
        Assert.True(plan.BilledFrames<1900);
    }
    [Fact]public void FiftyReferencesCoverEveryOverlapAndBothVideoEnds(){
        var plan=SamAdaptiveSegmentPlan.Native(1800,Enumerable.Range(0,18).Select(i=>i*100).ToArray(),Enumerable.Range(1,1800).ToArray(),null,53,8);
        var refs=AssemblyPolicy.ReferenceIndices(1800,plan);
        Assert.Equal(50,refs.Length);Assert.Equal(50,refs.Distinct().Count());Assert.Equal(0,refs[0]);Assert.Equal(1799,refs[^1]);
        foreach(var pair in plan.Windows.Zip(plan.Windows.Skip(1)))foreach(var frame in Enumerable.Range(pair.Second.InputStart,pair.First.InputEndExclusive-pair.Second.InputStart))Assert.Contains(frame,refs);
    }
    [Fact]public void SparseKeyframesReducePartsWithoutTranscoding(){
        var plan=SamAdaptiveSegmentPlan.Native(223,new[]{0,100},Enumerable.Range(1,223).ToArray(),new[]{0,50,110,200},53,8);
        Assert.Equal(2,plan.PartCount);Assert.Equal(223,plan.Windows.Sum(w=>w.CoreFrames));
    }
    [Fact]public void ProgressFreezesFinishedElapsedTimeAndMarksInterruptedStageFailed(){
        using(var run=AssemblyProgress.Begin("test","baseline")){AssemblyProgress.Start("upload");AssemblyProgress.Upload(50,100);Assert.Equal(50,run.Read().Stages.Single().Percent);AssemblyProgress.Done("upload");AssemblyProgress.Start("inference");}
        var stages=AssemblyProgress.Read("test","baseline")!.Stages;
        Assert.Equal("COMPLETED",stages[0].State);Assert.Equal("FAILED",stages[1].State);
    }
}
