
using System.Text.Json;
using ProofLab.Api;
namespace ProofLab.Tests;
public sealed class SuspensionReviewTests {
    private static string Answer(string decision="RELOCATED",string camera="STATIC",double x=500)=>
        JsonSerializer.Serialize(new {decision,camera,justification=new {es="Salida de cuerda visible.",en="Visible cord exit."},
            points=Enumerable.Range(0,3).Select(frame=>new {frame,visible=true,x,y=250,confidence=.9})});
    [Fact] public void ReviewRequiresBilingualReasonAndValidNativeNormalizedCoordinates() {
        var a=SuspensionReviewPolicy.Parse(Answer());
        Assert.Equal("RELOCATED",a.Decision);Assert.Equal(3,a.Points.Length);
        Assert.Throws<FormatException>(()=>SuspensionReviewPolicy.Parse(Answer(x:1000)));
        Assert.Throws<FormatException>(()=>SuspensionReviewPolicy.Parse(Answer(camera:"APPROVED")));
        Assert.Throws<FormatException>(()=>SuspensionReviewPolicy.Parse(Answer(decision:"AUTO_PASS")));
        Assert.Throws<FormatException>(()=>SuspensionReviewPolicy.Parse(Answer().Replace("Visible cord exit.","")));
    }
    [Fact] public void InconclusiveAndMovingCameraAreRecordedWithoutCertifyingGeometry() {
        var a=SuspensionReviewPolicy.Parse(Answer("INCONCLUSIVE","MOVING"));
        Assert.Equal("INCONCLUSIVE",a.Decision);Assert.Equal("MOVING",a.Camera);
        Assert.Contains("You do not override",SuspensionReviewPolicy.Prompt);
        Assert.Contains("projected image length",SuspensionReviewPolicy.Prompt);
    }

    private sealed class FakeHttp:HttpMessageHandler,IHttpClientFactory {
        public HttpClient CreateClient(string name)=>new(this,false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            var path=request.RequestUri!.AbsolutePath;
            var text=request.Method==HttpMethod.Delete?"{}":path.EndsWith("/files")?"{\"id\":\"file-synthetic\"}":
                path.EndsWith("/input_tokens")?"{\"input_tokens\":1000}":
                "{\"id\":\"resp-test\",\"model\":\"muse-spark-1.3\",\"status\":\"incomplete\",\"usage\":{\"input_tokens\":1000,\"output_tokens\":8192},\"output\":[]}";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StringContent(text)});
        }
    }
    [Fact] public async Task PreservesIncompleteProviderResponseBeforeRejectingIt() {
        var path=Path.GetTempFileName();await File.WriteAllBytesAsync(path,new byte[500]);
        try {
            var transport=new AssemblyMetaTransport(new FakeHttp());string? captured=null;
            var video=new AssemblyVideoInput(new string('a',64),new string('b',64),500,"",50,new string('c',64));
            var frames=Enumerable.Range(0,3).Select(i=>new KnotFrame(i*10,i/3d,408,720,new string('d',64))).ToArray();
            await Assert.ThrowsAsync<InvalidDataException>(()=>transport.ReviewSuspensionAsync(path,video,frames,
                [new byte[100],new byte[100],new byte[100]],"{}","LLM_SYNTHETIC_OFFLINE_TEST",.05,
                _=>Task.CompletedTask,raw=>{captured=raw;return Task.CompletedTask;},CancellationToken.None));
            Assert.Contains("incomplete",captured);
        } finally {File.Delete(path);}
    }
    [Fact] public void ReviewIdentityBindsExactSamAnalysisAndProviderBytes() {
        var receipt=new SamVideoReceipt("a","b","v","sam-3.1","object","p",DateTimeOffset.UtcNow,
            "c","r",50,50,Enumerable.Range(0,50).Select(i=>i/30d).ToArray(),"d","complete",.01,"one");
        var state=new SamVideoState("x","sam-3.1","object","b","a",50,.01,"c",50,false,"p","one");
        var a=new SamVideoArtifact(state,receipt,"{}");
        var key=SuspensionReviewPolicy.Key(a);
        Assert.NotEqual(key,SuspensionReviewPolicy.Key(a with {Receipt=receipt with {AnalysisId="two"}}));
        Assert.NotEqual(key,SuspensionReviewPolicy.Key(a with {Receipt=receipt with {ResponseJsonSha256="other"}}));
        Assert.NotEqual(key,SuspensionReviewPolicy.Key(a with {Receipt=receipt with {OriginalVideoSha256="other"}}));
        Assert.Equal(key,SuspensionReviewPolicy.Key(a));
    }
}
