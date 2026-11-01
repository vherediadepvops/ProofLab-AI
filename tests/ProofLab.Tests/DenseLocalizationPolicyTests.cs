using ProofLab.Api;
namespace ProofLab.Tests;

public sealed class DenseLocalizationPolicyTests {
    private static KnotFrame[] Frames(int count=50)=>Enumerable.Range(0,count)
        .Select(i=>new KnotFrame(i,i*.2,1080,1920,new string('a',64))).ToArray();
    private static KnotPoint[] Points(int count=50)=>Enumerable.Range(0,count)
        .Select(i=>new KnotPoint(i,true,500,150,.95)).ToArray();

    [Fact] public void IsolatedWrongObservationsDoNotDiscardTheDominantExit() {
        var points=Points();
        for(var i=0;i<50;i+=5)points[i]=points[i] with {X=650+i,Y=300+i};
        var result=DenseLocalizationPolicy.Resolve(Frames(),points,"STATIC");
        Assert.Equal("CONSENSUS_ESTIMATE",result.Status);
        Assert.Equal(40,result.InlierOrdinals.Length);Assert.Equal(500,result.X);Assert.Equal(150,result.Y);
        Assert.True(result.TimeCoverage>.9);Assert.Equal(0,result.SpreadPixels);
    }
    [Fact] public void CompetingClustersAreNotAveragedIntoAnInventedPoint() {
        var points=Points();
        for(var i=0;i<25;i++)points[i]=points[i] with {X=700};
        var result=DenseLocalizationPolicy.Resolve(Frames(),points,"STATIC");
        Assert.Equal("NO_DOMINANT_CLUSTER",result.Status);Assert.Null(result.X);Assert.Null(result.Y);
    }
    [Theory][InlineData("MOVING")][InlineData("UNCLEAR")]
    public void CameraMustSupportAFixedPoint(string camera) {
        Assert.Equal("CAMERA_UNRESOLVED",DenseLocalizationPolicy.Resolve(Frames(),Points(),camera).Status);
    }
    [Fact] public void RepeatedCoordinatesInOneShortMomentAreInsufficient() {
        var points=Points();
        for(var i=10;i<50;i++)points[i]=points[i] with {Visible=false,X=0,Y=0,Confidence=0};
        var result=DenseLocalizationPolicy.Resolve(Frames(),points,"STATIC");
        Assert.Equal("INSUFFICIENT_TIME_COVERAGE",result.Status);Assert.Null(result.X);
    }
    [Fact] public void ThreeRoundsUseDistinctFramesAndStopWhenFewerThanFiftyRemain() {
        var used=new HashSet<int>();
        for(var round=0;round<3;round++) {
            var next=DenseLocalizationPolicy.NextReferences(160,used);
            Assert.Equal(50,next.Length);Assert.All(next,i=>Assert.True(used.Add(i)));
        }
        Assert.Equal(150,used.Count);Assert.Empty(DenseLocalizationPolicy.NextReferences(160,used));
    }
    [Fact] public void NativePixelGeometryIsUsedForPortraitVideo() {
        var points=Points();
        for(var i=0;i<50;i+=2)points[i]=points[i] with {Y=170};
        Assert.Equal("NO_DOMINANT_CLUSTER",DenseLocalizationPolicy.Resolve(Frames(),points,"STATIC").Status);
    }
}
