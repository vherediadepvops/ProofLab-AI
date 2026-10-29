namespace ProofLab.Api;

/// <summary>
/// Offline-only bounded concurrency study. Progress means that a local fake
/// segment worker advanced; it is NOT proof of a Meta SAM inference.
/// Production integration must independently record per-part provider attempt,
/// quoted budget, actual response SHA and receipt, with manual consent.
/// </summary>
public sealed record SamAdaptiveOfflineProgress(
    int Part,string Stage,int Events,int Deltas,int OutputChars);

public sealed record SamAdaptiveChunkOutcome<T>(
    int Part,string State,T? Value);

public sealed record SamAdaptiveBatchOutcome<T>(
    IReadOnlyList<SamAdaptiveChunkOutcome<T>> Parts,int MaxInFlight)
{
    public int Completed => Parts.Count(x=>x.State=="OFFLINE_COMPLETED");
    public int Failed => Parts.Count(x=>x.State=="OFFLINE_FAILED_NO_AUTOMATIC_RETRY");
    public int Skipped => Parts.Count(x=>x.State=="OFFLINE_SKIPPED_AFTER_FAILURE");
    public int Cancelled => Parts.Count(x=>x.State=="OFFLINE_CANCELLED");
    public bool AllCompleted => Completed==Parts.Count && Failed==0 &&
        Skipped==0 && Cancelled==0;
}

public static class SamAdaptiveOfflineScheduler
{
    public static async Task<SamAdaptiveBatchOutcome<T>> RunAsync<T>(
        SamAdaptiveSegmentPlan plan,
        Func<SamAdaptiveWindow,Func<SamVideoSseProgress,ValueTask>,
            CancellationToken,Task<T>> fakeOfflineWorker,
        Func<SamAdaptiveOfflineProgress,ValueTask>? onProgress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(fakeOfflineWorker);
        using var slots=new SemaphoreSlim(plan.MaxInFlight);
        var aborted=0;
        var records=new SamAdaptiveChunkOutcome<T>[plan.PartCount];
        async Task Execute(SamAdaptiveWindow part)
        {
            var acquired=false;
            try
            {
                await slots.WaitAsync(ct);
                acquired=true;
                if(Volatile.Read(ref aborted)!=0)
                {
                    records[part.Part]=new(part.Part,
                        "OFFLINE_SKIPPED_AFTER_FAILURE",default);
                    return;
                }
                async ValueTask Stream(SamVideoSseProgress progress)
                {
                    if(onProgress is not null)
                        await onProgress(new(part.Part,
                            "OFFLINE_"+progress.Stage,progress.Events,
                            progress.DeltaEvents,progress.OutputChars));
                }
                await Stream(new("PART_STARTED",0,0,0));
                var value=await fakeOfflineWorker(part,Stream,ct);
                if(value is null)
                    throw new InvalidDataException("Offline segment returned no data.");
                records[part.Part]=new(part.Part,"OFFLINE_COMPLETED",value);
                await Stream(new("PART_COMPLETED",0,0,0));
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested)
            {
                Interlocked.Exchange(ref aborted,1);
                records[part.Part]=new(part.Part,"OFFLINE_CANCELLED",default);
            }
            catch
            {
                // Do not expose provider/output/secret exception text.
                // Already admitted tasks must complete; queued tasks stop.
                Interlocked.Exchange(ref aborted,1);
                records[part.Part]=new(part.Part,
                    "OFFLINE_FAILED_NO_AUTOMATIC_RETRY",default);
                if(onProgress is not null)
                {
                    try { await onProgress(new(part.Part,
                        "OFFLINE_PART_FAILED_NO_AUTOMATIC_RETRY",0,0,0)); }
                    catch { /* Progress notification must not retry work. */ }
                }
            }
            finally
            {
                if(acquired)slots.Release();
            }
        }
        await Task.WhenAll(plan.Windows.Select(Execute));
        return new(records,plan.MaxInFlight);
    }
}
