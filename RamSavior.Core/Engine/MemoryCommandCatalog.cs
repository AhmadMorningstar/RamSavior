namespace RamSavior.Core.Engine;

public record MemoryCommandInfo(
    MemoryListCommand Command,
    string Title,
    string Description,
    bool IsAdvanced);

/// <summary>
/// Single source of truth for what each purge command does and how risky it is.
/// Both the CLI (--items) and GUI (Custom mode checkboxes) read from this instead of
/// each keeping their own copy of descriptions that could drift out of sync.
/// </summary>
public static class MemoryCommandCatalog
{
    public static readonly MemoryCommandInfo[] All =
    [
        new(MemoryListCommand.EmptyWorkingSets,
            "Empty Working Sets",
            "Moves active app memory into the Standby/Modified lists. Low risk — apps just reclaim pages as needed afterward.",
            IsAdvanced: false),

        new(MemoryListCommand.EmptyPriority0StandbyList,
            "Empty Priority-0 Standby",
            "Clears cache pages least likely to be reused. Minimal disk-reload risk. This is what Smart Clean uses.",
            IsAdvanced: false),

        new(MemoryListCommand.EmptySystemWorkingSet,
            "Empty System Working Set",
            "Flushes kernel and driver working sets. Can briefly affect system responsiveness right after running.",
            IsAdvanced: true),

        new(MemoryListCommand.EmptyModifiedPageList,
            "Empty Modified Page List",
            "Forces all dirty (modified) pages to be written to disk immediately. Causes a short burst of disk I/O.",
            IsAdvanced: true),

        new(MemoryListCommand.EmptyStandbyList,
            "Empty Standby List (Full)",
            "Purges the ENTIRE standby cache, including frequently-used pages. Apps and DLLs will reload from disk on next use — this is the setting most likely to cause a brief stutter.",
            IsAdvanced: true),
    ];

    /// <summary>
    /// The order these commands must run in for a purge to be effective — evicting the
    /// standby list before flushing working sets misses memory that was about to be
    /// released. Any custom selection runs in this order regardless of the order the
    /// user checked the boxes in.
    /// </summary>
    public static readonly MemoryListCommand[] CanonicalOrder =
    [
        MemoryListCommand.EmptyWorkingSets,
        MemoryListCommand.EmptySystemWorkingSet,
        MemoryListCommand.EmptyModifiedPageList,
        MemoryListCommand.EmptyPriority0StandbyList,
        MemoryListCommand.EmptyStandbyList
    ];
}
