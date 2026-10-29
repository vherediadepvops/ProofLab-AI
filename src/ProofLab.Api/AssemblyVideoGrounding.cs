using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
namespace ProofLab.Api;

public sealed partial class AssemblyLocator
{
    private sealed record FullRun(string Root,AssemblyVideoAttempt Attempt,AssemblyEvidence? Evidence,bool NoInference);
    private string VideoRoot(string sha)=>Folder(AssemblyPolicy.VideoGroundingKey(sha));
    private static bool ValidSha(string value)=>value is {Length:64} && value.All(Uri.IsHexDigit);
    private static string PtsHash(double[] pts)=>KnotPolicy.Hash(string.Join("\n",pts.Select(p=>p.ToString("R",CultureInfo.InvariantCulture))));
    private static async Task<string> FileHash(string path,CancellationToken ct) {
        await using var input=File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(input,ct)).ToLowerInvariant();
    }
    private static async Task ValidateFrames(string root,KnotFrame[] anchors,CancellationToken ct)
    {
        if(anchors.Length!=3 || anchors.Select(f=>f.Index).Distinct().Count()!=3 ||
            anchors.Any(f=>f.Index<0 || !double.IsFinite(f.TimeSeconds) || f.TimeSeconds<0 ||
                f.Width is <2 or >8192 || f.Height is <2 or >8192 ||
                f.Width!=anchors[0].Width || f.Height!=anchors[0].Height || !ValidSha(f.Sha256)))
            throw new InvalidDataException("Invalid original anchor metadata.");
        for(var i=0;i<3;i++) {
            var path=Path.Combine(root,$"frame-{i}.jpg");
            if(new FileInfo(path).Length is <100 or >8_000_000 || await FileHash(path,ct)!=anchors[i].Sha256)
                throw new InvalidDataException("Original anchor integrity mismatch.");
        }
    }
    private async Task<AssemblyEvidence?> RecoverLegacyGrounding(string root,string sha,string key,CancellationToken ct)
    {
        var attemptPath=Path.Combine(root,"attempt.json");var rawPath=Path.Combine(root,"response.json");
        if(!File.Exists(attemptPath) || !File.Exists(rawPath))return null;
        // Re-derive from the preserved completed response; never rewrite provider bytes.
        var attempt=await Read<JsonElement>(attemptPath,ct);
        if(attempt.GetProperty("sha256").GetString()!=sha || attempt.GetProperty("key").GetString()!=key ||
            attempt.GetProperty("protocol").GetString()!=AssemblyPolicy.GroundingProtocol ||
            attempt.GetProperty("model").GetString()!=AssemblyPolicy.Model || key!=AssemblyPolicy.GroundingKey(sha))
            throw new InvalidDataException("Historical grounding attempt provenance mismatch.");
        var anchors=attempt.GetProperty("frames").Deserialize<KnotFrame[]>(Json) ??
            throw new InvalidDataException("Missing historical anchors.");
        await ValidateFrames(root,anchors,ct);
        if(new FileInfo(rawPath).Length>300000)throw new InvalidDataException("Oversized provider response.");
        var raw=await File.ReadAllTextAsync(rawPath,ct);
        try {
            using var doc=JsonDocument.Parse(raw);var provider=doc.RootElement;
            var id=provider.GetProperty("id").GetString()!;
            var usage=provider.GetProperty("usage");
            int usedInput=usage.GetProperty("input_tokens").GetInt32(),usedOutput=usage.GetProperty("output_tokens").GetInt32();
            AssemblyMetaTransport.ValidateReceipt(raw,id,usedInput,usedOutput);
            var proposal=AssemblyPolicy.ParseGrounding(AssemblyMetaTransport.OutputText(raw),anchors);
            return new(key,sha,AssemblyPolicy.Model,AssemblyPolicy.GroundingProtocol,anchors,
                KnotPolicy.Hash(raw),id,attempt.GetProperty("createdAtUtc").GetDateTimeOffset(),proposal,usedInput,usedOutput,
                Derivation:"REPARSED_PRESERVED_RESPONSE_ABSENT_ENTITY_CONFIDENCE_V2");
        }
        catch(Exception ex) when(ex is JsonException or FormatException or KeyNotFoundException) {
            return null; // Still unresolved: no automatic paid retry.
        }
    }
    private async Task<FullRun[]> FullRuns(string sha,CancellationToken ct)
    {
        var runs=Path.Combine(VideoRoot(sha),"runs");
        if(!Directory.Exists(runs))return [];
        var records=new List<FullRun>();
        foreach(var root in Directory.GetDirectories(runs).OrderBy(p=>p,StringComparer.Ordinal)) {
            var runId=Path.GetFileName(root);
            if(runId.Length!=32 || runId.Any(c=>!Uri.IsHexDigit(c)))continue;
            var path=Path.Combine(root,"attempt.json");if(!File.Exists(path))continue;
            var attempt=await Read<AssemblyVideoAttempt>(path,ct);
            if(attempt.RunId!=runId || attempt.Sha256!=sha ||
                attempt.Key!=AssemblyPolicy.VideoRunKey(sha,runId) || attempt.Model!=AssemblyPolicy.Model ||
                attempt.Protocol!=AssemblyPolicy.VideoGroundingProtocol ||
                attempt.VideoInput.SourceSha256!=sha || !ValidSha(attempt.VideoInput.SubmittedMp4Sha256) ||
                !ValidSha(attempt.VideoInput.FramePtsSha256) || attempt.VideoInput.SourceFrameCount<3 ||
                attempt.VideoInput.Scope!="FULL_VIDEO_REMUX_WITH_3_NATIVE_ANCHORS")
                throw new InvalidDataException("Full-video attempt provenance mismatch.");
            AssemblyEvidence? evidence=null;
            if(File.Exists(Path.Combine(root,"evidence.json"))) {
                evidence=await Read<AssemblyEvidence>(Path.Combine(root,"evidence.json"),ct);
                var rawPath=Path.Combine(root,"response.json");
                if(new FileInfo(rawPath).Length>300000)throw new InvalidDataException("Oversized provider response.");
                var raw=await File.ReadAllTextAsync(rawPath,ct);
                var uploaded=await Read<AssemblyVideoInput>(Path.Combine(root,"video-input.json"),ct);
                if(evidence.CacheKey!=attempt.Key || evidence.VideoSha256!=sha || evidence.RunId!=runId ||
                    evidence.Protocol!=attempt.Protocol || evidence.Model!=attempt.Model ||
                    evidence.ResponseSha256!=KnotPolicy.Hash(raw) || evidence.VideoInput!=uploaded ||
                    uploaded with {ProviderFileId=""}!=attempt.VideoInput ||
                    string.IsNullOrWhiteSpace(uploaded.ProviderFileId) ||
                    !evidence.Frames.SequenceEqual(attempt.Frames))
                    throw new InvalidDataException("Full-video evidence provenance mismatch.");
                await ValidateFrames(root,evidence.Frames,ct);
                AssemblyMetaTransport.ValidateReceipt(raw,evidence.ProviderResponseId,evidence.InputTokens,evidence.OutputTokens);
                var rebuilt=AssemblyPolicy.ParseGrounding(AssemblyMetaTransport.OutputText(raw),evidence.Frames);
                if(JsonSerializer.Serialize(rebuilt,Json)!=JsonSerializer.Serialize(evidence.Proposal,Json))
                    throw new InvalidDataException("Full-video target differs from provider response.");
                AssemblyPolicy.Ground(evidence); // Also checks key and automatic-scene invariants.
            }
            var noInference=false;
            if(File.Exists(Path.Combine(root,"outcome.json"))) {
                var outcome=await Read<AssemblyGroundingOutcome>(Path.Combine(root,"outcome.json"),ct);
                noInference=outcome.Status=="NO_INFERENCE_SENT";
                if(noInference && (evidence is not null || File.Exists(Path.Combine(root,"response.json"))))
                    throw new InvalidDataException("Conflicting full-video attempt outcome.");
            }
            records.Add(new(root,attempt,evidence,noInference));
        }
        return records.OrderByDescending(r=>r.Attempt.CreatedAtUtc).ThenByDescending(r=>r.Attempt.RunId).ToArray();
    }
    private async Task<(AssemblyEvidence? Legacy,FullRun[] Runs,bool Pending)> GroundingRecords(string sha,CancellationToken ct)
    {
        var legacyKey=AssemblyPolicy.GroundingKey(sha);
        var legacy=await Existing(Folder(legacyKey),sha,legacyKey,ct,true);
        var runs=await FullRuns(sha,ct);
        var legacyPending=File.Exists(Path.Combine(Folder(legacyKey),"attempt.json")) && legacy is null;
        return(legacy,runs,HistoricalAttemptPending(sha) || legacyPending ||
            runs.Any(r=>r.Evidence is null && !r.NoInference));
    }
    public async Task<SamSceneDefinition[]> TrackingHistoryAsync(string id,string role,CancellationToken ct,bool quote=false)
    {
        var (video,_,_)=await Source(id,role,ct,quote);
        var records=await GroundingRecords(video.Sha256,ct);
        var scenes=new List<SamSceneDefinition>();
        foreach(var run in records.Runs) {
            var scene=run.Evidence is null?null:AssemblyPolicy.Ground(run.Evidence);
            if(scene is not null)scenes.Add(scene);
        }
        var reviewed=await ApprovedCore(id,role,ct,quote);if(reviewed is not null)scenes.Add(reviewed);
        var legacy=records.Legacy is null?null:AssemblyPolicy.Ground(records.Legacy);if(legacy is not null)scenes.Add(legacy);
        return scenes.DistinctBy(s=>s.Key).ToArray();
    }
    public Task<SamSceneDefinition?> TrackingAsync(string id,string role,CancellationToken ct)=>TrackingCore(id,role,ct,false);
    public Task<SamSceneDefinition?> TrackingForQuoteAsync(string id,string role,CancellationToken ct)=>TrackingCore(id,role,ct,true);
    private async Task<SamSceneDefinition?> TrackingCore(string id,string role,CancellationToken ct,bool quote)
    {
        var (video,_,_)=await Source(id,role,ct,quote);
        var records=await GroundingRecords(video.Sha256,ct);
        var latest=records.Runs.FirstOrDefault(r=>r.Evidence is not null)?.Evidence;
        if(latest is not null)return AssemblyPolicy.Ground(latest);
        return await ApprovedCore(id,role,ct,quote) ??
            (records.Legacy is null?null:AssemblyPolicy.Ground(records.Legacy));
    }
    public Task<AssemblyGroundingQuote> GroundingQuoteAsync(string id,string role,CancellationToken ct,string? mode=null)=>
        VideoQuote(id,role,ct,true,AssemblyPolicy.Mode(mode));
    private async Task<AssemblyGroundingQuote> VideoQuote(string id,string role,CancellationToken ct,bool quote,string mode)
    {
        var (video,_,_)=await Source(id,role,ct,quote);
        var records=await GroundingRecords(video.Sha256,ct);
        var key=AssemblyPolicy.VideoGroundingKey(video.Sha256);
        var budget=AssemblyPolicy.VideoBudget(configuration);
        bool fresh=mode=="api-meta";
        if(records.Pending)return new(key,video.Sha256,budget,false,true,false,"PRIOR_ATTEMPT",null,null,mode,fresh);
        if(fresh)return new(key,video.Sha256,budget,false,false,runtime.ManualTokenCallsPermitted,
            "NEEDS_LOCALIZATION",null,null,mode,true);
        var evidence=records.Runs.FirstOrDefault(r=>r.Evidence is not null)?.Evidence;
        var reviewed=evidence is null?await ApprovedCore(id,role,ct,quote):null;
        evidence??=reviewed is null?records.Legacy:null;
        var scene=reviewed ?? (evidence is null?null:AssemblyPolicy.Ground(evidence));
        var status=scene is not null?"READY":evidence is not null?AssemblyPolicy.GroundingStatus(evidence):"NEEDS_LOCALIZATION";
        return new(key,video.Sha256,budget,scene is not null || evidence is not null,false,
            scene is not null || (status=="NEEDS_LOCALIZATION" && runtime.ManualTokenCallsPermitted),
            status,scene,evidence,mode,false,EvidenceProtocol:evidence?.Protocol ?? (reviewed is null?null:AssemblyPolicy.Protocol));
    }
    public Task<AssemblyGroundingQuote> GroundAsync(string id,string role,KnotRunRequest input,CancellationToken ct)=>
        GroundAsync(id,role,new AssemblyGroundingRunRequest(input.VideoSha256,input.CacheKey,input.ModelApiToken,
            input.ConfirmImageTransfer,input.ConfirmPaidCall,input.MaximumEstimatedCostUsd),ct);
    public async Task<AssemblyGroundingQuote> GroundAsync(string id,string role,AssemblyGroundingRunRequest input,CancellationToken ct)
    {
        var mode=AssemblyPolicy.Mode(input.Mode);
        await gate.WaitAsync(ct);
        try {
            var (video,_,_)=await Source(id,role,ct);
            var key=AssemblyPolicy.VideoGroundingKey(video.Sha256);
            if(input.VideoSha256!=video.Sha256 || input.CacheKey!=key)throw new ArgumentException("Video or grounding quote changed.");
            var root=VideoRoot(video.Sha256);Directory.CreateDirectory(root);
            var historical=Folder(AssemblyPolicy.Key(video.Sha256));Directory.CreateDirectory(historical);
            var legacy=Folder(AssemblyPolicy.GroundingKey(video.Sha256));Directory.CreateDirectory(legacy);
            await using var currentLock=new FileStream(Path.Combine(root,".lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            await using var historicalLock=new FileStream(Path.Combine(historical,".lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            await using var legacyLock=new FileStream(Path.Combine(legacy,".lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            var initial=await VideoQuote(id,role,ct,false,mode);
            if(initial.PriorAttempt)throw new InvalidOperationException("Previous grounding attempt requires reconciliation.");
            if(mode=="api-meta-cache" && initial.CacheHit) {
                if(initial.Evidence is {Derivation:not null} recovered) {
                    var recoveredPath=Path.Combine(legacy,"recovered-evidence.v2.json");
                    if(!File.Exists(recoveredPath))await Save(recoveredPath,recovered,ct);
                }
                return initial;
            }
            if(!input.ConfirmImageTransfer || !input.ConfirmVideoTransfer || !input.ConfirmPaidCall ||
                input.MaximumEstimatedCostUsd!=initial.MaximumEstimatedCostUsd ||
                !SamManualApiTokenPolicy.Accepts(input.ModelApiToken))
                throw new ArgumentException("Explicit full-video transfer, anchors and quoted maximum cost required.");
            if(!runtime.CanRunWithToken(input.ModelApiToken))throw new InvalidOperationException("Meta session gates blocked.");
            var pts=await runtime.ReadSourceFramePtsAsync(video,ct);
            if(pts.Length<3 || pts.Any(p=>!double.IsFinite(p)||p<0) ||
                pts.Zip(pts.Skip(1),(a,b)=>b<=a).Any(v=>v))
                throw new InvalidDataException("Invalid source frame timestamps.");
            var runId=Guid.NewGuid().ToString("N");var runRoot=Path.Combine(root,"runs",runId);
            Directory.CreateDirectory(runRoot);
            var mp4=Path.Combine(runRoot,"full-video.mp4");
            await runtime.CreateMp4Async(video.Path,mp4,ct);
            var submittedPts=await runtime.ReadFramePtsAsync(mp4,ct);
            if(submittedPts.Length!=pts.Length || submittedPts.Any(p=>!double.IsFinite(p)||p<0) ||
                pts.Where((p,i)=>Math.Abs(submittedPts[i]-p)>.005).Any() ||
                submittedPts.Zip(submittedPts.Skip(1),(a,b)=>b<=a).Any(v=>v))
                throw new InvalidDataException("MP4 remux changed source timing. No upload allowed.");
            var bytes=new FileInfo(mp4).Length;
            if(bytes is <1 or >1_073_741_824)throw new InvalidDataException("Full video exceeds Files API size limit.");
            var prepared=new List<KnotFrame>();var pixels=new List<byte[]>();
            foreach(var fraction in new[]{.1,.5,.9}) {
                var index=(int)Math.Round((pts.Length-1)*fraction);
                var destination=Path.Combine(runRoot,$"frame-{prepared.Count}.jpg");
                prepared.Add(await frames.ExtractAsync(video.Path,destination,index,pts[index],ct));
                pixels.Add(await File.ReadAllBytesAsync(destination,ct));
            }
            await ValidateFrames(runRoot,prepared.ToArray(),ct);
            var videoInput=new AssemblyVideoInput(video.Sha256,await FileHash(mp4,ct),bytes,"",pts.Length,PtsHash(pts));
            var attempt=new AssemblyVideoAttempt(video.Sha256,AssemblyPolicy.VideoRunKey(video.Sha256,runId),
                runId,AssemblyPolicy.Model,AssemblyPolicy.VideoGroundingProtocol,DateTimeOffset.UtcNow,
                initial.MaximumEstimatedCostUsd,prepared.ToArray(),videoInput);
            ct.ThrowIfCancellationRequested();runtime.ReserveOrThrow(input.ModelApiToken);
            await Save(Path.Combine(runRoot,"attempt.json"),attempt,CancellationToken.None);
            AssemblyVideoProviderResult result;
            using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(10));
            try {
                result=await transport.LocateVideoGroundingAsync(mp4,videoInput,prepared.ToArray(),pixels.ToArray(),
                    input.ModelApiToken,initial.MaximumEstimatedCostUsd,
                    uploaded=>Save(Path.Combine(runRoot,"video-input.json"),uploaded,CancellationToken.None),deadline.Token);
            } catch(AssemblyNoInferenceSentException) {
                await Save(Path.Combine(runRoot,"outcome.json"),new AssemblyGroundingOutcome("NO_INFERENCE_SENT",DateTimeOffset.UtcNow),CancellationToken.None);
                throw;
            }
            var response=result.Response;
            await File.WriteAllTextAsync(Path.Combine(runRoot,"response.json"),response.Raw,CancellationToken.None);
            AssemblyMetaTransport.ValidateReceipt(response.Raw,response.ProviderResponseId,response.InputTokens,response.OutputTokens);
            var proposal=AssemblyPolicy.ParseGrounding(AssemblyMetaTransport.OutputText(response.Raw),prepared.ToArray());
            var evidence=new AssemblyEvidence(attempt.Key,video.Sha256,AssemblyPolicy.Model,AssemblyPolicy.VideoGroundingProtocol,
                prepared.ToArray(),KnotPolicy.Hash(response.Raw),response.ProviderResponseId,DateTimeOffset.UtcNow,
                proposal,response.InputTokens,response.OutputTokens,result.VideoInput,runId);
            var scene=AssemblyPolicy.Ground(evidence);
            await Save(Path.Combine(runRoot,"evidence.json"),evidence,CancellationToken.None);
            return new(key,video.Sha256,initial.MaximumEstimatedCostUsd,false,false,scene is not null,
                AssemblyPolicy.GroundingStatus(evidence),scene,evidence,mode,false,EvidenceProtocol:evidence.Protocol);
        } finally {gate.Release();}
    }
}
