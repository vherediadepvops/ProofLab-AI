using System.Collections.Concurrent;
using ProofLab.Api;

namespace ProofLab.Tests;

/// <summary>
/// Synthetic schedules, never real Meta traffic or a budget reservation.
/// </summary>
public sealed class SamAdaptiveSegmentTests
{
    [Theory]
    [InlineData(1000,19,1072,0.2144)]
    [InlineData(1500,28,1608,0.3216)]
    [InlineData(1828,34,1960,0.3920)]
    [InlineData(50,1,50,0.0100)]
    [InlineData(2500,1,2500,0.5000)]
    public void FrameCountDeterminesSegmentsAndTrueOverlapBilling(
        int frames,int expectedParts,int billed,double cost)
    {
        var plan=SamAdaptiveSegmentPlan.Create(frames);
        Assert.Equal(expectedParts,plan.PartCount);
        Assert.Equal(billed,plan.BilledFrames);
        Assert.Equal(cost,plan.EstimatedSamCostUsd,4);
        Assert.Equal(Math.Max(50,frames)*0.0002,plan.UnsplitSamCostUsd,5);
        Assert.Equal(Math.Min(expectedParts,4),plan.MaxInFlight);
        Assert.Equal(4*(expectedParts-1),plan.OverlapExtraFrames);
        Assert.True(plan.EstimatedSamCostUsd<=plan.BudgetCapUsd);
        Assert.Equal(0,plan.Windows[0].CoreStart);
        Assert.Equal(frames,plan.Windows[^1].CoreEndExclusive);
        Assert.All(plan.Windows,w=>{
            Assert.InRange(w.CoreFrames,50,expectedParts==1?2500:500);
            Assert.True(w.InputFrames>=50);
            Assert.InRange(w.InputStart,0,frames-50);
            Assert.InRange(w.InputEndExclusive,50,frames);
        });
        for(var i=1;i<expectedParts;i++)
        {
            Assert.Equal(plan.Windows[i-1].CoreEndExclusive,
                plan.Windows[i].CoreStart);
            Assert.Equal(plan.Windows[i].InputStart+2,
                plan.Windows[i].CoreStart);
            Assert.Equal(plan.Windows[i-1].InputEndExclusive-2,
                plan.Windows[i-1].CoreEndExclusive);
        }
    }

    [Fact]
    public void BudgetNeverSilentlyExceedsTheFullVideoCap()
    {
        var plan=SamAdaptiveSegmentPlan.Create(2400);
        Assert.True(plan.BudgetReducedPartCount);
        Assert.Equal(26,plan.PartCount);
        Assert.Equal(2500,plan.BilledFrames);
        Assert.Equal(.50,plan.EstimatedSamCostUsd,5);
        Assert.Equal(2400,plan.Windows.Sum(w=>w.CoreFrames));
        Assert.Equal(4,plan.MaxInFlight);
    }

    [Fact]
    public void CustomCostCapCanReduceParallelPartsOrRefuseTheJob()
    {
        var p=SamAdaptiveSegmentPlan.Create(1000,53,maxInFlight:8,
            maximumSamCostUsd:.2008);
        Assert.Equal(2,p.PartCount);
        Assert.True(p.BudgetReducedPartCount);
        Assert.Equal(.2008,p.EstimatedSamCostUsd,4);
        Assert.Throws<InvalidOperationException>(()=>
            SamAdaptiveSegmentPlan.Create(1000,53,4,.1999));
    }

    [Fact]
    public void ConcurrentLimitMayBeRaisedExplicitlyButNeverByInferringRPM()
    {
        var p=SamAdaptiveSegmentPlan.Create(1500,maxInFlight:28);
        Assert.Equal(28,p.PartCount);
        Assert.Equal(28,p.MaxInFlight);
        var defaultPolicy=SamAdaptiveSegmentPlan.Create(1500);
        Assert.Equal(4,defaultPolicy.MaxInFlight);
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            SamAdaptiveSegmentPlan.Create(1500,maxInFlight:33));
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            SamAdaptiveSegmentPlan.Create(1500,maxInFlight:0));
    }

    [Fact]
    public void SourcePtsFingerprintMustPreservePhysicalObservationTiming()
    {
        var p=SamAdaptiveSegmentPlan.Create(1000);
        var pts=Enumerable.Range(0,1000).Select(i=>i/59.94).ToArray();
        var sha=p.VerifySourcePts(pts);
        Assert.Equal(64,sha.Length);
        pts[15]=pts[14];
        Assert.Throws<InvalidDataException>(()=>p.VerifySourcePts(pts));
        pts[15]=15/59.94;
        pts[37]+=.001;
        Assert.NotEqual(sha,p.VerifySourcePts(pts));
    }

    [Theory]
    [InlineData(49)]
    [InlineData(2501)]
    public void RejectsSourceOutsideCurrentRealVideoPolicy(int frames)=>
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            SamAdaptiveSegmentPlan.Create(frames));

    [Fact]
    public async Task SchedulerNeverExceedsFourRunningByDefault()
    {
        var plan=SamAdaptiveSegmentPlan.Create(1000);
        var active=0;
        var highest=0;
        var attempted=new ConcurrentBag<int>();
        var steps=new ConcurrentBag<SamAdaptiveOfflineProgress>();
        var result=await SamAdaptiveOfflineScheduler.RunAsync(
            plan,async(w,emit,ct)=>{
                attempted.Add(w.Part);
                var now=Interlocked.Increment(ref active);
                while(true)
                {
                    var old=Volatile.Read(ref highest);
                    if(now<=old || Interlocked.CompareExchange(ref highest,now,old)==old)
                        break;
                }
                await emit(new("SSE_DELTA_PROGRESS",10,9,2048));
                await Task.Delay(15,ct);
                Interlocked.Decrement(ref active);
                return "synthetic-only";
            },
            p=>{steps.Add(p);return ValueTask.CompletedTask;},
            CancellationToken.None);
        Assert.True(result.AllCompleted);
        Assert.Equal(19,attempted.Count);
        Assert.InRange(highest,2,4);
        Assert.Equal(4,result.MaxInFlight);
        Assert.Equal(19,steps.Count(x=>x.Stage=="OFFLINE_SSE_DELTA_PROGRESS"));
        Assert.Equal(0,result.Failed);
    }

    [Fact]
    public async Task Explicit28WayModeCanStart28TasksWithoutAutomaticRetries()
    {
        var plan=SamAdaptiveSegmentPlan.Create(1500,maxInFlight:28);
        var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started=0;
        var operation=SamAdaptiveOfflineScheduler.RunAsync(
            plan,async(w,report,ct)=>{
                Interlocked.Increment(ref started);
                await gate.Task.WaitAsync(ct);
                return w.Part;
            },null,CancellationToken.None);
        try
        {
            var until=DateTime.UtcNow.AddSeconds(3);
            while(Volatile.Read(ref started)<28 && DateTime.UtcNow<until)
                await Task.Delay(5);
            Assert.Equal(28,Volatile.Read(ref started));
        }
        finally {gate.TrySetResult();}
        var result=await operation;
        Assert.True(result.AllCompleted);
        Assert.Equal(28,result.Completed);
    }

    [Fact]
    public async Task FailedPartStopsQueuedWorkButPreservesAlreadyStartedResults()
    {
        var plan=SamAdaptiveSegmentPlan.Create(1000,maxInFlight:4);
        var started=new ConcurrentBag<int>();
        var releaseFirstWave=new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOtherParts=new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var failureReported=new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var operation=SamAdaptiveOfflineScheduler.RunAsync(
            plan,async(w,progress,ct)=>{
                started.Add(w.Part);
                await releaseFirstWave.Task.WaitAsync(ct);
                if(w.Part==0)throw new InvalidDataException("fixture failure");
                await releaseOtherParts.Task.WaitAsync(ct);
                return w.Part.ToString();
            },
            p=>{
                if(p.Stage=="OFFLINE_PART_FAILED_NO_AUTOMATIC_RETRY")
                    failureReported.TrySetResult();
                return ValueTask.CompletedTask;
            },CancellationToken.None);
        try
        {
            var until=DateTime.UtcNow.AddSeconds(3);
            while(started.Count<4 && DateTime.UtcNow<until)
                await Task.Delay(5);
            Assert.Equal(4,started.Count);
            releaseFirstWave.TrySetResult();
            await failureReported.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally {
            releaseFirstWave.TrySetResult();
            releaseOtherParts.TrySetResult();
        }
        var result=await operation;
        Assert.Equal(1,result.Failed);
        Assert.Equal(15,result.Skipped);
        Assert.Equal(3,result.Completed);
        Assert.Equal(4,started.Count);
        Assert.False(result.AllCompleted);
    }

    [Fact]
    public async Task CancelledScheduleDoesNotStartProviderOrOfflineWork()
    {
        var p=SamAdaptiveSegmentPlan.Create(1000,maxInFlight:4);
        using var source=new CancellationTokenSource();
        source.Cancel();
        var executed=0;
        var outcomes=await SamAdaptiveOfflineScheduler.RunAsync(
            p,(w,report,ct)=>{
                Interlocked.Increment(ref executed);
                return Task.FromResult("unexpected");
            },null,source.Token);
        Assert.Equal(0,executed);
        Assert.Equal(p.PartCount,outcomes.Cancelled);
        Assert.False(outcomes.AllCompleted);
    }
}
