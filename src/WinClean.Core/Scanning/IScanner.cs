using WinClean.Core.Models;

namespace WinClean.Core.Scanning;

/// <summary>
/// Scans rules to find what could be cleaned. Scanning never deletes anything: deletion happens
/// only in the cleaner engine, and only after the user has reviewed the results and confirmed.
/// </summary>
public interface IScanner
{
    /// <summary>Scans all rules, reporting progress and supporting cancellation at every step.</summary>
    Task<ScanResult> ScanAsync(
        IReadOnlyList<Rule> rules,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
