using RamSavior.Core.Engine;
using RamSavior.Core.ProcessTrim;

namespace RamSavior.Core.Automation;

public readonly record struct FocusModeTickResult(
    int ProcessesTrimmed,
    IReadOnlyList<string> TrimmedNames,
    CleanupResult SystemCleanResult);

/// <summary>
/// The inverse of the rest of this app's approach: instead of picking what to clean,
/// the user picks what to PROTECT, and everything else gets trimmed as hard as this app
/// is willing to go, on a repeating timer while active. Built for "I'm running Brave and
/// a game, trim everything that isn't those two."
///
/// Safety notes:
/// - A short built-in list of core OS process names is always skipped, regardless of the
///   keep-list — trimming these has no real benefit and isn't worth the (usually denied
///   anyway) attempt. This is a courtesy skip, not a claim that trimming them would be
///   dangerous — EmptyWorkingSet just pages memory out, it doesn't terminate anything.
/// - Matching is by PROCESS NAME, not PID, since a multi-process app (most modern
///   browsers, for instance) runs many processes under one name — keeping "brave" means
///   keeping every brave.exe process, not just one.
/// - Each tick also runs a system-wide Normal-tier clean, so standby cache freed by all
///   those individual trims actually gets reclaimed, not just moved around.
/// </summary>
public sealed class FocusModeEngine : IDisposable
{
    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "smss", "csrss", "wininit", "services", "lsass",
        "winlogon", "dwm", "fontdrvhost", "MemCompression", "RamSavior", "ramsvr"
    };

    private readonly Func<HashSet<string>> _getKeepList;
    private Timer? _timer;

    public event Action<FocusModeTickResult>? TickCompleted;

    public FocusModeEngine(Func<HashSet<string>> getKeepList)
    {
        _getKeepList = getKeepList;
    }

    public bool IsRunning => _timer is not null;

    public void Start(TimeSpan interval)
    {
        _timer ??= new Timer(_ => SafeTick(), null, TimeSpan.Zero, interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void SafeTick()
    {
        try { Tick(); }
        catch
        {
            // Never let a bad tick take down the host app — retried on the next interval.
        }
    }

    private void Tick()
    {
        var keep = _getKeepList();
        var trimmedNames = new List<string>();

        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (ProtectedProcessNames.Contains(process.ProcessName)) continue;
                if (keep.Contains(process.ProcessName)) continue;

                var (success, _) = ProcessTrimmer.TrimProcess(process.Id);
                if (success) trimmedNames.Add(process.ProcessName);
            }
            catch
            {
                // Access denied / already exited — skip and move on.
            }
            finally
            {
                process.Dispose();
            }
        }

        var systemResult = CleanupEngine.Run(CleanMode.Normal);

        TickCompleted?.Invoke(new FocusModeTickResult(trimmedNames.Count, trimmedNames, systemResult));
    }

    public void Dispose() => Stop();
}
