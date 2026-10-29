using ProofLab.Api;

namespace ProofLab.Tests;

/// <summary>Purely synthetic seed coverage; no paid SAM or Spark inference.</summary>
public sealed class SamAdaptiveGroundedPlanTests
{
    private static readonly string Hash=new('a',64);
    private static SamSceneDefinition Scene(int frames,params int[] seeds) =>
        new(Hash,Hash,Hash,Hash,1920,1080,"simple_pendulum",
            [new SamSceneRole("1","object")],
            seeds.Select(i=>new SamSceneSeed(i,"object",100,100,120,120)).ToArray(),
            null,null);

    [Fact]
    public void ThreeAuthentic667FrameSparkAnchorsBoundTheSafeFragmentCount()
    {
        var spark=Scene(667,67,333,599);
        var optimal=SamAdaptiveGroundedPlan.Select(667,spark);
        Assert.Equal(13,optimal.IdealParts);
        Assert.Equal(4,optimal.Plan.PartCount);
        Assert.Equal(3,optimal.VerifiedSparkSeedCount);
        Assert.True(optimal.ReducedForSparseSparkSeeds);
        Assert.Equal(4,optimal.Plan.MaxInFlight);
        Assert.True(optimal.Plan.EstimatedSamCostUsd<=.5);
        Assert.All(optimal.Plan.Windows,w=>{
            var actual=SamAdaptiveGroundedPlan.SourceSeeds(spark,w);
            Assert.NotEmpty(actual);
            Assert.All(actual,s=>Assert.InRange(s.Frame,w.InputStart,w.InputEndExclusive-1));
        });
    }

    [Fact]
    public void EveryGroundedPartPromptUsesOnlyItsOwnOriginalSparkSeed()
    {
        var spark=Scene(667,67,333,599);
        var decision=SamAdaptiveGroundedPlan.Select(667,spark);
        Assert.Equal(4,decision.Plan.PartCount);
        foreach(var window in decision.Plan.Windows)
        {
            var prompt=SamAdaptiveGroundedPlan.BuildPrompt(spark,window);
            Assert.True(AssemblyPolicy.IsSamPrompt(prompt));
            var seed=SamAdaptiveGroundedPlan.SourceSeeds(spark,window);
            Assert.NotEmpty(seed);
            foreach(var anchor in seed)
            {
                var local=anchor.Frame-window.InputStart;
                Assert.Contains("<"+local+"f>",prompt);
                Assert.InRange(local,0,window.InputFrames-1);
            }
            Assert.DoesNotContain("metal pendulum bob",prompt,
                StringComparison.OrdinalIgnoreCase);
        }
        var uncovered=SamAdaptiveSegmentPlan.Create(
            667,alignToTwoFrameIntraKeyframes:true);
        Assert.Equal(13,uncovered.PartCount);
        Assert.Contains(uncovered.Windows,w=>
            SamAdaptiveGroundedPlan.SourceSeeds(spark,w).Length==0);
        var firstUncovered=uncovered.Windows.First(w=>
            SamAdaptiveGroundedPlan.SourceSeeds(spark,w).Length==0);
        Assert.Throws<InvalidDataException>(()=>
            SamAdaptiveGroundedPlan.BuildPrompt(spark,firstUncovered));
    }

    [Fact]
    public void SingleVerifiedSeedCanGroundTwoAdjacentOverlappingWindows()
    {
        var spark=Scene(667,333);
        var result=SamAdaptiveGroundedPlan.Select(667,spark);
        Assert.Equal(2,result.Plan.PartCount);
        Assert.True(result.ReducedForSparseSparkSeeds);
        Assert.Equal(672,result.Plan.SubmittedFrames);
        Assert.Equal(.1344,result.Plan.EstimatedSamCostUsd,5);
    }

    [Fact]
    public void EmptyAndOutOfRangeAnchorsNeverTriggerUnverifiedPaidTracking()
    {
        Assert.Throws<InvalidDataException>(()=>
            SamAdaptiveGroundedPlan.Select(667,Scene(667)));
        Assert.Throws<InvalidDataException>(()=>
            SamAdaptiveGroundedPlan.Select(667,Scene(667,999,1000)));
    }

    [Fact]
    public void AllWindowsGroundedWhenThereAreDenseAuthenticAnchors()
    {
        var spark=Scene(1000,Enumerable.Range(0,1000).Where(x=>x%15==0).ToArray());
        var result=SamAdaptiveGroundedPlan.Select(1000,spark,maxInFlight:8);
        Assert.Equal(19,result.Plan.PartCount);
        Assert.Equal(8,result.Plan.MaxInFlight);
        Assert.False(result.ReducedForSparseSparkSeeds);
        Assert.Equal(19,SamAdaptiveSegmentPlan.Create(1000).PartCount);
    }

    [Fact]
    public void PreflightNeverIncreasesSamPricingOrConcurrencyToRecoverSparseCoverage()
    {
        var scene=Scene(1500,150,750,1350);
        var normal=SamAdaptiveSegmentPlan.Create(1500,maxInFlight:4,
            alignToTwoFrameIntraKeyframes:true);
        var grounded=SamAdaptiveGroundedPlan.Select(1500,scene,maxInFlight:4);
        Assert.True(grounded.Plan.PartCount<normal.PartCount);
        Assert.True(grounded.Plan.EstimatedSamCostUsd<=.50);
        Assert.True(grounded.Plan.EstimatedSamCostUsd<=normal.EstimatedSamCostUsd);
        Assert.Equal(4,grounded.Plan.MaxInFlight);
    }

    [Fact]
    public void ExplicitMaximumPartCountBoundsExistingDynamicPlanner()
    {
        var plan=SamAdaptiveSegmentPlan.Create(667,maximumPartCount:4,
            alignToTwoFrameIntraKeyframes:true);
        Assert.Equal(4,plan.PartCount);
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            SamAdaptiveSegmentPlan.Create(667,maximumPartCount:0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            SamAdaptiveSegmentPlan.Create(667,maximumPartCount:51));
    }
}
