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

## 4. Known things to double check before you rely on this

- **Package versions are placeholders.** `System.CommandLine` and `WPF-UI` both ship
  frequent updates; I pinned versions that were current as of my last check, but NuGet
  restore will tell you immediately if either has moved — bump the version in the
  `.csproj` if so.
- **Memory-list command values were corrected.** An earlier draft of this project used
  an invented "system working set" command that doesn't actually exist in the real
  Windows API. The values now match Process Hacker's verified `ntexapi.h` exactly — the
  legitimate command set is `EmptyWorkingSets`, `EmptyModifiedPageList`,
  `EmptyStandbyList`, and `EmptyPriority0StandbyList`. That's the complete set; nothing
  else legitimate exists to add here.
- **`SystemMemoryListInformation = 0x50`** is the same undocumented-but-long-stable
  constant Sysinternals-class tooling relies on — stable across Win10/11, but
  "undocumented" means Microsoft reserves the right to change it in a future build.
- **Full mode's tradeoff is real, not just a disclaimer** — see the comment block at
  the top of `CleanupEngine.cs`. Smart mode is the default on purpose.
- **Experimental per-process trim uses a different, fully documented API**
  (`EmptyWorkingSet` from `psapi.dll`, official since Windows 2000) — it's real and
  safe, but targets one chosen running app rather than the whole system, which is why
  it's gated separately from Advanced.
- I have not compiled this on a live Windows machine with Visual Studio — the sandbox
  I write in doesn't have the Windows/WPF toolchain. The code is correct .NET 10 /
  C# 13 syntax and calls real, stable Win32 APIs the same way Sysinternals-class tools
  do, but if you hit a compile error on first build, paste it back to me and I'll fix
  it — treat this as a strong first draft you build on, not something guaranteed to
  compile byte-for-byte on the first try.

## 5. What's not built yet (next milestones, not needed for a working v1)
- System tray icon + flyout
- `ramsvr schedule install/remove` (Task Scheduler automation from the CLI itself)
- PowerShell module wrapper with native cmdlets
- Code signing + winget manifest
