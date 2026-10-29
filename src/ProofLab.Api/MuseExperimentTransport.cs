using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
namespace ProofLab.Api;

public sealed class MuseExperimentTransport(IHttpClientFactory clients):IMuseExperimentTransport
{
    public async Task<KnotProviderResult> EvaluateAsync(string inputJson,string token,CancellationToken ct)
    {
        if(!SamManualApiTokenPolicy.Accepts(token)||inputJson.Length>16000)
            throw new ArgumentException("Invalid investigator input.");
        var bilingual=new {type="object",additionalProperties=false,required=new[]{"es","en"},
            properties=new {es=new {type="string"},en=new {type="string"}}};
        var nullableNumber=new {type=new[]{"number","null"}};
        var format=new {type="json_schema",name="pendulum_research_director",strict=true,
            schema=new {type="object",additionalProperties=false,
                required=new[]{"decision","justification","limitations","protocol","hypothesis","falsification",
                    "nextLengthMeters","periodSeconds","tolerancePercent"},
                properties=new {
                    decision=new {type="string",@enum=new[]{"PROPOSE_EXPERIMENT","REPEAT_RECORDING","INCONCLUSIVE"}},
                    justification=bilingual,limitations=bilingual,protocol=bilingual,hypothesis=bilingual,
                    falsification=bilingual,nextLengthMeters=nullableNumber,periodSeconds=nullableNumber,
                    tolerancePercent=nullableNumber}}};
        var payload=new {model=MuseExperimentPolicy.Model,stream=false,
            max_output_tokens=MuseExperimentPolicy.MaximumOutputTokens,reasoning=new {effort="low"},
            text=new {format},input=new[]{
                new {role="system",content=MuseExperimentPolicy.Prompt},
                new {role="user",content=inputJson}}};
        var http=clients.CreateClient("meta");
        async Task<string> Post(string endpoint) {
            using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.meta.ai/v1/"+endpoint);
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
            request.Content=JsonContent.Create(payload);
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            if(!response.IsSuccessStatusCode)
                throw new HttpRequestException("Meta investigator request failed.",null,response.StatusCode);
            await using var input=await response.Content.ReadAsStreamAsync(ct);
            using var output=new MemoryStream();var bytes=new byte[8192];int n;
            while((n=await input.ReadAsync(bytes,ct))>0){
                if(output.Length+n>262144)throw new FormatException("Provider response limit.");
                output.Write(bytes,0,n);
            }
            return System.Text.Encoding.UTF8.GetString(output.ToArray());
        }
        // One token-count API request followed by at most ONE inference request.
        // Both count toward the user's five-API-call allowance. No automatic retries.
        using(var counted=JsonDocument.Parse(await Post("responses/input_tokens"))) {
            var count=counted.RootElement.GetProperty("input_tokens").GetInt32();
            if(count<=0||count>MuseExperimentPolicy.MaximumInputTokens)
                throw new InvalidOperationException("Investigator input exceeds the approved cost ceiling.");
        }
        var raw=await Post("responses");
        // The workflow archives raw output BEFORE checking semantics/status.
        return new(raw,"",0,0);
    }

    public static KnotProviderResult Validate(string raw)
    {
        using var doc=JsonDocument.Parse(raw);var r=doc.RootElement;
        if(r.GetProperty("status").GetString()!="completed" ||
           r.GetProperty("model").GetString()!=MuseExperimentPolicy.Model)
            throw new FormatException("Incomplete or wrong-model response.");
        var id=r.GetProperty("id").GetString()??"";
        var input=r.GetProperty("usage").GetProperty("input_tokens").GetInt32();
        var output=r.GetProperty("usage").GetProperty("output_tokens").GetInt32();
        if(id.Length is <1 or >200 || input<0 || output<0)
            throw new FormatException("Invalid provider receipt.");
        return new(raw,id,input,output);
    }
}
