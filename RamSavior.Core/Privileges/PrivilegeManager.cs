using System.Runtime.InteropServices;
using RamSavior.Core.Native;

namespace RamSavior.Core.Privileges;

public static class PrivilegeManager
{
    public const string SeProfileSingleProcessPrivilege = "SeProfileSingleProcessPrivilege";
    public const string SeIncreaseQuotaPrivilege = "SeIncreaseQuotaPrivilege";

    /// <summary>
    /// Enables a privilege on the current process token. Admin elevation alone is NOT
    /// enough — several privileges exist on the token but stay disabled until explicitly
    /// enabled here. Skipping this step is why "just run as admin" purge tools silently
    /// fail with STATUS_PRIVILEGE_NOT_HELD.
    /// </summary>
    public static bool TryEnablePrivilege(string privilegeName, out string? error)
    {
        error = null;
        IntPtr tokenHandle = IntPtr.Zero;

        try
        {
            if (!NativeMethods.OpenProcessToken(
                    NativeMethods.GetCurrentProcess(),
                    NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY,
                    out tokenHandle))
            {
                error = $"OpenProcessToken failed (Win32 error {Marshal.GetLastWin32Error()}). Is the process elevated?";
                return false;
            }

            if (!NativeMethods.LookupPrivilegeValue(null, privilegeName, out LUID luid))
            {
                error = $"LookupPrivilegeValue failed for '{privilegeName}' (Win32 error {Marshal.GetLastWin32Error()}).";
                return false;
            }

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES
                {
                    Luid = luid,
                    Attributes = NativeMethods.SE_PRIVILEGE_ENABLED
                }
            };

            bool adjusted = NativeMethods.AdjustTokenPrivileges(
                tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);

            // AdjustTokenPrivileges can return true while GetLastError still reports
            // ERROR_NOT_ALL_ASSIGNED — that combination means it silently did nothing.
            int lastError = Marshal.GetLastWin32Error();
            if (!adjusted || lastError != 0)
            {
                error = $"AdjustTokenPrivileges did not fully assign '{privilegeName}' (Win32 error {lastError}). " +
                        "The account may lack this privilege in local security policy, or the process isn't elevated.";
                return false;
            }

            return true;
        }
        finally
        {
            if (tokenHandle != IntPtr.Zero)
                NativeMethods.CloseHandle(tokenHandle);
        }
    }

    /// <summary>Enables both privileges the memory-list purge needs. Call once at startup.</summary>
    public static (bool Success, string? Error) EnableRequiredPrivileges()
    {
        if (!TryEnablePrivilege(SeProfileSingleProcessPrivilege, out var err1))
            return (false, err1);

        if (!TryEnablePrivilege(SeIncreaseQuotaPrivilege, out var err2))
            return (false, err2);

        return (true, null);
    }
}
