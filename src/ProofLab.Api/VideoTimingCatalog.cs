using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace ProofLab.Api;

public interface IVideoTimingReader {
    Task<int?> ReadVideoFrameCountAsync(string path,CancellationToken ct);
    Task<double[]> ReadVideoFramePtsAsync(string path,CancellationToken ct);
}
public sealed record VideoFrameCount(int Frames,string Source,string PtsSha256="");

/// <summary>Two bounded local workers, shared in-flight work, immutable-source
/// stamp checks and private persistent results. Quoting never calls a provider.</summary>
public sealed class VideoTimingCatalog(IWebHostEnvironment environment,IVideoTimingReader reader,
    IHostApplicationLifetime lifetime,ILogger<VideoTimingCatalog> logger)
{
    private const string Version="NATIVE_VIDEO_TIMING_V1";
    private sealed record Stamp(long Bytes,long Modified);
    private sealed record Record(string Version,string Sha,long Bytes,long Modified,int Count,
        string Source,double[]? Pts,string Digest);
    private readonly SemaphoreSlim workers=new(2,2);
    private readonly SemaphoreSlim probes=new(2,2);
    private readonly ConcurrentDictionary<string,Lazy<Task<VideoFrameCount>>> counts=new();
    private readonly ConcurrentDictionary<string,Lazy<Task<double[]>>> timelines=new();
    private readonly ConcurrentDictionary<string,byte> warmed=new();
    private static Stamp Snapshot(string path) {
        var f=new FileInfo(path);if(!f.Exists)throw new FileNotFoundException("Original video missing.");
        return new(f.Length,f.LastWriteTimeUtc.Ticks);
    }
    private static string Identity(SamVideoSource source,Stamp stamp) {
        if(source.Sha256.Length!=64 || source.Sha256.Any(c=>!Uri.IsHexDigit(c)))throw new ArgumentException("Invalid video SHA.");
        return source.Sha256+":"+stamp.Bytes+":"+stamp.Modified;
    }
    private string PathFor(string sha,string kind)=>Path.Combine(environment.ContentRootPath,
        ".private","physics","video-timing",sha.ToLowerInvariant()+"."+kind+".json");
    private static string Digest(double[] pts)=>SamVideoArchive.Hash(string.Join("\n",pts.Select(t=>
        t.ToString("R",System.Globalization.CultureInfo.InvariantCulture))));
    private static bool Valid(double[] p)=>p.Length is >=50 and <=15000 && Math.Abs(p[0])<=.005 &&
        p.All(v=>double.IsFinite(v)&&v>=0) && p.Zip(p.Skip(1),(a,b)=>b>a).All(v=>v);
    private async Task<Record?> Read(SamVideoSource source,Stamp stamp,string kind) {
        var path=PathFor(source.Sha256,kind);
        if(!File.Exists(path))return null;
        try {
            if(new FileInfo(path).Length>2_000_000)return null;
            var r=JsonSerializer.Deserialize<Record>(await File.ReadAllTextAsync(path,lifetime.ApplicationStopping));
            if(r is null || r.Version!=Version || r.Sha!=source.Sha256 || r.Bytes!=stamp.Bytes ||
                r.Modified!=stamp.Modified || r.Count <1)return null;
            if(kind=="pts" && (r.Pts is null || r.Count!=r.Pts.Length || !Valid(r.Pts) || r.Digest!=Digest(r.Pts)))return null;
            return r;
        }catch(JsonException){return null;}catch(IOException){return null;}
    }
    private async Task Save(SamVideoSource source,Stamp stamp,string kind,int count,string origin,double[]? pts) {
        if(Snapshot(source.Path)!=stamp)throw new InvalidDataException("Video changed during timing preparation.");
        var path=PathFor(source.Sha256,kind);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        var data=new Record(Version,source.Sha256,stamp.Bytes,stamp.Modified,count,origin,pts,pts is null?"":Digest(pts));
        try {
            await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(data),lifetime.ApplicationStopping);
            File.Move(temporary,path,true);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private void Warm(SamVideoSource source,Stamp stamp,int count) {
        // No need to decode a video outside the current single-call frame budget.
        if(count is <50 or >2500 || !warmed.TryAdd(Identity(source,stamp),0))return;
        _=WarmCore(source);
    }
    private async Task WarmCore(SamVideoSource source) {
        try{await PtsAsync(source,lifetime.ApplicationStopping);}
        catch(Exception ex) when(ex is not OutOfMemoryException) {
            logger.LogDebug("Background video timing unavailable; explicit processing will verify again.");
        }
    }
    public async Task<VideoFrameCount> CountAsync(SamVideoSource source,CancellationToken ct) {
        var stamp=Snapshot(source.Path);var key=Identity(source,stamp);
        var exact=await Read(source,stamp,"pts");
        if(exact is not null)return new(exact.Count,exact.Source,exact.Digest);
        var task=counts.GetOrAdd(key,_=>new Lazy<Task<VideoFrameCount>>(()=>CountCore(source,stamp)));
        try{return await task.Value.WaitAsync(ct);}
        catch {if(task.IsValueCreated && task.Value.IsCompleted && !task.Value.IsCompletedSuccessfully)
                counts.TryRemove(key,out _);throw;}
        finally{Trim();}
    }
    private async Task<VideoFrameCount> CountCore(SamVideoSource source,Stamp stamp) {
        var cached=await Read(source,stamp,"count");
        if(cached is not null){Warm(source,stamp,cached.Count);return new(cached.Count,cached.Source);}
        int? count;
        await probes.WaitAsync(lifetime.ApplicationStopping);
        try{count=await reader.ReadVideoFrameCountAsync(source.Path,lifetime.ApplicationStopping);}
        finally{probes.Release();}
        if(count is >0) {
            const string origin="CONTAINER_DECLARED_FRAME_COUNT_ESTIMATE";
            await Save(source,stamp,"count",count.Value,origin,null);
            Warm(source,stamp,count.Value);return new(count.Value,origin);
        }
        var pts=await PtsAsync(source,lifetime.ApplicationStopping);
        return new(pts.Length,"FFPROBE_BEST_EFFORT_SOURCE_FRAME_PTS_NORMALIZED_TO_FIRST",Digest(pts));
    }
    public async Task<double[]> PtsAsync(SamVideoSource source,CancellationToken ct) {
        var stamp=Snapshot(source.Path);var key=Identity(source,stamp);
        var task=timelines.GetOrAdd(key,_=>new Lazy<Task<double[]>>(()=>PtsCore(source,stamp)));
        try{return (await task.Value.WaitAsync(ct)).ToArray();}
        catch {if(task.IsValueCreated && task.Value.IsCompleted && !task.Value.IsCompletedSuccessfully)
                timelines.TryRemove(key,out _);throw;}
        finally{Trim();}
    }
    private async Task<double[]> PtsCore(SamVideoSource source,Stamp stamp) {
        var cached=await Read(source,stamp,"pts");if(cached?.Pts is not null)return cached.Pts;
        await workers.WaitAsync(lifetime.ApplicationStopping);
        try {
            // The background path has not performed the submit-time SHA check.
            // Only a verified original may populate a persistent scientific timeline.
            await using(var stream=File.OpenRead(source.Path)) {
                var hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,lifetime.ApplicationStopping)).ToLowerInvariant();
                if(hash!=source.Sha256)throw new InvalidDataException("Original SHA mismatch.");
            }
            var pts=await reader.ReadVideoFramePtsAsync(source.Path,lifetime.ApplicationStopping);
            if(!Valid(pts))throw new InvalidDataException("Invalid original frame times.");
            await Save(source,stamp,"pts",pts.Length,"FFPROBE_BEST_EFFORT_SOURCE_FRAME_PTS_NORMALIZED_TO_FIRST",pts);
            return pts;
        }finally{workers.Release();}
    }
    private void Trim() {
        if(counts.Count>64)foreach(var pair in counts.Where(p=>p.Value.IsValueCreated&&p.Value.Value.IsCompleted).Take(32))counts.TryRemove(pair.Key,out _);
        if(timelines.Count>64)foreach(var pair in timelines.Where(p=>p.Value.IsValueCreated&&p.Value.Value.IsCompleted).Take(32))timelines.TryRemove(pair.Key,out _);
        if(warmed.Count>256)warmed.Clear();
    }
}
