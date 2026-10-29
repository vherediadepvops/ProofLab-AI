using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ProofLab.Api;

/// <summary>
/// OFFLINE four-part HEVC MP4 generator for an owner-controlled QA recording.
/// Never sends data to a model or touches billing. Each output is validated
/// against the original source's frame PTS and decoded per-frame SHA-256.
/// The encoder's "lossless" name is NOT trusted without those checks.
/// </summary>
public sealed record SamFourWayClipReceipt(
    int Part, string RelativeFileName, int SourceStart, int SourceEndExclusive,
    int FrameCount, string Mp4Sha256, string FrameHashListSha256,
    double MaxAbsPtsErrorSeconds, long FileBytes, long EncodeElapsedMs);

public sealed record SamFourWayClipManifest(
    string OriginalVideoSha256, string SourcePtsSha256, int SourceFrames,
    int TotalSubmittedFrames, double EstimatedSamCostUsd,
    IReadOnlyList<SamFourWayClipReceipt> Clips,
    long EndToEndElapsedMs=0,
    string ProcessingMethod="FOUR_SERIAL_HEVC_NVENC_LOSSLESS");

public sealed class SamFourWayClipBuilder(
    VideoMediaDecoder decoder, string ffmpegExecutable)
{
    private static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
    internal const int MaxClipBytes=240_000_000;

    private static Process Start(string executable,IEnumerable<string> args)
    {
        var psi=new ProcessStartInfo(executable) {
            UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardOutput=true,RedirectStandardError=true,
            RedirectStandardInput=true
        };
        foreach(var arg in args)psi.ArgumentList.Add(arg);
        return new Process {StartInfo=psi};
    }

    internal static async Task<string> ExecuteAsync(
        string binary,IEnumerable<string> args,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        using var process=Start(binary,args);
        process.Start();
        try
        {
            var stdout=process.StandardOutput.ReadToEndAsync(deadline.Token);
            var stderr=process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            var error=await stderr;
            var text=await stdout;
            if(process.ExitCode!=0 || text.Length>2_000_000)
                throw new InvalidDataException(
                    "Local FFmpeg decoding or lossless encoding failed, code="+
                    process.ExitCode.ToString(Inv)+"; no clip is eligible for Meta.");
            _=error; // never include source paths in logs or exceptions
            return text;
        }
        finally
        {
            if(!process.HasExited)
                try {process.Kill(entireProcessTree:true);} catch {}
        }
    }

    public static string SelectFramesFilter(SamFourWayWindow window) =>
        "select=between(n\\,"+
        window.InputStart.ToString(Inv)+"\\,"+
        (window.InputEndExclusive-1).ToString(Inv)+")";

    internal static async Task<string[]> FrameHashesAsync(
        string ffmpeg,string path,SamFourWayWindow? sourceWindow,
        CancellationToken ct)
    {
        var args=new List<string> {
            "-hide_banner","-loglevel","error","-nostdin",
            "-protocol_whitelist","file,pipe","-threads","2",
            "-i",path,"-map","0:v:0","-an","-sn","-dn"
        };
        if(sourceWindow is not null)
        {
            args.Add("-vf");args.Add(SelectFramesFilter(sourceWindow));
            args.Add("-frames:v");args.Add(sourceWindow.InputFrames.ToString(Inv));
        }
        args.AddRange(new[]{
            "-fps_mode","vfr","-pix_fmt","yuv420p",
            "-f","framehash","-hash","SHA256","pipe:1"
        });
        var raw=await ExecuteAsync(ffmpeg,args,ct);
        return raw.Split('\n',StringSplitOptions.RemoveEmptyEntries)
            .Where(line=>line.StartsWith("0,",StringComparison.Ordinal))
            .Select(line=>line.Split(',')[^1].Trim().ToLowerInvariant()).ToArray();
    }

    public async Task<SamFourWayClipManifest> BuildAsync(
        string sourceFile,string destinationDirectory,string expectedOriginalSha,
        CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(ffmpegExecutable) ||
           !File.Exists(ffmpegExecutable) || !File.Exists(sourceFile) ||
           expectedOriginalSha.Length!=64 ||
           expectedOriginalSha.Any(c=>!Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid local clip source or tool.");
        var normalizedSource=Path.GetFullPath(sourceFile);
        var normalizedTarget=Path.GetFullPath(destinationDirectory);
        if(normalizedTarget.Equals(Path.GetDirectoryName(normalizedSource),
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Clip destination must differ from source directory.");
        string inputSha;
        await using(var originalStream=File.OpenRead(normalizedSource))
            inputSha=Convert.ToHexString(await SHA256.HashDataAsync(
                originalStream,ct)).ToLowerInvariant();
        if(!inputSha.Equals(expectedOriginalSha,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original source SHA-256 mismatch.");

        var pts=await decoder.ReadVideoFramePtsAsync(normalizedSource,ct);
        var plan=SamFourWayPlan.Create(pts);
        Directory.CreateDirectory(normalizedTarget);
        var result=new List<SamFourWayClipReceipt>(4);

        for(var part=0;part<4;part++)
        {
            var window=plan.Windows[part];
            var filename="sam-parallel4-part-"+part.ToString(Inv)+".mp4";
            var output=Path.Combine(normalizedTarget,filename);
            if(File.Exists(output))
                throw new InvalidOperationException(
                    "A SAM QA MP4 already exists. Preserve this experiment; do not overwrite.");
            var temporary=Path.Combine(normalizedTarget,
                "."+Guid.NewGuid().ToString("N")+".tmp.mp4");
            var timer=Stopwatch.StartNew();
            try
            {
                var args=new[]{
                    "-hide_banner","-loglevel","error","-nostdin",
                    "-protocol_whitelist","file,pipe","-threads","2",
                    "-i",normalizedSource,"-map","0:v:0","-an","-sn","-dn",
                    "-vf",SelectFramesFilter(window)+",setpts=PTS-STARTPTS",
                    "-fps_mode","vfr","-c:v","hevc_nvenc","-preset","p4",
                    "-tune","lossless","-rc","constqp","-qp","0",
                    "-bf","0","-pix_fmt","yuv420p",
                    "-tag:v","hvc1","-movflags","+faststart",
                    "-n","-f","mp4",temporary
                };
                await ExecuteAsync(ffmpegExecutable,args,ct);
                var bytes=new FileInfo(temporary).Length;
                if(bytes is <10000 or >MaxClipBytes)
                    throw new InvalidDataException(
                        "QA clip outside permitted SAM upload size; no upload allowed.");
                var actualPts=await decoder.ReadVideoFramePtsAsync(temporary,ct);
                if(actualPts.Length!=window.InputFrames)
                    throw new InvalidDataException("QA clip frame count is not exact.");
                var error=0d;
                for(var i=0;i<actualPts.Length;i++)
                {
                    var delta=Math.Abs(actualPts[i]-
                        (pts[window.GlobalFrame(i)]-window.InputFirstPts));
                    if(!double.IsFinite(delta) || delta>0.005)
                        throw new InvalidDataException(
                            "QA clip PTS are not aligned with original source.");
                    error=Math.Max(error,delta);
                }

                var expected=await FrameHashesAsync(
                    ffmpegExecutable,normalizedSource,window,ct);
                var actual=await FrameHashesAsync(
                    ffmpegExecutable,temporary,null,ct);
                if(expected.Length!=window.InputFrames ||
                   actual.Length!=expected.Length ||
                   expected.Where((hash,i)=>
                        hash.Length!=64 || hash!=actual[i]).Any())
                    throw new InvalidDataException(
                        "QA clip decoded pixels changed from original.");
                var hashes=Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(string.Join("\n",expected)))).ToLowerInvariant();
                string clipSha;
                await using(var clipStream=File.OpenRead(temporary))
                    clipSha=Convert.ToHexString(
                        await SHA256.HashDataAsync(clipStream,ct))
                        .ToLowerInvariant();
                timer.Stop();
                File.Move(temporary,output,overwrite:false);
                result.Add(new(part,filename,window.InputStart,
                    window.InputEndExclusive,window.InputFrames,clipSha,
                    hashes,error,bytes,timer.ElapsedMilliseconds));
            }
            finally
            {
                if(File.Exists(temporary))File.Delete(temporary);
            }
        }
        return new(expectedOriginalSha.ToLowerInvariant(),
            plan.SourcePtsSha256,plan.SourceFrames,plan.SubmittedFrames,
            plan.EstimatedSamCostUsd,result);
    }
}
