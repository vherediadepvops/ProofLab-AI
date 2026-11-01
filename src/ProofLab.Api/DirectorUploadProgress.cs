using System;
using System.Collections.Generic;
using System.Linq;

namespace ProofLab.Api;

public sealed class DirectorUploadConflictException : InvalidOperationException
{
    public DirectorUploadConflictException() : base("An upload with this identifier already exists.") { }
}
public sealed record DirectorUploadPhaseSnapshot(string Key,string Status,long ElapsedMs);
public sealed record DirectorUploadProgressSnapshot(string UploadId,string StudyId,string Role,
    string State,string Phase,long ReceivedBytes,long TotalBytes,DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,IReadOnlyList<DirectorUploadPhaseSnapshot> Phases,long ElapsedMs);

/// <summary>Transient telemetry; no director gate, I/O, SQLite or provider calls.</summary>
public sealed class DirectorUploadProgress
{
    private static readonly string[] PhaseKeys=[
        "queue","validate","receive_and_hash","probe","local_tracking","spectral","poster","persist","complete"];
    private const int MaximumTerminalSnapshots=128;
    private static readonly TimeSpan TerminalRetention=TimeSpan.FromHours(2);
    private readonly object sync=new();
    private readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
    private readonly TimeProvider clock;
    private long terminalSequence;
    public DirectorUploadProgress(TimeProvider? clock=null) {this.clock=clock??TimeProvider.System;}

    private sealed class PhaseEntry
    {
        public string Status="PENDING";
        public long StartedTick;
        public long ElapsedMs;
    }
    private sealed class Entry(string uploadId,string studyId,string role,long totalBytes,
        DateTimeOffset startedAtUtc,long startedTick)
    {
        public readonly string UploadId=uploadId;
        public readonly string StudyId=studyId;
        public readonly string Role=role;
        public readonly long TotalBytes=totalBytes;
        public readonly DateTimeOffset StartedAtUtc=startedAtUtc;
        public readonly long StartedTick=startedTick;
        public readonly PhaseEntry[] Phases=PhaseKeys.Select(_=>new PhaseEntry()).ToArray();
        public DateTimeOffset UpdatedAtUtc=startedAtUtc;
        public string State="RUNNING";
        public int CurrentPhase;
        public long ReceivedBytes;
        public long? FinishedTick;
        public long FinishedOrder;
    }
    private static string NormalizeId(string value)
    {
        if(value is not {Length:32} || value.Any(c=>c is not
           (>= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')))
            throw new ArgumentException("Invalid upload identity.");
        return value.ToLowerInvariant();
    }
    private static void ValidateRole(string role)
    {
        if(role is not ("baseline" or "followup"))throw new ArgumentException("Invalid video role.");
    }
    public void Begin(string studyId,string role,string uploadId,long totalBytes)
    {
        studyId=NormalizeId(studyId);uploadId=NormalizeId(uploadId);ValidateRole(role);
        if(totalBytes<=0)throw new ArgumentException("Upload length must be positive.");
        lock(sync) {
            if(entries.ContainsKey(uploadId))throw new DirectorUploadConflictException();
            var now=clock.GetUtcNow();PruneLocked(now);
            var tick=clock.GetTimestamp();
            var entry=new Entry(uploadId,studyId,role,totalBytes,now,tick);
            entry.Phases[0].Status="RUNNING";entry.Phases[0].StartedTick=tick;
            entries.Add(uploadId,entry);
        }
    }
    public void Phase(string uploadId,string phase)
    {
        uploadId=NormalizeId(uploadId);var index=Array.IndexOf(PhaseKeys,phase);
        if(index<0)throw new ArgumentException("Invalid upload phase.");
        if(phase=="complete"){Complete(uploadId);return;}
        lock(sync) {
            if(!entries.TryGetValue(uploadId,out var entry) || entry.State!="RUNNING" || index<=entry.CurrentPhase)return;
            var tick=clock.GetTimestamp();CloseCurrent(entry,"COMPLETED",tick);
            entry.CurrentPhase=index;entry.Phases[index].Status="RUNNING";entry.Phases[index].StartedTick=tick;
            entry.UpdatedAtUtc=clock.GetUtcNow();
        }
    }
    public void Received(string uploadId,long bytes)
    {
        uploadId=NormalizeId(uploadId);
        if(bytes<0)throw new ArgumentException("Received byte count cannot be negative.");
        lock(sync) {
            if(!entries.TryGetValue(uploadId,out var entry) || entry.State!="RUNNING")return;
            if(bytes>entry.TotalBytes)throw new ArgumentException("Received byte count exceeds upload length.");
            if(bytes<=entry.ReceivedBytes)return;
            entry.ReceivedBytes=bytes;entry.UpdatedAtUtc=clock.GetUtcNow();
        }
    }
    public void Complete(string uploadId)
    {
        uploadId=NormalizeId(uploadId);
        lock(sync) {
            if(!entries.TryGetValue(uploadId,out var entry) || entry.State!="RUNNING")return;
            var tick=clock.GetTimestamp();CloseCurrent(entry,"COMPLETED",tick);
            entry.CurrentPhase=PhaseKeys.Length-1;entry.Phases[entry.CurrentPhase].Status="COMPLETED";
            Finish(entry,"COMPLETED",tick);PruneLocked(entry.UpdatedAtUtc);
        }
    }
    public void Fail(string uploadId,bool cancelled)
    {
        uploadId=NormalizeId(uploadId);
        lock(sync) {
            if(!entries.TryGetValue(uploadId,out var entry) || entry.State!="RUNNING")return;
            var tick=clock.GetTimestamp();var state=cancelled?"CANCELLED":"FAILED";
            CloseCurrent(entry,state,tick);Finish(entry,state,tick);PruneLocked(entry.UpdatedAtUtc);
        }
    }
    public DirectorUploadProgressSnapshot? Read(string studyId,string role,string uploadId)
    {
        studyId=NormalizeId(studyId);uploadId=NormalizeId(uploadId);ValidateRole(role);
        lock(sync) {
            PruneLocked(clock.GetUtcNow());
            if(!entries.TryGetValue(uploadId,out var entry) || entry.StudyId!=studyId || entry.Role!=role)return null;
            var tick=entry.FinishedTick??clock.GetTimestamp();
            var phases=entry.Phases.Select((p,i)=>new DirectorUploadPhaseSnapshot(PhaseKeys[i],p.Status,
                p.Status=="RUNNING"?Elapsed(p.StartedTick,tick):p.ElapsedMs)).ToArray();
            return new(entry.UploadId,entry.StudyId,entry.Role,entry.State,PhaseKeys[entry.CurrentPhase],
                entry.ReceivedBytes,entry.TotalBytes,entry.StartedAtUtc,entry.UpdatedAtUtc,phases,
                Elapsed(entry.StartedTick,tick));
        }
    }
    private long Elapsed(long start,long end)=>Math.Max(0,(long)clock.GetElapsedTime(start,end).TotalMilliseconds);
    private void CloseCurrent(Entry entry,string status,long tick)
    {
        var phase=entry.Phases[entry.CurrentPhase];if(phase.Status!="RUNNING")return;
        phase.ElapsedMs=Elapsed(phase.StartedTick,tick);phase.Status=status;
    }
    private void Finish(Entry entry,string state,long tick)
    {
        entry.State=state;entry.FinishedTick=tick;entry.FinishedOrder=++terminalSequence;entry.UpdatedAtUtc=clock.GetUtcNow();
    }
    private void PruneLocked(DateTimeOffset now)
    {
        var terminal=entries.Values.Where(e=>e.State!="RUNNING").OrderBy(e=>e.FinishedOrder).ToArray();
        foreach(var entry in terminal)if(now-entry.UpdatedAtUtc>=TerminalRetention)entries.Remove(entry.UploadId);
        var retained=terminal.Where(e=>entries.ContainsKey(e.UploadId)).ToArray();
        for(var i=0;i<retained.Length-MaximumTerminalSnapshots;i++)entries.Remove(retained[i].UploadId);
    }
}
