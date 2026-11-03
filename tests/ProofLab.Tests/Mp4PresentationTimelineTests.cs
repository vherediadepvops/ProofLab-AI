using System.Buffers.Binary;
using System.Text;
using ProofLab.Api;

namespace ProofLab.Tests;

public sealed class Mp4PresentationTimelineTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "prooflab-timeline-" + Guid.NewGuid().ToString("N"));
    public Mp4PresentationTimelineTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private string File(string name, byte[] data) { string p = Path.Combine(root, name); System.IO.File.WriteAllBytes(p, data); return p; }
    private static byte[] Join(params byte[][] items) => items.SelectMany(x => x).ToArray();
    private static byte[] U32(uint n) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, n); return b; }
    private static byte[] U64(ulong n) { var b = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, n); return b; }
    private static byte[] Box(string type, params byte[][] children)
    {
        byte[] data = Join(children); return Join(U32((uint)data.Length + 8), Encoding.ASCII.GetBytes(type), data);
    }
    private static byte[] Full(int version, params byte[][] fields) => Join(new byte[] { (byte)version, 0, 0, 0 }, Join(fields));
    private static byte[] Header(string type, int version, uint duration, uint scale = 600)
    {
        byte[] dates = new byte[version == 1 ? 16 : 8];
        byte[] d = version == 1 ? U64(duration) : U32(duration);
        return Box(type, Full(version, dates, type == "tkhd" ? Join(U32(1), U32(0), d) : Join(U32(scale), d)));
    }
    private static byte[] Movie(bool original, int version = 0, uint sampleSize = 4,
        bool hasEdit = true, uint edits = 1, uint rate = 65536, uint runCount = 3, bool audioFirst = false)
    {
        byte[] edit = Box("edts", Box("elst", Full(version, U32(edits),
            version == 1 ? U64(original ? 299u : 300u) : U32(original ? 299u : 300u),
            version == 1 ? U64(original ? 10u : 20u) : U32(original ? 10u : 20u), U32(rate))));
        byte[] samples = Box("stbl",
            Box("stts", Full(0, U32(1), U32(runCount), U32(100))),
            Box("ctts", Full(1, U32(1), U32(3), U32(original ? unchecked((uint)-10) : 0))),
            Box("stsz", Full(0, U32(sampleSize), U32(3))));
        byte[] track = Box("trak", Header("tkhd", version, original ? 299u : 300u), hasEdit ? edit : [],
            Box("mdia", Header("mdhd", version, original ? 310u : 320u),
                Box("hdlr", Full(0, U32(0), Encoding.ASCII.GetBytes("vide"))), Box("minf", samples)));
        byte[] audio = audioFirst ? Box("trak", Box("mdia", Box("hdlr", Full(0, U32(0), Encoding.ASCII.GetBytes("soun"))))) : [];
        return Join(Box("ftyp", Encoding.ASCII.GetBytes("isom")),
            Box("moov", Header("mvhd", version, original ? 299u : 300u), audio, track),
            Box("mdat", Enumerable.Repeat((byte)(original ? 71 : 83), 12).ToArray()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RestoresSignedCtsAndEditWithoutChangingMediaOrSource(int version)
    {
        byte[] source = Movie(true, version), expected = Movie(true, version);
        Array.Fill(expected, (byte)83, expected.Length - 12, 12);
        string a = File("source.mov", source), b = File("copy.mp4", Movie(false, version));
        var plan = Assert.IsType<Mp4PresentationTimeline>(Mp4PresentationTimeline.TryRead(a));
        Assert.Equal(600u, plan.MovieTimescale); Assert.Equal(600u, plan.MediaTimescale);
        Assert.True(plan.Restore(b));
        Assert.Equal(expected, System.IO.File.ReadAllBytes(b));
        Assert.Equal(source, System.IO.File.ReadAllBytes(a));
    }

    [Fact] public void SelectsVideoWhenAudioIsFirst()
    {
        var plan = Mp4PresentationTimeline.TryRead(File("source.mov", Movie(true, audioFirst: true)));
        string dest = File("copy.mp4", Movie(false));
        Assert.NotNull(plan); Assert.True(plan.Restore(dest));
    }
    [Fact] public void MismatchedSamplesLeavePrivateCopyUntouched()
    {
        var plan = Mp4PresentationTimeline.TryRead(File("source.mov", Movie(true)));
        byte[] before = Movie(false, sampleSize: 5); string dest = File("copy.mp4", before);
        Assert.False(plan!.Restore(dest)); Assert.Equal(before, System.IO.File.ReadAllBytes(dest));
    }
    [Theory]
    [InlineData(false, 1, 65536)]
    [InlineData(true, 2, 65536)]
    [InlineData(true, 1, 32768)]
    public void UnsupportedEditsKeepExistingVerifiedRemuxPath(bool hasEdit, uint count, uint rate)
    {
        Assert.Null(Mp4PresentationTimeline.TryRead(File("source.mov", Movie(true, hasEdit: hasEdit, edits: count, rate: rate))));
    }
    [Fact] public void UnsupportedContainerDoesNotActivatePatching()
    {
        Assert.Null(Mp4PresentationTimeline.TryRead(File("source.avi", Encoding.ASCII.GetBytes("RIFFfakeAVI "))));
    }
    [Fact] public void RejectsCorruptAtomBounds()
    {
        byte[] data = Movie(true); BinaryPrimitives.WriteUInt32BigEndian(data, uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => Mp4PresentationTimeline.TryRead(File("source.mov", data)));
    }
    [Fact] public void RejectsTimingTableThatDoesNotCoverItsSamples()
    {
        Assert.Throws<InvalidDataException>(() => Mp4PresentationTimeline.TryRead(File("source.mov", Movie(true, runCount: 2))));
    }
    [Fact] public void InvalidOutputIsNeverPartiallyPatched()
    {
        var plan = Mp4PresentationTimeline.TryRead(File("source.mov", Movie(true)));
        byte[] before = Movie(false, runCount: 2); string dest = File("copy.mp4", before);
        Assert.Throws<InvalidDataException>(() => plan!.Restore(dest));
        Assert.Equal(before, System.IO.File.ReadAllBytes(dest));
    }
}
