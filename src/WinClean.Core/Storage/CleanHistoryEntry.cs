namespace WinClean.Core.Storage;

/// <summary>What one rule contributed to a completed clean.</summary>
public sealed record HistoryRuleEntry(
    string RuleId,
    string RuleName,
    long BytesFreed,
    int FilesRemoved,
    int FilesSkipped);

/// <summary>Summary of one completed clean, stored locally as JSON.</summary>
public sealed record CleanHistoryEntry(
    DateTime CompletedAtUtc,
    long TotalBytesFreed,
    int TotalFilesRemoved,
    int TotalFilesSkipped,
    IReadOnlyList<HistoryRuleEntry> Rules);
