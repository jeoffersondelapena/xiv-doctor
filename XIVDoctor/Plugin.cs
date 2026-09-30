using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Game.Chat;
using Dalamud.Plugin.Services;

namespace XIVDoctor;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/doctor";
    private const string Reason = "requested by XIV Doctor";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly IFramework framework;
    private readonly IClientState clientState;
    private DateTime? loginAt;
    private DateTime? zoneAfterLoginAt;
    private DateTime? lastChatAt;

    private readonly ICallGateSubscriber<bool> iinactHealthy;
    private readonly ICallGateSubscriber<string> iinactStatus;
    private readonly ICallGateSubscriber<string, bool> iinactRestart;
    private readonly ICallGateSubscriber<bool> browsingwayHealthy;
    private readonly ICallGateSubscriber<string> browsingwayStatus;
    private readonly ICallGateSubscriber<string, bool> browsingwayRestart;

    private readonly DiagLog? diag;
    private readonly FrameWatch? frameWatch;
    private int busy;
    private DateTime? reminderShownAt;
    private DateTime lastReminderCheck = DateTime.MinValue;
    private DateTime? lastBeat;
    private int notesSeen = -1;
    private string lastNotes = "";

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commands, IChatGui chat, IPluginLog log, IFramework framework, IClientState clientState)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.chat = chat;
        this.log = log;
        this.framework = framework;
        this.clientState = clientState;

        iinactHealthy = pluginInterface.GetIpcSubscriber<bool>("IINACT.Healthy");
        iinactStatus = pluginInterface.GetIpcSubscriber<string>("IINACT.Status");
        iinactRestart = pluginInterface.GetIpcSubscriber<string, bool>("IINACT.Restart");
        browsingwayHealthy = pluginInterface.GetIpcSubscriber<bool>("Browsingway.Healthy");
        browsingwayStatus = pluginInterface.GetIpcSubscriber<string>("Browsingway.Status");
        browsingwayRestart = pluginInterface.GetIpcSubscriber<string, bool>("Browsingway.Restart");

        diag = OpenDiagLog();
        frameWatch = diag is null ? null : new FrameWatch(diag.Write);
        LoginReport.Instruction = ReadInstruction();
        diag?.Write($"XIV Doctor {typeof(Plugin).Assembly.GetName().Version} loaded, pid {Environment.ProcessId}");

        clientState.Login += OnLogin;
        clientState.TerritoryChanged += OnTerritoryChanged;
        chat.ChatMessage += OnChatMessage;
        framework.Update += OnUpdate;

        commands.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "status: how the parser and the renderer are; fix: restart or load whatever is unwell",
        });
    }

    public void Dispose()
    {
        clientState.Login -= OnLogin;
        chat.ChatMessage -= OnChatMessage;
        clientState.TerritoryChanged -= OnTerritoryChanged;
        framework.Update -= OnUpdate;
        commands.RemoveHandler(Command);
        frameWatch?.Dispose();
        diag?.Write(DiagLog.UnloadingLine(GameClosing()));
        diag?.Dispose();
    }

    private bool GameClosing()
    {
        try { return framework.IsFrameworkUnloading; }
        catch (Exception) { return false; }
    }

    private void OnLogin()
    {
        loginAt = DateTime.UtcNow;
        lastChatAt = null;
        // The zone usually loads a moment before Dalamud raises Login; count that load as the zone event.
        zoneAfterLoginAt = clientState.TerritoryType != 0 ? DateTime.UtcNow : null;
        Task.Run(LoginCheck);
    }

    // every line counts, whoever printed it: the report waits for the flood to stop, not for a particular plugin
    private void OnChatMessage(IHandleableChatMessage message) => lastChatAt = DateTime.UtcNow;

    private void OnTerritoryChanged(uint territory)
    {
        if (loginAt is not null && zoneAfterLoginAt is null)
            zoneAfterLoginAt = DateTime.UtcNow;
    }

    // One line after the login flood: what the parser and the renderer say, plus any note left for the player.
    private async Task LoginCheck()
    {
        var started = loginAt ?? DateTime.UtcNow;
        (Report iinact, Report browsingway) reports;
        while (true)
        {
            reports = await framework.RunOnFrameworkThread(Probe);
            var both = reports.iinact.Loaded && reports.iinact.Healthy && reports.browsingway.Loaded && reports.browsingway.Healthy;
            var sinceLogin = (DateTime.UtcNow - started).TotalSeconds;
            double? sinceZone = zoneAfterLoginAt is { } z ? (DateTime.UtcNow - z).TotalSeconds : null;
            double? sinceChat = lastChatAt is { } c ? (DateTime.UtcNow - c).TotalSeconds : null;
            if (LoginReport.ReadyToPrint(sinceLogin, sinceZone, sinceChat, both))
                break;
            await Task.Delay(1000);
        }
        var waitedOut = (DateTime.UtcNow - started).TotalSeconds >= LoginReport.HealthWaitSeconds;
        var attention = ReadAttention();
        var line = LoginReport.Line(reports.iinact, reports.browsingway, attention, waitedOut);
        diag?.Write("login: " + line);
        chat.Print(line);
        reminderShownAt = DateTime.UtcNow;
        notesSeen = attention.Count;
        lastNotes = string.Join("\n", attention);
        loginAt = null;
    }

    // once a minute: a file read per frame would be waste
    private readonly ReloadWatch reloadWatch = new();
    private DateTime lastReloadPoll = DateTime.MinValue;
    private int reloading;

    // dev plugins listed in reload.txt (one internal name per line; none when the file is missing) reload on a rebuild
    private void PollRebuilds(DateTime now)
    {
        if ((now - lastReloadPoll).TotalSeconds < ReloadWatch.PollSeconds || reloading != 0) return;
        lastReloadPoll = now;
        var stamps = new List<(string, DateTime)>();
        foreach (var name in WatchedPlugins())
        {
            try
            {
                var dll = PluginControl.DllPath(pluginInterface, name);
                if (dll != null && File.Exists(dll)) stamps.Add((name, File.GetLastWriteTimeUtc(dll)));
            }
            catch (Exception ex) { log.Warning($"XIV Doctor: could not look at {name}: {ex.Message}"); }
        }
        foreach (var name in reloadWatch.Changed(stamps, now))
        {
            chat.Print($"XIV Doctor: {name} was rebuilt; reloading it.");
            reloading++;
            var plugin = name;
            Task.Run(async () =>
            {
                try
                {
                    var did = await PluginControl.Load(pluginInterface, plugin);
                    log.Information($"XIV Doctor: {plugin} {did}");
                    await framework.RunOnFrameworkThread(() => chat.Print($"XIV Doctor: {plugin} {did}."));
                }
                catch (Exception ex) { await framework.RunOnFrameworkThread(() => chat.PrintError($"XIV Doctor: {plugin} did not reload ({ex.Message}).")); }
                finally { Interlocked.Decrement(ref reloading); }
            });
        }
    }

    private IEnumerable<string> WatchedPlugins()
    {
        var path = Path.Combine(pluginInterface.ConfigDirectory.FullName, "reload.txt");
        if (!File.Exists(path)) return Array.Empty<string>();
        return File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'));
    }

    private void OnUpdate(IFramework _)
    {
        frameWatch?.Tick();
        var now = DateTime.UtcNow;
        if (DiagLog.HeartbeatDue(lastBeat, now))
        {
            lastBeat = now;
            diag?.Write(DiagLog.HeartbeatLine(clientState.IsLoggedIn, clientState.TerritoryType));
        }
        if (clientState.IsLoggedIn) PollRebuilds(now);
        if ((now - lastReminderCheck).TotalSeconds < 60 || loginAt is not null || !clientState.IsLoggedIn)
            return;
        lastReminderCheck = now;
        var attention = ReadAttention();
        var text = string.Join("\n", attention);
        if (notesSeen > 0 && attention.Count == 0)
            chat.Print("XIV Doctor: attention cleared.");
        notesSeen = attention.Count;
        // a new or changed note goes out at once; an unchanged one every half hour
        if (attention.Count > 0 && (text != lastNotes || LoginReport.ReminderDue(reminderShownAt, now, true)))
        {
            chat.Print(LoginReport.Reminder(attention));
            reminderShownAt = now;
        }
        lastNotes = text;
    }

    private string AttentionPath => Path.Combine(pluginInterface.ConfigDirectory.FullName, "attention.txt");

    private string ReadInstruction()
    {
        try
        {
            var path = Path.Combine(pluginInterface.ConfigDirectory.FullName, "instruction.txt");
            var line = File.Exists(path) ? File.ReadLines(path).FirstOrDefault(l => l.Trim().Length > 0)?.Trim() : null;
            return string.IsNullOrEmpty(line) ? LoginReport.DefaultInstruction : line;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "instruction file unreadable");
            return LoginReport.DefaultInstruction;
        }
    }

    private IReadOnlyList<string> ReadAttention()
    {
        try
        {
            var path = AttentionPath;
            return File.Exists(path) ? File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToList() : new List<string>();
        }
        catch (Exception ex)
        {
            log.Warning(ex, "attention file unreadable");
            return new List<string>();
        }
    }

    private DiagLog? OpenDiagLog()
    {
        try
        {
            DateTime started;
            try { started = System.Diagnostics.Process.GetCurrentProcess().StartTime; }
            catch (Exception) { started = DateTime.Now; }
            return new DiagLog(Path.Combine(pluginInterface.ConfigDirectory.FullName, "diag"), started, Environment.ProcessId);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "diagnostic log unavailable");
            return null;
        }
    }

    private void OnCommand(string command, string args)
    {
        var (iinact, browsingway) = Probe();
        chat.Print($"XIV Doctor: {Doctor.Summary(iinact, browsingway)}.");
        diag?.Write($"/doctor {args.Trim()}: {Doctor.Summary(iinact, browsingway)}");
        if (args.Trim() != "fix")
            return;

        var plan = Doctor.Plan(iinact, browsingway);
        chat.Print("XIV Doctor: " + string.Join(", ", plan.Select(Doctor.Describe)) + ".");
        diag?.Write("plan: " + string.Join(", ", plan.Select(Doctor.Describe)));
        if (Interlocked.Exchange(ref busy, 1) != 0)
        {
            chat.PrintError("XIV Doctor: a fix is already running.");
            return;
        }
        Task.Run(async () =>
        {
            try
            {
                foreach (var step in plan)
                {
                    await Run(step);
                    diag?.Write($"done: {Doctor.Describe(step)}");
                    // Restarts are asynchronous: see the layer go unhealthy first, or a stale "healthy" ends the wait early.
                    var watched = step is Step.RestartParser or Step.LoadIinact ? iinactHealthy : browsingwayHealthy;
                    await WaitUntilUnhealthy(watched, TimeSpan.FromSeconds(5));
                    // The renderer's fresh pages must find a live parser, so the parser settles first.
                    if (step is Step.RestartParser or Step.LoadIinact)
                        await WaitUntil(iinactHealthy, TimeSpan.FromSeconds(45));
                }
                var verdict = await WaitForHealth(plan);
                diag?.Write(verdict);
                chat.Print(verdict);
            }
            catch (Exception ex)
            {
                log.Error(ex, "fix failed");
                diag?.Write($"fix FAILED: {ex.GetType().Name}: {ex.Message}");
                chat.PrintError($"XIV Doctor: {ex.Message}. Fall back to /xldisableplugintemp and /xlenableplugintemp for that plugin.");
            }
            finally
            {
                Interlocked.Exchange(ref busy, 0);
            }
        });
    }

    // "done" has to be the last line; the restarts settle asynchronously.
    private async Task<string> WaitForHealth(IReadOnlyList<Step> plan)
    {
        var watchParser = plan.Contains(Step.RestartParser) || plan.Contains(Step.LoadIinact);
        var watchRenderer = plan.Contains(Step.RestartRenderer) || plan.Contains(Step.LoadBrowsingway);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            var parserOk = !watchParser || await framework.RunOnFrameworkThread(() => Healthy(iinactHealthy));
            var rendererOk = !watchRenderer || await framework.RunOnFrameworkThread(() => Healthy(browsingwayHealthy));
            if (parserOk && rendererOk)
                return "XIV Doctor: done; everything reports healthy.";
            await Task.Delay(500);
        }
        var stuck = new List<string>();
        if (watchParser && !await framework.RunOnFrameworkThread(() => Healthy(iinactHealthy)))
            stuck.Add("IINACT");
        if (watchRenderer && !await framework.RunOnFrameworkThread(() => Healthy(browsingwayHealthy)))
            stuck.Add("Browsingway");
        return $"XIV Doctor: done, but {string.Join(" and ", stuck)} has not reported healthy after 45 s; "
               + "use /xldisableplugintemp then /xlenableplugintemp on it.";
    }

    private async Task WaitUntil(ICallGateSubscriber<bool> healthy, TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < deadline && !await framework.RunOnFrameworkThread(() => Healthy(healthy)))
            await Task.Delay(500);
    }

    private async Task WaitUntilUnhealthy(ICallGateSubscriber<bool> healthy, TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < deadline && await framework.RunOnFrameworkThread(() => Healthy(healthy)))
            await Task.Delay(250);
    }

    private static bool Healthy(ICallGateSubscriber<bool> healthy)
    {
        try { return healthy.InvokeFunc(); }
        catch (Exception) { return false; }
    }

    private async Task Run(Step step)
    {
        switch (step)
        {
            case Step.LoadIinact:
                chat.Print($"XIV Doctor: IINACT {await PluginControl.Load(pluginInterface, "IINACT")}.");
                break;
            case Step.RestartParser:
                if (!iinactRestart.InvokeFunc(Reason))
                    chat.PrintError("XIV Doctor: IINACT declined the restart (one is already running).");
                break;
            case Step.LoadBrowsingway:
                chat.Print($"XIV Doctor: Browsingway {await PluginControl.Load(pluginInterface, "Browsingway")}.");
                break;
            case Step.RestartRenderer:
                if (!browsingwayRestart.InvokeFunc(Reason))
                    chat.PrintError("XIV Doctor: Browsingway could not restart its renderer.");
                break;
        }
    }

    private (Report iinact, Report browsingway) Probe() =>
        (Ask("IINACT", iinactHealthy, iinactStatus), Ask("Browsingway", browsingwayHealthy, browsingwayStatus));

    private Report Ask(string internalName, ICallGateSubscriber<bool> healthy, ICallGateSubscriber<string> status)
    {
        try
        {
            return new Report(true, healthy.InvokeFunc(), status.InvokeFunc());
        }
        catch (IpcNotReadyError)
        {
            return Report.Absent($"not loaded ({PluginControl.State(pluginInterface, internalName)})");
        }
        catch (Exception ex)
        {
            log.Warning(ex, $"{internalName} status call failed");
            return new Report(true, false, $"error: {ex.Message}");
        }
    }
}
