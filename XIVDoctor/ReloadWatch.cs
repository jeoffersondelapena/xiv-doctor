namespace XIVDoctor;

// A rebuilt dev plugin never reloads by itself here: Wine passes no file-change events from the Mac side, so the
// DLL's timestamp is polled instead. A change counts once it has stopped moving, so a build in progress is not loaded.
public sealed class ReloadWatch
{
    public const double PollSeconds = 5;
    public const double SettleSeconds = 3;

    private readonly Dictionary<string, DateTime> seen = new();
    private readonly Dictionary<string, DateTime> pending = new();

    /// <summary>Plugins whose DLL changed and settled since the last look; the first look only records what is there.</summary>
    public IReadOnlyList<string> Changed(IEnumerable<(string Name, DateTime Written)> dlls, DateTime now)
    {
        var due = new List<string>();
        foreach (var (name, written) in dlls)
        {
            if (!seen.TryGetValue(name, out var last)) { seen[name] = written; continue; }
            if (written == last) { pending.Remove(name); continue; }
            if (!pending.ContainsKey(name)) pending[name] = written;
            if ((now - written).TotalSeconds < SettleSeconds) continue;
            seen[name] = written;
            pending.Remove(name);
            due.Add(name);
        }
        return due;
    }
}
