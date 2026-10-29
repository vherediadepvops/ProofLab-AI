using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ProofLab.Api;

/// <summary>
/// Four frame-exact, lossless HEVC clips from ONE source decode and four
/// simultaneous NVIDIA encoders. This is a LOCAL test tool; never uploads.
/// All PTS and decoded frame hashes must match the original before any
/// output may be considered suitable for a future independently approved SAM
/// video call. It never touches the primary video's contents.
/// </summary>
public sealed class SamFourWayOnePassClips(
    VideoMediaDecoder decoder,string ffmpegExecutable)
{
    public async Task<SamFourWayClipManifest> BuildAsync(
        string sourceFile,string destinationDirectory,string expectedOriginalSha,
        CancellationToken ct)
    {
        var stopwatch=Stopwatch.StartNew();
        if(expectedOriginalSha.Length!=64 ||
           expectedOriginalSha.Any(x=>!Uri.IsHexDigit(x)) ||
           !File.Exists(sourceFile) || !File.Exists(ffmpegExecutable))
            throw new ArgumentException("Unsafe input to offline four-part clip preparation.");
        var source=Path.GetFullPath(sourceFile);
        var folder=Path.GetFullPath(destinationDirectory);
        if(!folder.Split(Path.DirectorySeparatorChar).Contains(".private",
                StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Four-part media must remain in ignored private storage.");
        string sourceSha;
        await using(var input=File.OpenRead(source))
            sourceSha=Convert.ToHexString(await SHA256.HashDataAsync(input,ct))
                .ToLowerInvariant();
        if(!sourceSha.Equals(expectedOriginalSha,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original SHA-256 mismatch.");
        var pts=await decoder.ReadVideoFramePtsAsync(source,ct);
        var plan=SamFourWayPlan.Create(pts);
        Directory.CreateDirectory(folder);
        var names=Enumerable.Range(0,4).Select(n=>
            "sam-parallel4-onepass-part-"+n.ToString(CultureInfo.InvariantCulture)+".mp4")
            .ToArray();
        foreach(var name in names)
            if(File.Exists(Path.Combine(folder,name)))
                throw new InvalidOperationException(
                    "An existing QA clip must not be overwritten.");
        var temporary=names.Select(n=>Path.Combine(folder,
            "."+Guid.NewGuid().ToString("N")+".tmp.mp4")).ToArray();

        try
        {
            var graph=new StringBuilder("[0:v]split=4[v0][v1][v2][v3]");
            for(var i=0;i<4;i++)
                graph.Append(";[v").Append(i).Append(']')
                    .Append(SamFourWayClipBuilder.SelectFramesFilter(plan.Windows[i]))
                    .Append(",setpts=PTS-STARTPTS[o").Append(i).Append(']');
            var args=new List<string>{
                "-hide_banner","-loglevel","error","-nostdin",
                "-protocol_whitelist","file,pipe","-threads","2",
                "-i",source,"-filter_complex",graph.ToString()
            };
            for(var i=0;i<4;i++)
            {
                args.AddRange(new[]{
                    "-map","[o"+i+"]","-fps_mode","vfr",
                    "-c:v","hevc_nvenc","-preset","p4",
                    "-tune","lossless","-rc","constqp","-qp","0",
                    "-bf","0","-pix_fmt","yuv420p",
                    "-tag:v","hvc1","-movflags","+faststart",
                    "-an","-n","-f","mp4",temporary[i]
                });
            }
            await SamFourWayClipBuilder.ExecuteAsync(
                ffmpegExecutable,args,ct);

            // Compare every output with a SINGLE original-source decode.
            // Re-decoding the entire source once per segment was a large
            // fraction of the old offline QA duration.
            var allOriginalFrameHashes=await SamFourWayClipBuilder.FrameHashesAsync(
                ffmpegExecutable,source,null,ct);
            if(allOriginalFrameHashes.Length!=plan.SourceFrames ||
               allOriginalFrameHashes.Any(hash=>hash.Length!=64))
                throw new InvalidDataException(
                    "Original source frame hashes are incomplete.");
            var receipt=new List<SamFourWayClipReceipt>();
            for(var i=0;i<4;i++)
            {
                var window=plan.Windows[i];
                var file=new FileInfo(temporary[i]);
                if(!file.Exists || file.Length is <10_000 or >SamFourWayClipBuilder.MaxClipBytes)
                    throw new InvalidDataException(
                        "Generated segment exceeds per-file Meta transfer bounds.");
                var observed=await decoder.ReadVideoFramePtsAsync(temporary[i],ct);
                if(observed.Length!=window.InputFrames)
                    throw new InvalidDataException("One-pass segment frame count changed.");
                var maxPtsDelta=0d;
                for(var frame=0;frame<window.InputFrames;frame++)
                {
                    var expected=pts[window.GlobalFrame(frame)]-window.InputFirstPts;
                    var delta=Math.Abs(observed[frame]-expected);
                    if(!double.IsFinite(delta) || delta>0.005)
                        throw new InvalidDataException(
                            "One-pass clip PTS no longer match the original.");
                    maxPtsDelta=Math.Max(maxPtsDelta,delta);
                }
                var originalHashes=allOriginalFrameHashes
                    .Skip(window.InputStart).Take(window.InputFrames).ToArray();
                var clipHashes=await SamFourWayClipBuilder.FrameHashesAsync(
                    ffmpegExecutable,temporary[i],null,ct);
                if(originalHashes.Length!=window.InputFrames ||
                   clipHashes.Length!=originalHashes.Length ||
                   originalHashes.Where((hash,index)=>
                       hash.Length!=64 || hash!=clipHashes[index]).Any())
                    throw new InvalidDataException(
                        "One-pass segment decoded pixels changed.");
                var hashesSha=Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(
                        string.Join("\n",originalHashes)))).ToLowerInvariant();
                string fileSha;
                await using(var stream=File.OpenRead(temporary[i]))
                    fileSha=Convert.ToHexString(
                        await SHA256.HashDataAsync(stream,ct)).ToLowerInvariant();
                receipt.Add(new(i,names[i],window.InputStart,
                    window.InputEndExclusive,window.InputFrames,fileSha,
                    hashesSha,maxPtsDelta,file.Length,0));
            }
            // Every clip validated BEFORE moving any one into the final path.
            for(var i=0;i<4;i++)
                File.Move(temporary[i],Path.Combine(folder,names[i]),overwrite:false);
            stopwatch.Stop();
            return new(expectedOriginalSha.ToLowerInvariant(),plan.SourcePtsSha256,
                plan.SourceFrames,plan.SubmittedFrames,plan.EstimatedSamCostUsd,
                receipt,stopwatch.ElapsedMilliseconds,
                "ONE_SOURCE_DECODE_FOUR_CONCURRENT_LOSSLESS_NVENC_HEVC_SINGLE_SOURCE_HASH_PASS");
        }
        finally
        {
            foreach(var filename in temporary)
                if(File.Exists(filename))File.Delete(filename);
        }
    }
}
