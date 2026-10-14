using WinClean.Core.Models;

namespace WinClean.Core.Scanning;

/// <summary>
/// Scan outcome for a single rule: reclaimable totals, the individual findings, the concrete
/// roots that were scanned (the cleaner re-validates containment against these at delete
/// time), and notes explaining anything that was skipped.
/// </summary>
public sealed record RuleScanResult(
    Rule Rule,
    long TotalBytes,
    int FileCount,
    IReadOnlyList<ScanFinding> Findings,
    IReadOnlyList<string> Roots,
    IReadOnlyList<string> Notes);
