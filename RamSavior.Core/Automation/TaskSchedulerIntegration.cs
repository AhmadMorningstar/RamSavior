using System.Diagnostics;

namespace RamSavior.Core.Automation;

/// <summary>
/// Uses schtasks.exe rather than a registry Run-key entry. This matters specifically
/// because this app's manifest requires admin: a Run-key entry does NOT reliably
/// re-elevate an app at logon (behavior is inconsistent across Windows versions), while
/// a Scheduled Task with "Run with highest privileges" + an "At log on" trigger handles
/// elevation correctly and consistently. schtasks.exe is built into every Windows
/// install, so this needs no extra dependency.
/// </summary>
public static class TaskSchedulerIntegration
{
    public static (bool Success, string? Error) InstallStartupTask(string taskName, string exePath, string arguments = "")
    {
        string innerCommand = BuildQuotedCommand(exePath, arguments);
        string args = $"/Create /TN \"{taskName}\" /TR \"{innerCommand}\" /SC ONLOGON /RL HIGHEST /F";
        return RunSchtasks(args);
    }

    public static (bool Success, string? Error) InstallIntervalTask(string taskName, string exePath, string arguments, int intervalMinutes)
    {
        string innerCommand = BuildQuotedCommand(exePath, arguments);
        string args = $"/Create /TN \"{taskName}\" /TR \"{innerCommand}\" /SC MINUTE /MO {intervalMinutes} /RL HIGHEST /F";
        return RunSchtasks(args);
    }

    private static string BuildQuotedCommand(string exePath, string arguments)
    {
        // schtasks /TR takes one quoted string containing the whole command line, so the
        // exe path's own quotes need escaping for the outer quoting to survive.
        string escapedExe = exePath.Replace("\"", "\\\"");
        return string.IsNullOrWhiteSpace(arguments)
            ? $"\\\"{escapedExe}\\\""
            : $"\\\"{escapedExe}\\\" {arguments}";
    }

    public static (bool Success, string? Error) RemoveTask(string taskName)
    {
        return RunSchtasks($"/Delete /TN \"{taskName}\" /F");
    }

    public static bool TaskExists(string taskName)
    {
        var (success, _) = RunSchtasks($"/Query /TN \"{taskName}\"", suppressErrorOnNotFound: true);
        return success;
    }

    private static (bool Success, string? Error) RunSchtasks(string arguments, bool suppressErrorOnNotFound = false)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return (false, "Failed to start schtasks.exe.");

            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(10_000);

            if (process.ExitCode == 0) return (true, null);
            if (suppressErrorOnNotFound) return (false, null);

            return (false, string.IsNullOrWhiteSpace(stderr) ? $"schtasks.exe exited with code {process.ExitCode}." : stderr.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
