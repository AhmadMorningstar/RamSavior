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
}
