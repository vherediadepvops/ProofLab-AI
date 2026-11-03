using System.Text.Json;
namespace ProofLab.Api;

public sealed partial class AssemblyLocator
{
    private sealed record DenseAttempt(string Protocol,int MaximumAttempts);
    private sealed record DenseManifest(string Protocol,int Rounds,string StopReason,string[] ResponseHashes);

    private async Task<AssemblyEvidence> ContinueDenseLocalization(string root,AssemblyEvidence first,
        SamVideoSource video,double[] pts,string mp4,string token,CancellationToken ct)
    {
        var rounds=new List<AssemblyEvidence>{first};
        var combined=DenseLocalizationPolicy.Combine(rounds);
        var stop="ATTEMPTS_EXHAUSTED";
        while(!DenseLocalizationPolicy.Complete(combined) && rounds.Count<DenseLocalizationPolicy.MaximumAttempts) {
            var indices=DenseLocalizationPolicy.NextReferences(pts.Length,combined.Frames.Select(f=>f.Index));
            if(indices.Length!=50){stop="NO_NEW_FIFTY_FRAMES";break;}
            var number=rounds.Count+1;var folder=Path.Combine(root,$"dense-round-{number}");
            Directory.CreateDirectory(folder);
            using var progressRound=AssemblyProgress.Round(number);
            AssemblyProgress.Start("references");
            var anchors=await frames.ExtractBatchAsync(video.Path,folder,indices.Select(i=>(i,pts[i])).ToArray(),ct);
            await ValidateFrames(folder,anchors,ct);
            if(!anchors.Select(f=>f.Index).SequenceEqual(indices) || anchors.Any(f=>Math.Abs(f.TimeSeconds-pts[f.Index])>.005))
                throw new InvalidDataException("Additional reference timestamps changed.");
            var pixels=await Task.WhenAll(Enumerable.Range(0,50).Select(i=>File.ReadAllBytesAsync(Path.Combine(folder,$"frame-{i}.jpg"),ct)));
            AssemblyProgress.Done("references");
            var uploaded=first.VideoInput ?? throw new InvalidDataException("Missing uploaded video identity.");
            var attempt=new AssemblyVideoAttempt(first.VideoSha256,first.CacheKey,first.RunId!,first.Model,
                first.Protocol,DateTimeOffset.UtcNow,AssemblyPolicy.VideoBudget(configuration),anchors,
                uploaded with {ProviderFileId=""});
            ct.ThrowIfCancellationRequested();runtime.ReserveOrThrow(token);
            await Save(Path.Combine(folder,"attempt.json"),attempt,CancellationToken.None);
            // Only a completed insufficient answer advances to another round. A transport,
            // receipt or parsing failure stops here; its possible charge remains auditable.
            var result=await transport.LocateVideoGroundingAsync(mp4,uploaded,anchors,pixels,token,
                attempt.MaximumEstimatedCostUsd,
                value=>Save(Path.Combine(folder,"video-input.json"),value,CancellationToken.None),ct);
            await File.WriteAllTextAsync(Path.Combine(folder,"response.json"),result.Response.Raw,CancellationToken.None);
            AssemblyProgress.Start("verify");
            var r=result.Response;
            AssemblyMetaTransport.ValidateReceipt(r.Raw,r.ProviderResponseId,r.InputTokens,r.OutputTokens);
            var proposal=AssemblyPolicy.ParseGrounding(AssemblyMetaTransport.OutputText(r.Raw),anchors);
            var evidence=new AssemblyEvidence(first.CacheKey,video.Sha256,first.Model,first.Protocol,anchors,
                KnotPolicy.Hash(r.Raw),r.ProviderResponseId,DateTimeOffset.UtcNow,proposal,r.InputTokens,r.OutputTokens,
                result.VideoInput,first.RunId);
            if(!DenseLocalizationPolicy.IsDense(evidence))throw new InvalidDataException("Fifty object and suspension observations required.");
            await Save(Path.Combine(folder,"evidence.json"),evidence,CancellationToken.None);
            rounds.Add(evidence);combined=DenseLocalizationPolicy.Combine(rounds);
            AssemblyProgress.Done("verify");
        }
        if(DenseLocalizationPolicy.Complete(combined))stop="CONSENSUS_REACHED";
        await Save(Path.Combine(root,"reviewed-evidence.json"),combined,CancellationToken.None);
        await Save(Path.Combine(root,"dense-localization.json"),new DenseManifest(DenseLocalizationPolicy.Protocol,
            rounds.Count,stop,rounds.Select(r=>r.ResponseSha256).ToArray()),CancellationToken.None);
        return combined;
    }

    private async Task<AssemblyEvidence?> ReadDenseLocalization(string root,AssemblyEvidence first,CancellationToken ct)
    {
        var kind=await Read<DenseAttempt>(Path.Combine(root,"dense-localization-attempt.json"),ct);
        if(ProgressiveSuspensionPolicy.Supports(kind.Protocol))return await ReadProgressiveSuspension(root,first,ct);
        var finalPath=Path.Combine(root,"reviewed-evidence.json");var manifestPath=Path.Combine(root,"dense-localization.json");
        if(!File.Exists(finalPath)||!File.Exists(manifestPath))return null;
        var start=await Read<DenseAttempt>(Path.Combine(root,"dense-localization-attempt.json"),ct);
        var manifest=await Read<DenseManifest>(manifestPath,ct);
        if(start.Protocol!=DenseLocalizationPolicy.Protocol || start.MaximumAttempts!=DenseLocalizationPolicy.MaximumAttempts ||
            manifest.Protocol!=start.Protocol || manifest.Rounds is <1 or >DenseLocalizationPolicy.MaximumAttempts ||
            manifest.ResponseHashes.Length!=manifest.Rounds || manifest.StopReason is not
                ("CONSENSUS_REACHED" or "NO_NEW_FIFTY_FRAMES" or "ATTEMPTS_EXHAUSTED"))
            throw new InvalidDataException("Invalid dense localization manifest.");
        var rounds=new List<AssemblyEvidence>{first};
        for(var n=2;n<=manifest.Rounds;n++) {
            var folder=Path.Combine(root,$"dense-round-{n}");
            var attempt=await Read<AssemblyVideoAttempt>(Path.Combine(folder,"attempt.json"),ct);
            var saved=await Read<AssemblyEvidence>(Path.Combine(folder,"evidence.json"),ct);
            var input=await Read<AssemblyVideoInput>(Path.Combine(folder,"video-input.json"),ct);
            var rawPath=Path.Combine(folder,"response.json");
            if(new FileInfo(rawPath).Length>300000)throw new InvalidDataException("Oversized dense response.");
            var raw=await File.ReadAllTextAsync(rawPath,ct);
            if(attempt.Sha256!=first.VideoSha256 || attempt.Key!=first.CacheKey || attempt.RunId!=first.RunId ||
                attempt.Model!=first.Model || attempt.Protocol!=first.Protocol ||
                attempt.VideoInput!=(first.VideoInput! with {ProviderFileId=""}) || input!=first.VideoInput ||
                saved.VideoInput!=input || saved.CacheKey!=attempt.Key || saved.VideoSha256!=attempt.Sha256 ||
                saved.RunId!=attempt.RunId || saved.Model!=attempt.Model || saved.Protocol!=attempt.Protocol ||
                saved.Derivation is not null || saved.ResponseSha256!=KnotPolicy.Hash(raw) ||
                !saved.Frames.SequenceEqual(attempt.Frames))throw new InvalidDataException("Dense round provenance mismatch.");
            await ValidateFrames(folder,saved.Frames,ct);
            AssemblyMetaTransport.ValidateReceipt(raw,saved.ProviderResponseId,saved.InputTokens,saved.OutputTokens);
            var proposal=AssemblyPolicy.ParseGrounding(AssemblyMetaTransport.OutputText(raw),saved.Frames);
            if(JsonSerializer.Serialize(proposal,Json)!=JsonSerializer.Serialize(saved.Proposal,Json))
                throw new InvalidDataException("Dense coordinates differ from preserved response.");
            rounds.Add(saved);
        }
        var rebuilt=DenseLocalizationPolicy.Combine(rounds);
        var final=await Read<AssemblyEvidence>(finalPath,ct);
        if(!manifest.ResponseHashes.SequenceEqual(rounds.Select(r=>r.ResponseSha256)) ||
            (manifest.StopReason=="CONSENSUS_REACHED")!=DenseLocalizationPolicy.Complete(rebuilt) ||
            JsonSerializer.Serialize(final,Json)!=JsonSerializer.Serialize(rebuilt,Json))
            throw new InvalidDataException("Dense consensus differs from preserved observations.");
        return rebuilt;
    }
}
