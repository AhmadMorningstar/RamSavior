using System.Diagnostics;
using RamSavior.Core.Engine;
using RamSavior.Core.Monitoring;

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

    public event Action<CleanupResult>? CleanupFired;
    public event Action<string>? Skipped;

    public AutomationEngine(Func<AutomationTrigger> getTrigger, Func<CleanupResult> cleanupAction)
    {
        _getTrigger = getTrigger;
        _cleanupAction = cleanupAction;
    }

    public void Start()
    {
        // Poll every 30s regardless of the user's interval setting — this is just how
        // often we CHECK conditions, not how often we clean. Keeps threshold/idle
        // conditions responsive without spinning a tight loop.
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

        if (trigger.RequireIdleMinutes > 0 && MemoryStatus.GetIdleTime() < TimeSpan.FromMinutes(trigger.RequireIdleMinutes))
        {
            Skipped?.Invoke("not_idle");
            return;
        }

        var reading = MemoryStatus.Read();

        bool intervalDue = trigger.IntervalEnabled &&
            (DateTime.UtcNow - _lastCleanUtc) >= TimeSpan.FromMinutes(trigger.IntervalMinutes);

        bool freeMemDue = trigger.FreeMemoryThresholdEnabled &&
            reading.AvailablePhysicalGB <= trigger.FreeMemoryBelowGB;

        bool loadPercentDue = trigger.LoadPercentThresholdEnabled &&
            reading.MemoryLoadPercent >= trigger.LoadAbovePercent;

        if (!intervalDue && !freeMemDue && !loadPercentDue) return;

        var result = _cleanupAction();
        _lastCleanUtc = DateTime.UtcNow;
        CleanupFired?.Invoke(result);
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
