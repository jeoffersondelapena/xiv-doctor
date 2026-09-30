namespace XIVDoctor;

// A frozen frame loop cannot report itself; a timer thread notices the missing ticks and says what it still can.
public sealed class FrameWatch : IDisposable
{
    public const int StallSeconds = 2;
    public const int RepeatSeconds = 3;

    private readonly Timer timer;
    private readonly Action<string> write;
    private long lastFrame = Environment.TickCount64;
    private long reportedAt;
    private long longestGap;
    private long longestEndedAt;

    public FrameWatch(Action<string> write)
    {
        this.write = write;
        timer = new Timer(_ => Check(Environment.TickCount64), null, 1000, 1000);
    }

    public void Tick()
    {
        var now = Environment.TickCount64;
        var gap = now - lastFrame;
        if (gap > longestGap)
        {
            longestGap = gap;
            longestEndedAt = now;
        }
        Volatile.Write(ref lastFrame, now);
    }

    /// <summary>The longest wait between two frames since the last call and how long ago it ended, both in milliseconds. Frame thread only.</summary>
    public (long Milliseconds, long EndedAgo) TakeLongest()
    {
        var result = (longestGap, longestGap == 0 ? 0 : Environment.TickCount64 - longestEndedAt);
        longestGap = 0;
        return result;
    }

    /// <summary>The line due at <paramref name="now"/> (milliseconds), or null. <paramref name="reportedAt"/> is 0 outside a reported stall.</summary>
    public static string? Verdict(long now, long lastFrame, long reportedAt, string facts)
    {
        var stalled = (now - lastFrame) / 1000;
        if (stalled < StallSeconds)
            return reportedAt != 0 ? "frame loop resumed" : null;
        if (reportedAt != 0 && (now - reportedAt) / 1000 < RepeatSeconds)
            return null;
        return $"frame loop stalled {stalled}s; {facts}";
    }

    private void Check(long now)
    {
        try
        {
            var line = Verdict(now, Volatile.Read(ref lastFrame), reportedAt, Facts());
            if (line is null) return;
            reportedAt = line == "frame loop resumed" ? 0 : now;
            write(line);
        }
        catch (Exception)
        {
        }
    }

    // if these stop appearing during a stall, every managed thread is held, not just the frame loop
    private static string Facts()
    {
        ThreadPool.GetAvailableThreads(out var free, out _);
        ThreadPool.GetMaxThreads(out var max, out _);
        return $"gc {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}, pool busy {max - free}, queued {ThreadPool.PendingWorkItemCount}";
    }

    public void Dispose() => timer.Dispose();
}
