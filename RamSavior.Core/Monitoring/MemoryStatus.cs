using RamSavior.Core.Native;

namespace RamSavior.Core.Monitoring;

public readonly record struct MemoryReading(
    double TotalPhysicalGB,
    double AvailablePhysicalGB,
    uint MemoryLoadPercent);

/// <summary>
/// Reads live memory stats via GlobalMemoryStatusEx — same struct Task Manager itself
/// reads. Deliberately NOT using System.Diagnostics.PerformanceCounter: that API pays a
/// first-call cost to spin up its counter category, which is unnecessary overhead for a
/// tool that's supposed to be lightweight and gets called frequently (e.g. --watch mode).
/// </summary>
public static class MemoryStatus
{
    public static MemoryReading Read()
    {
        var status = MEMORYSTATUSEX.Create();
        if (!NativeMethods.GlobalMemoryStatusEx(ref status))
        {
            throw new InvalidOperationException(
                $"GlobalMemoryStatusEx failed (Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}).");
        }

        const double bytesPerGB = 1024.0 * 1024.0 * 1024.0;

        return new MemoryReading(
            TotalPhysicalGB: status.ullTotalPhys / bytesPerGB,
            AvailablePhysicalGB: status.ullAvailPhys / bytesPerGB,
            MemoryLoadPercent: status.dwMemoryLoad);
    }

    /// <summary>
    /// True if the user is in a fullscreen game/video or presentation mode. Uses the
    /// same native signal Windows itself uses to suppress notifications — far more
    /// reliable than maintaining a hardcoded list of game .exe names.
    /// </summary>
    public static bool IsUserBusyOrFullscreen()
    {
        int hr = NativeMethods.SHQueryUserNotificationState(out var state);
        if (hr != 0) return false; // if the query fails, don't block cleanup on a guess

        return state is QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY
            or QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
            or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;
    }
}
