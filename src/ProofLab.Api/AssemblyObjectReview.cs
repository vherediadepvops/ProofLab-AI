using System.Security.Cryptography;
using System.Text.Json;
namespace ProofLab.Api;

public sealed partial class AssemblyLocator
{
    private async Task<AssemblyEvidence?> ReadObjectReview(string root,AssemblyEvidence draft,CancellationToken ct)
    {
        var evidencePath=Path.Combine(root,"reviewed-evidence.json");
        if(!File.Exists(evidencePath))return null;
        var attempt=await Read<ObjectReviewAttempt>(Path.Combine(root,"object-review-attempt.json"),ct);
        if(attempt.Protocol!=ObjectReviewPolicy.Protocol || attempt.SourceSha256!=draft.VideoSha256 ||
            attempt.DraftResponseSha256!=draft.ResponseSha256 || attempt.EvidenceKey!=draft.CacheKey ||
            attempt.References.Length is <2 or >16 || attempt.References.Select(x=>x.Ordinal).Distinct().Count()!=attempt.References.Length)
            throw new InvalidDataException("Visual review provenance changed.");
        foreach(var r in attempt.References) {
            if(r.Ordinal<0 || r.Ordinal>=draft.Frames.Length || r.Frame!=draft.Frames[r.Ordinal] ||
                r.Candidate!=draft.Proposal.Observations.Single(o=>o.Frame==r.Ordinal && o.Role=="object") ||
                await FileHash(Path.Combine(root,$"review-{r.Ordinal}.jpg"),ct)!=r.MarkedSha256)
                throw new InvalidDataException("Visual review image or candidate changed.");
        }
        var rawPath=Path.Combine(root,"object-review-response.json");
        if(new FileInfo(rawPath).Length>300000)throw new InvalidDataException("Review response too large.");
        var raw=await File.ReadAllTextAsync(rawPath,ct);using var json=JsonDocument.Parse(raw);
        var result=new KnotProviderResult(raw,json.RootElement.GetProperty("id").GetString()!,
            json.RootElement.GetProperty("usage").GetProperty("input_tokens").GetInt32(),
            json.RootElement.GetProperty("usage").GetProperty("output_tokens").GetInt32());
        var derived=ObjectReviewPolicy.Derive(draft,result,attempt.References);
        var saved=await Read<AssemblyEvidence>(evidencePath,ct);
        if(JsonSerializer.Serialize(derived,Json)!=JsonSerializer.Serialize(saved,Json))throw new InvalidDataException("Reviewed evidence differs from preserved responses.");
        return derived;
    }

    private async Task<AssemblyEvidence> ReviewObject(string root,AssemblyEvidence draft,SamVideoSource video,double[] pts,string token,CancellationToken ct)
    {
        AssemblyProgress.Start("target-review");
        var existing=await ReadObjectReview(root,draft,ct);
        if(existing is not null){AssemblyProgress.Done("target-review");return existing;}
        if(File.Exists(Path.Combine(root,"object-review-attempt.json")))throw new InvalidOperationException("Previous visual review requires reconciliation.");
        var plan=adaptiveClips is null?null:await adaptiveClips.PlanAsync(video,pts,null,53,8,ct);
        var selected=ObjectReviewPolicy.Select(draft.Frames,plan);
        var images=new List<byte[]>();var references=new List<ObjectReviewImage>();
        foreach(var ordinal in selected) {
            var frame=draft.Frames[ordinal];var candidate=draft.Proposal.Observations.Single(o=>o.Frame==ordinal && o.Role=="object");
            var dest=Path.Combine(root,$"review-{ordinal}.jpg");
            // An interrupted LOCAL preparation can be resumed without a paid request.
            var marked=File.Exists(dest)?await File.ReadAllBytesAsync(dest,ct):
                await frames.MarkObjectAsync(Path.Combine(root,$"frame-{ordinal}.jpg"),frame,candidate,dest,ct);
            images.Add(marked);references.Add(new(ordinal,frame,candidate,Convert.ToHexString(SHA256.HashData(marked)).ToLowerInvariant()));
        }
        ct.ThrowIfCancellationRequested();runtime.ReserveOrThrow(token);
        await Save(Path.Combine(root,"object-review-attempt.json"),new ObjectReviewAttempt(ObjectReviewPolicy.Protocol,video.Sha256,
            draft.ResponseSha256,draft.CacheKey,references.ToArray(),DateTimeOffset.UtcNow,ObjectReviewPolicy.EstimatedCostUsd),CancellationToken.None);
        // No retries. Preserve any completed raw response before validation or SAM submission.
        using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var result=await transport.ReviewObjectAsync(references.ToArray(),images.ToArray(),token,deadline.Token);
        await File.WriteAllTextAsync(Path.Combine(root,"object-review-response.json"),result.Raw,CancellationToken.None);
        var reviewed=ObjectReviewPolicy.Derive(draft,result,references.ToArray());
        await Save(Path.Combine(root,"reviewed-evidence.json"),reviewed,CancellationToken.None);
        AssemblyProgress.Done("target-review");
        return reviewed;
    }
}
