namespace XIVDoctor;

public static class LoginReport
{
    public const double HealthWaitSeconds = 90;
    public const double SettleAfterZoneSeconds = 5;
    public const double SettleCapSeconds = 45;
    public const int ReminderMinutes = 30;
    public const string DefaultInstruction = "Paste this line to your assistant.";
    // the exact wording is personal and lives in instruction.txt beside the config, not in the repo
    public static string Instruction { get; set; } = DefaultInstruction;

    // a login line is easy to miss; standing notes repeat until cleared
    public static bool ReminderDue(DateTime? lastShown, DateTime now, bool anyNotes)
        => anyNotes && (lastShown is null || (now - lastShown.Value).TotalMinutes >= ReminderMinutes);

    public static string Reminder(IReadOnlyList<string> attention)
        => "XIV Doctor: attention, still open. " + string.Join(" ", attention.Select(p => p.TrimEnd('.') + ".")) + " " + Instruction;

    /// <summary>Print once both layers answered (or the wait ran out) and the zone's own login lines have landed.</summary>
    public static bool ReadyToPrint(double sinceLogin, double? sinceZone, bool bothHealthy)
    {
        var healthSettled = bothHealthy || sinceLogin >= HealthWaitSeconds;
        var linesSettled = (sinceZone is >= SettleAfterZoneSeconds) || sinceLogin >= SettleCapSeconds;
        return healthSettled && linesSettled;
    }

    public static string Line(Report iinact, Report browsingway, IReadOnlyList<string> attention, bool waitedOut = false)
    {
        var fine = iinact.Loaded && iinact.Healthy && browsingway.Loaded && browsingway.Healthy;
        if (fine && attention.Count == 0)
            return $"XIV Doctor: all good; parser healthy, {browsingway.Status}.";
        var parts = new List<string>();
        if (!fine)
        {
            var parser = iinact.Loaded ? (iinact.Healthy ? "healthy" : "unwell") : iinact.Status;
            var suffix = waitedOut ? $" (still not ready after {HealthWaitSeconds:F0} s)" : "";
            parts.Add($"IINACT {parser} | Browsingway {browsingway.Status}{suffix}; use /overlays fix");
        }
        parts.AddRange(attention);
        var line = "XIV Doctor: attention. " + string.Join(" ", parts.Select(p => p.TrimEnd('.') + "."));
        return attention.Count > 0 ? line + " " + Instruction : line;
    }
}
