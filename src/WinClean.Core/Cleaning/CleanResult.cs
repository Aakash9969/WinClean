namespace WinClean.Core.Cleaning;

/// <summary>The final summary of a clean: space freed, files removed, files skipped and why.</summary>
public sealed record CleanResult(IReadOnlyList<RuleCleanResult> Rules)
{
    /// <summary>Total bytes actually freed (in dry-run: estimated).</summary>
    public long TotalBytesFreed => Rules.Sum(rule => rule.BytesFreed);

    /// <summary>Total files removed (in dry-run: would be removed).</summary>
    public int TotalFilesRemoved => Rules.Sum(rule => rule.FilesRemoved);

    /// <summary>Total files skipped with a reason.</summary>
    public int TotalFilesSkipped => Rules.Sum(rule => rule.FilesSkipped);

    /// <summary>Every skipped entry with its reason, across all rules.</summary>
    public IReadOnlyList<FileCleanOutcome> SkippedOutcomes =>
        Rules.SelectMany(rule => rule.Outcomes)
             .Where(outcome => outcome.Kind == FileOutcomeKind.Skipped)
             .ToList();
}
