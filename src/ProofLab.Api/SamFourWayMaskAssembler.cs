using System.Text;
using System.Text.Json;

namespace ProofLab.Api;

public sealed record SamFourWayRawPart(
    int Part,string SourceVideoSha256,string SceneKey,
    bool ProviderCompleted,string RawResponseJson);

public sealed record SamFourWayMaskAssembly(
    string Provenance,int MaskFrames,string WireOutputText,
    string SourcePtsSha256,int SourceFrames,bool SeamsVerified);

/// <summary>
/// Rebase and concatenate the COMPLETE, unmodified SAM box/mask payloads
/// after independent provider evidence and seam checks have succeeded.
/// The only edits to a mask line are global frame index and canonical ID.
/// This is NOT a provider-generated monolithic response, and must NOT be
/// indexed as one without a new multi-part archival provenance protocol.
/// </summary>
public static class SamFourWayMaskAssembler
{
    private sealed record FrameMaskLine(int Frame,string Id,string Payload);

    private static JsonDocument ParseEnvelope(string source)
    {
        try{return JsonDocument.Parse(source);}
        catch(JsonException ex){
            throw new InvalidDataException("Malformed SAM video response JSON.",ex);
        }
    }

    public static string OutputWire(string responseJson)
    {
        if(responseJson.Length is <10 or >85_000_000)
            throw new InvalidDataException("SAM response exceeds safe bounds.");
        using var doc=ParseEnvelope(responseJson);
        var root=doc.RootElement;
        if(root.ValueKind!=JsonValueKind.Object ||
           !root.TryGetProperty("model",out var model) ||
           model.ValueKind!=JsonValueKind.String || model.GetString()!="sam-3.1" ||
           !root.TryGetProperty("status",out var status) ||
           status.ValueKind!=JsonValueKind.String || status.GetString()!="completed" ||
           !root.TryGetProperty("output",out var output) ||
           output.ValueKind!=JsonValueKind.Array)
            throw new InvalidDataException("SAM provider output is not completed.");
        var lines=new List<string>();
        foreach(var message in output.EnumerateArray())
        {
            if(!message.TryGetProperty("content",out var content) ||
               content.ValueKind!=JsonValueKind.Array)continue;
            foreach(var part in content.EnumerateArray())
                if(part.TryGetProperty("type",out var type) &&
                   type.ValueKind==JsonValueKind.String &&
                   type.GetString()=="output_text" &&
                   part.TryGetProperty("text",out var text) &&
                   text.ValueKind==JsonValueKind.String)
                    lines.Add(text.GetString()??"");
        }
        return string.Join("\n",lines);
    }

    private static IReadOnlyList<FrameMaskLine> Frames(string output)
    {
        var result=new List<FrameMaskLine>();
        var previous=-1;
        foreach(var input in output.Split('\n'))
        {
            var line=input.TrimEnd('\r');
            if(line.Length==0)continue;
            if(line.Length>200_000)
                throw new InvalidDataException("SAM frame record too large.");
            if(line[0]!='<' || line.Length<15)
                throw new InvalidDataException("Malformed SAM frame header.");
            var end=line.IndexOf("f>",StringComparison.Ordinal);
            if(end<2 || end>8 ||
               !int.TryParse(line.AsSpan(1,end-1),out var frame) ||
               frame<=previous || frame>=15000)
                throw new InvalidDataException("Unordered SAM frame record.");
            var payload=line.IndexOf("<|box;",end+2,StringComparison.Ordinal);
            if(payload<=end+2 || !line.EndsWith("|>",StringComparison.Ordinal) ||
               !line.Contains("<|mask;",StringComparison.Ordinal))
                throw new InvalidDataException("SAM box/mask record missing.");
            var id=line[(end+2)..payload];
            if(id.Length is <1 or >128 ||
               !id.All(c=>char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                throw new InvalidDataException("Invalid SAM target ID.");
            result.Add(new(frame,id,line[payload..]));
            previous=frame;
        }
        return result;
    }

    private static void VerifyWireBox(
        string payload, SamFourWayFrame row)
    {
        if(!payload.StartsWith("<|box;",StringComparison.Ordinal))
            throw new InvalidDataException("SAM line lacks a box header.");
        var end=payload.IndexOf("|>",StringComparison.Ordinal);
        if(end is < 5 or > 512)
            throw new InvalidDataException("SAM box record malformed.");
        var fields=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var field in payload[6..end].Split(';'))
        {
            var pair=field.Split('=',2);
            if(pair.Length!=2 || !int.TryParse(pair[1],out var value) ||
               !fields.TryAdd(pair[0],value))
                throw new InvalidDataException("SAM box metadata invalid.");
        }
        if(!fields.TryGetValue("x1",out var left) ||
           !fields.TryGetValue("y1",out var top) ||
           !fields.TryGetValue("x2",out var rightInclusive) ||
           !fields.TryGetValue("y2",out var bottomInclusive) ||
           !fields.TryGetValue("w",out var width) ||
           !fields.TryGetValue("h",out var height) ||
           width<=0 || height<=0 ||
           left<0 || top<0 || rightInclusive<left ||
           bottomInclusive<top ||
           rightInclusive>=width || bottomInclusive>=height ||
           Math.Abs(row.Left-left)>0.501 ||
           Math.Abs(row.Top-top)>0.501 ||
           Math.Abs(row.Right-(rightInclusive+1))>0.501 ||
           Math.Abs(row.Bottom-(bottomInclusive+1))>0.501 ||
           row.FrameIndex<0)
            throw new InvalidDataException(
                "SAM raw box geometry and decoded observation disagree.");
    }

    private static string Response(string output) =>
        JsonSerializer.Serialize(new {
            @object="response",model="sam-3.1",status="completed",
            output=new[]{new{
                type="message",content=new[]{new{type="output_text",text=output}}
            }}
        });

    /// <summary>
    /// OFFLINE reference partition. This is explicitly NOT four real Meta
    /// inferences: frames and masks are sliced from ONE previously completed
    /// full-video response. IDs are relabeled for a seam verification test.
    /// </summary>
    public static IReadOnlyList<SamFourWayRawPart> PartitionReferenceFixture(
        SamFourWayPlan plan,string sourceSha,string sceneKey,
        string fullResponseJson)
    {
        var full=Frames(OutputWire(fullResponseJson));
        var result=new List<SamFourWayRawPart>();
        for(var part=0;part<4;part++)
        {
            var window=plan.Windows[part];
            var wire=new StringBuilder();
            foreach(var row in full)
            {
                if(row.Frame<window.InputStart ||
                   row.Frame>=window.InputEndExclusive)continue;
                wire.Append('<').Append(row.Frame-window.InputStart)
                    .Append("f>").Append('p').Append(part)
                    .Append(row.Payload).Append('\n');
            }
            result.Add(new(part,sourceSha,sceneKey,true,
                Response(wire.ToString())));
        }
        return result;
    }

    public static SamFourWayMaskAssembly Assemble(
        SamFourWayPlan plan,SamFourWayMerge decoded,
        IReadOnlyList<SamFourWayRawPart> rawParts)
    {
        if(rawParts.Count!=4 || decoded.SourceFrames!=plan.SourceFrames ||
           !decoded.EverySeamVerified)
            throw new InvalidDataException("Incomplete or unverified SAM segment seams.");
        var actual=decoded.ObservedFrames.ToDictionary(f=>f.FrameIndex);
        var combined=new SortedDictionary<int,string>();
        // At the three seams the SAME global frame is present in adjacent
        // parts. For the strict offline equivalence benchmark every raw
        // box+mask payload (excluding local frame and object ID) must match.
        // Independent provider responses can legitimately differ; any such
        // difference remains UNVERIFIED and needs a later raster-IoU review.
        var overlappingMaskPayloads=new Dictionary<int,string>();

        foreach(var window in plan.Windows)
        {
            var segment=rawParts.SingleOrDefault(p=>p.Part==window.Part);
            if(segment is null || !segment.ProviderCompleted ||
               segment.SourceVideoSha256!=decoded.SourceSha256 ||
               segment.SceneKey!=decoded.SceneKey)
                throw new InvalidDataException("SAM segment provenance mismatch.");
            var lines=Frames(OutputWire(segment.RawResponseJson));
            foreach(var line in lines)
            {
                if(line.Frame<0 || line.Frame>=window.InputFrames)
                    throw new InvalidDataException("SAM segment frame out of bounds.");
                var global=window.GlobalFrame(line.Frame);
                if(overlappingMaskPayloads.TryGetValue(global,out var previousPayload))
                {
                    if(!string.Equals(previousPayload,line.Payload,
                         StringComparison.Ordinal))
                        throw new InvalidDataException(
                            "Two SAM fragments disagree on a shared mask; no verified seam.");
                }
                else overlappingMaskPayloads.Add(global,line.Payload);
                if(global<window.CoreStart || global>=window.CoreEndExclusive)
                    continue;
                if(!actual.TryGetValue(global,out var row) || row.Part!=window.Part ||
                   row.ObjectId!=line.Id)
                    throw new InvalidDataException(
                        "SAM masks disagree with verified decoded frame observations.");
                VerifyWireBox(line.Payload,row);
                if(!combined.TryAdd(global,"<"+global+"f>0"+line.Payload+"\n"))
                    throw new InvalidDataException(
                        "SAM masks disagree with verified decoded frame observations.");
            }
        }
        if(combined.Count!=actual.Count ||
           combined.Keys.Except(actual.Keys).Any())
            throw new InvalidDataException("Missing or extra SAM wire frames.");
        var output=new StringBuilder();
        foreach(var row in combined.Values)output.Append(row);
        // Never give this a native provider receipt. The new output is a
        // DERIVED composition of four individually authenticated streams.
        return new("FOUR_SEGMENTS_DERIVED_NOT_A_NATIVE_PROVIDER_RESPONSE",
            combined.Count,output.ToString(),
            plan.SourcePtsSha256,plan.SourceFrames,true);
    }

    public static string OfflineParserEnvelope(SamFourWayMaskAssembly merged) =>
        Response(merged.WireOutputText);
}
