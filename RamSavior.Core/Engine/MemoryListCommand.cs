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

/// <summary>
/// The tier a user picks, modeled after the "Normal / Moderate / Advanced / Experimental"
/// difficulty ramp so a newcomer isn't shown a wall of checkboxes on day one, while a
/// power user can still get exactly the granularity they want.
/// </summary>
public enum CleanMode
{
    /// <summary>Priority-0 standby only. Minimal disk-reload risk. Safe as an automatic default.</summary>
    Normal,
    /// <summary>Working sets + Priority-0 standby. A step up in freed memory, still low risk.</summary>
    Moderate,
    /// <summary>User picks exactly which of the 4 real purge commands to run.</summary>
    Custom
}
