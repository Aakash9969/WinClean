namespace WinClean.Core.Scanning;

/// <summary>Live progress of a running scan. Reported when a rule starts and when a rule completes.</summary>
public sealed record ScanProgress(
    int CompletedRules,
    int TotalRules,
    string? CurrentRuleId,
    long BytesFoundSoFar,
    int FilesFoundSoFar);
