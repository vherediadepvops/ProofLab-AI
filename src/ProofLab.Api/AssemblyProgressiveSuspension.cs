using System.Text.Json;
namespace ProofLab.Api;

public sealed partial class AssemblyLocator
{
    private static readonly double[] SuspensionZooms=[1.5,3];
    private async Task<AssemblyEvidence> ReviewProgressiveSuspension(string root,AssemblyEvidence first,string token,CancellationToken ct)
    {
        var answers=new List<SuspensionZoomAnswer>();var receipts=new List<KnotProviderResult>();
        var points=first.Proposal.Suspension.Points;string inputHash=first.ResponseSha256;
        for(int stage=0;stage<2;stage++) {
            double zoom=SuspensionZooms[stage];
            using var progressRound=AssemblyProgress.Round(stage+2);
            string folder=Path.Combine(root,$"zoom-{stage+2}");Directory.CreateDirectory(folder);
            if(File.Exists(Path.Combine(folder,"attempt.json")))throw new InvalidOperationException("Previous zoom inference requires reconciliation.");
            AssemblyProgress.Start("zoom-references");
            var images=new List<byte[]>();var references=new List<SuspensionZoomImage>();
            for(int i=0;i<50;i++) {
                var frame=first.Frames[i];var crop=ProgressiveSuspensionPolicy.Crop(frame,points[i],zoom);
                // Always read the original native frame, never the previous enlargement.
                var path=Path.Combine(folder,$"crop-{i}.jpg");
                var bytes=await frames.CropZoomSuspensionAsync(Path.Combine(root,$"frame-{i}.jpg"),frame,crop,path,ct);
                references.Add(new(i,frame,crop with {Sha256=await FileHash(path,ct)}));images.Add(bytes);
            }
            AssemblyProgress.Done("zoom-references");
            ct.ThrowIfCancellationRequested();runtime.ReserveOrThrow(token);
            var attempt=new SuspensionZoomAttempt(ProgressiveSuspensionPolicy.Protocol,first.VideoSha256,first.CacheKey,
                inputHash,zoom,references.ToArray(),AssemblyPolicy.VideoBudget(configuration));
            await Save(Path.Combine(folder,"attempt.json"),attempt,CancellationToken.None);
            AssemblyProgress.Start("zoom-inference");
            var result=await transport.ReviewZoomSuspensionAsync(references.ToArray(),images.ToArray(),zoom,token,attempt.MaximumEstimatedCostUsd,ct);
            await File.WriteAllTextAsync(Path.Combine(folder,"response.json"),result.Raw,CancellationToken.None);
            AssemblyProgress.Done("zoom-inference");AssemblyProgress.Start("zoom-verify");
            AssemblyMetaTransport.ValidateReceipt(result.Raw,result.ProviderResponseId,result.InputTokens,result.OutputTokens);
            var answer=ProgressiveSuspensionPolicy.Parse(AssemblyMetaTransport.OutputText(result.Raw),references.ToArray());
            answers.Add(answer);receipts.Add(result);points=answer.Points;
            inputHash=KnotPolicy.Hash(inputHash+"\n"+KnotPolicy.Hash(result.Raw));
            AssemblyProgress.Done("zoom-verify");
        }
        var combined=ProgressiveSuspensionPolicy.Combine(first,answers[0],answers[1],receipts[0],receipts[1]);
        await Save(Path.Combine(root,"reviewed-evidence.json"),combined,CancellationToken.None);
        await Save(Path.Combine(root,"dense-localization.json"),new DenseManifest(ProgressiveSuspensionPolicy.Protocol,3,
            "ZOOM_REVIEWS_COMPLETE",new[]{first.ResponseSha256}.Concat(receipts.Select(r=>KnotPolicy.Hash(r.Raw))).ToArray()),CancellationToken.None);
        return combined;
    }

    private async Task<AssemblyEvidence?> ReadProgressiveSuspension(string root,AssemblyEvidence first,CancellationToken ct)
    {
        if(!File.Exists(Path.Combine(root,"reviewed-evidence.json"))||!File.Exists(Path.Combine(root,"dense-localization.json")))return null;
        var marker=await Read<DenseAttempt>(Path.Combine(root,"dense-localization-attempt.json"),ct);
        var manifest=await Read<DenseManifest>(Path.Combine(root,"dense-localization.json"),ct);
        if(!ProgressiveSuspensionPolicy.Supports(marker.Protocol)||marker.MaximumAttempts!=3||
            manifest.Protocol!=marker.Protocol||manifest.Rounds!=3||manifest.StopReason!="ZOOM_REVIEWS_COMPLETE"||
            manifest.ResponseHashes.Length!=3||manifest.ResponseHashes[0]!=first.ResponseSha256||first.Frames.Length!=50)
            throw new InvalidDataException("Invalid progressive localization manifest.");
        var points=first.Proposal.Suspension.Points;string inputHash=first.ResponseSha256;
        var answers=new List<SuspensionZoomAnswer>();var receipts=new List<KnotProviderResult>();
        for(int stage=0;stage<2;stage++) {
            var folder=Path.Combine(root,$"zoom-{stage+2}");
            var attempt=await Read<SuspensionZoomAttempt>(Path.Combine(folder,"attempt.json"),ct);
            if(attempt.Protocol!=marker.Protocol||attempt.SourceSha256!=first.VideoSha256||attempt.EvidenceKey!=first.CacheKey||
                attempt.InputResponseSha256!=inputHash||attempt.Zoom!=SuspensionZooms[stage]||attempt.References.Length!=50||
                !double.IsFinite(attempt.MaximumEstimatedCostUsd)||attempt.MaximumEstimatedCostUsd is <.025 or >1)
                throw new InvalidDataException("Zoom source/round provenance changed.");
            for(int i=0;i<50;i++) {
                var r=attempt.References[i];var expected=ProgressiveSuspensionPolicy.Crop(first.Frames[i],points[i],attempt.Zoom);
                if(r.Ordinal!=i||r.Frame!=first.Frames[i]||r.Crop with {Sha256=""}!=expected||
                    r.Crop.Sha256!=await FileHash(Path.Combine(folder,$"crop-{i}.jpg"),ct))
                    throw new InvalidDataException("Zoom frame, crop geometry or pixels changed.");
            }
            var rawPath=Path.Combine(folder,"response.json");
            if(new FileInfo(rawPath).Length>300_000)throw new InvalidDataException("Zoom response too large.");
            var raw=await File.ReadAllTextAsync(rawPath,ct);using var json=JsonDocument.Parse(raw);var j=json.RootElement;
            var result=new KnotProviderResult(raw,j.GetProperty("id").GetString()!,j.GetProperty("usage").GetProperty("input_tokens").GetInt32(),j.GetProperty("usage").GetProperty("output_tokens").GetInt32());
            AssemblyMetaTransport.ValidateReceipt(raw,result.ProviderResponseId,result.InputTokens,result.OutputTokens);
            if(manifest.ResponseHashes[stage+1]!=KnotPolicy.Hash(raw))throw new InvalidDataException("Zoom response changed.");
            var answer=ProgressiveSuspensionPolicy.Parse(AssemblyMetaTransport.OutputText(raw),attempt.References,marker.Protocol);
            answers.Add(answer);receipts.Add(result);points=answer.Points;inputHash=KnotPolicy.Hash(inputHash+"\n"+KnotPolicy.Hash(raw));
        }
        var rebuilt=ProgressiveSuspensionPolicy.Combine(first,answers[0],answers[1],receipts[0],receipts[1],marker.Protocol);
        var saved=await Read<AssemblyEvidence>(Path.Combine(root,"reviewed-evidence.json"),ct);
        if(JsonSerializer.Serialize(saved,Json)!=JsonSerializer.Serialize(rebuilt,Json))throw new InvalidDataException("Progressive evidence differs from preserved responses.");
        return rebuilt;
    }
}
