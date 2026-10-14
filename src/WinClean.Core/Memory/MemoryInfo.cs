namespace WinClean.Core.Memory;

/// <summary>Snapshot of physical memory status.</summary>
public sealed record MemoryInfo(long TotalBytes, long AvailableBytes)
{
    public long UsedBytes => TotalBytes - AvailableBytes;

    public double UsedPercent => TotalBytes <= 0 ? 0 : UsedBytes * 100.0 / TotalBytes;
}
