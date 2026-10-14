namespace WinClean.Core.Scanning;

/// <summary>Reclaimable totals for one rule category (e.g. "System", "Browsers").</summary>
public sealed record CategorySummary(string Name, long TotalBytes, int FileCount);
