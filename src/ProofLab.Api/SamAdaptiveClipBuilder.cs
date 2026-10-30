using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProofLab.Api;

/// <summary>
/// New plans copy original compressed packets into at most three keyframe-aligned clips.
/// No re-encoding, provider uploads or original-file edits.
/// Every decoded frame is compared byte-for-byte (SHA256) to the original.
/// A failed/incomplete clip disqualifies the entire batch from paid transport.
/// </summary>
public sealed partial class SamAdaptiveClipBuilder(
    VideoMediaDecoder decoder,IConfiguration configuration) : ISamAdaptiveClipBuilder
{
    public const string Protocol="SAM_ADAPTIVE_PLANNED_IDR_ORIENTED_PTS_PIXEL_SHA_V2";
    private static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
    private readonly string directory = ResolveDirectory(configuration);

    private static string ResolveDirectory(IConfiguration config)
    {
        var location=config["Media:FfmpegDirectory"];
        if(string.IsNullOrWhiteSpace(location))
            location=Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
                "Microsoft","WinGet","Links");
        return location;
    }
    private string Ffmpeg => Path.Combine(directory,
        OperatingSystem.IsWindows()?"ffmpeg.exe":"ffmpeg");
    private string Ffprobe => Path.Combine(directory,
        OperatingSystem.IsWindows()?"ffprobe.exe":"ffprobe");

    private static Process NewProcess(string binary,IEnumerable<string> args)
    {
        var info=new ProcessStartInfo(binary){
            UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardOutput=true,RedirectStandardError=true,
            RedirectStandardInput=true
        };
        foreach(var arg in args)info.ArgumentList.Add(arg);
        return new Process {StartInfo=info};
    }
    private static async Task<string> Call(string executable,IEnumerable<string> args,
        int maxStdout,CancellationToken ct)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(6));
        using var process=NewProcess(executable,args);
        process.Start();
        try
        {
            var output=process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error=process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            _=await error; // no private paths or potentially sensitive media logs
            var data=await output;
            if(process.ExitCode!=0 || data.Length>maxStdout)
                throw new InvalidDataException(
                    "Offline frame-exact media preparation failed; no upload permitted.");
            return data;
        }
        finally
        {
            if(!process.HasExited)try{process.Kill(entireProcessTree:true);}catch{}
        }
    }
    private sealed record FrameGeometry(int Width,int Height,int RotationDegrees)
    {
        public SamOrientedFrameGeometry Display=>SamOrientedFrameGeometry.FromStored(Width,Height,RotationDegrees);
    }

    private async Task<FrameGeometry> ReadGeometry(string filename,CancellationToken ct)
    {
        var raw=await Call(Ffprobe,new[]{
            "-v","error","-protocol_whitelist","file,pipe",
            "-select_streams","v:0",
            "-show_entries","stream=width,height:stream_tags=rotate:stream_side_data=rotation",
            "-of","json","-i",filename
        },30_000,ct);
        using var doc=JsonDocument.Parse(raw);
        var streams=doc.RootElement.GetProperty("streams");
        if(streams.GetArrayLength()!=1)
            throw new InvalidDataException("Missing or ambiguous video stream geometry.");
        var stream=streams[0];
        var width=stream.GetProperty("width").GetInt32();
        var height=stream.GetProperty("height").GetInt32();
        if(width is <2 or >8192 || height is <2 or >8192)
            throw new InvalidDataException("Video dimensions are unsafe.");
        var rotation=0;
        if(stream.TryGetProperty("tags",out var tags) &&
           tags.TryGetProperty("rotate",out var value))
        {
            if(!int.TryParse(value.GetString(),out var fromTag))
                throw new InvalidDataException("Unrecognized video rotation tag.");
            rotation=fromTag;
        }
        if(stream.TryGetProperty("side_data_list",out var extra))
        {
            foreach(var entry in extra.EnumerateArray())
            {
                if(entry.TryGetProperty("rotation",out var angle))
                {
                    var side=angle.GetInt32();
                    if(rotation!=0 && side!=rotation)
                        throw new InvalidDataException("Conflicting video rotation metadata.");
                    rotation=side;
                }
            }
        }
        return new(width,height,rotation);
    }

    private async Task<string[]> FrameHashes(
        string path,CancellationToken ct)
    {
        var raw=await Call(Ffmpeg,new[]{
            "-hide_banner","-loglevel","error","-nostdin",
            "-protocol_whitelist","file,pipe","-threads","2",
            "-i",path,"-map","0:v:0","-an","-sn","-dn",
            "-pix_fmt","yuv420p","-f","framehash",
            "-hash","SHA256","pipe:1"
        },600_000,ct);
        return raw.Split('\n',StringSplitOptions.RemoveEmptyEntries)
            .Where(line=>line.StartsWith("0,",StringComparison.Ordinal))
            .Select(line=>line.Split(',')[^1].Trim().ToLowerInvariant()).ToArray();
    }

    public async Task<SamAdaptiveClipManifest> PrepareAsync(
        SamVideoSource source,IReadOnlyList<double> originalPts,
        SamAdaptiveSegmentPlan plan,string freshRunDirectory,CancellationToken ct)
    {
        if(!decoder.Available || !File.Exists(Ffmpeg) || !File.Exists(Ffprobe) ||
           !File.Exists(source.Path) || plan.PartCount<1 ||
           !plan.IntraKeyframeAligned ||
           !Path.GetFullPath(freshRunDirectory).Split(Path.DirectorySeparatorChar)
               .Contains(".private",StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Unsafe or unavailable local SAM frame preparation.");
        var timer=Stopwatch.StartNew();
        var ptsSha=plan.VerifySourcePts(originalPts);
        var originalGeometry=await ReadGeometry(source.Path,ct);
        var originalDisplay=originalGeometry.Display;
        // Original PTS are already verified and cached by VideoTimingCatalog.
        string originalSha;
        await using(var original=File.OpenRead(source.Path))
            originalSha=Convert.ToHexString(await SHA256.HashDataAsync(original,ct))
                .ToLowerInvariant();
        if(originalSha!=source.Sha256.ToLowerInvariant())
            throw new InvalidDataException("Original video SHA-256 changed.");
        var folder=Path.GetFullPath(freshRunDirectory);
        if(Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new InvalidOperationException(
                "Adaptive output is not a fresh directory. No overwrites or paid retry.");
        Directory.CreateDirectory(folder);
        if(!plan.OriginalPacketCopy) {
            if(plan.PartCount==1)return await PrepareContinuousAsync(source,originalPts,plan,folder,originalSha,originalDisplay,ptsSha,timer,ct);
            throw new InvalidDataException("New segmented runs must use original keyframes, never a lossless transcode.");
        }
        return await PrepareNativeAsync(source,originalPts,plan,folder,originalSha,originalDisplay,ptsSha,timer,ct);
    }

    // A continuous recovery needs no artificial IDRs or large lossless encode:
    // copy the original video stream, retain rotation, verify every pixel and PTS.
    private async Task<SamAdaptiveClipManifest> PrepareContinuousAsync(
        SamVideoSource source,IReadOnlyList<double> originalPts,SamAdaptiveSegmentPlan plan,
        string folder,string originalSha,SamOrientedFrameGeometry display,string ptsSha,
        Stopwatch timer,CancellationToken ct)
    {
        var window=plan.Windows.Single();
        if(window.InputStart!=0 || window.CoreStart!=0 ||
           window.InputEndExclusive!=originalPts.Count || window.CoreEndExclusive!=originalPts.Count)
            throw new InvalidDataException("Continuous recovery must cover the whole original.");
        var path=Path.Combine(folder,"part-00.mp4");
        await decoder.CreateSamCompatibleMp4Async(source.Path,path,ct);
        if((await ReadGeometry(path,ct)).Display!=display)
            throw new InvalidDataException("Continuous copy changed displayed dimensions.");
        var pts=await decoder.ReadVideoFramePtsAsync(path,ct);
        if(pts.Length!=originalPts.Count)
            throw new InvalidDataException("Continuous copy lost source frames.");
        var worst=0d;
        for(var i=0;i<pts.Length;i++) {
            var delta=Math.Abs(pts[i]-originalPts[i]);
            if(!double.IsFinite(delta) || delta>0.005)
                throw new InvalidDataException("Continuous copy changed source timestamps.");
            worst=Math.Max(worst,delta);
        }
        var hashes=await Task.WhenAll(FrameHashes(source.Path,ct),FrameHashes(path,ct));
        if(hashes[0].Length!=originalPts.Count || !hashes[0].SequenceEqual(hashes[1]))
            throw new InvalidDataException("Continuous copy changed original pixels.");
        string clipSha;
        await using(var stream=File.OpenRead(path))
            clipSha=Convert.ToHexString(await SHA256.HashDataAsync(stream,ct)).ToLowerInvariant();
        var frameHash=Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join("\n",hashes[0])))).ToLowerInvariant();
        var clip=new SamAdaptiveClip(0,0,originalPts.Count,0,originalPts.Count,
            originalPts.Count,"part-00.mp4",new FileInfo(path).Length,clipSha,
            frameHash,worst,plan.BilledFrames);
        var manifest=new SamAdaptiveClipManifest(originalSha,ptsSha,originalPts.Count,
            plan.SubmittedFrames,plan.BilledFrames,plan.EstimatedSamCostUsd,
            "SAM_CONTINUOUS_ORIGINAL_REMUX_PTS_PIXEL_SHA_V1",[clip],timer.ElapsedMilliseconds);
        await File.WriteAllTextAsync(Path.Combine(folder,"clip-manifest.json"),
            JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=true}),ct);
        return manifest;
    }
}
