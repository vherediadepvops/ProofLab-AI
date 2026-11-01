using ProofLab.Api;

namespace ProofLab.Tests;

public sealed class DirectorUploadProgressTests
{
    private const string Study = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Upload = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset utc = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        private long tick;
        public override DateTimeOffset GetUtcNow() => utc;
        public override long GetTimestamp() => tick;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan duration) { utc += duration; tick += duration.Ticks; }
    }

    [Fact]
    public void ReportsActualPhasesBytesAndStableTerminalTiming()
    {
        var clock = new Clock();
        var store = new DirectorUploadProgress(clock);
        store.Begin(Study, "baseline", Upload, 10);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        store.Phase(Upload, "validate");
        clock.Advance(TimeSpan.FromMilliseconds(40));
        store.Phase(Upload, "receive_and_hash");
        store.Received(Upload, 4);
        store.Received(Upload, 3);
        clock.Advance(TimeSpan.FromMilliseconds(60));
        var active = store.Read(Study, "baseline", Upload)!;
        Assert.Equal("RUNNING", active.State);
        Assert.Equal("receive_and_hash", active.Phase);
        Assert.Equal(4, active.ReceivedBytes);
        Assert.Equal(200, active.ElapsedMs);
        Assert.Equal(100, active.Phases.Single(p => p.Key == "queue").ElapsedMs);
        Assert.Equal(40, active.Phases.Single(p => p.Key == "validate").ElapsedMs);
        Assert.Equal(60, active.Phases.Single(p => p.Key == "receive_and_hash").ElapsedMs);
        Assert.Equal("PENDING", active.Phases.Single(p => p.Key == "poster").Status);
        Assert.DoesNotContain(active.Phases, p => p.Key is "local_tracking" or "spectral" or "playback");
        Assert.Throws<ArgumentException>(() => store.Received(Upload, 11));
        store.Received(Upload, 10);
        clock.Advance(TimeSpan.FromMilliseconds(30));
        store.Complete(Upload);
        var completed = store.Read(Study, "baseline", Upload)!;
        clock.Advance(TimeSpan.FromSeconds(1));
        store.Fail(Upload, cancelled: true);
        store.Phase(Upload, "probe");
        store.Received(Upload, 9);
        var unchanged = store.Read(Study, "baseline", Upload)!;
        Assert.Equal("COMPLETED", unchanged.State);
        Assert.Equal("complete", unchanged.Phase);
        Assert.Equal(10, unchanged.ReceivedBytes);
        Assert.Equal(230, unchanged.ElapsedMs);
        Assert.Equal(completed.UpdatedAtUtc, unchanged.UpdatedAtUtc);
    }

    [Fact]
    public void DuplicateIdentityCannotReplaceProgressAndReadsAreScoped()
    {
        var store = new DirectorUploadProgress(new Clock());
        store.Begin(Study.ToUpperInvariant(), "baseline", Upload.ToUpperInvariant(), 10);
        store.Received(Upload, 4);
        Assert.Throws<DirectorUploadConflictException>(() => store.Begin(Study, "followup", Upload, 20));
        var original = store.Read(Study, "baseline", Upload)!;
        Assert.Equal(Study, original.StudyId);
        Assert.Equal(Upload, original.UploadId);
        Assert.Equal(10, original.TotalBytes);
        Assert.Equal(4, original.ReceivedBytes);
        Assert.Null(store.Read(Study, "followup", Upload));
        Assert.Null(store.Read(new string('c', 32), "baseline", Upload));
        Assert.Null(store.Read(Study, "baseline", new string('d', 32)));
        Assert.Throws<ArgumentException>(() => store.Read(Study, "baseline", "../invalid"));
        store.Fail(Upload.ToUpperInvariant(), cancelled: true);
        store.Complete(Upload);
        var cancelled = store.Read(Study, "baseline", Upload)!;
        Assert.Equal("CANCELLED", cancelled.State);
        Assert.Equal("CANCELLED", cancelled.Phases.Single(p => p.Key == "queue").Status);
    }

    [Fact]
    public void PrunesTerminalSnapshotsWithoutLimitingOrExpiringActiveUploads()
    {
        var clock = new Clock();
        var store = new DirectorUploadProgress(clock);
        var active = Enumerable.Range(1, 130).Select(i => i.ToString("x32")).ToArray();
        foreach (var id in active) store.Begin(Study, "baseline", id, 10);
        for (var i = 1000; i < 1130; i++) {
            var id = i.ToString("x32");
            store.Begin(Study, "baseline", id, 10);
            store.Complete(id);
        }
        Assert.Null(store.Read(Study, "baseline", 1000.ToString("x32")));
        Assert.NotNull(store.Read(Study, "baseline", 1129.ToString("x32")));
        Assert.All(active, id => Assert.Equal("RUNNING", store.Read(Study, "baseline", id)!.State));
        clock.Advance(TimeSpan.FromHours(3));
        Assert.Null(store.Read(Study, "baseline", 1129.ToString("x32")));
        Assert.All(active, id => Assert.Equal("RUNNING", store.Read(Study, "baseline", id)!.State));
    }
}
