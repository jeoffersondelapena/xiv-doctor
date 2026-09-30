using XIVDoctor;
using Xunit;

public class DiagLogTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 22, 0, 0);

    [Fact]
    public void File_names_carry_the_start_time_and_pid()
    {
        Assert.Equal("doctor-20260905-215211-364.log", DiagLog.FileName(new DateTime(2026, 9, 5, 21, 52, 11), 364));
    }

    [Fact]
    public void Only_files_past_the_retention_window_are_pruned()
    {
        var files = new[] { "/d/doctor-20260905-195211-364.log", "/d/doctor-20260829-090000-12.log", "/d/doctor-20260828-090000-12.log", "/d/notes.log" };
        Assert.Equal(new[] { "/d/doctor-20260828-090000-12.log" }, DiagLog.FilesToPrune(files, Now));
    }

    [Fact]
    public void A_heartbeat_is_due_at_start_and_then_every_five_seconds()
    {
        Assert.True(DiagLog.HeartbeatDue(null, Now));
        Assert.False(DiagLog.HeartbeatDue(Now, Now.AddSeconds(4)));
        Assert.True(DiagLog.HeartbeatDue(Now, Now.AddSeconds(5)));
    }

    [Fact]
    public void The_heartbeat_line_keeps_the_prefix_the_freeze_watcher_matches()
    {
        Assert.Equal("heartbeat: logged in True; territory 130", DiagLog.HeartbeatLine(true, 130));
    }

    [Fact]
    public void A_memory_line_is_due_at_start_and_then_once_a_minute()
    {
        Assert.True(DiagLog.MemoryDue(null, Now));
        Assert.False(DiagLog.MemoryDue(Now, Now.AddSeconds(59)));
        Assert.True(DiagLog.MemoryDue(Now, Now.AddSeconds(60)));
    }

    [Fact]
    public void The_memory_line_keeps_the_shape_the_watcher_reads()
    {
        Assert.Equal("memory: managed 812 MB, committed 1490 MB, process 9800 MB; players 23; territory 131",
            DiagLog.MemoryLine(812L * 1024 * 1024 + 5, 1490L * 1024 * 1024, 9800L * 1024 * 1024, 23, 131));
    }

    [Fact]
    public void The_last_line_tells_a_closing_game_from_a_plugin_switched_off()
    {
        Assert.Equal("unloading", DiagLog.UnloadingLine(false));
        Assert.Equal("unloading; game closing", DiagLog.UnloadingLine(true));
        // both keep the word the freeze watcher reads as "on purpose"
        Assert.Contains("unloading", DiagLog.UnloadingLine(true));
    }

    [Fact]
    public void Writes_land_in_the_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "doctor-diag-test-" + Guid.NewGuid());
        using (var log = new DiagLog(dir, Now, 42))
            log.Write("hello");
        Assert.Contains("] hello", File.ReadAllText(Path.Combine(dir, "doctor-20260905-220000-42.log")));
        Directory.Delete(dir, true);
    }
}
