namespace WinClean.Core.Cleaning;

/// <summary>Live progress of a running clean. Reported per rule and every 25 files within a rule.</summary>
public sealed record CleanProgress(
    int CompletedRules,
    int TotalRules,
    string? CurrentRuleId,
    int FilesProcessed,
    long BytesFreedSoFar);
