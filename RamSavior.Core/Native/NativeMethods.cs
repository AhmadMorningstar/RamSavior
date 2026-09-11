using System.Runtime.InteropServices;

namespace RamSavior.Core.Native;

/// <summary>
/// All raw P/Invoke signatures live here, and only here. Nothing outside this
/// folder should reference DllImport directly — keeps the unsafe surface auditable.
/// </summary>
internal static class NativeMethods
{
    // ---- ntdll.dll : undocumented but stable since XP, used by Task Manager itself ----
    [DllImport("ntdll.dll")]
    internal static extern int NtSetSystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength);

    internal const int SystemMemoryListInformation = 0x50; // 80 decimal, stable across Win10/11

    // ---- kernel32.dll ----
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    // ---- advapi32.dll : token privilege management ----
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(
        IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LookupPrivilegeValue(
        string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState,
        int bufferLengthInBytes,
        IntPtr previousState,
        IntPtr returnLengthInBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    // ---- shell32.dll : native "is the user busy / fullscreen" signal ----
    [DllImport("shell32.dll")]
    internal static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE state);

    // ---- user32.dll : idle-time detection for automation's idle-requirement gate ----
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    internal const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    internal const uint TOKEN_QUERY = 0x0008;
    internal const uint SE_PRIVILEGE_ENABLED = 0x00000002;
}

internal enum QUERY_USER_NOTIFICATION_STATE
{
    QUNS_NOT_PRESENT = 1,
    QUNS_BUSY = 2,
    QUNS_RUNNING_D3D_FULL_SCREEN = 3,
    QUNS_PRESENTATION_MODE = 4,
    QUNS_ACCEPTS_NOTIFICATIONS = 5,
    QUNS_QUIET_TIME = 6,
    QUNS_APP = 7
}
