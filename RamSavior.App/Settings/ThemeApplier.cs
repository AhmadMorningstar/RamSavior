using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace RamSavior.App.Settings;

public static class ThemeApplier
{
    public static void Apply(AppSettings settings)
    {
        ApplicationTheme theme = settings.Theme switch
        {
            ThemeChoice.Light => ApplicationTheme.Light,
            ThemeChoice.Dark => ApplicationTheme.Dark,
            _ => IsWindowsUsingLightTheme() ? ApplicationTheme.Light : ApplicationTheme.Dark
        };

        // updateAccent:false — we set the accent ourselves right after, and letting this
        // call also touch accent resources just adds another order-of-operations question
        // on top of the known library bug below.
        ApplicationThemeManager.Apply(theme, Wpf.Ui.Controls.WindowBackdropType.Mica, updateAccent: false);

        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.AccentColorHex)!;
        ApplicationAccentColorManager.Apply(color, theme, false);

        // --- Workaround for a known open WPF-UI bug (lepoco/wpfui#1481): the built-in
        // accent manager sets a different set of resource keys than what the actual
        // Fluent control styles (Accent.xaml) read from, so some controls silently never
        // pick up the change. We set the commonly-referenced keys ourselves as a direct
        // override — safe even if a key name turns out unused by this WPF-UI version,
        // since an unmatched dictionary key is simply ignored, not an error. ---
        var brush = new SolidColorBrush(color);
        var app = System.Windows.Application.Current;
        app.Resources["SystemAccentColor"] = color;
        app.Resources["SystemAccentColorPrimary"] = color;
        app.Resources["SystemAccentColorSecondary"] = color;
        app.Resources["SystemAccentColorTertiary"] = color;
        app.Resources["SystemAccentBrush"] = brush;
        app.Resources["AccentFillColorDefaultBrush"] = brush;
        app.Resources["AccentFillColorSecondaryBrush"] = brush;
        app.Resources["AccentFillColorTertiaryBrush"] = brush;
        app.Resources["AccentTextFillColorPrimaryBrush"] = brush;

        // Force every currently-open window to re-pull resources rather than waiting on
        // DynamicResource propagation, which is exactly what's failing per the bug above.
        //
        // On top of that: switching the Mica backdrop a second time is a separate,
        // documented WPF-UI bug (lepoco/wpfui#927, #1193 — "switch theme twice and Mica
        // breaks"). The window's backdrop composition gets left in a stale state that a
        // plain re-apply doesn't clear, so every switch after the first renders wrong.
        // The library's own fix for this (PR #1094) is to explicitly tear the backdrop
        // down before reapplying it rather than just reapplying on top of whatever's
        // already attached — do the same here defensively, since it's a harmless no-op
        // if this WPF-UI version already handles it internally.
        const Wpf.Ui.Controls.WindowBackdropType backdrop = Wpf.Ui.Controls.WindowBackdropType.Mica;
        foreach (Window window in app.Windows)
        {
            Wpf.Ui.Controls.WindowBackdrop.RemoveBackdrop(window);
            ApplicationThemeManager.Apply(window);
            Wpf.Ui.Controls.WindowBackdrop.ApplyBackdrop(window, backdrop);
        }
    }

    /// <summary>
    /// Reads the same registry value Windows itself uses for app light/dark mode,
    /// instead of relying on a WPF-UI helper method whose exact name can vary by
    /// version — this is a value Windows has kept stable since Windows 10 1607.
    /// </summary>
    /// <summary>Internal (not private) so MainWindow's quick light/dark toggle can resolve
    /// what "System" currently resolves to, without depending on a WPF-UI query API.</summary>
    internal static bool IsWindowsUsingLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return (int)(key?.GetValue("AppsUseLightTheme") ?? 0) == 1;
        }
        catch
        {
            return false; // default to dark if the read fails for any reason
        }
    }
}
