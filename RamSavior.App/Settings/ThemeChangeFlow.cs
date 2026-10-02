using System.Windows;

namespace RamSavior.App.Settings;

/// <summary>
/// The one path every Light/Dark change goes through (Settings radios and the quick
/// toggle on the main window), so they behave identically:
///
///   1. the choice is saved, but NOTHING on screen changes;
///   2. if the look would actually differ, a "restart required" window is shown;
///   3. only "Restart now" applies the theme — and "restart" here means the existing
///      window rebuild, not a process restart, so the tray icon, automation and
///      Focus-Mode settings are untouched.
///
/// "Later" leaves the choice saved; it simply takes effect on the next launch.
/// </summary>
internal static class ThemeChangeFlow
{
    /// <returns>True when the caller must now rebuild its windows (the theme was
    /// committed and applied). False when nothing visible needs to change.</returns>
    public static bool RequestChange(Window owner, AppSettings settings, ThemeChoice choice)
    {
        settings.Theme = choice;
        SettingsStore.Save(settings);

        // Picked something that looks identical to what's showing (e.g. "Follow system"
        // while Windows is already dark) — or switched back to the current look after
        // an earlier pending change. Nothing to restart for.
        if (!ThemeApplier.IsPendingRestart(settings))
        {
            ThemeApplier.CommitPending(settings);
            return false;
        }

        var dialog = new RestartRequiredWindow { Owner = owner };
        dialog.ShowDialog();

        if (!dialog.RestartRequested)
            return false;

        ThemeApplier.CommitPending(settings);
        ThemeApplier.Apply(settings);
        return true;
    }
}
