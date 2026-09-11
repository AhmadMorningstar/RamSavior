using System.Diagnostics;
using System.Runtime.InteropServices;
using RamSavior.Core.Monitoring;
using RamSavior.Core.Native;
using RamSavior.Core.Privileges;

namespace RamSavior.Core.Engine;

public readonly record struct CleanupResult(
    bool Success,
    CleanMode Mode,
    double BeforeAvailableGB,
    double AfterAvailableGB,
    double FreedGB,
    TimeSpan Duration,
    string? Error);

/// <summary>
/// Orchestrates the actual purge. Two things worth calling out:
///
/// 1. Sequencing matters. Evicting the standby list before flushing working sets misses
///    memory that working-set flush was about to release. Order below is deliberate.
///
/// 2. "Smart" mode is the honest default. Full-purging the entire standby list forces
///    Windows to reload commonly-used DLLs/files from disk on next access — that's the
///    exact stutter this tool is supposed to prevent, not cause. Priority-0-only targets
///    cache least likely to be reused, so it frees memory with much lower reload risk.
///    Full mode still exists because sometimes you genuinely want max memory back (e.g.
///    before launching a memory-hungry app) — but it's an explicit user choice, not the
///    default, and the CLI/GUI should say so out loud.
/// </summary>
public static class CleanupEngine
{
    public static CleanupResult Run(CleanMode mode)
    {
        var commands = mode switch
        {
            CleanMode.Moderate => new[] { MemoryListCommand.EmptyWorkingSets, MemoryListCommand.EmptyPriority0StandbyList },
            _ => new[] { MemoryListCommand.EmptyPriority0StandbyList }
        };

        return RunCommands(commands, mode);
    }

    /// <summary>
    /// Runs an arbitrary user-picked set of purge commands (Custom mode). Commands are
    /// executed in MemoryCommandCatalog.CanonicalOrder regardless of the order they were
    /// selected in — sequencing correctness isn't something the user should have to think
    /// about when ticking boxes.
    /// </summary>
    public static CleanupResult RunCustom(IReadOnlySet<MemoryListCommand> selected)
    {
        if (selected.Count == 0)
        {
            return new CleanupResult(false, CleanMode.Custom, 0, 0, 0, TimeSpan.Zero,
                "No items selected — pick at least one cleanup action.");
        }

        var ordered = MemoryCommandCatalog.CanonicalOrder.Where(selected.Contains).ToArray();
        return RunCommands(ordered, CleanMode.Custom);
    }

    private static CleanupResult RunCommands(IReadOnlyList<MemoryListCommand> commands, CleanMode mode)
    {
        var sw = Stopwatch.StartNew();

        var (privilegesOk, privilegeError) = PrivilegeManager.EnableRequiredPrivileges();
        if (!privilegesOk)
        {
            return new CleanupResult(false, mode, 0, 0, 0, sw.Elapsed,
                $"Privilege enable failed: {privilegeError} (run elevated / as Administrator).");
        }

        double before = MemoryStatus.Read().AvailablePhysicalGB;

        try
        {
            foreach (var command in commands)
                Execute(command);
        }
        catch (InvalidOperationException ex)
        {
            return new CleanupResult(false, mode, before, before, 0, sw.Elapsed, ex.Message);
        }

        double after = WaitForSettle(TimeSpan.FromMilliseconds(800));
        sw.Stop();

        double freed = Math.Max(0, Math.Round(after - before, 2));

        return new CleanupResult(true, mode, Math.Round(before, 2), Math.Round(after, 2), freed, sw.Elapsed, null);
    }

    private static void Execute(MemoryListCommand command)
    {
        IntPtr pCommand = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(pCommand, (int)command);
            int status = NativeMethods.NtSetSystemInformation(
                NativeMethods.SystemMemoryListInformation, pCommand, sizeof(int));

            if (status != 0)
            {
                throw new InvalidOperationException(
                    $"NtSetSystemInformation({command}) returned NTSTATUS 0x{status:X8}.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pCommand);
        }
    }

    /// <summary>
    /// Bounded polling instead of a fixed Thread.Sleep — we said we wouldn't repeat the
    /// "arbitrary Start-Sleep" mistake we called out in competing tools, so this doesn't
    /// either. Stops early once readings stabilize, caps out at the timeout regardless.
    /// </summary>
    private static double WaitForSettle(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        double last = MemoryStatus.Read().AvailablePhysicalGB;

        while (sw.Elapsed < timeout)
        {
            Thread.Sleep(50);
            double now = MemoryStatus.Read().AvailablePhysicalGB;
            if (Math.Abs(now - last) < 0.01) return now; // stabilized within ~10MB
            last = now;
        }

        return last;
    }
}
