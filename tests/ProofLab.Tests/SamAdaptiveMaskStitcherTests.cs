using System.Text;
using System.Text.Json;
using ProofLab.Api;

namespace ProofLab.Tests;

public sealed class SamAdaptiveMaskStitcherTests
{
    private static string Envelope(string wire)=>
        JsonSerializer.Serialize(new {
            model="sam-3.1",status="completed",
            output=new[]{new{content=new[]{new{type="output_text",text=wire}}}}
        });
    private static SamAdaptivePartOutput[] Parts(
        SamAdaptiveSegmentPlan plan,int? mutatedPart=null,bool omitSeam=false)
    {
        return plan.Windows.Select(w=>{
            var result=new StringBuilder();
            for(var frame=w.InputStart;frame<w.InputEndExclusive;frame++)
            {
                if(omitSeam && w.Part==1 && frame==w.InputStart)
                    continue;
                var content=frame==w.InputStart && mutatedPart==w.Part
                    ? "ALTERED" : "SYNTHETIC";
                result.Append('<').Append(frame-w.InputStart)
                    .Append("f>1<|box;x1=20;y1=30;x2=40;y2=50;w=1080;h=1920|>")
                    .Append("<|mask;x=20;y=30;data=21,21,")
                    .Append(content).Append("|>\n");
            }
            var raw=Envelope(result.ToString());
            return new SamAdaptivePartOutput(w.Part,
                w.InputStart,w.InputEndExclusive,
                w.CoreStart,w.CoreEndExclusive,
                SamVideoArchive.Hash(raw),raw);
        }).ToArray();
    }

    [Fact]
    public void SeedFirstThenBackwardStreamPreservesFramesAndCanonicalTarget()
    {
        var plan=SamAdaptiveSegmentPlan.Create(182,alignToTwoFrameIntraKeyframes:true);
        var parts=Parts(plan).Select(p=>{
            var lines=SamFourWayMaskAssembler.OutputWire(p.OriginalProviderResponseJson)
                .Split('\n',StringSplitOptions.RemoveEmptyEntries);
            var raw=Envelope(string.Join('\n',lines.Skip(18).Concat(lines.Take(18).Reverse()))+"\n");
            return p with{OriginalProviderResponseJson=raw,ResponseJsonSha256=SamVideoArchive.Hash(raw)};
        }).ToArray();
        var result=SamAdaptiveMaskStitcher.Stitch(plan,parts);
        Assert.Equal(182,result.FramesWithMasks);Assert.True(result.SeamsVerified);
        var wire=SamFourWayMaskAssembler.OutputWire(result.DerivedResponseJson);
        Assert.StartsWith("<0f>1",wire);Assert.DoesNotContain("f>0<|box;",wire);
        var duplicate=parts[0].OriginalProviderResponseJson;
        var repeated=SamFourWayMaskAssembler.OutputWire(duplicate);
        var invalid=Envelope(repeated+repeated.Split('\n')[0]+"\n");
        parts[0]=parts[0] with{OriginalProviderResponseJson=invalid,ResponseJsonSha256=SamVideoArchive.Hash(invalid)};
        Assert.Throws<InvalidDataException>(()=>SamAdaptiveMaskStitcher.Stitch(plan,parts));
    }
    [Theory]
    [InlineData(0.97,true)]
    [InlineData(0.89,false)]
    [InlineData(0,false)]
    public void RasterOverlapChecksEverySharedFrame(double iou,bool expected)
    {
        var plan=SamAdaptiveSegmentPlan.Create(182,alignToTwoFrameIntraKeyframes:true);
        var result=SamAdaptiveMaskStitcher.Stitch(plan,Parts(plan,mutatedPart:1),(a,b)=>iou);
        Assert.Equal(expected,result.SeamsVerified);
    }
    [Fact]
    public void DynamicallySplit667MasksJoinWithoutMissingFrames()
    {
        var plan=SamAdaptiveSegmentPlan.Create(667,
            alignToTwoFrameIntraKeyframes:true);
        var parts=Parts(plan);
        var full=SamAdaptiveMaskStitcher.Stitch(plan,parts);
        Assert.True(plan.PartCount>4);
        Assert.Equal(667,full.SourceFrames);
        Assert.Equal(667,full.FramesWithMasks);
        Assert.Equal(0,full.MissingCoreFrames);
        Assert.True(full.SeamsVerified);
        Assert.True(full.SuitableForScientificView);
        Assert.Equal(plan.PartCount-1,full.Seams.Count);
        Assert.All(full.Seams,x=>Assert.True(x.IdenticalMasks>0));
        Assert.Contains("DERIVED_FROM_INDEPENDENT",full.Provenance);
        Assert.NotEqual(parts[0].ResponseJsonSha256,full.DerivedResponseSha256);
    }

    [Fact]
    public void DivergentOverlapNeverPassesAsVerified()
    {
        var plan=SamAdaptiveSegmentPlan.Create(667,
            alignToTwoFrameIntraKeyframes:true);
        var parts=Parts(plan,mutatedPart:1);
        var result=SamAdaptiveMaskStitcher.Stitch(plan,parts);
        Assert.False(result.SeamsVerified);
        Assert.False(result.SuitableForScientificView);
        Assert.Contains(result.Seams,s=>s.ContradictoryMasks>0);
    }

    [Fact]
    public void MissingWholePartIsNotConvertedIntoImaginaryEvidence()
    {
        var p=SamAdaptiveSegmentPlan.Create(667,
            alignToTwoFrameIntraKeyframes:true);
        Assert.Throws<InvalidDataException>(()=>
            SamAdaptiveMaskStitcher.Stitch(p,Parts(p).Take(p.PartCount-1).ToArray()));
    }

    [Fact]
    public void ResponseShaAndFrameIndexAreRequired()
    {
        var p=SamAdaptiveSegmentPlan.Create(667,
            alignToTwoFrameIntraKeyframes:true);
        var parts=Parts(p);
        parts[0]=parts[0] with {ResponseJsonSha256=new string('f',64)};
        Assert.Throws<InvalidDataException>(()=>
            SamAdaptiveMaskStitcher.Stitch(p,parts));
        parts=Parts(p);
        var raw=SamFourWayMaskAssembler.OutputWire(parts[0].OriginalProviderResponseJson);
        parts[0]=parts[0] with {
            OriginalProviderResponseJson=Envelope(
                raw.Replace("<0f>1","<9000f>1",StringComparison.Ordinal))
        };
        parts[0]=parts[0] with {
            ResponseJsonSha256=SamVideoArchive.Hash(parts[0].OriginalProviderResponseJson)
        };
        Assert.Throws<InvalidDataException>(()=>
            SamAdaptiveMaskStitcher.Stitch(p,parts));
    }

    [Fact]
    public void MissingFirstSharedDetectionPreventsSeamVerification()
    {
        var p=SamAdaptiveSegmentPlan.Create(667,
            alignToTwoFrameIntraKeyframes:true);
        var result=SamAdaptiveMaskStitcher.Stitch(p,Parts(p,omitSeam:true));
        // Several shared observations remain; missing one must be preserved
        // as a missing observation, not interpolated.
        Assert.Equal(667,result.FramesWithMasks);
        Assert.True(result.Seams[0].CommonMasks>=1);
        Assert.True(result.Seams[0].IdenticalMasks>=1);
    }
}
