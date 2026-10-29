using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
namespace ProofLab.Api;

/// <summary>No retries, tools or follow-up inferences. Full video uses a bounded
/// token admission check on the actual uploaded video plus anchor-image payload.</summary>
public sealed class AssemblyMetaTransport(IHttpClientFactory clients) : IAssemblyTransport
{
    private static void CheckImages(byte[][] images,string token) {
        if(images.Length!=3 || images.Any(i=>i.Length is <100 or >8_000_000) ||
            !SamManualApiTokenPolicy.Accepts(token))throw new ArgumentException("Invalid grounding input.");
    }
    private static object Payload(List<object> content,bool grounding) {
        var input=new[]{new {type="message",role="user",content}};
        var point=new {type="object",additionalProperties=false,
            required=new[]{"frame","visible","x","y","confidence"},properties=new {
                frame=new {type="integer"},visible=new {type="boolean"},
                x=new {type="number"},y=new {type="number"},confidence=new {type="number"}}};
        var observation=new {type="object",additionalProperties=false,
            required=new[]{"frame","role","visible","x1","y1","x2","y2","confidence"},properties=new {
                frame=new {type="integer"},role=new {type="string",@enum=AssemblyPolicy.RoleNames},
                visible=new {type="boolean"},x1=new {type="number"},y1=new {type="number"},
                x2=new {type="number"},y2=new {type="number"},confidence=new {type="number"}}};
        var properties=new Dictionary<string,object> {
            ["arrangement"]=new {type="string",@enum=new[]{"simple_pendulum","pulley_system","unclear"}},
            ["observations"]=new {type="array",items=observation},["points"]=new {type="array",items=point}
        };
        if(grounding)properties["target"]=new {type="object",additionalProperties=false,
            required=new[]{"status","justification"},properties=new {
                status=new {type="string",@enum=new[]{"CLEAR_SINGLE_OBJECT","AMBIGUOUS","NOT_VISIBLE"}},
                justification=new {type="object",additionalProperties=false,required=new[]{"es","en"},
                    properties=new {es=new {type="string"},en=new {type="string"}}}
            }};
        var format=new {type="json_schema",name=grounding?"hanging_object_grounding":"hanging_assembly",strict=true,
            schema=new {type="object",additionalProperties=false,required=properties.Keys.ToArray(),properties}};
        return new {model=AssemblyPolicy.Model,input,stream=false,max_output_tokens=AssemblyPolicy.MaximumOutputTokens,
            reasoning=new {effort=grounding?"medium":"low"},text=new {format}};
    }
    private static async Task<string> ReadResponse(HttpResponseMessage response,CancellationToken ct) {
        if(!response.IsSuccessStatusCode)throw new HttpRequestException("Meta grounding request failed.",null,response.StatusCode);
        await using var stream=await response.Content.ReadAsStreamAsync(ct);
        using var output=new MemoryStream();var buffer=new byte[8192];int read;
        while((read=await stream.ReadAsync(buffer,ct))>0) {
            if(output.Length+read>262144)throw new FormatException("Provider output too large.");
            output.Write(buffer,0,read);
        }
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }
    private static async Task<string> Post(HttpClient http,string endpoint,object payload,string token,CancellationToken ct) {
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.meta.ai/v1/"+endpoint);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);request.Content=JsonContent.Create(payload);
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        return await ReadResponse(response,ct);
    }
    private static KnotProviderResult Completed(string raw) {
        using var doc=JsonDocument.Parse(raw);var root=doc.RootElement;
        var id=root.GetProperty("id").GetString() ?? "";var usage=root.GetProperty("usage");
        var result=new KnotProviderResult(raw,id,usage.GetProperty("input_tokens").GetInt32(),usage.GetProperty("output_tokens").GetInt32());
        ValidateReceipt(raw,id,result.InputTokens,result.OutputTokens);
        return result;
    }
    public Task<KnotProviderResult> LocateAsync(byte[][] images,string token,CancellationToken ct)=>LocateCore(images,token,ct,false);
    public Task<KnotProviderResult> LocateGroundingAsync(byte[][] images,string token,CancellationToken ct)=>LocateCore(images,token,ct,true);
    private async Task<KnotProviderResult> LocateCore(byte[][] images,string token,CancellationToken ct,bool grounding) {
        CheckImages(images,token);
        var content=new List<object>{new {type="input_text",text=grounding?AssemblyPolicy.GroundingPrompt:AssemblyPolicy.Prompt}};
        content.AddRange(images.Select(i=>(object)new {type="input_image",image_url="data:image/jpeg;base64,"+Convert.ToBase64String(i)}));
        var payload=Payload(content,grounding);var http=clients.CreateClient("meta");
        using(var count=JsonDocument.Parse(await Post(http,"responses/input_tokens",payload,token,ct))) {
            var tokens=count.RootElement.GetProperty("input_tokens").GetInt32();
            if(tokens<=0 || tokens>AssemblyPolicy.MaximumInputTokens)
                throw new InvalidOperationException("Grounding context exceeds approved admission budget. No inference sent.");
        }
        return Completed(await Post(http,"responses",payload,token,ct));
    }
    public async Task<AssemblyVideoProviderResult> LocateVideoGroundingAsync(string mp4Path,AssemblyVideoInput videoInput,
        KnotFrame[] anchors,byte[][] images,string token,double maximumCostUsd,Func<AssemblyVideoInput,Task> uploaded,CancellationToken ct)
    {
        bool inferenceSubmitted=false;string? fileId=null;
        var http=clients.CreateClient("meta");
        try {
            CheckImages(images,token);
            if(!double.IsFinite(maximumCostUsd) || maximumCostUsd is <.025 or >1 ||
                anchors.Length!=3 || anchors.Any(f=>f.Index<0 || f.Index>=videoInput.SourceFrameCount ||
                    !double.IsFinite(f.TimeSeconds) || f.TimeSeconds<0 || f.Width<2 || f.Height<2) ||
                videoInput.Bytes is <1 or >1_073_741_824 || new FileInfo(mp4Path).Length!=videoInput.Bytes ||
                videoInput.Scope!="FULL_VIDEO_REMUX_WITH_3_NATIVE_ANCHORS")
                throw new ArgumentException("Invalid full-video grounding input.");
            await using(var input=File.OpenRead(mp4Path)) {
                using var multipart=new MultipartFormDataContent();
                multipart.Add(new StringContent("user_data"),"purpose");
                using var video=new StreamContent(input);
                video.Headers.ContentType=new MediaTypeHeaderValue("video/mp4");
                multipart.Add(video,"file","experiment.mp4");
                using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.meta.ai/v1/files");
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);request.Content=multipart;
                using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
                using var doc=JsonDocument.Parse(await ReadResponse(response,ct));
                fileId=doc.RootElement.GetProperty("id").GetString();
                if(string.IsNullOrWhiteSpace(fileId) || fileId.Length>200 ||
                    !fileId.StartsWith("file-",StringComparison.Ordinal) ||
                    fileId.Any(c=>!char.IsAsciiLetterOrDigit(c) && c!='-' && c!='_'))
                    throw new FormatException("Invalid uploaded file ID.");
            }
            var submitted=videoInput with {ProviderFileId=fileId};
            await uploaded(submitted);
            var content=new List<object> {
                new {type="input_text",text=AssemblyPolicy.VideoGroundingPrompt},
                new {type="input_file",file_id=fileId}
            };
            for(var i=0;i<3;i++) {
                var anchor=anchors[i];
                content.Add(new {type="input_text",text="REFERENCE IMAGE ordinal="+i.ToString(CultureInfo.InvariantCulture)+
                    "; source_frame_index="+anchor.Index.ToString(CultureInfo.InvariantCulture)+
                    "; source_timestamp_seconds="+anchor.TimeSeconds.ToString("R",CultureInfo.InvariantCulture)+
                    "; oriented_width="+anchor.Width.ToString(CultureInfo.InvariantCulture)+
                    "; oriented_height="+anchor.Height.ToString(CultureInfo.InvariantCulture)+
                    ". Return this image's boxes as frame="+i.ToString(CultureInfo.InvariantCulture)+"."});
                content.Add(new {type="input_image",image_url="data:image/jpeg;base64,"+Convert.ToBase64String(images[i])});
            }
            var payload=Payload(content,true);
            using(var count=JsonDocument.Parse(await Post(http,"responses/input_tokens",payload,token,ct))) {
                var tokens=count.RootElement.GetProperty("input_tokens").GetInt32();
                if(tokens<=0 || tokens>AssemblyPolicy.VideoInputLimit(maximumCostUsd) ||
                    AssemblyPolicy.MaximumTokenCost(tokens)>maximumCostUsd+1e-9)
                    throw new InvalidOperationException("Full-video tokens exceed the approved maximum. No inference sent.");
            }
            // Only this transition makes a subsequent failure potentially billable.
            inferenceSubmitted=true;
            var responseResult=Completed(await Post(http,"responses",payload,token,ct));
            return new(responseResult,submitted);
        }
        catch(Exception ex) when(!inferenceSubmitted && ex is not AssemblyNoInferenceSentException) {
            throw new AssemblyNoInferenceSentException(ex);
        }
        finally {
            // This request owns the temporary upload; preserve its ID locally, then
            // remove the provider copy. Cleanup never retries or replaces a result.
            if(fileId is not null) {
                try {
                    using var cleanupDeadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var cleanup=new HttpRequestMessage(HttpMethod.Delete,"https://api.meta.ai/v1/files/"+Uri.EscapeDataString(fileId));
                    cleanup.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
                    using var removed=await http.SendAsync(cleanup,HttpCompletionOption.ResponseHeadersRead,cleanupDeadline.Token);
                } catch { /* Provider file ID remains in the immutable upload receipt. */ }
            }
        }
    }

    public static void ValidateReceipt(string raw,string providerId,int inputTokens,int outputTokens)
    {
        using var doc=JsonDocument.Parse(raw);
        var root=doc.RootElement;
        var usage=root.GetProperty("usage");
        if(root.GetProperty("status").GetString()!="completed" ||
            root.GetProperty("model").GetString()!=AssemblyPolicy.Model ||
            string.IsNullOrWhiteSpace(providerId) || providerId.Length>200 ||
            root.GetProperty("id").GetString()!=providerId || inputTokens<0 || outputTokens<0 ||
            usage.GetProperty("input_tokens").GetInt32()!=inputTokens ||
            usage.GetProperty("output_tokens").GetInt32()!=outputTokens)
            throw new InvalidDataException("Automatic grounding provider receipt mismatch.");
    }
    public static string OutputText(string raw)
    {
        using var doc=JsonDocument.Parse(raw);
        var texts=doc.RootElement.GetProperty("output").EnumerateArray()
            .Where(o=>o.TryGetProperty("type",out var t)&&t.GetString()=="message")
            .SelectMany(o=>o.GetProperty("content").EnumerateArray())
            .Where(c=>c.GetProperty("type").GetString()=="output_text")
            .Select(c=>c.GetProperty("text").GetString()).ToArray();
        if(texts.Length!=1 || string.IsNullOrWhiteSpace(texts[0]))
            throw new FormatException("Expected one structured knot result.");
        return texts[0]!;
    }
}