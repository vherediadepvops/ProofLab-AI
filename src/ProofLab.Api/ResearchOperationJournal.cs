using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
namespace ProofLab.Api;

// Durable metadata only. No request body, token, provider output or exception message.
public sealed record ResearchOperation(string Id, string StudyId, string Role, string Stage,
    DateTimeOffset StartedAtUtc, DateTimeOffset DeadlineUtc, int ProcessId, long ProcessStartTicks,
    string State = "RUNNING", DateTimeOffset? FinishedAtUtc = null);
public sealed class ResearchOperationJournal(IWebHostEnvironment environment)
{
    private readonly string root = Path.Combine(environment.ContentRootPath, ".private", "physics", "research-operations");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static bool ValidId(string? id) => id is { Length: 32 } && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private string Folder(string study) { if (!ValidId(study)) throw new ArgumentException("Invalid study ID."); return Path.Combine(root, study); }
    private static async Task Save(string file, ResearchOperation value)
    {
        await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, value, Json); await stream.FlushAsync(); stream.Flush(true);
    }
    public async Task<ResearchOperation> BeginAsync(string study, string role, string stage, string id)
    {
        if (!ValidId(id) || role is not ("baseline" or "followup")) throw new ArgumentException("Invalid operation identity.");
        var dir = Folder(study); Directory.CreateDirectory(dir);
        using var process = Process.GetCurrentProcess(); var now = DateTimeOffset.UtcNow;
        var record = new ResearchOperation(id, study, role, stage, now, now.AddMinutes(70), process.Id, process.StartTime.ToUniversalTime().Ticks);
        await Save(Path.Combine(dir, id + ".started.json"), record); return record;
    }
    public Task FinishAsync(ResearchOperation operation, bool completed) => Save(Path.Combine(Folder(operation.StudyId), operation.Id + ".finished.json"),
        operation with { State = completed ? "COMPLETED" : "REVIEW_REQUIRED", FinishedAtUtc = DateTimeOffset.UtcNow });
    public static string EffectiveState(ResearchOperation operation)
    {
        if (operation.State != "RUNNING") return operation.State;
        try
        {
            using var process = Process.GetProcessById(operation.ProcessId);
            if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == operation.ProcessStartTicks && DateTimeOffset.UtcNow < operation.DeadlineUtc) return "RUNNING";
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        return "INTERRUPTED"; // Never replay a possibly billed request after a restart.
    }
    public async Task<ResearchOperation[]> ReadAsync(string study, CancellationToken ct)
    {
        var dir = Folder(study); if (!Directory.Exists(dir)) return [];
        var records = new List<ResearchOperation>();
        foreach (var start in Directory.EnumerateFiles(dir, "*.started.json"))
        {
            ct.ThrowIfCancellationRequested(); var finish = start.Replace(".started.json", ".finished.json");
            try
            {
                var record = JsonSerializer.Deserialize<ResearchOperation>(await File.ReadAllTextAsync(File.Exists(finish) ? finish : start, ct), Json);
                if (record is not null && record.StudyId == study) records.Add(record with { State = EffectiveState(record) });
            }
            catch (IOException) { }
            catch (JsonException) { } // A concurrent atomic receipt may still be flushing.
        }
        return records.OrderByDescending(x => x.StartedAtUtc).ToArray();
    }
}
