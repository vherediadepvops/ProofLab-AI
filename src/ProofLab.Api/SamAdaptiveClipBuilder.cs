using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProofLab.Api;

/// <summary>
/// Prepare N EXACT source-frame clips by transcoding ONE private intermediate
/// with a keyframe every TWO frames, then stream-copying windows starting at
/// verified intra keyframes. No provider uploads and no local source edits.
/// Every decoded frame is compared byte-for-byte (SHA256) to the original.
/// A failed/incomplete clip disqualifies the entire batch from paid transport.
/// </summary>
public sealed class SamAdaptiveClipBuilder(
    VideoMediaDecoder decoder,IConfiguration configuration) : ISamAdaptiveClipBuilder
{
    public const string Protocol="SAM_ADAPTIVE_INTRA2_SOURCE_PTS_PIXEL_SHA_V1";
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
    private sealed record FrameGeometry(int Width,int Height,int RotationDegrees);

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

    private sealed record PacketTimeline(double[] Pts,bool[] KeyFrames);

    // The generated intermediate uses -bf 0, i.e. packet/decode/presentation
    // order are identical. Packet-level FFprobe avoids repeatedly decoding
    // gigabyte-scale lossless video merely to extract timestamp metadata.
    private async Task<PacketTimeline> ReadIntraPacketTimeline(
        string path,CancellationToken ct)
    {
        var json=await Call(Ffprobe,new[]{
            "-v","error","-protocol_whitelist","file,pipe",
            "-select_streams","v:0",
            "-show_entries","packet=pts_time,flags",
            "-of","json","-i",path
        },1_000_000,ct);
        using var document=JsonDocument.Parse(json);
        var packets=document.RootElement.GetProperty("packets");
        if(packets.ValueKind!=JsonValueKind.Array || packets.GetArrayLength()>2500)
            throw new InvalidDataException("Unsafe packet timeline.");
        var times=new List<double>();
        var keys=new List<bool>();
        foreach(var packet in packets.EnumerateArray())
        {
            var raw=packet.GetProperty("pts_time").GetString();
            if(!double.TryParse(raw,NumberStyles.Float,Inv,out var time) ||
               !double.IsFinite(time) || time<0 ||
               (times.Count>0 && time<=times[^1]))
                throw new InvalidDataException("Generated video has unsafe packet PTS.");
            times.Add(time);
            keys.Add(packet.GetProperty("flags").GetString()?.Contains('K')==true);
        }
        if(times.Count<50 || Math.Abs(times[0])>0.005)
            throw new InvalidDataException("Generated MP4 has bad initial PTS.");
        return new(times.ToArray(),keys.ToArray());
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
        var actualPts=await decoder.ReadVideoFramePtsAsync(source.Path,ct);
        if(plan.VerifySourcePts(actualPts)!=ptsSha)
            throw new InvalidDataException("Original video PTS are not verified.");
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
        var intra=Path.Combine(folder,"source-intra2.mp4");
        await Call(Ffmpeg,new[]{
            "-hide_banner","-loglevel","error","-nostdin",
            "-protocol_whitelist","file,pipe","-threads","2",
            "-i",source.Path,"-map","0:v:0","-an","-sn","-dn",
            "-fps_mode","vfr","-c:v","hevc_nvenc",
            "-preset","p4","-tune","lossless",
            "-rc","constqp","-qp","0","-bf","0","-g","2",
            "-pix_fmt","yuv420p","-tag:v","hvc1",
            "-movflags","+faststart","-n","-f","mp4",intra
        },10_000,ct);
        var intermediate=new FileInfo(intra);
        if(!intermediate.Exists || intermediate.Length is <10000 or >2_000_000_000)
            throw new InvalidDataException("Intermediate lossless video invalid or too large.");
        var intermediateGeometry=await ReadGeometry(intra,ct);
        if(intermediateGeometry!=originalGeometry)
            throw new InvalidDataException(
                "HEVC intermediate changed source dimensions or orientation.");
        var intraInfo=await ReadIntraPacketTimeline(intra,ct);
        var intraPts=intraInfo.Pts;
        if(intraPts.Length!=originalPts.Count)
            throw new InvalidDataException("Lossless intermediate lost source frames.");
        for(var i=0;i<intraPts.Length;i++)
            if(Math.Abs(intraPts[i]-originalPts[i])>0.005)
                throw new InvalidDataException("Lossless intermediate shifted source PTS.");

        // Verify keyframe alignment before copying: a start on any non-keyframe
        // could introduce silent pre-roll and corrupt global SAM frame IDs.
        if(plan.Windows.Any(w=>w.InputStart%2!=0 ||
            !intraInfo.KeyFrames[w.InputStart]))
            throw new InvalidDataException("A requested segment does not start on an intra keyframe.");

        var sourceHashes=await FrameHashes(source.Path,ct);
        var encodedHashes=await FrameHashes(intra,ct);
        if(sourceHashes.Length!=originalPts.Count ||
           !sourceHashes.SequenceEqual(encodedHashes))
            throw new InvalidDataException("Lossless intermediate changed original pixels.");
        var clips=new List<SamAdaptiveClip>(plan.PartCount);
        foreach(var w in plan.Windows)
        {
            var name="part-"+w.Part.ToString("D2",Inv)+".mp4";
            var path=Path.Combine(folder,name);
            await Call(Ffmpeg,new[]{
                "-hide_banner","-loglevel","error","-nostdin",
                "-protocol_whitelist","file,pipe","-threads","2",
                "-ss",intraPts[w.InputStart].ToString("R",Inv),
                "-i",intra,"-map","0:v:0",
                "-an","-sn","-dn",
                "-frames:v",w.InputFrames.ToString(Inv),
                "-c:v","copy","-tag:v","hvc1",
                "-avoid_negative_ts","make_zero",
                "-movflags","+faststart",
                "-n","-f","mp4",path
            },10_000,ct);
            var info=new FileInfo(path);
            if(!info.Exists || info.Length is <10_000 or >240_000_000)
                throw new InvalidDataException("Segment is invalid or exceeds Meta file size.");
            var clipGeometry=await ReadGeometry(path,ct);
            if(clipGeometry!=originalGeometry)
                throw new InvalidDataException(
                    "The fragment changed source dimensions or rotation.");
            var clipInfo=await ReadIntraPacketTimeline(path,ct);
            var clipPts=clipInfo.Pts;
            if(clipPts.Length!=w.InputFrames)
                throw new InvalidDataException("Stream-copy produced wrong segment frame count.");
            var worst=0d;
            for(var i=0;i<clipPts.Length;i++)
            {
                var delta=Math.Abs(clipPts[i]-
                    (originalPts[w.GlobalFrame(i)]-originalPts[w.InputStart]));
                if(!double.IsFinite(delta) || delta>0.005)
                    throw new InvalidDataException("Segment PTS changed from original.");
                worst=Math.Max(worst,delta);
            }
            var observedHashes=await FrameHashes(path,ct);
            var expectedHashes=sourceHashes.AsSpan(w.InputStart,w.InputFrames).ToArray();
            if(!observedHashes.SequenceEqual(expectedHashes))
                throw new InvalidDataException("Segment pixels do not match source frames.");
            string sha;
            await using(var stream=File.OpenRead(path))
                sha=Convert.ToHexString(await SHA256.HashDataAsync(stream,ct))
                    .ToLowerInvariant();
            var hashesSha=Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(string.Join("\n",expectedHashes)))).ToLowerInvariant();
            clips.Add(new(w.Part,w.InputStart,w.InputEndExclusive,
                w.CoreStart,w.CoreEndExclusive,w.InputFrames,
                name,info.Length,sha,hashesSha,worst,w.BilledFrames));
        }
        timer.Stop();
        var manifest=new SamAdaptiveClipManifest(originalSha,ptsSha,
            originalPts.Count,plan.SubmittedFrames,plan.BilledFrames,
            plan.EstimatedSamCostUsd,Protocol,clips,timer.ElapsedMilliseconds);
        var json=JsonSerializer.Serialize(manifest,new JsonSerializerOptions{
            WriteIndented=true
        });
        await using(var fs=new FileStream(Path.Combine(folder,"clip-manifest.json"),
            FileMode.CreateNew,FileAccess.Write,FileShare.None,
            65_536,FileOptions.WriteThrough))
        {
            var bytes=Encoding.UTF8.GetBytes(json);
            await fs.WriteAsync(bytes,ct);
            await fs.FlushAsync(ct);
        }
        // Prevent untrusted/large extra files from crossing into Meta transfer
        // (source-intra2 is local only and never uploaded).
        return manifest;
    }
}
