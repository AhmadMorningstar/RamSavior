using System.Runtime.InteropServices;
using RamSavior.Core.Native;

namespace RamSavior.Core.Monitoring;

public readonly record struct MemoryComposition(
    double ZeroedMB,
    double FreeMB,
    double ModifiedMB,
    double StandbyTotalMB,
    IReadOnlyList<double> StandbyByPriorityMB);

/// <summary>
/// This is what RAMMap's detailed breakdown is built on — we were missing it entirely
/// until now, showing only a single "available GB" number. Unlike CleanupEngine's SET
/// commands, this is a QUERY: a wrong field here produces wrong displayed numbers at
/// worst, never a destabilizing action, so it's a much safer place to rely on a
/// verified-but-technically-undocumented struct layout.
/// </summary>
public static class MemoryCompositionReader
{
    public static MemoryComposition? TryRead()
    {
        int size = Marshal.SizeOf<SYSTEM_MEMORY_LIST_INFORMATION>();
        IntPtr buffer = Marshal.AllocHGlobal(size);

        try
        {
            int status = NativeMethods.NtQuerySystemInformation(
                NativeMethods.SystemMemoryListInformation, buffer, size, out _);

            if (status != 0) return null; // non-zero NTSTATUS — don't guess further, just report unavailable

            var info = Marshal.PtrToStructure<SYSTEM_MEMORY_LIST_INFORMATION>(buffer);
            double pageSize = Environment.SystemPageSize; // real, documented .NET API — not a guess
            const double bytesPerMB = 1024.0 * 1024.0;

            double ToMB(UIntPtr pages) => pages.ToUInt64() * pageSize / bytesPerMB;

            var standbyByPriority = info.PageCountByPriority.Select(ToMB).ToList();

            return new MemoryComposition(
                ZeroedMB: ToMB(info.ZeroPageCount),
                FreeMB: ToMB(info.FreePageCount),
                ModifiedMB: ToMB(info.ModifiedPageCount) + ToMB(info.ModifiedNoWritePageCount),
                StandbyTotalMB: standbyByPriority.Sum(),
                StandbyByPriorityMB: standbyByPriority);
        }
        catch
        {
            return null; // never let a diagnostic read crash the host app
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
