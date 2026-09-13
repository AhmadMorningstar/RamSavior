using RamSavior.App.Settings;
using RamSavior.Core.Automation;

namespace RamSavior.App.Automation;

public sealed class FocusModeController : IDisposable
{
    private readonly AppSettings _settings;
    private readonly FocusModeEngine _engine;

    public event Action<FocusModeTickResult>? TickCompleted;

    public FocusModeController(AppSettings settings)
    {
        _settings = settings;
        _engine = new FocusModeEngine(() => new HashSet<string>(_settings.FocusMode.KeepProcessNames, StringComparer.OrdinalIgnoreCase));
        _engine.TickCompleted += result => TickCompleted?.Invoke(result);
    }

    public bool IsRunning => _engine.IsRunning;

    public void Start() => _engine.Start(TimeSpan.FromSeconds(Math.Max(5, _settings.FocusMode.IntervalSeconds)));
    public void Stop() => _engine.Stop();

    public void Dispose() => _engine.Dispose();
}
