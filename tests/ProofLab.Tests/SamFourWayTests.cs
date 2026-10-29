using ProofLab.Api;

namespace ProofLab.Tests;

/// <summary>
/// All inputs are synthetic. No media transfer or billable Meta calls.
/// The API transport is deliberately NOT involved in these tests.
/// </summary>
public sealed class SamFourWayTests
{
    private static readonly string Video=new('a',64);
    private static readonly string Scene=new('b',64);
    private static double[] Timeline(int n=1800)=>
        Enumerable.Range(0,n).Select(i=>i/60d).ToArray();
    private static SamFourWayDetection Detection(SamFourWayWindow window,
        int sourceFrame,string? id=null,double dx=0)
    {
        var x=100+sourceFrame*0.05+dx;
        var y=60+Math.Sin(sourceFrame/60d)*10;
        return new(sourceFrame-window.InputStart,
            id??"id-"+window.Part, x,y,x-8,y-8,x+8,y+8);
    }
    private static SamFourWaySegment Segment(SamFourWayWindow window,
        Func<int,bool>? include=null,double dx=0)
    {
        return new(window.Part,Video,Scene,true,
            Enumerable.Range(window.InputStart,window.InputFrames)
                .Where(i=>include?.Invoke(i)??true)
                .Select(i=>Detection(window,i,dx:dx)).ToArray());
    }
    private static (SamFourWayPlan Plan,double[] Pts) Prepare(int n=1800)
    {
        var pts=Timeline(n);
        return (SamFourWayPlan.Create(pts),pts);
    }

    [Fact]
    public void FourPartitionsAreFrameExactWithBoundedOverlap()
    {
        var (plan,pts)=Prepare();
        Assert.Equal(4,plan.Windows.Count);
        Assert.Equal(1800,plan.SourceFrames);
        Assert.Equal(1812,plan.SubmittedFrames);
        Assert.Equal(.3624,plan.EstimatedSamCostUsd,4);
        Assert.All(plan.Windows,w=>Assert.Equal(450,w.CoreFrames));
        Assert.Equal(0,plan.Windows[0].CoreStart);
        Assert.Equal(1800,plan.Windows[^1].CoreEndExclusive);
        Assert.Equal(448,plan.Windows[1].InputStart);
        Assert.Equal(902,plan.Windows[1].InputEndExclusive);
        Assert.Equal(pts[448]-pts[448],plan.LocalFramePts(pts,plan.Windows[1],0),8);
        Assert.Equal(pts[900]-pts[448],
            plan.LocalFramePts(pts,plan.Windows[1],452),8);
        Assert.Equal(0,plan.Windows[0].GlobalFrame(0));
        Assert.Equal(1800,plan.Windows.Sum(w=>w.CoreFrames));
    }

    [Fact]
    public void RejectsUnsafePtsAndInsufficientBudget()
    {
        Assert.Throws<ArgumentException>(()=>SamFourWayPlan.Create(Timeline(199)));
        Assert.Throws<ArgumentException>(()=>SamFourWayPlan.Create(Timeline(2501)));
        Assert.Throws<ArgumentException>(()=>SamFourWayPlan.Create(Timeline(2500)));
        var backward=Timeline();backward[300]=backward[299];
        Assert.Throws<ArgumentException>(()=>SamFourWayPlan.Create(backward));
        var nan=Timeline();nan[75]=double.NaN;
        Assert.Throws<ArgumentException>(()=>SamFourWayPlan.Create(nan));
    }

    [Fact]
    public void JoinFourSegmentsTracksSameBobAcrossDifferentProviderObjectIds()
    {
        var (plan,pts)=Prepare();
        var rows=plan.Windows.Select(w=>Segment(w)).ToArray();
        var merged=SamFourWayStitcher.Merge(plan,pts,Video,Scene,rows);
        Assert.Equal(1800,merged.ObservedFrames.Count);
        Assert.Equal(1d,merged.Coverage);
        Assert.True(merged.EverySeamVerified);
        Assert.Equal(3,merged.Seams.Count);
        Assert.All(merged.Seams,s=>Assert.Equal(4,s.VerifiedCommonFrames));
        Assert.Equal("id-0",merged.ObservedFrames[449].ObjectId);
        Assert.Equal("id-1",merged.ObservedFrames[450].ObjectId);
        Assert.Equal(pts[450],merged.ObservedFrames[450].SourcePts);
        Assert.Equal(Enumerable.Range(0,1800),
            merged.ObservedFrames.Select(f=>f.FrameIndex));
    }

    [Fact]
    public void ContradictoryOverlapIsRejectedRatherThanSeamingByGuess()
    {
        var (plan,pts)=Prepare();
        var chunks=plan.Windows.Select(w=>Segment(w)).ToArray();
        chunks[1]=Segment(plan.Windows[1],dx:50);
        Assert.Throws<InvalidDataException>(()=>
            SamFourWayStitcher.Merge(plan,pts,Video,Scene,chunks));
    }

    [Fact]
    public void MissingMasksStayMissingAndUnverifiedSeamsCannotBeApproved()
    {
        var (plan,pts)=Prepare();
        // Remove the four seam validation frames from part 1, including
        // non-core overlap; do not interpolate or invent any masks.
        var boundary=450;
        var chunks=plan.Windows.Select(w=>
            w.Part==1 ? Segment(w,i=>Math.Abs(i-boundary)>3) : Segment(w)
        ).ToArray();
        var merged=SamFourWayStitcher.Merge(plan,pts,Video,Scene,chunks);
        Assert.True(merged.ObservedFrames.Count<1800);
        Assert.False(merged.EverySeamVerified);
        Assert.False(merged.Seams[0].IdentityVerified);
        Assert.DoesNotContain(merged.ObservedFrames,f=>f.FrameIndex==450);
        Assert.True(merged.Coverage<1);
    }

    [Fact]
    public void RejectsSourceTimelineTamperingEvenWhenFrameCountMatches()
    {
        var (plan,pts)=Prepare();
        var rows=plan.Windows.Select(w=>Segment(w)).ToArray();
        pts[37]+=0.001;
        Assert.False(plan.MatchesSourcePts(pts));
        Assert.Throws<InvalidDataException>(()=>
            SamFourWayStitcher.Merge(plan,pts,Video,Scene,rows));
    }

    [Fact]
    public void RejectsIncompleteOrAmbiguousProviderChunks()
    {
        var (plan,pts)=Prepare();
        var original=plan.Windows.Select(w=>Segment(w)).ToArray();
        var missing=original.Take(3).ToArray();
        Assert.Throws<InvalidDataException>(()=>
            SamFourWayStitcher.Merge(plan,pts,Video,Scene,missing));
        original[2]=original[2] with {ProviderCompleted=false};
        Assert.Throws<InvalidDataException>(()=>
            SamFourWayStitcher.Merge(plan,pts,Video,Scene,original));
        original[2]=original[2] with {ProviderCompleted=true,SceneKey="another-scene"};
        Assert.Throws<InvalidDataException>(()=>
            SamFourWayStitcher.Merge(plan,pts,Video,Scene,original));
    }

    [Fact]
    public async Task StreamsProgressFromFourPartsWithoutWaitingForOtherParts()
    {
        var (plan,pts)=Prepare();
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started=0;
        var messages=new System.Collections.Concurrent.ConcurrentBag<SamFourWayProgress>();
        var operation=SamFourWayStreaming.RunAsync(
            plan,pts,Video,Scene,
            async (w,progress,ct)=>{
                Interlocked.Increment(ref started);
                await progress(new("SSE_DELTA_PROGRESS",5,4,200));
                await release.Task.WaitAsync(ct);
                return Segment(w);
            },
            p=>{messages.Add(p);return ValueTask.CompletedTask;},
            CancellationToken.None);
        try
        {
            var until=DateTime.UtcNow.AddSeconds(3);
            while(Volatile.Read(ref started)!=4 && DateTime.UtcNow<until)
                await Task.Delay(5);
            Assert.Equal(4,Volatile.Read(ref started));
            Assert.Equal(4,messages.Count(m=>m.Stage=="SSE_DELTA_PROGRESS"));
        }
        finally {release.TrySetResult();}
        var merged=await operation;
        Assert.True(merged.EverySeamVerified);
        Assert.Equal(4,messages.Count(m=>m.Stage=="CHUNK_PROVIDER_COMPLETED"));
    }

    [Fact]
    public async Task OneFailedPartDoesNotRetryOrCancelTheOtherThree()
    {
        var (plan,pts)=Prepare();
        var attempted=new int[4];
        var successful=0;
        var run=SamFourWayStreaming.RunAsync(
            plan,pts,Video,Scene,
            async (w,progress,ct)=>{
                Interlocked.Increment(ref attempted[w.Part]);
                await Task.Delay(2,ct);
                if(w.Part==2)throw new InvalidDataException("synthetic failure");
                Interlocked.Increment(ref successful);
                return Segment(w);
            },null,CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(()=>run);
        Assert.Equal(new[]{1,1,1,1},attempted);
        Assert.Equal(3,successful);
    }

    [Fact]
    public void BenchmarkUsesOnlyIntersectingFramesAndReportsImageSpaceErrors()
    {
        var (plan,pts)=Prepare();
        var chunks=plan.Windows.Select(w=>Segment(w)).ToArray();
        var merged=SamFourWayStitcher.Merge(plan,pts,Video,Scene,chunks);
        var perfect=SamFourWayBenchmark.Compare(merged,merged.ObservedFrames);
        Assert.Equal(1800,perfect.ComparedFrames);
        Assert.Equal(0,perfect.P95CentroidErrorPx);
        Assert.True(perfect.SeamsVerified);
        var offset=merged.ObservedFrames
            .Select(x=>x with {CentroidX=x.CentroidX+2}).ToArray();
        var wrong=SamFourWayBenchmark.Compare(merged,offset);
        Assert.Equal(2,wrong.MeanCentroidErrorPx,6);
        Assert.Equal(2,wrong.P95CentroidErrorPx,6);
        var incomplete=SamFourWayBenchmark.Compare(merged,offset.Take(900).ToArray());
        Assert.Equal(900,incomplete.ComparedFrames);
        Assert.Equal(900,incomplete.MissingCandidateFrames);
    }
}
