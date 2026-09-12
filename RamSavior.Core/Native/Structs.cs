using System.Runtime.InteropServices;

namespace RamSavior.Core.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct MEMORYSTATUSEX
{
    public uint dwLength;
    public uint dwMemoryLoad;
    public ulong ullTotalPhys;
    public ulong ullAvailPhys;
    public ulong ullTotalPageFile;
    public ulong ullAvailPageFile;
    public ulong ullTotalVirtual;
    public ulong ullAvailVirtual;
    public ulong ullAvailExtendedVirtual;

    public static MEMORYSTATUSEX Create()
    {
        return new MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
        };
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct LUID
{
    public uint LowPart;
    public int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
internal struct LUID_AND_ATTRIBUTES
{
    public LUID Luid;
    public uint Attributes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TOKEN_PRIVILEGES
{
    public uint PrivilegeCount;
    public LUID_AND_ATTRIBUTES Privileges; // single-privilege layout; we adjust one at a time
}

[StructLayout(LayoutKind.Sequential)]
internal struct LASTINPUTINFO
{
    public uint cbSize;
    public uint dwTime;
}

/// <summary>
/// Layout verified against Process Hacker's phnt/ntexapi.h (the same reference used
/// to fix the purge command values earlier). This is used only with NtQuerySystemInformation
/// (a READ) — unlike the SET commands elsewhere in this app, a wrong field here can only
/// produce wrong displayed numbers, never a destabilizing action, so the risk profile is
/// very different. x64-only, matching this project's win-x64 publish target — ULONG_PTR
/// is 8 bytes on x64.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_MEMORY_LIST_INFORMATION
{
    public UIntPtr ZeroPageCount;
    public UIntPtr FreePageCount;
    public UIntPtr ModifiedPageCount;
    public UIntPtr ModifiedNoWritePageCount;
    public UIntPtr BadPageCount;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public UIntPtr[] PageCountByPriority;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public UIntPtr[] RepurposedPagesByPriority;
    public UIntPtr ModifiedPageCountPageFile;
}
