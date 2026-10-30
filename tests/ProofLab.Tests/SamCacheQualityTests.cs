using System.Text.Json;
using ProofLab.Api;
namespace ProofLab.Tests;
public class SamCacheQualityTests
{
    private sealed class Worker:ISamPeriodWorker {
        public int Calls;
        public Task<string> MeasureAsync(object input,CancellationToken ct){Calls++;return Task.FromResult(JsonSerializer.Serialize(new SamCacheQuality(false,"PIVOT_MISSING",null,0,120,150),new JsonSerializerOptions(JsonSerializerDefaults.Web)));}
    }
    private static SamVideoArtifact Archive() {
        var sha=new string('a',64);var raw="{}";var hash=SamVideoArchive.Hash(raw);
        var state=new SamVideoState("cached","sam-3.1","bob",sha,new string('b',64),150,.03,hash,null,false,"test",new string('c',32));
        var receipt=new SamVideoReceipt(state.CacheKey,sha,"video","sam-3.1","bob","test",DateTimeOffset.UtcNow,hash,"response",null,150,Enumerable.Range(0,150).Select(i=>i/30d).ToArray(),new string('d',64),"test",.03,state.AnalysisId);
        return new(state,receipt,raw);
    }
    [Fact] public async Task InsufficientEvidenceIsExcludedWithoutDeletingReceipts_AndDecisionIsCached() {
        var root=Path.Combine(Path.GetTempPath(),"prooflab-quality-"+Guid.NewGuid().ToString("N"));
        try {
            var index=new SamVideoEvidenceIndex(root);var worker=new Worker();
            var service=new SamReusableEvidence(null!,null!,null!,index,worker);var a=Archive();
            var source=new SamVideoSource(a.State.SourceSha256,"video","unused","h264",1080,1920,5);
            var first=await service.AssessAsync(a,source,"study","baseline",default);
            Assert.False(first.Usable);Assert.Equal("PIVOT_MISSING",first.Reason);
            Assert.Equal(first,await service.AssessAsync(a,source,"study","baseline",default));Assert.Equal(1,worker.Calls);
            // Changing bound geometry/receipt cannot inherit an earlier quality decision.
            var changed=a with {Receipt=a.Receipt with {FramePtsSeconds=new[]{0d,.1d}}};
            await service.AssessAsync(changed,source,"study","baseline",default);Assert.Equal(2,worker.Calls);
            Assert.Equal("{}",a.RawResponseJson);
            await Assert.ThrowsAsync<InvalidDataException>(()=>service.AssessAsync(a with {RawResponseJson="tampered"},source,"study","baseline",default));
            Assert.Equal(2,worker.Calls);
        }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact] public async Task QualityVerdictsSurviveRestart_AndRemainSeparateForEachAnalysis() {
        var root=Path.Combine(Path.GetTempPath(),"prooflab-quality-"+Guid.NewGuid().ToString("N"));
        try {
            var a=Archive();var key=SamReusableEvidence.Fingerprint(a);var good=new SamCacheQuality(true,"MEASUREMENTS_AVAILABLE",1.42,8,150,150);
            await new SamVideoEvidenceIndex(root).WriteQualityAsync(key,a.State.SourceSha256,a.State.AnalysisId!,SamReusableEvidence.Policy,good,default);
            var reopened=new SamVideoEvidenceIndex(root);Assert.Equal(good,await reopened.ReadQualityAsync(key,default));
            Assert.Null(await reopened.ReadQualityAsync(SamReusableEvidence.Fingerprint(a with {Receipt=a.Receipt with {AnalysisId=new string('e',32)}}),default));
        }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
