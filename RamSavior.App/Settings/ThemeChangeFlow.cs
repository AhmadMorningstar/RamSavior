using System.Windows;

namespace RamSavior.App.Settings;

/// <summary>
/// The one path every appearance change goes through - Light/Dark (Settings radios and the
/// quick toggle on the main window) and accent color (presets, custom picker, reset) - so
/// they all behave identically:
///
///   1. the choice is saved, but NOTHING on screen changes;
///   2. if the look would actually differ, a "restart required" window is shown;
///   3. only "Restart now" applies it - and "restart" here means the existing window
///      rebuild, not a process restart, so the tray icon, automation and Focus-Mode
///      settings are untouched.
///
/// "Later" leaves the choice saved; it simply takes effect on the next launch. If a theme
/// change and an accent change are both pending, one restart applies both.
/// </summary>
internal static class ThemeChangeFlow
{
    /// <returns>True when the caller must now rebuild its windows (the change was
    /// committed and applied). False when nothing visible needs to change yet.</returns>
    public static bool RequestChange(Window owner, AppSettings settings, ThemeChoice choice)
    {
        settings.Theme = choice;
        return SaveAndPrompt(owner, settings);
    }

    /// <inheritdoc cref="RequestChange(Window, AppSettings, ThemeChoice)"/>
    public static bool RequestAccentChange(Window owner, AppSettings settings, string accentHex)
    {
        settings.AccentColorHex = accentHex;
        return SaveAndPrompt(owner, settings);
    }

    private static bool SaveAndPrompt(Window owner, AppSettings settings)
    {
        SettingsStore.Save(settings);

        // Picked something that looks identical to what's showing (e.g. "Follow system"
        // while Windows is already dark) - or switched back to the current look after an
        // earlier pending change. Nothing to restart for.
        if (!ThemeApplier.IsPendingRestart(settings))
            return false;

        var dialog = new RestartRequiredWindow { Owner = owner };
        dialog.ShowDialog();

        if (!dialog.RestartRequested)
            return false;

        ThemeApplier.CommitPending(settings);
        ThemeApplier.Apply(settings);
        return true;
    }
}
