using System.Collections.Concurrent;
using System.Net;
namespace ProofLab.Api;

// Per-request context: no token, filename or uploaded-file identifier is exposed.
public static class AssemblyProgress {
    public sealed record Stage(string Key,string State,long ElapsedMs,double? Percent);
    public sealed record Snapshot(string RunId,DateTimeOffset StartedAtUtc,Stage[] Stages);
    private static readonly ConcurrentDictionary<string,Run> Runs=new();
    private static readonly AsyncLocal<Run?> Current=new();
    public sealed class Run:IDisposable {
        private readonly object sync=new();
        private readonly Dictionary<string,(DateTimeOffset Start,DateTimeOffset? End,string State,double? Percent)> stages=new();
        public readonly string Id=Guid.NewGuid().ToString("N");
        public readonly DateTimeOffset Started=DateTimeOffset.UtcNow;
        public void Start(string key) {lock(sync)stages[key]=(DateTimeOffset.UtcNow,null,"IN_PROGRESS",null);}
        public void Percent(string key,double percent) {lock(sync)if(stages.TryGetValue(key,out var s))stages[key]=s with {Percent=Math.Clamp(percent,0,100)};}
        public void Done(string key) {lock(sync)if(stages.TryGetValue(key,out var s))stages[key]=s with {End=DateTimeOffset.UtcNow,State="COMPLETED",Percent=100};}
        public Snapshot Read() {lock(sync)return new(Id,Started,stages.Select(s=>new Stage(s.Key,s.Value.State,(long)((s.Value.End??DateTimeOffset.UtcNow)-s.Value.Start).TotalMilliseconds,s.Value.Percent)).ToArray());}
        public void Dispose() {lock(sync)foreach(var key in stages.Keys.ToArray()) {var s=stages[key];if(s.State=="IN_PROGRESS")stages[key]=s with {End=DateTimeOffset.UtcNow,State="FAILED"};}Current.Value=null;}
    }
    public static Run Begin(string study,string role) {
        if(Runs.Count>128)foreach(var key in Runs.OrderBy(x=>x.Value.Started).Take(32).Select(x=>x.Key))Runs.TryRemove(key,out _);
        var run=new Run();Runs[study+":"+role]=run;Current.Value=run;return run;
    }
    public static Snapshot? Read(string study,string role)=>Runs.TryGetValue(study+":"+role,out var run)?run.Read():null;
    public static void Start(string key)=>Current.Value?.Start(key);
    public static void Done(string key)=>Current.Value?.Done(key);
    public static void Upload(long bytes,long total)=>Current.Value?.Percent("upload",100d*bytes/total);
    public sealed class UploadContent(Stream source,long length):HttpContent {
        protected override bool TryComputeLength(out long value){value=length;return true;}
        protected override Task SerializeToStreamAsync(Stream target,TransportContext? context)=>Copy(target,CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream target,TransportContext? context,CancellationToken ct)=>Copy(target,ct);
        private async Task Copy(Stream target,CancellationToken ct){var buffer=new byte[128*1024];long sent=0;int read;while((read=await source.ReadAsync(buffer,ct))>0){await target.WriteAsync(buffer.AsMemory(0,read),ct);sent+=read;Upload(sent,length);}}
    }
}
