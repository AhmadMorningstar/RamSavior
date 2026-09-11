using System.CommandLine;
using System.Text.Json;
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

var rootCommand = new RootCommand(
    "RAM Savior (ramsvr) — Windows memory cleaner.\n" +
    "Quick start:\n" +
    "  ramsvr clean              Run a Smart (Normal-tier) clean, safe defaults.\n" +
    "  ramsvr status --watch     Watch live memory stats.\n" +
    "  ramsvr clean --help       See every cleaning option, explained.\n" +
    "Every command below has its own --help with plain-language descriptions.");

// ---- clean ----
var cleanCommand = new Command("clean", "Run a memory cleanup pass.");
cleanCommand.Options.Add(modeOption);
cleanCommand.Options.Add(jsonOption);
cleanCommand.Options.Add(quietOption);
cleanCommand.Options.Add(forceOption);
cleanCommand.Options.Add(logOption);
cleanCommand.Options.Add(itemsOption);

cleanCommand.SetAction(parseResult =>
{
    var mode = parseResult.GetValue(modeOption);
    bool json = parseResult.GetValue(jsonOption);
    bool quiet = parseResult.GetValue(quietOption);
    bool force = parseResult.GetValue(forceOption);
    string? logPath = parseResult.GetValue(logOption);
    string? itemsRaw = parseResult.GetValue(itemsOption);

    if (!force && MemoryStatus.IsUserBusyOrFullscreen())
    {
        if (json)
            Console.WriteLine(JsonSerializer.Serialize(new { skipped = true, reason = "fullscreen_or_presentation" }));
        else if (!quiet)
            Console.WriteLine("Skipped: fullscreen app or presentation mode detected. Use --force to override.");

        return ExitSkipped;
    }

    CleanupResult result;

    if (!string.IsNullOrWhiteSpace(itemsRaw))
    {
        var selected = new HashSet<MemoryListCommand>();
        foreach (var token in itemsRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse<MemoryListCommand>(token, ignoreCase: true, out var parsed))
            {
                Console.Error.WriteLine($"Unrecognized item '{token}'. Valid values: " +
                    string.Join(", ", Enum.GetNames<MemoryListCommand>()));
                return ExitGeneralFailure;
            }
            selected.Add(parsed);
        }

        result = CleanupEngine.RunCustom(selected);
    }
    else
    {
        result = CleanupEngine.Run(mode);
    }

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
            Console.WriteLine($"RAM Savior — {result.Mode} clean complete in {result.Duration.TotalMilliseconds:F0}ms");
            Console.WriteLine($"  Available before: {result.BeforeAvailableGB:F2} GB");
            Console.WriteLine($"  Available after:  {result.AfterAvailableGB:F2} GB");
            Console.WriteLine($"  Freed:            {result.FreedGB:F2} GB");
        }
        else
        {
            Console.Error.WriteLine($"RAM Savior — cleanup failed: {result.Error}");
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

rootCommand.Subcommands.Add(cleanCommand);
rootCommand.Subcommands.Add(statusCommand);

return await rootCommand.Parse(args).InvokeAsync();
