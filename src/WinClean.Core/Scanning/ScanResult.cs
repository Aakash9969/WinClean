namespace WinClean.Core.Scanning;

/// <summary>The outcome of a full scan across all enabled rules.</summary>
public sealed record ScanResult(IReadOnlyList<RuleScanResult> Rules)
{
    /// <summary>Total reclaimable bytes across all rules.</summary>
    public long TotalBytes => Rules.Sum(rule => rule.TotalBytes);

    /// <summary>Total number of files that would be removed.</summary>
    public int TotalFiles => Rules.Sum(rule => rule.FileCount);

    /// <summary>Totals grouped by rule category, in the order the categories were first encountered.</summary>
    public IReadOnlyList<CategorySummary> Categories =>
        Rules
            .GroupBy(rule => rule.Rule.Category, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CategorySummary(
                group.Key,
                group.Sum(rule => rule.TotalBytes),
                group.Sum(rule => rule.FileCount)))
            .ToList();
}
