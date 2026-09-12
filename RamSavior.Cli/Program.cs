using System.CommandLine;
using System.Text.Json;
using RamSavior.Core.Automation;
using RamSavior.Core.Engine;
using RamSavior.Core.Logging;
using RamSavior.Core.Monitoring;

// Exit codes — automation (Task Scheduler, PowerShell scripts) branches on these:
//   0 = success
//   1 = general failure
//   2 = insufficient privileges
//   3 = skipped (excluded/busy state active — not an error)
const int ExitOk = 0;
const int ExitGeneralFailure = 1;
const int ExitInsufficientPrivileges = 2;
const int ExitSkipped = 3;

// Colored output only when writing to a real console — piping to a file or another
// program should get plain text, not ANSI escape codes mixed into the data.
bool SupportsColor() => !Console.IsOutputRedirected;
void WriteColored(string text, ConsoleColor color)
{
    if (!SupportsColor()) { Console.WriteLine(text); return; }
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.WriteLine(text);
    Console.ForegroundColor = prev;
}
void WriteSuccess(string text) => WriteColored(text, ConsoleColor.Green);
void WriteWarn(string text) => WriteColored(text, ConsoleColor.Yellow);
void WriteFail(string text) => WriteColored(text, ConsoleColor.Red);

var jsonOption = new Option<bool>("--json", "-j")
{
    Description = "Emit machine-readable JSON instead of human-readable text."
};

var modeOption = new Option<CleanMode>("--mode", "-m")
{
    Description = "Cleanup tier. 'normal' targets low-reuse standby cache only (default, recommended, safe for automation). " +
                   "'moderate' also flushes working sets for a bit more freed memory. Use --items for full manual control.",
    DefaultValueFactory = _ => CleanMode.Normal
};

var quietOption = new Option<bool>("--quiet", "-q")
{
    Description = "Suppress non-essential output."
};

var forceOption = new Option<bool>("--force", "-f")
{
    Description = "Run even if a fullscreen app or presentation mode is detected."
};

var logOption = new Option<string?>("--log")
{
    Description = "Path to an NDJSON log file to append this run's result to."
};

var itemsOption = new Option<string?>("--items")
{
    Description = "Comma-separated list of specific purge commands to run instead of --mode " +
                   "(e.g. EmptyWorkingSets,EmptyPriority0StandbyList). Overrides --mode when set. " +
                   "Valid values: EmptyWorkingSets, EmptyModifiedPageList, " +
                   "EmptyStandbyList, EmptyPriority0StandbyList."
};

var dryRunOption = new Option<bool>("--dry-run")
{
    Description = "Show what would run and current memory stats, without changing anything."
};

var rootCommand = new RootCommand(
    "RAM Savior (ramsvr) — Windows memory cleaner.\n" +
    "Quick start:\n" +
    "  ramsvr clean              Run a Smart (Normal-tier) clean, safe defaults.\n" +
    "  ramsvr clean --dry-run    See what a clean would do without doing it.\n" +
    "  ramsvr status --watch     Watch live memory stats.\n" +
    "  ramsvr schedule install --trigger startup   Run at every login.\n" +
    "Every command below has its own --help with plain-language descriptions.");

// ---- clean ----
var cleanCommand = new Command("clean", "Run a memory cleanup pass.");
cleanCommand.Options.Add(modeOption);
cleanCommand.Options.Add(jsonOption);
cleanCommand.Options.Add(quietOption);
cleanCommand.Options.Add(forceOption);
cleanCommand.Options.Add(logOption);
cleanCommand.Options.Add(itemsOption);
cleanCommand.Options.Add(dryRunOption);

cleanCommand.SetAction(parseResult =>
{
    var mode = parseResult.GetValue(modeOption);
    bool json = parseResult.GetValue(jsonOption);
    bool quiet = parseResult.GetValue(quietOption);
    bool force = parseResult.GetValue(forceOption);
    string? logPath = parseResult.GetValue(logOption);
    string? itemsRaw = parseResult.GetValue(itemsOption);
    bool dryRun = parseResult.GetValue(dryRunOption);

    if (!force && MemoryStatus.IsUserBusyOrFullscreen())
    {
        if (json)
            Console.WriteLine(JsonSerializer.Serialize(new { skipped = true, reason = "fullscreen_or_presentation" }));
        else if (!quiet)
            WriteWarn("Skipped: fullscreen app or presentation mode detected. Use --force to override.");

        return ExitSkipped;
    }

    HashSet<MemoryListCommand>? selected = null;
    if (!string.IsNullOrWhiteSpace(itemsRaw))
    {
        selected = new HashSet<MemoryListCommand>();
        foreach (var token in itemsRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse<MemoryListCommand>(token, ignoreCase: true, out var parsed))
            {
                WriteFail($"Unrecognized item '{token}'. Valid values: " + string.Join(", ", Enum.GetNames<MemoryListCommand>()));
                return ExitGeneralFailure;
            }
            selected.Add(parsed);
        }
    }

    if (dryRun)
    {
        var preview = selected is not null ? CleanupEngine.PreviewCustom(selected) : CleanupEngine.Preview(mode);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                dryRun = true,
                mode = preview.Mode.ToString(),
                wouldRun = preview.WouldRun.Select(i => i.Title),
                currentAvailableGB = preview.CurrentAvailableGB,
                currentStandbyMB = preview.CurrentStandbyMB,
                privilegesAvailable = preview.PrivilegesAvailable,
                privilegeNote = preview.PrivilegeNote
            }));
        }
        else if (!quiet)
        {
            Console.WriteLine("Dry run — nothing was changed.");
            Console.WriteLine($"  Would run: {(preview.WouldRun.Count == 0 ? "(nothing selected)" : string.Join(", ", preview.WouldRun.Select(i => i.Title)))}");
            Console.WriteLine($"  Currently available: {preview.CurrentAvailableGB:F2} GB");
            if (preview.CurrentStandbyMB >= 0) Console.WriteLine($"  Currently in standby: {preview.CurrentStandbyMB:F0} MB");
            if (preview.PrivilegesAvailable) WriteSuccess("  Privileges: OK"); else WriteFail($"  Privileges: {preview.PrivilegeNote}");
        }

        return ExitOk;
    }

    var result = selected is not null ? CleanupEngine.RunCustom(selected) : CleanupEngine.Run(mode);

    if (logPath is not null)
        JsonLogger.Append(logPath, result);

    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            success = result.Success,
            mode = result.Mode.ToString(),
            beforeAvailableGB = result.BeforeAvailableGB,
            afterAvailableGB = result.AfterAvailableGB,
            freedGB = result.FreedGB,
            durationMs = result.Duration.TotalMilliseconds,
            error = result.Error
        }));
    }
    else if (!quiet)
    {
        if (result.Success)
        {
            WriteSuccess($"RAM Savior — {result.Mode} clean complete in {result.Duration.TotalMilliseconds:F0}ms");
            Console.WriteLine($"  Available before: {result.BeforeAvailableGB:F2} GB");
            Console.WriteLine($"  Available after:  {result.AfterAvailableGB:F2} GB");
            Console.WriteLine($"  Freed:            {result.FreedGB:F2} GB");
        }
        else
        {
            WriteFail($"RAM Savior — cleanup failed: {result.Error}");
        }
    }

    if (!result.Success)
    {
        bool privilegeIssue = result.Error?.Contains("Privilege", StringComparison.OrdinalIgnoreCase) == true;
        return privilegeIssue ? ExitInsufficientPrivileges : ExitGeneralFailure;
    }

    return ExitOk;
});

// ---- status ----
var watchOption = new Option<bool>("--watch", "-w") { Description = "Continuously print status until Ctrl+C." };
var intervalOption = new Option<int>("--interval") { Description = "Seconds between readings in --watch mode.", DefaultValueFactory = _ => 3 };

var statusCommand = new Command("status", "Show current memory status.");
statusCommand.Options.Add(jsonOption);
statusCommand.Options.Add(watchOption);
statusCommand.Options.Add(intervalOption);

statusCommand.SetAction(parseResult =>
{
    bool json = parseResult.GetValue(jsonOption);
    bool watch = parseResult.GetValue(watchOption);
    int interval = parseResult.GetValue(intervalOption);

    do
    {
        var reading = MemoryStatus.Read();

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                totalGB = Math.Round(reading.TotalPhysicalGB, 2),
                availableGB = Math.Round(reading.AvailablePhysicalGB, 2),
                loadPercent = reading.MemoryLoadPercent,
                timestamp = DateTimeOffset.UtcNow.ToString("O")
            }));
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {reading.AvailablePhysicalGB:F2} GB free of {reading.TotalPhysicalGB:F2} GB  ({reading.MemoryLoadPercent}% load)");
        }

        if (watch) Thread.Sleep(TimeSpan.FromSeconds(interval));
    }
    while (watch);

    return ExitOk;
});

// ---- schedule ----
const string CliScheduledTaskName = "RamSaviorCliSchedule";

var triggerOption = new Option<string>("--trigger")
{
    Description = "'startup' runs at every login. 'interval' runs every --interval-minutes.",
    DefaultValueFactory = _ => "startup"
};
var intervalMinutesOption = new Option<int>("--interval-minutes") { DefaultValueFactory = _ => 30 };
var scheduleModeOption = new Option<CleanMode>("--mode") { DefaultValueFactory = _ => CleanMode.Normal };

var scheduleCommand = new Command("schedule", "Manage a Windows Scheduled Task that runs ramsvr automatically.");

var scheduleInstallCommand = new Command("install", "Create or update the scheduled task.");
scheduleInstallCommand.Options.Add(triggerOption);
scheduleInstallCommand.Options.Add(intervalMinutesOption);
scheduleInstallCommand.Options.Add(scheduleModeOption);

scheduleInstallCommand.SetAction(parseResult =>
{
    string trigger = parseResult.GetValue(triggerOption)!;
    int intervalMinutes = parseResult.GetValue(intervalMinutesOption);
    var mode = parseResult.GetValue(scheduleModeOption);

    string exePath = Environment.ProcessPath ?? string.Empty;
    string args = $"clean --mode {mode} --quiet";

    var (success, error) = trigger.Equals("interval", StringComparison.OrdinalIgnoreCase)
        ? TaskSchedulerIntegration.InstallIntervalTask(CliScheduledTaskName, exePath, args, intervalMinutes)
        : TaskSchedulerIntegration.InstallStartupTask(CliScheduledTaskName, exePath, args);

    if (success) { WriteSuccess($"Scheduled task '{CliScheduledTaskName}' installed ({trigger})."); return ExitOk; }

    WriteFail($"Failed to install scheduled task: {error}");
    return ExitGeneralFailure;
});

var scheduleRemoveCommand = new Command("remove", "Remove the scheduled task.");
scheduleRemoveCommand.SetAction(_ =>
{
    var (success, error) = TaskSchedulerIntegration.RemoveTask(CliScheduledTaskName);
    if (success) { WriteSuccess($"Scheduled task '{CliScheduledTaskName}' removed."); return ExitOk; }

    WriteFail($"Failed to remove scheduled task: {error}");
    return ExitGeneralFailure;
});

scheduleCommand.Subcommands.Add(scheduleInstallCommand);
scheduleCommand.Subcommands.Add(scheduleRemoveCommand);

rootCommand.Subcommands.Add(cleanCommand);
rootCommand.Subcommands.Add(statusCommand);
rootCommand.Subcommands.Add(scheduleCommand);

return await rootCommand.Parse(args).InvokeAsync();
