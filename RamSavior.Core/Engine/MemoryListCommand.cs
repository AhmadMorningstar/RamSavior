namespace RamSavior.Core.Engine;

public enum MemoryListCommand
{
    EmptyWorkingSets = 1,
    EmptySystemWorkingSet = 2,
    EmptyModifiedPageList = 3,
    EmptyStandbyList = 4,
    EmptyPriority0StandbyList = 5
}

/// <summary>The purge intensity a user picks. Smart is the default on purpose — see CleanupEngine docs.</summary>
public enum CleanMode
{
    /// <summary>Only evicts Priority-0 standby pages: least-likely-to-be-reused cache. Minimal disk-reload risk.</summary>
    Smart,
    /// <summary>Full sequential flush: working sets → system working set → modified list → entire standby list.</summary>
    Full,
    /// <summary>User-picked combination of individual purge commands.</summary>
    Custom
}
