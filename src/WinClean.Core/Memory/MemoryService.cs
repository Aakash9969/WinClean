using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinClean.Core.Memory;

/// <summary>
/// The memory optimizer. Reclaims RAM by emptying the working sets of running processes
/// (SetProcessWorkingSetSize with -1/-1 — the documented, reversible mechanism standard
/// memory cleaners use). Critical system processes and WinClean itself are never touched;
/// anything that cannot be opened is skipped with a reason. No process is ever terminated,
/// and no undocumented NT APIs (standby-list purges) are used.
/// </summary>
public sealed class MemoryService : IMemoryService
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessSetQuota = 0x0100;
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidParameter = 87;

    /// <summary>Processes whose working sets must never be touched.</summary>
    private static readonly HashSet<string> CriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System",
        "Idle",
        "Registry",
        "smss",
        "csrss",
        "wininit",
        "winlogon",
        "MemCompression",
    };

    /// <summary>
    /// How long to let the memory manager finish moving trimmed dirty pages to the
    /// pagefile/standby before reading the after-state — so the freed number is honest
    /// rather than pessimistic (immediate readings miss pages still being written out).
    /// </summary>
    private static readonly TimeSpan MeasureSettleDelay = TimeSpan.FromSeconds(1);

    public MemoryInfo GetMemoryInfo()
    {
        MEMORYSTATUSEX status = new()
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>(),
        };

        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new InvalidOperationException(
                $"GlobalMemoryStatusEx failed with error {Marshal.GetLastWin32Error()}.");
        }

        return new MemoryInfo((long)status.ullTotalPhys, (long)status.ullAvailPhys);
    }

    public async Task<MemoryOptimizeResult> OptimizeAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(() => Optimize(cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private MemoryOptimizeResult Optimize(CancellationToken cancellationToken)
    {
        MemoryInfo before = GetMemoryInfo();
        List<ProcessTrimOutcome> outcomes = [];
        int currentProcessId = Environment.ProcessId;

        foreach (Process process in Process.GetProcesses())
        using (process)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string name;
            try
            {
                name = process.ProcessName;
            }
            catch
            {
                name = "unknown";
            }

            if (IsExcluded(name, process.Id, currentProcessId))
            {
                string reason = process.Id == currentProcessId
                    ? "excluded: WinClean itself"
                    : "excluded: critical system process";
                outcomes.Add(new ProcessTrimOutcome(name, process.Id, false, reason));
                continue;
            }

            if (TryTrimProcess(process.Id, out string? error))
            {
                outcomes.Add(new ProcessTrimOutcome(name, process.Id, true, null));
            }
            else
            {
                outcomes.Add(new ProcessTrimOutcome(name, process.Id, false, error));
            }
        }

        // Let the memory manager finish moving trimmed dirty pages out of physical RAM
        // before measuring, so the freed number is honest instead of pessimistic.
        cancellationToken.WaitHandle.WaitOne(MeasureSettleDelay);

        MemoryInfo after = GetMemoryInfo();
        long freed = Math.Max(0, before.UsedBytes - after.UsedBytes);

        return new MemoryOptimizeResult(
            before,
            after,
            outcomes.Count(outcome => outcome.WasTrimmed),
            outcomes.Count(outcome => outcome.WasSkipped),
            freed,
            outcomes);
    }

    /// <summary>True for processes the optimizer must never touch (critical system processes and itself).</summary>
    internal static bool IsExcluded(string processName, int processId, int currentProcessId) =>
        processId == currentProcessId || CriticalProcesses.Contains(processName);

    public bool TryTrimProcess(int processId, out string? error)
    {
        error = null;

        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation | ProcessSetQuota, false, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            int code = Marshal.GetLastWin32Error();
            error = code switch
            {
                ErrorAccessDenied => "access denied — restart WinClean elevated to trim this process",
                ErrorInvalidParameter => "process no longer exists",
                _ => $"could not open the process (error {code})",
            };
            return false;
        }

        try
        {
            // -1/-1 asks Windows to empty the working set as much as possible.
            if (!SetProcessWorkingSetSize(handle, (nint)(-1), (nint)(-1)))
            {
                int code = Marshal.GetLastWin32Error();
                error = code == ErrorAccessDenied
                    ? "access denied — restart WinClean elevated to trim this process"
                    : $"could not trim the working set (error {code})";
                return false;
            }

            return true;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
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
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(
        IntPtr process,
        nint minimumWorkingSetSize,
        nint maximumWorkingSetSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
