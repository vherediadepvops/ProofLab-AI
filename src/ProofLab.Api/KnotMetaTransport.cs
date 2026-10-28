using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
namespace ProofLab.Api;

/// <summary>One text-model inference, no tools, retries, or automatic follow-up.
/// Token preflight is a conservative admission check, NOT an invoice estimate.</summary>
public sealed class KnotMetaTransport(IHttpClientFactory clients) : IKnotTransport
{
    public async Task<KnotProviderResult> LocateAsync(byte[][] images,string token,CancellationToken ct)
    {
        if(images.Length!=3 || images.Any(i=>i.Length is <100 or >8_000_000) ||
            !SamManualApiTokenPolicy.Accepts(token)) throw new ArgumentException("Invalid knot input.");
        var content=new List<object>{new {type="input_text",text=KnotPolicy.Prompt}};
        content.AddRange(images.Select(i=>(object)new {type="input_image",
            image_url="data:image/jpeg;base64,"+Convert.ToBase64String(i)}));
        var input=new[]{new {type="message",role="user",content}};
        var fields=new {
            frame=new {type="integer"}, visible=new {type="boolean"},
            x=new {type="number"}, y=new {type="number"}, confidence=new {type="number"}
        };
        var format=new {type="json_schema",name="pendulum_rope_exit",strict=true,
            schema=new {type="object",additionalProperties=false,required=new[]{"points"},
                properties=new {points=new {type="array",items=new {
                    type="object",additionalProperties=false,
                    required=new[]{"frame","visible","x","y","confidence"},properties=fields
                }}}}};
        var payload=new {model=KnotPolicy.Model,input,stream=false,
            max_output_tokens=KnotPolicy.MaximumOutputTokens,
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
            if(tokens<=0 || tokens>KnotPolicy.MaximumInputTokens)
                throw new InvalidOperationException("Knot context exceeds approved admission budget. No inference sent.");
        }
        var raw=await Post("responses",payload);
        using var doc=JsonDocument.Parse(raw);
        var root=doc.RootElement;
        if(root.GetProperty("status").GetString()!="completed" ||
            root.GetProperty("model").GetString()!=KnotPolicy.Model)
            throw new FormatException("Incomplete or wrong model response.");
        var id=root.GetProperty("id").GetString() ?? "";
        if(id.Length is <1 or >200)throw new FormatException("Missing provider ID.");
        var usage=root.GetProperty("usage");
        var usedInput=usage.GetProperty("input_tokens").GetInt32();
        var usedOutput=usage.GetProperty("output_tokens").GetInt32();
        if(usedInput<0 || usedOutput<0)throw new FormatException("Invalid usage.");
        return new(raw,id,usedInput,usedOutput);
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
