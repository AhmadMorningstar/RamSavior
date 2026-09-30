using RamSavior.App.Settings;
using RamSavior.Core.Automation;
using RamSavior.Core.Engine;
using RamSavior.Core.Logging;

namespace RamSavior.App.Automation;

public sealed class AutomationController : IDisposable
{
    private readonly AppSettings _settings;
    private readonly AutomationEngine _engine;

    public event Action<CleanupResult>? CleanupFired;
    public event Action<string, double>? ProcessAutoTrimmed;

    public AutomationController(AppSettings settings)
    {
        _settings = settings;
        _engine = new AutomationEngine(() => _settings.Automation, RunConfiguredClean);
        _engine.CleanupFired += result =>
        {
            JsonLogger.Append(SettingsStore.HistoryLogPath, result, source: "automation");
            CleanupFired?.Invoke(result);
        };
        _engine.ProcessAutoTrimmed += (name, mb) => ProcessAutoTrimmed?.Invoke(name, mb);
    }

    private CleanupResult RunConfiguredClean()
    {
        // Custom automation items are only ever honored while the main window is in
        // Advanced or Experimental mode AND the user explicitly picked Custom in
        // Configure. If the window gets switched back to Normal mode, automation falls
        // straight back to the safe Normal/Moderate tier below, even though the saved
        // item list is left untouched (so it's there again if they switch back).
        if (_settings.AutomationTier == CleanMode.Custom &&
            _settings.EnableAdvancedCleaning &&
            _settings.AutomationCustomItems.Count > 0)
        {
            return CleanupEngine.RunCustom(new HashSet<MemoryListCommand>(_settings.AutomationCustomItems));
        }

        var tier = _settings.AutomationTier == CleanMode.Moderate ? CleanMode.Moderate : CleanMode.Normal;
        return CleanupEngine.Run(tier);
    }

    public void Start() => _engine.Start();
    public void Stop() => _engine.Stop();

    public void Dispose() => _engine.Dispose();
}
