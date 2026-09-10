namespace RamSavior.Core.Engine;

/// <summary>
/// Values verified against Process Hacker's ntexapi.h (the reference implementation
/// security researchers use for this undocumented API) — not the same numbering an
/// earlier draft of this project used, which invented a nonexistent "system working
/// set" command. The real command set is exactly these four, matching what
/// Process Hacker itself exposes to users. There is no fifth "advanced" purge command
/// hiding anywhere else — this is the complete legitimate set.
/// </summary>
public enum MemoryListCommand
{
    EmptyWorkingSets = 2,
    EmptyModifiedPageList = 3,
    EmptyStandbyList = 4,
    EmptyPriority0StandbyList = 5
}

/// <summary>The purge intensity a user picks. Smart is the default on purpose — see CleanupEngine docs.</summary>
public enum CleanMode
{
    /// <summary>Only evicts Priority-0 standby pages: least-likely-to-be-reused cache. Minimal disk-reload risk.</summary>
    Smart,
    /// <summary>Full sequential flush: working sets → modified list → entire standby list.</summary>
    Full,
    /// <summary>User-picked combination of individual purge commands.</summary>
    Custom
}
