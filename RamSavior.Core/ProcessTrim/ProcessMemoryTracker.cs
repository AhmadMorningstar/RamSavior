namespace RamSavior.Core.ProcessTrim;

public readonly record struct LeakSuspect(int Pid, string Name, double CurrentMB, double GrowthPercent);

/// <summary>
/// A simple, honest heuristic — not a real leak detector. It flags a process whose
/// working set has grown monotonically (no drops) across the tracked sample window by
/// more than a threshold percentage. That catches "this app just keeps climbing," which
/// is the practical symptom users actually notice; it does not diagnose WHY, and a
/// process legitimately loading more data (e.g. opening more browser tabs) will also
/// trip it. Treat it as "worth a look," not a verdict.
/// </summary>
public sealed class ProcessMemoryTracker
{
    private const int MaxSamplesPerProcess = 6;
    private const double GrowthThresholdPercent = 20.0;

    private readonly Dictionary<int, List<double>> _history = new();

    public void RecordSample(IReadOnlyList<ProcessMemoryInfo> currentProcesses)
    {
        var seenPids = new HashSet<int>();

        foreach (var proc in currentProcesses)
        {
            seenPids.Add(proc.Pid);

            if (!_history.TryGetValue(proc.Pid, out var samples))
            {
                samples = new List<double>();
                _history[proc.Pid] = samples;
            }

            samples.Add(proc.WorkingSetMB);
            if (samples.Count > MaxSamplesPerProcess)
                samples.RemoveAt(0);
        }

        // Drop tracking for processes that exited, so the dictionary doesn't grow forever.
        foreach (var pid in _history.Keys.Where(p => !seenPids.Contains(p)).ToList())
            _history.Remove(pid);
    }

    public IReadOnlyList<LeakSuspect> GetSuspects(IReadOnlyList<ProcessMemoryInfo> currentProcesses)
    {
        var results = new List<LeakSuspect>();

        foreach (var proc in currentProcesses)
        {
            if (!_history.TryGetValue(proc.Pid, out var samples) || samples.Count < MaxSamplesPerProcess)
                continue;

            bool monotonicallyIncreasing = true;
            for (int i = 1; i < samples.Count; i++)
            {
                if (samples[i] < samples[i - 1] - 1.0) // allow ~1MB noise
                {
                    monotonicallyIncreasing = false;
                    break;
                }
            }

            if (!monotonicallyIncreasing) continue;

            double growthPercent = (samples[^1] - samples[0]) / Math.Max(samples[0], 1.0) * 100.0;
            if (growthPercent >= GrowthThresholdPercent)
                results.Add(new LeakSuspect(proc.Pid, proc.Name, proc.WorkingSetMB, Math.Round(growthPercent, 1)));
        }

        return results;
    }
}
