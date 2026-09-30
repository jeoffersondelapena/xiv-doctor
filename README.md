# XIV Doctor

Renamed from Overlay Doctor on 2026-09-29: it now fronts the overlays, the Mac-side watchers' notes and a fix command, so the old name had grown too narrow. The command is `/doctor`.

One in-game command that brings the ACT overlay stack back and relays the Mac's notes: `/doctor`.

```
/doctor            how the IINACT parser and the Browsingway renderer are right now
/doctor fix        restart whichever layer is unwell, or load a plugin that is missing
```

It is a Dalamud plugin that depends on nothing but Dalamud, so it still exists when IINACT or
Browsingway failed to load. It talks to both over Dalamud's plugin-to-plugin channel (`IINACT.Healthy`,
`IINACT.Restart`, `Browsingway.Healthy`, `Browsingway.Restart`, provided by the macOS forks of those
plugins) and reaches for Dalamud's plugin manager only to load a plugin that is not running.

| File | What it is |
|---|---|
| `XIVDoctor/Doctor.cs` | the decision: which steps, in which order (pure, tested) |
| `XIVDoctor/PluginControl.cs` | load or reload another plugin through Dalamud's internals |
| `XIVDoctor/Plugin.cs` | the command, the IPC calls, the chat lines |
| `XIVDoctor/DiagLog.cs` | the per-window diagnostic log and its once-a-minute heartbeat |
| `XIVDoctor.Tests/` | xunit; runs before every commit once the hook is enabled |

The IPC names above are a contract with the two forks (`iinact-fork`, `browsingway-fork`): change them in all
three places or not at all.

Every five seconds it also writes a heartbeat line to its per-window diagnostic log (`diag/doctor-<start>-<pid>.log`
in the config folder). The Mac-side freeze watcher in `xiv-mac-tools` reads that file: a log that stops beating
while the game process lives is a frozen frame loop, and a last line of `unloading` is the plugin switched off on
purpose. When the game itself is closing the line is `unloading; game closing`: a process still alive two minutes
after that has stopped closing, and the watcher ends it. The `heartbeat:` prefix and those two lines are a contract with the watcher. Once a minute a `memory:` line gives the managed heap, what the runtime has committed, the process size as Windows reports it, the players in view, the territory, and the longest wait between two frames in that minute with the time it ended; the watcher's memory report reads it beside the Mac's own figures for the window, and holds each of its own readings against that longest frame so a reading that is felt in the game shows. A timer thread adds `frame loop stalled Ns; …` lines while the frame loop is not ticking (with a few runtime counters, so a capture shows whether other managed threads still run) and `frame loop resumed` when it ends; the watcher reads those too.

Build: `dotnet build XIVDoctor -c Release` (needs a Dalamud dev install; on macOS XIV on Mac's).
The first build enables the versioned pre-commit hook (`git config core.hooksPath .githooks`).

A standing note ends with an instruction sentence. The default is neutral; put your own wording on the first line of `instruction.txt` in the plugin's config folder and it is used instead.
