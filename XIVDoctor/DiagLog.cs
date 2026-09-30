using System.Globalization;
using System.Text;

namespace XIVDoctor;

// Two clients share dalamud.log; the one that loses that file keeps no record.
public sealed class DiagLog : IDisposable
{
    public const int RetentionDays = 7;

    private readonly object gate = new();
    private readonly StreamWriter writer;

    public string Path { get; }

    public DiagLog(string directory, DateTime startedAt, int processId)
    {
        Directory.CreateDirectory(directory);
        foreach (var stale in FilesToPrune(Directory.GetFiles(directory, "doctor-*.log"), DateTime.Now))
        {
            try { File.Delete(stale); }
            catch (IOException) { }
        }
        Path = System.IO.Path.Combine(directory, FileName(startedAt, processId));
        var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public static string FileName(DateTime startedAt, int processId) => $"doctor-{startedAt:yyyyMMdd-HHmmss}-{processId}.log";

    public const int HeartbeatSeconds = 5;

    // The Mac-side freeze watcher keys on this prefix: a log that stops beating while the process lives is a frozen frame loop.
    public static string HeartbeatLine(bool loggedIn, uint territory) => $"heartbeat: logged in {loggedIn}; territory {territory}";

    public static bool HeartbeatDue(DateTime? last, DateTime now) => last is null || (now - last.Value).TotalSeconds >= HeartbeatSeconds;

    public const int MemorySeconds = 60;

    public static bool MemoryDue(DateTime? last, DateTime now) => last is null || (now - last.Value).TotalSeconds >= MemorySeconds;

    // read beside the Mac's own figures for the window: a managed heap that stays small clears the plugins' own objects
    public static string MemoryLine(long managedBytes, long committedBytes, long processBytes, int players, uint territory, long longestFrameMs, DateTime longestFrameAt) =>
        $"memory: managed {Mb(managedBytes)} MB, committed {Mb(committedBytes)} MB, process {Mb(processBytes)} MB; players {players}; territory {territory}"
        + $"; longest frame {longestFrameMs} ms at {longestFrameAt:HH:mm:ss.f}";

    private static long Mb(long bytes) => bytes / (1024 * 1024);

    // the watcher ends a process that outlives "game closing" by two minutes, never one that only says "unloading"
    public static string UnloadingLine(bool gameClosing) => gameClosing ? "unloading; game closing" : "unloading";

    /// <summary>Files older than the retention window; names that do not carry a date are kept.</summary>
    public static IEnumerable<string> FilesToPrune(IEnumerable<string> paths, DateTime now)
    {
        foreach (var path in paths)
        {
            var parts = System.IO.Path.GetFileName(path).Split('-');
            if (parts.Length < 3 || !DateTime.TryParseExact(parts[1], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                continue;
            if ((now.Date - day).TotalDays > RetentionDays)
                yield return path;
        }
    }

    public void Write(string message)
    {
        lock (gate)
        {
            try { writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}"); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        }
    }

    public void Dispose()
    {
        lock (gate)
            writer.Dispose();
    }
}
