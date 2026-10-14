using System.Diagnostics;
using WinClean.Core.Memory;

namespace WinClean.Tests;

/// <summary>
/// Tests for the memory optimizer. The exclusion policy is tested without touching real
/// processes; trimming is exercised only against a throwaway child process the test spawns
/// itself — never against the user's running applications.
/// </summary>
public sealed class MemoryServiceTests
{
    private readonly MemoryService _service = new();

    [Fact]
    public void GetMemoryInfo_ReturnsPlausibleValues()
    {
        MemoryInfo info = _service.GetMemoryInfo();

        Assert.True(info.TotalBytes > 0, $"Total memory should be positive, was {info.TotalBytes}.");
        Assert.True(info.AvailableBytes > 0, $"Available memory should be positive, was {info.AvailableBytes}.");
        Assert.True(info.AvailableBytes <= info.TotalBytes, "Available memory cannot exceed total memory.");
        Assert.True(info.UsedBytes >= 0, "Used memory cannot be negative.");
        Assert.InRange(info.UsedPercent, 0, 100);
    }

    [Theory]
    [InlineData("System")]
    [InlineData("Idle")]
    [InlineData("Registry")]
    [InlineData("smss")]
    [InlineData("csrss")]
    [InlineData("wininit")]
    [InlineData("winlogon")]
    [InlineData("MemCompression")]
    public void IsExcluded_TrueForCriticalSystemProcesses(string processName)
    {
        Assert.True(MemoryService.IsExcluded(processName, processId: 1234, currentProcessId: 999));
    }

    [Theory]
    [InlineData("explorer")]
    [InlineData("notepad")]
    [InlineData("chrome")]
    [InlineData("SomeRandomApp")]
    public void IsExcluded_FalseForOrdinaryProcesses(string processName)
    {
        Assert.False(MemoryService.IsExcluded(processName, processId: 1234, currentProcessId: 999));
    }

    [Fact]
    public void IsExcluded_TrueForTheCurrentProcessItself()
    {
        Assert.True(MemoryService.IsExcluded("WinClean", Environment.ProcessId, Environment.ProcessId));
    }

    [Fact]
    public void TryTrimProcess_TrimsAProcessWeOwn()
    {
        // A throwaway child process this test owns; trimmed and killed within the test.
        using Process child = Process.Start(new ProcessStartInfo("ping.exe", "-n 60 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Could not spawn a child process for the test.");

        try
        {
            Assert.True(_service.TryTrimProcess(child.Id, out string? error), $"Trim failed: {error}");
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
            }
        }
    }

    [Fact]
    public void TryTrimProcess_MissingProcess_ReturnsFalseWithReason()
    {
        // PID 0x7FFFFFFF is far beyond any real PID on Windows.
        Assert.False(_service.TryTrimProcess(int.MaxValue - 1, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
