using System.Security.Cryptography;
using System.Text.Json;
namespace ProofLab.Api;

public sealed partial class AssemblyLocator
{
    private async Task<AssemblyEvidence?> ReadObjectReview(string root,AssemblyEvidence draft,CancellationToken ct)
    {
        var dense=File.Exists(Path.Combine(root,"dense-localization-attempt.json"));
        if(dense) {
            // The dense aggregate is localization evidence, not an independent body review.
            var first=await Read<AssemblyEvidence>(Path.Combine(root,"evidence.json"),ct);
            var aggregate=await ReadDenseLocalization(root,first,ct);
            if(aggregate is null)return null;
            draft=aggregate;
        }
        var evidencePath=Path.Combine(root,dense?"body-reviewed-evidence.json":"reviewed-evidence.json");
        if(!File.Exists(evidencePath))return null;
        var attempt=await Read<ObjectReviewAttempt>(Path.Combine(root,"object-review-attempt.json"),ct);
        if(attempt.Protocol is not (ObjectReviewPolicy.Protocol or ObjectReviewPolicy.SuspensionProtocol or ObjectReviewPolicy.LegacyProtocol or ObjectReviewPolicy.DenseBodyProtocol or ProgressiveSuspensionPolicy.BodyProtocol) || attempt.SourceSha256!=draft.VideoSha256 ||
            attempt.DraftResponseSha256!=draft.ResponseSha256 || attempt.EvidenceKey!=draft.CacheKey ||
            dense!=(attempt.Protocol is ObjectReviewPolicy.DenseBodyProtocol or ProgressiveSuspensionPolicy.BodyProtocol) ||
            attempt.References.Length is <2 or >16 || attempt.References.Select(x=>x.Ordinal).Distinct().Count()!=attempt.References.Length)
            throw new InvalidDataException("Visual review provenance changed.");
        foreach(var r in attempt.References) {
            if(dense)await ReviewSource(root,r.Frame,ct);
            if(r.Ordinal<0 || r.Ordinal>=draft.Frames.Length || r.Frame!=draft.Frames[r.Ordinal] ||
                r.Candidate!=draft.Proposal.Observations.Single(o=>o.Frame==r.Ordinal && o.Role=="object") ||
                await FileHash(Path.Combine(root,$"review-{r.Ordinal}.jpg"),ct)!=r.MarkedSha256)
                throw new InvalidDataException("Visual review image or candidate changed.");
        }
        var cropped=attempt.References.Where(r=>r.SuspensionCrop is not null).ToArray();
        if((attempt.Protocol is ObjectReviewPolicy.Protocol or ObjectReviewPolicy.SuspensionProtocol && cropped.Length!=3) ||
            (attempt.Protocol is ObjectReviewPolicy.LegacyProtocol or ObjectReviewPolicy.DenseBodyProtocol or ProgressiveSuspensionPolicy.BodyProtocol && cropped.Length!=0))throw new InvalidDataException("Crop protocol mismatch.");
        foreach(var r in cropped) {
            var expected=ObjectReviewPolicy.Crop(r.Frame,ObjectReviewPolicy.CoarseSuspension(draft,r.Ordinal));
            if(r.SuspensionCrop! with {Sha256=""}!=expected ||
                await FileHash(Path.Combine(root,$"suspension-{r.Ordinal}.jpg"),ct)!=r.SuspensionCrop.Sha256)
                throw new InvalidDataException("Suspension crop geometry or pixels changed.");
        }
        var bodies=attempt.References.Where(r=>r.BodyCrop is not null).ToArray();
        if((attempt.Protocol is ObjectReviewPolicy.Protocol or ObjectReviewPolicy.DenseBodyProtocol or ProgressiveSuspensionPolicy.BodyProtocol && bodies.Length!=attempt.References.Length) ||
            (attempt.Protocol is not (ObjectReviewPolicy.Protocol or ObjectReviewPolicy.DenseBodyProtocol or ProgressiveSuspensionPolicy.BodyProtocol) && bodies.Length!=0))throw new InvalidDataException("Body crop protocol mismatch.");
        foreach(var r in bodies) {
            var expected=ObjectReviewPolicy.BodyRegion(r.Frame,r.Candidate);
            if(r.BodyCrop! with {Sha256=""}!=expected || await FileHash(Path.Combine(root,$"body-{r.Ordinal}.jpg"),ct)!=r.BodyCrop.Sha256)
                throw new InvalidDataException("Body crop geometry or pixels changed.");
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

    private async Task<string> ReviewSource(string root,KnotFrame frame,CancellationToken ct)
    {
        // Dense aggregates use global ordinals; files in each round use local ordinals.
        var folders=new[]{root}.Concat(Directory.GetDirectories(root,"dense-round-*")).ToArray();
        foreach(var folder in folders) {
            var attemptPath=Path.Combine(folder,"attempt.json");
            if(!File.Exists(attemptPath))continue;
            var attempt=await Read<AssemblyVideoAttempt>(attemptPath,ct);
            var ordinal=Array.FindIndex(attempt.Frames,f=>f==frame);
            if(ordinal<0)continue;
            var path=Path.Combine(folder,$"frame-{ordinal}.jpg");
            if(await FileHash(path,ct)!=frame.Sha256)throw new InvalidDataException("Body review source frame changed.");
            return path;
        }
        throw new InvalidDataException("Body review source frame unavailable.");
    }

    private async Task<AssemblyEvidence> ReviewObject(string root,AssemblyEvidence draft,SamVideoSource video,double[] pts,string token,CancellationToken ct)
    {
        AssemblyProgress.Start("target-review");
        var existing=await ReadObjectReview(root,draft,ct);
        if(existing is not null){AssemblyProgress.Done("target-review");return existing;}
        if(File.Exists(Path.Combine(root,"object-review-attempt.json")))throw new InvalidOperationException("Previous visual review requires reconciliation.");
        var dense=File.Exists(Path.Combine(root,"dense-localization-attempt.json"));
        if(dense) {
            var first=await Read<AssemblyEvidence>(Path.Combine(root,"evidence.json"),ct);
            draft=await ReadDenseLocalization(root,first,ct) ?? throw new InvalidDataException("Dense localization is incomplete.");
        }
        var plan=dense || adaptiveClips is null?null:await adaptiveClips.PlanAsync(video,pts,null,53,8,ct);
        var candidates=draft.Proposal.Observations.Where(o=>o.Role=="object").Select(o=>o.Frame).Order().ToArray();
        // Review representative body crops across the chosen round; retain all suspension observations.
        var selected=dense?Enumerable.Range(0,Math.Min(9,candidates.Length))
            .Select(n=>candidates[(int)Math.Round(n*(candidates.Length-1d)/Math.Max(1,Math.Min(9,candidates.Length)-1))]).Distinct().ToArray()
            :ObjectReviewPolicy.Select(draft.Frames,plan);
        var images=new List<byte[]>();var references=new List<ObjectReviewImage>();
        foreach(var ordinal in selected) {
            var frame=draft.Frames[ordinal];var candidate=draft.Proposal.Observations.Single(o=>o.Frame==ordinal && o.Role=="object");
            var dest=Path.Combine(root,$"review-{ordinal}.jpg");
            // An interrupted LOCAL preparation can be resumed without a paid request.
            var marked=File.Exists(dest)?await File.ReadAllBytesAsync(dest,ct):
                await frames.MarkObjectAsync(await ReviewSource(root,frame,ct),frame,candidate with {Visible=false},dest,ct);
            images.Add(marked);references.Add(new(ordinal,frame,candidate,Convert.ToHexString(SHA256.HashData(marked)).ToLowerInvariant()));
        }
        // Keep the full scene as context, but measure body boxes on enlarged, unmarked crops.
        var bodyBytes=new List<byte[]>();
        for(var i=0;i<references.Count;i++) {
            var r=references[i];var crop=ObjectReviewPolicy.BodyRegion(r.Frame,r.Candidate);
            var dest=Path.Combine(root,$"body-{r.Ordinal}.jpg");
            var bytes=File.Exists(dest)?await File.ReadAllBytesAsync(dest,ct):
                await frames.CropSuspensionAsync(await ReviewSource(root,r.Frame,ct),r.Frame,crop,dest,ct);
            references[i]=r with {BodyCrop=crop with {Sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}};
            bodyBytes.Add(bytes);
        }
        images.AddRange(bodyBytes);
        var cropBytes=new List<byte[]>();
        foreach(var ordinal in dense?Array.Empty<int>():ObjectReviewPolicy.SuspensionOrdinals(references.ToArray())) {
            var frame=draft.Frames[ordinal];
            var crop=ObjectReviewPolicy.Crop(frame,ObjectReviewPolicy.CoarseSuspension(draft,ordinal));
            var dest=Path.Combine(root,$"suspension-{ordinal}.jpg");
            var bytes=File.Exists(dest)?await File.ReadAllBytesAsync(dest,ct):
                await frames.CropSuspensionAsync(Path.Combine(root,$"frame-{ordinal}.jpg"),frame,crop,dest,ct);
            var index=references.FindIndex(r=>r.Ordinal==ordinal);
            references[index]=references[index] with {SuspensionCrop=crop with {Sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}};
            cropBytes.Add(bytes);
        }
        images.AddRange(cropBytes);
        ct.ThrowIfCancellationRequested();runtime.ReserveOrThrow(token);
        await Save(Path.Combine(root,"object-review-attempt.json"),new ObjectReviewAttempt(dense?(draft.Derivation==ProgressiveSuspensionPolicy.Protocol?ProgressiveSuspensionPolicy.BodyProtocol:ObjectReviewPolicy.DenseBodyProtocol):ObjectReviewPolicy.Protocol,video.Sha256,
            draft.ResponseSha256,draft.CacheKey,references.ToArray(),DateTimeOffset.UtcNow,ObjectReviewPolicy.EstimatedCostUsd),CancellationToken.None);
        // No retries. Preserve any completed raw response before validation or SAM submission.
        using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var result=await transport.ReviewObjectAsync(references.ToArray(),images.ToArray(),token,deadline.Token);
        await File.WriteAllTextAsync(Path.Combine(root,"object-review-response.json"),result.Raw,CancellationToken.None);
        var reviewed=ObjectReviewPolicy.Derive(draft,result,references.ToArray());
        await Save(Path.Combine(root,dense?"body-reviewed-evidence.json":"reviewed-evidence.json"),reviewed,CancellationToken.None);
        AssemblyProgress.Done("target-review");
        return reviewed;
    }
}
