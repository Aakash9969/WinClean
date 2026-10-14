using WinClean.Core.Models;

namespace WinClean.Core.Scanning;

/// <summary>
/// Scan outcome for a single rule: reclaimable totals, the individual findings, and notes
/// explaining anything that was skipped (missing paths, protected locations, symlinks,
/// unverifiable entries).
/// </summary>
public sealed record RuleScanResult(
    Rule Rule,
    long TotalBytes,
    int FileCount,
    IReadOnlyList<ScanFinding> Findings,
    IReadOnlyList<string> Notes);
