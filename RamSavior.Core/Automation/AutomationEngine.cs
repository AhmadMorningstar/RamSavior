using System.Diagnostics;
using RamSavior.Core.Engine;
using RamSavior.Core.Monitoring;
using RamSavior.Core.ProcessTrim;

namespace RamSavior.Core.Automation;

/// <summary>
/// Polls memory/idle state on a short interval (independent of the user's configured
/// clean interval) and decides whether any trigger condition is met. Deliberately NOT
/// tied to WPF — this can be driven by a plain System.Threading.Timer from the GUI, or
/// in principle a future background service, without dragging in UI dependencies.
/// </summary>
public sealed class AutomationEngine : IDisposable
{
    private readonly Func<CleanupResult> _cleanupAction;
    private readonly Func<AutomationTrigger> _getTrigger;
    private Timer? _pollTimer;
    private DateTime _lastCleanUtc = DateTime.MinValue;
    private DateOnly _lastTimeOfDayRunDate = DateOnly.MinValue;

    public event Action<CleanupResult>? CleanupFired;
    public event Action<string, double>? ProcessAutoTrimmed;
    public event Action<string>? Skipped;

    public AutomationEngine(Func<AutomationTrigger> getTrigger, Func<CleanupResult> cleanupAction)
    {
        _getTrigger = getTrigger;
        _cleanupAction = cleanupAction;
    }

    public void Start()
    {
        _pollTimer ??= new Timer(_ => SafeTick(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
    }

    public void Stop()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private void SafeTick()
    {
        try { Tick(); }
        catch
        {
            // Automation must never crash the host app — a bad tick just gets retried
            // on the next poll.
        }
    }

    private void Tick()
    {
        var trigger = _getTrigger();
        if (!trigger.Enabled) return;

        if (MemoryStatus.IsUserBusyOrFullscreen())
        {
            Skipped?.Invoke("fullscreen_or_presentation");
            return;
        }

        if (trigger.ExcludedProcessNames.Count > 0 && IsAnyExcludedProcessRunning(trigger.ExcludedProcessNames))
        {
            Skipped?.Invoke("excluded_process_running");
            return;
        }

        bool idleOk = trigger.RequireIdleMinutes == 0 ||
            MemoryStatus.GetIdleTime() >= TimeSpan.FromMinutes(trigger.RequireIdleMinutes);

        // Per-process auto-trim runs independently of the idle gate and the system-wide
        // conditions below — it's a much smaller, targeted action (one process's working
        // set), not a system-wide purge, so it doesn't need the same caution.
        if (trigger.PerProcessAutoTrimEnabled)
            RunPerProcessAutoTrim(trigger.PerProcessAutoTrimAboveMB);

        if (!idleOk)
        {
            Skipped?.Invoke("not_idle");
            return;
        }

        var reading = MemoryStatus.Read();
        var composition = MemoryCompositionReader.TryRead();

        bool intervalDue = trigger.IntervalEnabled &&
            (DateTime.UtcNow - _lastCleanUtc) >= TimeSpan.FromMinutes(trigger.IntervalMinutes);

        bool freeMemDue = trigger.FreeMemoryThresholdEnabled &&
            reading.AvailablePhysicalGB <= trigger.FreeMemoryBelowGB;

        bool loadPercentDue = trigger.LoadPercentThresholdEnabled &&
            reading.MemoryLoadPercent >= trigger.LoadAbovePercent;

        bool standbyDue = trigger.StandbyListThresholdEnabled &&
            composition is not null &&
            composition.Value.StandbyTotalMB >= trigger.StandbyListAboveMB;

        bool timeOfDayDue = trigger.TimeOfDayEnabled && IsTimeOfDayDue(trigger.TimeOfDay);

        if (!intervalDue && !freeMemDue && !loadPercentDue && !standbyDue && !timeOfDayDue) return;

        var result = _cleanupAction();
        _lastCleanUtc = DateTime.UtcNow;
        if (timeOfDayDue) _lastTimeOfDayRunDate = DateOnly.FromDateTime(DateTime.Now);
        CleanupFired?.Invoke(result);
    }

    private bool IsTimeOfDayDue(TimeSpan configuredTime)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_lastTimeOfDayRunDate == today) return false; // already fired today

        var now = DateTime.Now.TimeOfDay;
        // Fires once we've passed the configured time, within a 5-minute window, so we
        // don't miss it if a poll happens to land slightly after the mark.
        return now >= configuredTime && now < configuredTime.Add(TimeSpan.FromMinutes(5));
    }

    private void RunPerProcessAutoTrim(double thresholdMB)
    {
        foreach (var proc in ProcessTrimmer.GetTopProcessesByMemory(count: 10))
        {
            if (proc.WorkingSetMB < thresholdMB) continue;

            var (success, _) = ProcessTrimmer.TrimProcess(proc.Pid);
            if (success) ProcessAutoTrimmed?.Invoke(proc.Name, proc.WorkingSetMB);
        }
    }

    private static bool IsAnyExcludedProcessRunning(List<string> excludedNames)
    {
        foreach (var name in excludedNames)
        {
            var trimmed = name.Trim();
            if (trimmed.Length == 0) continue;

            var processes = Process.GetProcessesByName(trimmed);
            bool found = processes.Length > 0;
            foreach (var p in processes) p.Dispose();

            if (found) return true;
        }
        return false;
    }

    public void Dispose() => Stop();
}
