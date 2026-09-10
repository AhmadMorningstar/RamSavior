namespace RamSavior.App.Settings;

public enum ThemeChoice
{
    System,
    Light,
    Dark
}

public class AppSettings
{
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;

    /// <summary>Hex string, e.g. "#FFB800". Defaults to the amber you already have.</summary>
    public string AccentColorHex { get; set; } = "#FFB800";

    /// <summary>
    /// Gates the "advanced" purge items (full standby purge, modified-page flush, system
    /// working set) in Custom mode. Off by default — these are the options most likely
    /// to cause a brief stutter, so a user should opt in deliberately.
    /// </summary>
    public bool EnableAdvancedCleaning { get; set; } = false;
}
