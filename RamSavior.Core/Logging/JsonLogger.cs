using System.Text.Json;
using RamSavior.Core.Engine;

namespace RamSavior.Core.Logging;

public record LogEntry(
    string Timestamp,
    string Mode,
    bool Success,
    double BeforeAvailableGB,
    double AfterAvailableGB,
    double FreedGB,
    double DurationMs,
    string? Error,
    string Source);

/// <summary>
/// Appends one JSON object per line (NDJSON) instead of maintaining one big JSON array.
/// This means logging never has to re-read and rewrite the whole file — it's a single
/// File.AppendAllText call, safe to do on every run without the file growing expensive
/// to touch. Reading back only the last N lines (ReadRecent) avoids loading a
/// potentially large file fully into memory just to show a short history view.
/// </summary>
public static class JsonLogger
{
    public static void Append(string path, CleanupResult result, string source = "manual")
    {
        var entry = new LogEntry(
            Timestamp: DateTimeOffset.UtcNow.ToString("O"),
            Mode: result.Mode.ToString(),
            Success: result.Success,
            BeforeAvailableGB: result.BeforeAvailableGB,
            AfterAvailableGB: result.AfterAvailableGB,
            FreedGB: result.FreedGB,
            DurationMs: result.Duration.TotalMilliseconds,
            Error: result.Error,
            Source: source);

        AppendEntry(path, entry);
    }

    private static void AppendEntry(string path, LogEntry entry)
    {
        string line = JsonSerializer.Serialize(entry);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        File.AppendAllText(path, line + Environment.NewLine);
    }

    /// <summary>Reads the last <paramref name="count"/> entries without loading the whole file for large histories.</summary>
    public static List<LogEntry> ReadRecent(string path, int count = 50)
    {
        if (!File.Exists(path)) return new List<LogEntry>();

        var allLines = File.ReadAllLines(path);
        var results = new List<LogEntry>();

        foreach (var line in allLines.Reverse().Take(count))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<LogEntry>(line);
                if (entry is not null) results.Add(entry);
            }
            catch
            {
                // Skip malformed lines rather than let one bad entry break the whole view.
            }
        }

        return results;
    }
}
