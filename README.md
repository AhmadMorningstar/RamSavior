# RAM Savior — Setup Guide

## 1. Getting it into Visual Studio 2026

1. Copy the whole `RamSavior` folder wherever you keep projects.
2. Double-click `RamSavior.sln` — it opens all three projects together.
3. Right-click the solution → **Restore NuGet Packages**.
   - `RamSavior.Cli` needs `System.CommandLine` (currently beta — check NuGet.org for
     the latest `2.0.0-betaX` version and update the `<PackageReference>` version in
     `RamSavior.Cli.csproj` if restore fails on the pinned version).
   - `RamSavior.App` needs `WPF-UI` — same deal, check NuGet.org for the current stable
     version and update the pin in `RamSavior.App.csproj` if needed.
4. Set **RamSavior.App** as the startup project to run the GUI (F5).
5. To run the CLI, right-click `RamSavior.Cli` → **Set as Startup Project**, or just
   publish it and run `ramsvr.exe` from a terminal (see below) — that's the more
   realistic way you'll actually use it day to day.

Both projects need to run **elevated** (the app manifests already request
`requireAdministrator`) — Visual Studio itself should be running as admin, or you'll
get a UAC prompt when launching.

## 2. Publishing (creating the actual .exe files to distribute)

From a terminal in the solution folder:

```powershell
# CLI - single-file, self-contained, trimmed for fast startup
dotnet publish RamSavior.Cli -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\cli

# GUI - framework-dependent (smaller download; user needs .NET 10 Desktop Runtime)
dotnet publish RamSavior.App -c Release -r win-x64 --self-contained false -o publish\gui
```

Both `publish\cli\ramsvr.exe` and `publish\gui\RamSavior.exe` are what you'd zip up or
feed into an installer (Inno Setup / WiX / MSIX later).

## 3. Using the CLI

```powershell
ramsvr clean                       # Smart clean, human-readable output
ramsvr clean --mode full            # Full purge
ramsvr clean --json                 # Machine-readable, pipe into ConvertFrom-Json
ramsvr clean --log C:\logs\ram.ndjson
ramsvr status --watch --interval 5  # Live status loop
```

Exit codes for scripting: `0` success, `1` general failure, `2` insufficient
privileges, `3` skipped (fullscreen/presentation detected — use `--force` to override).

### Task Scheduler
Create a task pointing at `ramsvr.exe` with arguments `clean --quiet --log
C:\ProgramData\RamSavior\log.ndjson`, trigger however you like (on idle, on a timer),
and **check "Run with highest privileges"** — without that, you'll hit exit code 2.

## 6. What's new in this build: Automation, 4-tier modes, tray icon

- **Tiers**: Clean Mode is now Normal / Moderate / Advanced / Experimental. Advanced
  and Experimental both reveal the same checkbox picker (`CustomPanel`) — the
  difference is Experimental also shows per-process trimming. Both require an opt-in
  toggle in Settings before their radio button is even selectable.
- **Automation**: Settings → Automation lets you combine an interval ("every N
  minutes"), a free-memory threshold, a memory-load-% threshold, and an idle-time
  requirement — any enabled condition can fire a clean, idle/fullscreen/excluded-process
  checks can suppress one. Automation is hard-clamped to Normal or Moderate only; it
  will never run Advanced/Experimental unattended.
- **Tray icon**: closing the window now minimizes to tray instead of exiting (needed
  for automation to mean anything). Right-click → Open / Clean Now / Exit. True exit
  only happens from that menu.
- **CLI**: `--mode` now accepts `Normal` or `Moderate` (was `smart`/`full`); `--items`
  is unchanged.

## 7. Known things to double check before you rely on this

- **Package versions are placeholders.** `System.CommandLine` and `WPF-UI` both ship
  frequent updates; bump the version in the `.csproj` if NuGet restore complains.
- **`<UseWindowsForms>true</UseWindowsForms>`** was added to `RamSavior.App.csproj` for
  the tray icon (`System.Windows.Forms.NotifyIcon` — WPF has no native tray icon
  control). This pulls in WinForms/System.Drawing assemblies; it's a normal, common
  pattern for WPF apps that need a tray icon, not a mistake.
- **Memory-list command values were corrected** in an earlier round — see git history /
  prior notes. The legitimate set is `EmptyWorkingSets`, `EmptyModifiedPageList`,
  `EmptyStandbyList`, `EmptyPriority0StandbyList`, verified against Process Hacker's
  `ntexapi.h`.
- **True standby-list-size querying isn't implemented.** ISLC's dual-threshold trigger
  actually reads the real standby list byte count via `NtQuerySystemInformation` +
  `SYSTEM_MEMORY_LIST_INFORMATION` — we approximate the same spirit with free-GB and
  load-% thresholds instead, since that struct's exact layout has enough version
  variance that guessing it wrong risked corrupted reads. This is a good next step if
  you want ISLC-exact threshold behavior — flagged, not silently skipped.
- I have not compiled this on a live Windows machine — same caveat as always. If
  something doesn't compile, paste the error back and I'll fix it in the same turn.

## 9. What's new in this round (the "judge" feature batch)

- **Memory Composition bar**: RAM Savior finally shows what RAMMap always did — a live
  Active/Standby/Modified/Free/Zeroed breakdown, read via `NtQuerySystemInformation`
  (a safe QUERY, unlike the SET purge commands — worst case is a wrong number, never a
  destabilizing action).
- **Real standby-list-size threshold**: automation can now trigger on actual standby
  cache size (MB), the genuine ISLC-style dual-condition approach, not an approximation.
- **Time-of-day scheduling**: "also clean once daily at HH:mm", independent of the
  interval countdown (Wise Memory Optimizer's model).
- **Per-process auto-trim**: automation can trim any single process whose working set
  crosses a threshold, checked every 30s independent of the system-wide conditions
  (MemReduct's model), using the same safe documented `EmptyWorkingSet` API as the
  manual Experimental trim.
- **Dry-run mode**: a checkbox in the GUI and `--dry-run` in the CLI show exactly what
  would run and current stats, without touching memory.
- **Working-set leak heuristic**: flags processes whose memory has climbed steadily
  across sampled minutes. Explicitly a heuristic, not a diagnosis — see
  `ProcessMemoryTracker.cs` for the exact (honest) logic.
- **Start with Windows** + **global hotkey** (fixed Ctrl+Alt+R for now — no key-capture
  UI yet): both in Settings → Startup & Shortcuts.
- **Scheduled Task integration**, used by both the GUI toggle and the new CLI
  `ramsvr schedule install/remove` — uses `schtasks.exe` rather than a registry Run key,
  specifically because this app requires admin and Run-key entries don't reliably
  re-elevate at logon.
- **History viewer**: every manual and automated clean now logs to
  `%AppData%\RamSavior\history.ndjson`; Settings → History-icon button in the GUI shows
  the last 50.
- **First-run welcome screen**, shown once.
- **Danger Zone reset** in Settings: turns off automation/hotkey/startup task and wipes
  settings + history — not a full uninstaller, but a clean-slate reset.
- **Colored CLI output** (green/yellow/red), automatically disabled when output is
  piped/redirected so scripts never see stray ANSI codes.

## 10. Deferred, and why (I didn't fake these)

- **Update checker**: needs an actual release channel (GitHub Releases? your own
  endpoint?) to check against — nothing to point it at yet. Building the plumbing
  toward nothing would just be decorative.
- **Custom hotkey rebinding UI**: real work (key-capture, conflict detection) that
  didn't fit this round — fixed to Ctrl+Alt+R for now.
- **Full uninstaller (MSI/WiX)**: there's no installer at all yet (you're running
  published folders) — Danger Zone reset covers the "clean slate" need without pretending
  to be a real uninstaller.

## 12. This round: Focus Mode + full wide-layout redesign

- **Focus Mode (Experimental)**: pick apps to protect from a multi-select list (e.g.
  `brave`, `worldoftanks`), hit Start, and everything else gets trimmed via the same
  documented `EmptyWorkingSet` API on a repeating timer, plus a system-wide Normal
  clean each cycle to actually reclaim what was freed. Matching is by process NAME, not
  PID, so multi-process apps (most browsers) are protected in full. A short built-in
  list of core OS process names (`System`, `csrss`, `lsass`, `winlogon`, `dwm`, etc.)
  is always skipped as a courtesy — not because trimming them is dangerous, just
  pointless/usually-denied anyway.
- **Full UI redesign**: wide layout (880px) with a persistent left column (status +
  automation) and a right column that changes shape based on a new top-level **Mode**
  dropdown — Normal shows just the two safe tier radios; Advanced adds the Custom
  checkbox picker; Experimental adds Focus Mode, per-process trim, and leak detection.
  This dropdown now directly drives what were previously two separate Settings
  checkboxes (`EnableAdvancedCleaning`/`EnableExperimentalFeatures`) — those checkboxes
  were removed from Settings to avoid two controls fighting over the same state.
- **Welcome screen** now has a "Don't show this again" checkbox (checked by default).

## 14. This round: three real bugs + Focus Mode/Insights overhaul

- **Fixed: right-aligned Clean Mode radios.** `ui:CardControl`'s content wasn't
  stretching the way I assumed (no source access to know exactly why). Rather than
  guess at its template again, `MainWindow` no longer uses `CardControl`/`CardExpander`
  at all — every section is now a plain `Border` + `Grid`/`StackPanel` I fully control,
  which guarantees correct stretching regardless of WPF-UI's internal behavior. This
  also removes the unused collapse-chevrons that were taking up space for no reason.
- **Fixed: window shrinking to half-screen on Mode change.** The old `ReflowWindowSize()`
  called `SizeToContent` on every Mode/Compact-mode change — forcing that on a maximized
  window knocks it out of the maximized state, which is exactly the "shrinks for no
  reason" bug. The auto-fit-to-content logic now runs exactly once, on first launch, and
  only if the window starts in `WindowState.Normal`. After that, the window size is
  entirely yours — resizing it is on you, not automatic.
- **Wide layout, real columns.** Default width is now 1100px. Left column (status +
  Insights + automation) stays constant; middle column is Clean actions; a genuine
  fourth column appears for Experimental content instead of stacking it below and
  forcing a scroll — it only claims space when Experimental mode is selected.
- **New: Insights card** under Memory Status with three tabs — **Status** (top 3
  memory users right now), **History** (last 5 cleans, condensed), **Last Cleaned**
  (the single most recent result, prominent). Refreshes live on the Status tab; History/
  Last Cleaned refresh after any clean or on tab click.
- **Focus Mode's picker overhauled**: a search box filters the list without losing
  selections on filtered-out items; **Add Folder...** scans every `.exe` directly inside
  a chosen folder (top-level only, not recursive, to avoid accidentally sweeping in
  thousands of unrelated files from something like a Program Files root) and adds them
  pre-selected; **Add File...** adds one specific `.exe`. Both feed into the same
  dedicated list rather than a separate mechanism — an app you add by folder/file
  doesn't need to be running yet; Focus Mode will honor it by name once it does.

## 15. Known things to double check

- **Same `ControlAppearance` guess as before**, now used more (Focus Mode button,
  Insights tab buttons). If tab-switching or the Focus Mode button's color-swap throws,
  it's this enum/property naming again.
- **`FolderBrowserDialog`/`OpenFileDialog`** are fully-qualified inline
  (`System.Windows.Forms.FolderBrowserDialog`, `Microsoft.Win32.OpenFileDialog`) rather
  than via `using` statements, specifically to avoid re-triggering the `Brushes`/`Color`
  ambiguity bug in this same file. Ran the full sweep before zipping — clean.
- Folder scanning is **top-level only** by design (see above) — if you want recursive,
  say so and I'll add it as an option rather than change the default.
- Everything from prior rounds still applies. Same standing offer: paste any build
  error back and I'll fix it in the same turn.
