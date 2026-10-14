using WinClean.Core.Scanning;

namespace WinClean.Core.Cleaning;

/// <summary>
/// Deletes exactly the findings the user approved — nothing more. Every file is re-validated
/// at delete time; locked or in-use files are skipped with a reason and never force-deleted.
/// Dry-run mode executes the whole pipeline without deleting anything.
/// </summary>
public interface ICleaner
{
    Task<CleanResult> CleanAsync(
        IReadOnlyList<RuleScanResult> approvedRules,
        bool dryRun,
        IProgress<CleanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
