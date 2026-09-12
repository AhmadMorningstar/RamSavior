namespace RamSavior.Core.Automation;

/// <summary>
/// Configuration for when automation should fire. Each condition is independently
/// toggleable; when more than one is enabled, ANY satisfied condition triggers a clean
/// (interval OR threshold OR idle-crossed) — but idle and the fullscreen guard always
/// act as gates that can suppress a trigger regardless of which condition fired it.
///
/// Design notes on why it's shaped this way:
/// - Interval-based ("clean every N minutes") is Wise Memory Optimizer's core model —
///   simple, predictable, good for users who just want a steady baseline.
/// - Threshold-based is ISLC's specialty. ISLC specifically uses a DUAL condition
///   (standby list size AND free-memory both past their thresholds) rather than a
///   single free-memory number, since purging when there's barely any standby cache to
///   begin with wastes the purge. We approximate that spirit with two independently
///   toggleable thresholds (available-GB and memory-load-%) so a user can require both.
/// - Idle-requirement and process exclusion are what Wise Memory Optimizer calls "game
///   mode" — pause automation while specific apps are running, or while the user is
///   actively at the keyboard.
/// </summary>
public class AutomationTrigger
{
    public bool Enabled { get; set; } = false;

    public bool IntervalEnabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 30;

    public bool FreeMemoryThresholdEnabled { get; set; } = false;
    public double FreeMemoryBelowGB { get; set; } = 2.0;

    public bool LoadPercentThresholdEnabled { get; set; } = false;
    public int LoadAbovePercent { get; set; } = 85;

    /// <summary>
    /// The real ISLC-style condition, now that MemoryCompositionReader can read the
    /// actual standby list size via NtQuerySystemInformation rather than approximating
    /// it — purging only matters when there's meaningful standby cache to reclaim.
    /// </summary>
    public bool StandbyListThresholdEnabled { get; set; } = false;
    public double StandbyListAboveMB { get; set; } = 4096;

    /// <summary>Wise Memory Optimizer's "clean at a specific time daily" model, distinct from the interval countdown.</summary>
    public bool TimeOfDayEnabled { get; set; } = false;
    public TimeSpan TimeOfDay { get; set; } = new(3, 0, 0);

    /// <summary>
    /// MemReduct-style per-process auto-trim: independent of the system-wide clean
    /// conditions above, checks every poll tick whether any running process's working
    /// set has crossed this threshold and trims just that process if so.
    /// </summary>
    public bool PerProcessAutoTrimEnabled { get; set; } = false;
    public double PerProcessAutoTrimAboveMB { get; set; } = 1500;

    /// <summary>0 disables the idle requirement entirely.</summary>
    public int RequireIdleMinutes { get; set; } = 0;

    public List<string> ExcludedProcessNames { get; set; } = new();
}
