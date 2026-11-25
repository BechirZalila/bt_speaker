# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

The project conventions, commands and gotchas live in `AGENTS.md` (shared with other agents). Keep them there, not here:

@AGENTS.md

## Runtime flow (spans several files)
- `Program.cs`: `--list` exits early (console mode, no mutex); otherwise takes the `Local\BtBatteryTray` mutex and runs `TrayApp` (an `ApplicationContext`, no window).
- `TrayApp.cs` owns everything at runtime: one `NotifyIcon` per device, keyed by PnP instance id, all sharing one `ContextMenuStrip`.
- Refresh triggers: `DeviceWatcher` events (raised on a WinRT thread, posted to the UI thread through a `WindowsFormsSynchronizationContext`) restart a 1 s debounce timer, then call `Refresh(reason)`. A 5 min safety timer also calls `Refresh`. If the watcher aborts, it is restarted after 10 s.
- `Refresh` calls `BatteryReader.ReadAll()` (synchronous cfgmgr32 P/Invoke declared in `CfgMgr32.cs`), then adds, updates or removes icons. Disconnected and hidden devices (from `Settings`) get no icon. When no device is visible, a grey placeholder icon keeps the menu reachable. It also runs the low-battery alert (balloon tip), which fires once per discharge: the `_alerted` set is cleared only when the level rises above threshold + hysteresis.
- Updates: a separate timer (30 s, then every 24 h) calls `UpdateChecker.CheckAsync()`. A new version shows a bold menu item and a balloon. Clicking either one opens `UpdateProgressForm`, downloads and verifies the installer, then starts it silently. The app hides its icons and waits for the installer to kill it, and the installer relaunches it with `--updated`.
- `BatteryIcon.cs` renders the coloured percentage, `StartupRegistration.cs` handles the HKCU Run key, and `Log.cs` writes only when `--log` is passed.

There is no test project. Check a change with `dotnet build`, then `--list` for the reading logic or `--log` for event and refresh behaviour.
