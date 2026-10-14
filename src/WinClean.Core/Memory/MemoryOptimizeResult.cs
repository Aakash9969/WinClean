namespace WinClean.Core.Memory;

/// <summary>What happened when one process's working set was trimmed.</summary>
public sealed record ProcessTrimOutcome(string ProcessName, int Id, bool WasTrimmed, string? SkipReason)
{
    public bool WasSkipped => !WasTrimmed;
}

/// <summary>Result of one memory optimization run.</summary>
public sealed record MemoryOptimizeResult(
    MemoryInfo Before,
    MemoryInfo After,
    int TrimmedCount,
    int SkippedCount,
    long BytesFreed,
    IReadOnlyList<ProcessTrimOutcome> Outcomes)
{
    /// <summary>Every process that was refused or failed, with its reason.</summary>
    public IReadOnlyList<ProcessTrimOutcome> Skipped => Outcomes.Where(outcome => outcome.WasSkipped).ToList();
}
