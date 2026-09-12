using System.IO;
using System.Text.Json;

namespace RamSavior.App.Settings;

public static class SettingsStore
{
    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RamSavior");

    private static readonly string FilePath = Path.Combine(FolderPath, "settings.json");

    /// <summary>Shared NDJSON history log path, used by both manual cleans and automation.</summary>
    public static string HistoryLogPath => Path.Combine(FolderPath, "history.ndjson");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            // Corrupt or unreadable settings file shouldn't crash the app — fall back to defaults.
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Best-effort — not worth surfacing a dialog if AppData is somehow unwritable.
        }
    }

    /// <summary>Danger Zone reset: wipes settings.json and history.ndjson. Does not touch the
    /// installed files themselves — this isn't an uninstaller, just a clean-slate reset.</summary>
    public static void ResetToDefaultsAndDeleteHistory()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
            if (File.Exists(HistoryLogPath)) File.Delete(HistoryLogPath);
        }
        catch
        {
            // Best-effort, same as Save/Load above.
        }
    }
}
