namespace WinClean.Core.Memory;

/// <summary>
/// Reads memory status and reclaims RAM the way standard memory cleaners do: by trimming the
/// working sets of running processes. Nothing is ever terminated — pages simply move out of
/// physical RAM and applications fault them back on demand.
/// </summary>
public interface IMemoryService
{
    /// <summary>Current total and available physical memory.</summary>
    MemoryInfo GetMemoryInfo();

    /// <summary>Trims the working sets of all eligible running processes and reports the outcome.</summary>
    Task<MemoryOptimizeResult> OptimizeAsync(CancellationToken cancellationToken);

    /// <summary>Trims a single process's working set. Returns false with a reason on failure.</summary>
    bool TryTrimProcess(int processId, out string? error);
}
