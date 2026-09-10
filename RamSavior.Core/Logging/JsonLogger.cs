using System.Text.Json;
using RamSavior.Core.Engine;

namespace RamSavior.Core.Logging;

/// <summary>
/// Appends one JSON object per line (NDJSON) instead of maintaining one big JSON array.
/// This means logging never has to re-read and rewrite the whole file — it's a single
/// File.AppendAllText call, safe to do on every run without the file growing expensive
/// to touch.
/// </summary>
public static class JsonLogger
{
    public static void Append(string path, CleanupResult result)
    {
        var entry = new
        {
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
            mode = result.Mode.ToString(),
            success = result.Success,
            beforeAvailableGB = result.BeforeAvailableGB,
            afterAvailableGB = result.AfterAvailableGB,
            freedGB = result.FreedGB,
            durationMs = result.Duration.TotalMilliseconds,
            error = result.Error
        };

        string line = JsonSerializer.Serialize(entry);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        File.AppendAllText(path, line + Environment.NewLine);
    }
}
