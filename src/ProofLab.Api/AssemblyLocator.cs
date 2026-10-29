using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
namespace ProofLab.Api;

public sealed class AssemblyLocator(IWebHostEnvironment environment,
    ISamVideoRuntime runtime, IKnotFrames frames, IAssemblyTransport transport) : IAssemblyScenes
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate=new(1,1);
    private string Folder(string key)=>Path.Combine(environment.ContentRootPath,".private","physics","assembly",key);
    private async Task<(SamVideoSource Video,string Key,string Root)> Source(string id,string role,CancellationToken ct,bool quote=false,bool grounding=false)
    {
        var video=quote?await runtime.GetQuoteSourceAsync(id,role,ct):await runtime.GetVerifiedSourceAsync(id,role,ct);
        if(video.Sha256.Length!=64 || video.Sha256.Any(c=>!Uri.IsHexDigit(c)))
            throw new InvalidDataException("Invalid source SHA.");
        var key=grounding?AssemblyPolicy.GroundingKey(video.Sha256):AssemblyPolicy.Key(video.Sha256);
        return(video,key,Folder(key));
    }
    private static async Task Save<T>(string path,T data,CancellationToken ct)
    {
        await using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        await JsonSerializer.SerializeAsync(output,data,Json,ct);
    }
    private static async Task<T> Read<T>(string path,CancellationToken ct)
    {
        if(new FileInfo(path).Length>300000)throw new InvalidDataException("Oversized knot record.");
        await using var input=File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(input,Json,ct) ??
            throw new InvalidDataException("Invalid knot record.");
    }
    private async Task<AssemblyEvidence?> Existing(string root,string sha,string key,CancellationToken ct,bool grounding=false)
    {
        var path=Path.Combine(root,"evidence.json");
        if(!File.Exists(path))return null;
        var record=await Read<AssemblyEvidence>(path,ct);
        var rawPath=Path.Combine(root,"response.json");
        if(new FileInfo(rawPath).Length>300000)throw new InvalidDataException("Oversized provider record.");
        var raw=await File.ReadAllTextAsync(rawPath,ct);
        if(record.CacheKey!=key || record.VideoSha256!=sha || record.Model!=AssemblyPolicy.Model ||
            record.Protocol!=(grounding?AssemblyPolicy.GroundingProtocol:AssemblyPolicy.Protocol) || record.ResponseSha256!=KnotPolicy.Hash(raw) ||
            record.Frames.Length!=3)
            throw new InvalidDataException("Knot provenance mismatch.");
        for(var i=0;i<3;i++) {
            var bytes=await File.ReadAllBytesAsync(Path.Combine(root,$"frame-{i}.jpg"),ct);
            if(bytes.Length>8_000_000 ||
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()!=record.Frames[i].Sha256)
                throw new InvalidDataException("Knot frame integrity mismatch.");
        }
        if(grounding) {
            AssemblyMetaTransport.ValidateReceipt(raw,record.ProviderResponseId,record.InputTokens,record.OutputTokens);
            if(record.Frames.Any(f=>f.Index<0 || !double.IsFinite(f.TimeSeconds) || f.TimeSeconds<0))
                throw new InvalidDataException("Invalid source frame coordinates.");
        }
        var proposal=grounding?AssemblyPolicy.ParseGrounding(AssemblyMetaTransport.OutputText(raw),record.Frames):
            AssemblyPolicy.Parse(AssemblyMetaTransport.OutputText(raw),record.Frames);
        if(JsonSerializer.Serialize(proposal,Json)!=JsonSerializer.Serialize(record.Proposal,Json))
            throw new InvalidDataException("Knot proposal mismatch.");
        return record;
    }
    public async Task<AssemblyQuote> QuoteAsync(string id,string role,CancellationToken ct)
    {
        var (video,key,root)=await Source(id,role,ct,true);
        var evidence=await Existing(root,video.Sha256,key,ct);
        var attempted=File.Exists(Path.Combine(root,"attempt.json")) && evidence is null;
        return new(key,video.Sha256,AssemblyPolicy.MaximumEstimatedCostUsd,evidence is not null,
            attempted,!attempted && runtime.ManualTokenCallsPermitted,evidence,await ApprovedForQuoteAsync(id,role,ct));
    }
    public async Task<AssemblyEvidence> ReadAsync(string id,string role,string key,CancellationToken ct)
    {
        var (video,expected,root)=await Source(id,role,ct);
        if(key!=expected)throw new ArgumentException("Wrong knot key.");
        return await Existing(root,video.Sha256,key,ct) ??
            throw new FileNotFoundException("No knot evidence.");
    }
    public async Task<(byte[] Bytes,KnotFrame Frame)> ImageAsync(string id,string role,string key,int frame,CancellationToken ct)
    {
        if(frame is <0 or >2)throw new ArgumentException("Invalid frame.");
        var evidence=await ReadAsync(id,role,key,ct);
        return(await File.ReadAllBytesAsync(Path.Combine(Folder(key),$"frame-{frame}.jpg"),ct),evidence.Frames[frame]);
    }
    public Task<AssemblyEvidence> RunAsync(string id,string role,KnotRunRequest input,CancellationToken ct)=>
        RunCore(id,role,input,ct,false);
    private async Task<AssemblyEvidence> RunCore(string id,string role,KnotRunRequest input,CancellationToken ct,bool grounding)
    {
        await gate.WaitAsync(ct);
        try {
            var (video,key,root)=await Source(id,role,ct,false,grounding);
            if(input.VideoSha256!=video.Sha256 || input.CacheKey!=key)
                throw new ArgumentException("Video or quote changed.");
            Directory.CreateDirectory(root);
            // Share the historical localization lock: changing protocol must not
            // bypass an uncertain previous paid attempt or a concurrent request.
            var historicalRoot=Folder(AssemblyPolicy.Key(video.Sha256));
            if(grounding)Directory.CreateDirectory(historicalRoot);
            await using var historicalHeld=grounding?new FileStream(Path.Combine(historicalRoot,".lock"),
                FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None):null;
            await using var held=new FileStream(Path.Combine(root,".lock"),FileMode.OpenOrCreate,
                FileAccess.ReadWrite,FileShare.None);
            var cached=await Existing(root,video.Sha256,key,ct,grounding);
            if(cached is not null)return cached;
            if(File.Exists(Path.Combine(root,"attempt.json")) || (grounding && HistoricalAttemptPending(video.Sha256)))
                throw new InvalidOperationException("Previous knot attempt requires manual reconciliation.");
            if(!input.ConfirmImageTransfer || !input.ConfirmPaidCall ||
                !double.IsFinite(input.MaximumEstimatedCostUsd) ||
                input.MaximumEstimatedCostUsd!=AssemblyPolicy.MaximumEstimatedCostUsd ||
                !SamManualApiTokenPolicy.Accepts(input.ModelApiToken))
                throw new ArgumentException("Explicit image transfer, budget and valid token required.");
            if(!runtime.CanRunWithToken(input.ModelApiToken))
                throw new InvalidOperationException("Meta session gates blocked.");
            var pts=await runtime.ReadSourceFramePtsAsync(video,ct);
            if(pts.Length<3 || pts.Any(t=>!double.IsFinite(t)||t<0))throw new InvalidDataException("Insufficient source frame timestamps.");
            var prepared=new List<KnotFrame>();
            var pixels=new List<byte[]>();
            foreach(var fraction in new[]{0.1,0.5,0.9}) {
                var index=(int)Math.Round((pts.Length-1)*fraction);
                var destination=Path.Combine(root,$"frame-{prepared.Count}.jpg");
                prepared.Add(await frames.ExtractAsync(video.Path,destination,index,pts[index],ct));
                pixels.Add(await File.ReadAllBytesAsync(destination,ct));
            }
            ct.ThrowIfCancellationRequested();
            // Admission is consumed conservatively even if token counting fails.
            // The process-wide budget is shared with video SAM; no hidden quota.
            runtime.ReserveOrThrow(input.ModelApiToken);
            await Save(Path.Combine(root,"attempt.json"),new {
                video.Sha256,key,model=AssemblyPolicy.Model,protocol=grounding?AssemblyPolicy.GroundingProtocol:AssemblyPolicy.Protocol,
                createdAtUtc=DateTimeOffset.UtcNow,
                maximumEstimatedCostUsd=AssemblyPolicy.MaximumEstimatedCostUsd,
                status="MAY_BE_BILLABLE_NO_AUTOMATIC_RETRY",frames=prepared
            },CancellationToken.None);
            using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var response=grounding?await transport.LocateGroundingAsync(pixels.ToArray(),input.ModelApiToken,deadline.Token):
                await transport.LocateAsync(pixels.ToArray(),input.ModelApiToken,deadline.Token);
            // Preserve authentic response even if semantic validation fails.
            await File.WriteAllTextAsync(Path.Combine(root,"response.json"),response.Raw,CancellationToken.None);
            if(grounding)AssemblyMetaTransport.ValidateReceipt(response.Raw,response.ProviderResponseId,response.InputTokens,response.OutputTokens);
            var proposal=grounding?AssemblyPolicy.ParseGrounding(AssemblyMetaTransport.OutputText(response.Raw),prepared.ToArray()):
                AssemblyPolicy.Parse(AssemblyMetaTransport.OutputText(response.Raw),prepared.ToArray());
            var evidence=new AssemblyEvidence(key,video.Sha256,AssemblyPolicy.Model,
                grounding?AssemblyPolicy.GroundingProtocol:AssemblyPolicy.Protocol,
                prepared.ToArray(),KnotPolicy.Hash(response.Raw),response.ProviderResponseId,
                DateTimeOffset.UtcNow,proposal,response.InputTokens,response.OutputTokens);
            await Save(Path.Combine(root,"evidence.json"),evidence,CancellationToken.None);
            return evidence;
        }
        finally {gate.Release();}
    }
    private bool HistoricalAttemptPending(string sha)
    {
        var root=Folder(AssemblyPolicy.Key(sha));
        return File.Exists(Path.Combine(root,"attempt.json")) && !File.Exists(Path.Combine(root,"evidence.json"));
    }
    public Task<AssemblyGroundingQuote> GroundingQuoteAsync(string id,string role,CancellationToken ct)=>
        GroundingQuoteCore(id,role,ct,true);
    private async Task<AssemblyGroundingQuote> GroundingQuoteCore(string id,string role,CancellationToken ct,bool quote)
    {
        var (video,key,root)=await Source(id,role,ct,quote,true);
        var approved=await ApprovedCore(id,role,ct,quote);
        var evidence=await Existing(root,video.Sha256,key,ct,true);
        var scene=approved ?? (evidence is null?null:AssemblyPolicy.Ground(evidence));
        var attempted=scene is null && evidence is null && (File.Exists(Path.Combine(root,"attempt.json")) || HistoricalAttemptPending(video.Sha256));
        var status=scene is not null?"READY":evidence is not null?AssemblyPolicy.GroundingStatus(evidence):
            attempted?"PRIOR_ATTEMPT":"NEEDS_LOCALIZATION";
        return new(key,video.Sha256,AssemblyPolicy.MaximumEstimatedCostUsd,
            scene is not null || evidence is not null,attempted,
            scene is not null || (status=="NEEDS_LOCALIZATION" && runtime.ManualTokenCallsPermitted),
            status,scene,evidence);
    }
    public async Task<AssemblyGroundingQuote> GroundAsync(string id,string role,KnotRunRequest input,CancellationToken ct)
    {
        var initial=await GroundingQuoteCore(id,role,ct,false);
        if(input.VideoSha256!=initial.VideoSha256 || input.CacheKey!=initial.CacheKey)
            throw new ArgumentException("Video or grounding quote changed.");
        if(initial.Scene is not null)return initial;
        await RunCore(id,role,input,ct,true);
        return await GroundingQuoteCore(id,role,ct,false);
    }
    public Task<SamSceneDefinition?> TrackingAsync(string id,string role,CancellationToken ct)=>TrackingCore(id,role,ct,false);
    public Task<SamSceneDefinition?> TrackingForQuoteAsync(string id,string role,CancellationToken ct)=>TrackingCore(id,role,ct,true);
    private async Task<SamSceneDefinition?> TrackingCore(string id,string role,CancellationToken ct,bool quote)
    {
        var approved=await ApprovedCore(id,role,ct,quote);
        if(approved is not null)return approved;
        var (video,key,root)=await Source(id,role,ct,quote,true);
        var evidence=await Existing(root,video.Sha256,key,ct,true);
        return evidence is null?null:AssemblyPolicy.Ground(evidence);
    }
    public Task<SamSceneDefinition?> ApprovedAsync(string id,string role,CancellationToken ct)=>ApprovedCore(id,role,ct,false);
    public Task<SamSceneDefinition?> ApprovedForQuoteAsync(string id,string role,CancellationToken ct)=>ApprovedCore(id,role,ct,true);
    private async Task<SamSceneDefinition?> ApprovedCore(string id,string role,CancellationToken ct,bool quote)
    {
        var (video,key,root)=await Source(id,role,ct,quote);
        var path=Path.Combine(root,"approved.json");
        if(!File.Exists(path))return null;
        var definition=await Read<SamSceneDefinition>(path,ct);
        AssemblyPolicy.Validate(definition,video.Sha256);
        var evidence=await Existing(root,video.Sha256,key,ct) ??
            throw new InvalidDataException("Approved scene has no provider evidence.");
        var rebuilt=AssemblyPolicy.Approve(evidence,definition.Roles.Select(r=>r.Role).ToArray());
        if(definition.Key!=rebuilt.Key)throw new InvalidDataException("Approved scene differs from provider evidence.");
        return definition;
    }
    public async Task<SamSceneDefinition> ConfirmAsync(string id,string role,AssemblyConfirmRequest input,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try {
            var (video,key,root)=await Source(id,role,ct);
            if(input.VideoSha256!=video.Sha256 || input.CacheKey!=key || !input.ConfirmReviewed)
                throw new ArgumentException("Scene review required.");
            await using var held=new FileStream(Path.Combine(root,".lock"),FileMode.OpenOrCreate,
                FileAccess.ReadWrite,FileShare.None);
            var evidence=await Existing(root,video.Sha256,key,ct) ??
                throw new FileNotFoundException("No assembly evidence.");
            var definition=AssemblyPolicy.Approve(evidence,input.Roles);
            AssemblyPolicy.Validate(definition,video.Sha256);
            var prior=await ApprovedAsync(id,role,ct);
            // One immutable reviewed definition per evidence: review cannot change
            // the cache identity while a SAM request is running.
            if(prior is not null) {
                if(prior.Key!=definition.Key)throw new InvalidOperationException("This assembly review is already sealed.");
                return prior;
            }
            await Save(Path.Combine(root,"approved.json"),definition,ct);
            return definition;
        }
        finally {gate.Release();}
    }

}
