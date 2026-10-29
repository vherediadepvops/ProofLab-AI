using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
namespace ProofLab.Api;

/// <summary>One text-model inference, no tools, retries, or automatic follow-up.
/// Token preflight is a conservative admission check, NOT an invoice estimate.</summary>
public sealed class AssemblyMetaTransport(IHttpClientFactory clients) : IAssemblyTransport
{
    public Task<KnotProviderResult> LocateAsync(byte[][] images,string token,CancellationToken ct)=>LocateCore(images,token,ct,false);
    public Task<KnotProviderResult> LocateGroundingAsync(byte[][] images,string token,CancellationToken ct)=>LocateCore(images,token,ct,true);
    private async Task<KnotProviderResult> LocateCore(byte[][] images,string token,CancellationToken ct,bool grounding)
    {
        if(images.Length!=3 || images.Any(i=>i.Length is <100 or >8_000_000) ||
            !SamManualApiTokenPolicy.Accepts(token)) throw new ArgumentException("Invalid knot input.");
        var content=new List<object>{new {type="input_text",text=grounding?AssemblyPolicy.GroundingPrompt:AssemblyPolicy.Prompt}};
        content.AddRange(images.Select(i=>(object)new {type="input_image",
            image_url="data:image/jpeg;base64,"+Convert.ToBase64String(i)}));
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
            ["observations"]=new {type="array",items=observation},
            ["points"]=new {type="array",items=point}
        };
        if(grounding)properties["target"]=new {type="object",additionalProperties=false,
            required=new[]{"status","justification"},properties=new {
                status=new {type="string",@enum=new[]{"CLEAR_SINGLE_OBJECT","AMBIGUOUS","NOT_VISIBLE"}},
                justification=new {type="object",additionalProperties=false,required=new[]{"es","en"},
                    properties=new {es=new {type="string"},en=new {type="string"}}}
            }};
        var format=new {type="json_schema",name=grounding?"hanging_object_grounding":"hanging_assembly",strict=true,
            schema=new {type="object",additionalProperties=false,
                required=properties.Keys.ToArray(),properties}};
        var payload=new {model=AssemblyPolicy.Model,input,stream=false,
            max_output_tokens=AssemblyPolicy.MaximumOutputTokens,
            reasoning=new {effort="low"},text=new {format}};
        var http=clients.CreateClient("meta");
        async Task<string> Post(string endpoint,object body)
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.meta.ai/v1/"+endpoint);
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
            request.Content=JsonContent.Create(body);
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            if(!response.IsSuccessStatusCode)
                throw new HttpRequestException("Meta knot request failed.",null,response.StatusCode);
            await using var stream=await response.Content.ReadAsStreamAsync(ct);
            using var output=new MemoryStream();
            var buffer=new byte[8192];
            int read;
            while((read=await stream.ReadAsync(buffer,ct))>0) {
                if(output.Length+read>262144)throw new FormatException("Provider output too large.");
                output.Write(buffer,0,read);
            }
            return System.Text.Encoding.UTF8.GetString(output.ToArray());
        }
        // No hosted tools/history. Count includes scaffolding; it is deliberately
        // used as a conservative admission ceiling, not presented as billing usage.
        var counted=await Post("responses/input_tokens",payload);
        using(var count=JsonDocument.Parse(counted)) {
            var tokens=count.RootElement.GetProperty("input_tokens").GetInt32();
            if(tokens<=0 || tokens>AssemblyPolicy.MaximumInputTokens)
                throw new InvalidOperationException("Knot context exceeds approved admission budget. No inference sent.");
        }
        var raw=await Post("responses",payload);
        using var doc=JsonDocument.Parse(raw);
        var root=doc.RootElement;
        if(root.GetProperty("status").GetString()!="completed" ||
            root.GetProperty("model").GetString()!=AssemblyPolicy.Model)
            throw new FormatException("Incomplete or wrong model response.");
        var id=root.GetProperty("id").GetString() ?? "";
        if(id.Length is <1 or >200)throw new FormatException("Missing provider ID.");
        var usage=root.GetProperty("usage");
        var usedInput=usage.GetProperty("input_tokens").GetInt32();
        var usedOutput=usage.GetProperty("output_tokens").GetInt32();
        if(usedInput<0 || usedOutput<0)throw new FormatException("Invalid usage.");
        return new(raw,id,usedInput,usedOutput);
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
