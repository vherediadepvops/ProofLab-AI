namespace ProofLab.Api;

/// <summary>
/// Streaming progress is tagged by one of the four independently running
/// parts. Counters report provider SSE events, not predicted progress.
/// </summary>
public sealed record SamFourWayProgress(
    int Part,string Stage,int ProviderEvents,
    int ProviderDeltas,int ProviderOutputChars);

/// <summary>
/// Pure offline orchestration contract. Production integration MUST:
///  1. authenticate one Spark scene ONCE for the full source video,
///  2. split four exact video frame windows and reverify source PTS,
///  3. approve and durably reserve ALL FOUR possible billable requests,
///  4. use HTTPS SAM SSE for each, without automatic retries,
///  5. independently validate each provider response and source identity,
///  6. pass the decoded mask observations back to this coordinator.
///
/// This class itself contains NO FFmpeg/HTTP/keys or spending calls.
/// It waits for all started parts, even if one fails. Partial success is
/// NEVER presented as a completed scientific track.
/// </summary>
public static class SamFourWayStreaming
{
    public static async Task<SamFourWayMerge> RunAsync(
        SamFourWayPlan plan,IReadOnlyList<double> sourcePts,
        string sourceSha,string sparkSceneKey,
        Func<SamFourWayWindow,Func<SamVideoSseProgress,ValueTask>,
             CancellationToken,Task<SamFourWaySegment>> runOne,
        Func<SamFourWayProgress,ValueTask>? onProgress,
        CancellationToken ct,bool offlineFixture=false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(runOne);
        // Task.WhenAll concurrently awaits all four, without canceling
        // already started requests just because another chunk fails.
        // A caller must record a distinct attempt/receipt for every part.
        async Task<SamFourWaySegment> RunPart(SamFourWayWindow part)
        {
            async ValueTask Publish(SamVideoSseProgress p)
            {
                if(onProgress is not null)
                    await onProgress(new SamFourWayProgress(
                        part.Part,p.Stage,p.Events,p.DeltaEvents,p.OutputChars));
            }
            await Publish(new(offlineFixture
                ? "OFFLINE_CHUNK_STARTED" : "CHUNK_STARTED",0,0,0));
            try
            {
                var result=await runOne(part,Publish,ct);
                await Publish(new(result.ProviderCompleted
                    ? (offlineFixture?"OFFLINE_CHUNK_REPLAY_COMPLETED":"CHUNK_PROVIDER_COMPLETED")
                    : "CHUNK_INCOMPLETE",0,0,0));
                return result;
            }
            catch
            {
                await Publish(new("CHUNK_FAILED_NO_AUTOMATIC_RETRY",0,0,0));
                throw;
            }
        }
        var outcomes=await Task.WhenAll(plan.Windows.Select(RunPart));
        return SamFourWayStitcher.Merge(
            plan,sourcePts,sourceSha,sparkSceneKey,outcomes);
    }
}

public sealed record SamFourWayComparison(
    int ComparedFrames,int MissingReferenceFrames,
    int MissingCandidateFrames,double MeanCentroidErrorPx,
    double P95CentroidErrorPx,bool SeamsVerified);

/// <summary>
/// Offline comparison with an authentic FULL-run reference. Reports only
/// image-space centroid errors, not metre-scale calibration or accuracy.
/// A completed 4-way result with unverified seams stays inconclusive.
/// </summary>
public static class SamFourWayBenchmark
{
    public static SamFourWayComparison Compare(
        SamFourWayMerge candidate,
        IReadOnlyList<SamFourWayFrame> fullReference)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(fullReference);
        var full=new Dictionary<int,SamFourWayFrame>();
        foreach(var row in fullReference)
            if(!full.TryAdd(row.FrameIndex,row) ||
               row.FrameIndex<0 || row.FrameIndex>=candidate.SourceFrames)
                throw new InvalidDataException("Invalid full-run reference frame identity.");
        var observed=candidate.ObservedFrames.ToDictionary(x=>x.FrameIndex);
        var d=new List<double>();
        foreach(var (frame,original) in full)
        {
            if(!observed.TryGetValue(frame,out var test))continue;
            if(Math.Abs(test.SourcePts-original.SourcePts)>0.005)
                throw new InvalidDataException("Full-run comparison PTS mismatch.");
            var dx=test.CentroidX-original.CentroidX;
            var dy=test.CentroidY-original.CentroidY;
            d.Add(Math.Sqrt(dx*dx+dy*dy));
        }
        d.Sort();
        return new(d.Count,full.Keys.Count(i=>!observed.ContainsKey(i)),
            observed.Keys.Count(i=>!full.ContainsKey(i)),
            d.Count==0 ? double.NaN : d.Average(),
            d.Count==0 ? double.NaN :
                d[Math.Clamp((int)Math.Ceiling(d.Count*0.95)-1,0,d.Count-1)],
            candidate.EverySeamVerified);
    }
}
