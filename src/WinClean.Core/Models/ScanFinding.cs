namespace WinClean.Core.Models;

/// <summary>One file found by a scan, eligible for cleaning once the user has reviewed and approved it.</summary>
public sealed record ScanFinding(string RuleId, string Path, long SizeBytes);
