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
        // Deliberately clamped to Normal/Moderate even if something upstream ever tries
        // to set AutomationTier to Custom — unattended automation should never run a
        // user-picked Advanced/Experimental combination.
        var tier = _settings.AutomationTier == CleanMode.Moderate ? CleanMode.Moderate : CleanMode.Normal;
        return CleanupEngine.Run(tier);
    }

    public void Start() => _engine.Start();
    public void Stop() => _engine.Stop();

    public void Dispose() => _engine.Dispose();
}
