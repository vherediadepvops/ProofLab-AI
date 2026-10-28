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
    private async Task<(SamVideoSource Video,string Key,string Root)> Source(string id,string role,CancellationToken ct)
    {
        var video=await runtime.GetVerifiedSourceAsync(id,role,ct);
        if(video.Sha256.Length!=64 || video.Sha256.Any(c=>!Uri.IsHexDigit(c)))
            throw new InvalidDataException("Invalid source SHA.");
        var key=AssemblyPolicy.Key(video.Sha256);
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
    private async Task<AssemblyEvidence?> Existing(string root,string sha,string key,CancellationToken ct)
    {
        var path=Path.Combine(root,"evidence.json");
        if(!File.Exists(path))return null;
        var record=await Read<AssemblyEvidence>(path,ct);
        var raw=await File.ReadAllTextAsync(Path.Combine(root,"response.json"),ct);
        if(record.CacheKey!=key || record.VideoSha256!=sha || record.Model!=AssemblyPolicy.Model ||
            record.Protocol!=AssemblyPolicy.Protocol || record.ResponseSha256!=KnotPolicy.Hash(raw) ||
            record.Frames.Length!=3)
            throw new InvalidDataException("Knot provenance mismatch.");
        for(var i=0;i<3;i++) {
            var bytes=await File.ReadAllBytesAsync(Path.Combine(root,$"frame-{i}.jpg"),ct);
            if(bytes.Length>8_000_000 ||
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()!=record.Frames[i].Sha256)
                throw new InvalidDataException("Knot frame integrity mismatch.");
        }
        var proposal=AssemblyPolicy.Parse(AssemblyMetaTransport.OutputText(raw),record.Frames);
        if(JsonSerializer.Serialize(proposal,Json)!=JsonSerializer.Serialize(record.Proposal,Json))
            throw new InvalidDataException("Knot proposal mismatch.");
        return record;
    }
    public async Task<AssemblyQuote> QuoteAsync(string id,string role,CancellationToken ct)
    {
        var (video,key,root)=await Source(id,role,ct);
        var evidence=await Existing(root,video.Sha256,key,ct);
        var attempted=File.Exists(Path.Combine(root,"attempt.json")) && evidence is null;
        return new(key,video.Sha256,AssemblyPolicy.MaximumEstimatedCostUsd,evidence is not null,
            attempted,!attempted && runtime.ManualTokenCallsPermitted,evidence,await ApprovedAsync(id,role,ct));
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
    public async Task<AssemblyEvidence> RunAsync(string id,string role,KnotRunRequest input,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try {
            var (video,key,root)=await Source(id,role,ct);
            if(input.VideoSha256!=video.Sha256 || input.CacheKey!=key)
                throw new ArgumentException("Video or quote changed.");
            Directory.CreateDirectory(root);
            await using var held=new FileStream(Path.Combine(root,".lock"),FileMode.OpenOrCreate,
                FileAccess.ReadWrite,FileShare.None);
            var cached=await Existing(root,video.Sha256,key,ct);
            if(cached is not null)return cached;
            if(File.Exists(Path.Combine(root,"attempt.json")))
                throw new InvalidOperationException("Previous knot attempt requires manual reconciliation.");
            if(!input.ConfirmImageTransfer || !input.ConfirmPaidCall ||
                !double.IsFinite(input.MaximumEstimatedCostUsd) ||
                input.MaximumEstimatedCostUsd!=AssemblyPolicy.MaximumEstimatedCostUsd ||
                !SamManualApiTokenPolicy.Accepts(input.ModelApiToken))
                throw new ArgumentException("Explicit image transfer, budget and valid token required.");
            if(!runtime.CanRunWithToken(input.ModelApiToken))
                throw new InvalidOperationException("Meta session gates blocked.");
            var pts=await runtime.ReadFramePtsAsync(video.Path,ct);
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
                video.Sha256,key,model=AssemblyPolicy.Model,protocol=AssemblyPolicy.Protocol,
                createdAtUtc=DateTimeOffset.UtcNow,
                maximumEstimatedCostUsd=AssemblyPolicy.MaximumEstimatedCostUsd,
                status="MAY_BE_BILLABLE_NO_AUTOMATIC_RETRY",frames=prepared
            },CancellationToken.None);
            using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var response=await transport.LocateAsync(pixels.ToArray(),input.ModelApiToken,deadline.Token);
            // Preserve authentic response even if semantic validation fails.
            await File.WriteAllTextAsync(Path.Combine(root,"response.json"),response.Raw,CancellationToken.None);
            var proposal=AssemblyPolicy.Parse(AssemblyMetaTransport.OutputText(response.Raw),prepared.ToArray());
            var evidence=new AssemblyEvidence(key,video.Sha256,AssemblyPolicy.Model,AssemblyPolicy.Protocol,
                prepared.ToArray(),KnotPolicy.Hash(response.Raw),response.ProviderResponseId,
                DateTimeOffset.UtcNow,proposal,response.InputTokens,response.OutputTokens);
            await Save(Path.Combine(root,"evidence.json"),evidence,CancellationToken.None);
            return evidence;
        }
        finally {gate.Release();}
    }
    public async Task<SamSceneDefinition?> ApprovedAsync(string id,string role,CancellationToken ct)
    {
        var (video,key,root)=await Source(id,role,ct);
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
