using System.Buffers.Binary;
using System.Text;

namespace ProofLab.Api;

/// <summary>
/// Retains a single, normal-rate MOV edit and its original sample timing after
/// stream copy. FFmpeg can otherwise normalize signed CTS offsets and expose
/// a trailing sample excluded by the source edit. No media bytes are rewritten.
/// Unsupported layouts use the existing remux path and its mandatory PTS check.
/// </summary>
public sealed class Mp4PresentationTimeline
{
    private const int MaximumMovieBytes = 8 * 1024 * 1024;
    private const int MaximumSamples = 15_000;
    private readonly byte[] edit, decodeTimes;
    private readonly byte[]? compositionTimes;
    private readonly uint[] sampleSizes;
    private readonly ulong editDuration, mediaDuration;
    public uint MovieTimescale { get; }
    public uint MediaTimescale { get; }

    private Mp4PresentationTimeline(Movie movie, Layout layout, uint[] sizes)
    {
        MovieTimescale = Timescale(movie.Data, layout.MovieHeader);
        MediaTimescale = Timescale(movie.Data, layout.MediaHeader);
        edit = Payload(movie.Data, layout.Edit!.Value);
        decodeTimes = Payload(movie.Data, layout.DecodeTimes);
        compositionTimes = layout.CompositionTimes is {} c ? Payload(movie.Data, c) : null;
        sampleSizes = sizes;
        editDuration = ReadNumber(edit, 8, edit[0] == 1 ? 8 : 4);
        mediaDuration = Duration(movie.Data, layout.MediaHeader, false);
    }

    public static Mp4PresentationTimeline? TryRead(string source)
    {
        using var stream = File.OpenRead(source);
        var movie = ReadMovie(stream);
        if (movie is null) return null;
        var layout = ReadLayout(movie);
        if (layout is null || layout.Edit is not {} edit) return null;
        var raw = Payload(movie.Data, edit);
        if (raw.Length < 8 || raw[0] > 1 || U32(raw, 4) != 1) return null;
        int width = raw[0] == 1 ? 8 : 4;
        if (raw.Length != 8 + width * 2 + 4 || ReadNumber(raw, 8, width) == 0) return null;
        // Empty edits, multiple edits and non-unit playback rates need a separate
        // mapping; never guess a replacement timeline for those sources.
        if ((raw[8 + width] & 0x80) != 0 || U32(raw, 8 + width * 2) != 0x00010000) return null;
        uint[] sizes = SampleSizes(movie.Data, layout.Sizes);
        ValidateRuns(Payload(movie.Data, layout.DecodeTimes), sizes.Length);
        if (layout.CompositionTimes is {} c) ValidateRuns(Payload(movie.Data, c), sizes.Length);
        return new Mp4PresentationTimeline(movie, layout, sizes);
    }

    public bool Restore(string privateMp4)
    {
        using var stream = new FileStream(privateMp4, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var movie = ReadMovie(stream);
        if (movie is null) return false;
        var layout = ReadLayout(movie);
        if (layout is null || layout.TrackCount != 1 || layout.Edit is not {} targetEdit ||
            MovieTimescale != Timescale(movie.Data, layout.MovieHeader) ||
            MediaTimescale != Timescale(movie.Data, layout.MediaHeader) ||
            !sampleSizes.SequenceEqual(SampleSizes(movie.Data, layout.Sizes))) return false;
        // Fixed-size replacements keep every chunk offset and media byte intact.
        // Validate the complete plan before modifying even the in-memory movie.
        if (targetEdit.Length != edit.Length || layout.DecodeTimes.Length != decodeTimes.Length ||
            (compositionTimes is null) != (layout.CompositionTimes is null) ||
            (compositionTimes is not null && layout.CompositionTimes!.Value.Length != compositionTimes.Length)) return false;
        ValidateRuns(Payload(movie.Data, layout.DecodeTimes), sampleSizes.Length);
        if (layout.CompositionTimes is {} targetCtts) ValidateRuns(Payload(movie.Data, targetCtts), sampleSizes.Length);
        edit.CopyTo(movie.Data, targetEdit.Data);
        decodeTimes.CopyTo(movie.Data, layout.DecodeTimes.Data);
        if (compositionTimes is not null) compositionTimes.CopyTo(movie.Data, layout.CompositionTimes!.Value.Data);
        WriteDuration(movie.Data, layout.MovieHeader, false, editDuration);
        WriteDuration(movie.Data, layout.TrackHeader, true, editDuration);
        WriteDuration(movie.Data, layout.MediaHeader, false, mediaDuration);
        stream.Position = movie.Offset;
        stream.Write(movie.Data);
        stream.Flush(true);
        return true;
    }

    private sealed record Movie(byte[] Data, long Offset);
    private readonly record struct Box(string Type, int Data, int End) { public int Length => End - Data; }
    private sealed record Layout(Box MovieHeader, Box TrackHeader, Box MediaHeader,
        Box DecodeTimes, Box? CompositionTimes, Box Sizes, Box? Edit, int TrackCount);

    private static Movie? ReadMovie(Stream stream)
    {
        if (stream.Length < 8) return null;
        Span<byte> header = stackalloc byte[16];
        stream.Position = 0; stream.ReadExactly(header[..8]);
        string first = Encoding.ASCII.GetString(header[4..8]);
        if (first is not ("ftyp" or "moov" or "wide" or "free" or "mdat" or "skip")) return null;
        Movie? found = null;
        long position = 0;
        for (int count = 0; position < stream.Length; count++)
        {
            if (count >= 4096 || stream.Length - position < 8) throw Invalid();
            stream.Position = position; stream.ReadExactly(header[..8]);
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(header);
            string type = Encoding.ASCII.GetString(header[4..8]);
            int headerSize = 8;
            if (size == 1) { stream.ReadExactly(header[8..16]); size = BinaryPrimitives.ReadUInt64BigEndian(header[8..]); headerSize = 16; }
            if (size == 0) size = (ulong)(stream.Length - position);
            if (size < (ulong)headerSize || size > (ulong)(stream.Length - position)) throw Invalid();
            if (type == "moof") return null;
            if (type == "moov")
            {
                if (found is not null || size - (uint)headerSize > MaximumMovieBytes) throw Invalid();
                byte[] data = new byte[(int)size - headerSize]; stream.ReadExactly(data);
                found = new Movie(data, position + headerSize);
            }
            position += (long)size;
        }
        return found;
    }

    private static Layout? ReadLayout(Movie movie)
    {
        var top = Children(movie.Data, 0, movie.Data.Length);
        if (top.Any(b => b.Type == "mvex")) return null;
        Box mvhd = Required(top, "mvhd");
        var tracks = top.Where(b => b.Type == "trak").ToArray();
        foreach (var track in tracks)
        {
            var fields = Children(movie.Data, track.Data, track.End);
            Box mdia = Required(fields, "mdia");
            var media = Children(movie.Data, mdia.Data, mdia.End);
            Box handler = Required(media, "hdlr");
            if (handler.Length < 12) throw Invalid();
            if (Encoding.ASCII.GetString(movie.Data, handler.Data + 8, 4) != "vide") continue;
            Box minf = Required(media, "minf");
            Box stbl = Required(Children(movie.Data, minf.Data, minf.End), "stbl");
            var samples = Children(movie.Data, stbl.Data, stbl.End);
            Box? edts = Optional(fields, "edts");
            return new Layout(mvhd, Required(fields, "tkhd"), Required(media, "mdhd"),
                Required(samples, "stts"), Optional(samples, "ctts"), Required(samples, "stsz"),
                edts is {} e ? Optional(Children(movie.Data, e.Data, e.End), "elst") : null, tracks.Length);
        }
        return null;
    }

    private static Box[] Children(byte[] bytes, int start, int end)
    {
        var result = new List<Box>();
        while (start < end)
        {
            if (end - start < 8 || result.Count >= 4096) throw Invalid();
            ulong size = U32(bytes, start); int header = 8;
            string type = Encoding.ASCII.GetString(bytes, start + 4, 4);
            if (size == 1) { if (end - start < 16) throw Invalid(); size = ReadNumber(bytes, start + 8, 8); header = 16; }
            if (size == 0) size = (uint)(end - start);
            if (size < (uint)header || size > (uint)(end - start)) throw Invalid();
            result.Add(new Box(type, start + header, start + (int)size)); start += (int)size;
        }
        return result.ToArray();
    }
    private static Box? Optional(Box[] boxes, string type)
    {
        var matches = boxes.Where(b => b.Type == type).ToArray();
        if (matches.Length > 1) throw Invalid();
        return matches.Length == 0 ? null : matches[0];
    }
    private static Box Required(Box[] boxes, string type) => Optional(boxes, type) ?? throw Invalid();
    private static byte[] Payload(byte[] bytes, Box box) => bytes[box.Data..box.End];
    private static uint Timescale(byte[] bytes, Box box)
    {
        byte[] raw = Payload(bytes, box); int version = Version(raw);
        uint scale = U32(raw, version == 1 ? 20 : 12);
        if (scale == 0 || scale > int.MaxValue) throw Invalid();
        return scale;
    }
    private static int Version(byte[] raw) => raw.Length > 0 && raw[0] <= 1 ? raw[0] : throw Invalid();
    private static ulong Duration(byte[] bytes, Box box, bool track)
    {
        byte[] raw = Payload(bytes, box); int version = Version(raw);
        return ReadNumber(raw, (track ? 20 : 16) + (version == 1 ? 8 : 0), version == 1 ? 8 : 4);
    }
    private static void WriteDuration(byte[] bytes, Box box, bool track, ulong value)
    {
        byte[] raw = Payload(bytes, box); int version = Version(raw);
        int offset = (track ? 20 : 16) + (version == 1 ? 8 : 0), width = version == 1 ? 8 : 4;
        if (offset + width > raw.Length || (width == 4 && value > uint.MaxValue)) throw Invalid();
        var span = bytes.AsSpan(box.Data + offset, width);
        if (width == 8) BinaryPrimitives.WriteUInt64BigEndian(span, value);
        else BinaryPrimitives.WriteUInt32BigEndian(span, (uint)value);
    }
    private static uint[] SampleSizes(byte[] bytes, Box box)
    {
        byte[] raw = Payload(bytes, box);
        if (Version(raw) != 0) throw Invalid();
        uint size = U32(raw, 4), count = U32(raw, 8);
        if (count is 0 or > MaximumSamples || raw.Length != (size == 0 ? 12 + count * 4 : 12)) throw Invalid();
        return Enumerable.Range(0, (int)count).Select(i => size == 0 ? U32(raw, 12 + i * 4) : size).ToArray();
    }
    private static void ValidateRuns(byte[] raw, int samples)
    {
        _ = Version(raw); uint count = U32(raw, 4);
        if (count is 0 or > MaximumSamples || raw.Length != 8 + count * 8) throw Invalid();
        ulong total = 0;
        for (int i = 0; i < count; i++) { uint n = U32(raw, 8 + i * 8); if (n == 0) throw Invalid(); total += n; }
        if (total != (uint)samples) throw Invalid();
    }
    private static uint U32(byte[] bytes, int offset) => (uint)ReadNumber(bytes, offset, 4);
    private static ulong ReadNumber(byte[] bytes, int offset, int width)
    {
        if (offset < 0 || bytes.Length - offset < width) throw Invalid();
        return width == 4 ? BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)) :
            BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(offset, 8));
    }
    private static InvalidDataException Invalid() => new("Invalid or unsupported MP4 presentation metadata.");
}
