using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RamSavior.Core.ProcessTrim;

public readonly record struct ProcessMemoryInfo(int Pid, string Name, double WorkingSetMB);

/// <summary>
/// Unlike CleanupEngine (which calls the undocumented ntdll SystemMemoryListInformation
/// commands system-wide), this uses EmptyWorkingSet from psapi.dll — a fully documented,
/// officially supported Win32 API since Windows 2000. It trims ONE chosen process's
/// working set rather than the whole system, which is exactly why it's labeled
/// "Experimental" here: it's real and safe, but it targets a specific running app the
/// user picks, and trimming an app's working set can cause it to page back in slowly
/// on next use — worth understanding before using, even though the API itself is solid.
/// </summary>
public static class ProcessTrimmer
{
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public static IReadOnlyList<ProcessMemoryInfo> GetTopProcessesByMemory(int count = 20)
    {
        var results = new List<ProcessMemoryInfo>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                long workingSet = process.WorkingSet64;
                if (workingSet <= 0) continue;

                results.Add(new ProcessMemoryInfo(process.Id, process.ProcessName, workingSet / 1024.0 / 1024.0));
            }
            catch
            {
                // Some processes (protected/system) throw on property access even just to
                // read their name or working set — skip them rather than fail the whole list.
            }
            finally
            {
                process.Dispose();
            }
        }

        return results.OrderByDescending(p => p.WorkingSetMB).Take(count).ToList();
    }

    public static (bool Success, string? Error) TrimProcess(int pid)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(pid);

            bool ok = EmptyWorkingSet(process.Handle);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                return (false, $"EmptyWorkingSet failed (Win32 error {err}). " +
                                "The process may be protected, elevated above this app, or a system process.");
            }

            return (true, null);
        }
        catch (ArgumentException)
        {
            return (false, "That process is no longer running.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }
}
