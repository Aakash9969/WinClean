using Serilog;
using WinClean.Core.Models;
using WinClean.Core.Paths;
using WinClean.Core.Scanning;
using WinClean.Core.Special;

namespace WinClean.Core.Cleaning;

/// <summary>
/// The cleaner engine. Deletion only ever happens for findings the user explicitly approved,
/// and every single file is re-validated at delete time: it must still exist, still lie inside
/// a root this rule declared at scan time, pass the blocklist, and involve no symlink or
/// junction. Locked or in-use files are skipped with a reason — never force-deleted. Dry-run
/// mode runs the identical pipeline with deletion replaced by "would delete".
/// </summary>
public sealed class Cleaner : ICleaner
{
    private const int ProgressInterval = 25;
    private const string RecycleBinLabel = "Recycle Bin";

    private readonly PathValidator _validator;
    private readonly IRecycleBinEmptier _recycleBinEmptier;
    private readonly IRecycleBinEstimator _recycleBinEstimator;
    private readonly ILogger _logger;

    public Cleaner(
        PathValidator validator,
        IRecycleBinEmptier recycleBinEmptier,
        IRecycleBinEstimator recycleBinEstimator,
        ILogger logger)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _recycleBinEmptier = recycleBinEmptier ?? throw new ArgumentNullException(nameof(recycleBinEmptier));
        _recycleBinEstimator = recycleBinEstimator ?? throw new ArgumentNullException(nameof(recycleBinEstimator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CleanResult> CleanAsync(
        IReadOnlyList<RuleScanResult> approvedRules,
        bool dryRun,
        IProgress<CleanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approvedRules);

        _logger.Information(
            "Clean started ({Mode}) for {RuleCount} approved rule(s)",
            dryRun ? "dry run" : "live",
            approvedRules.Count);

        List<RuleCleanResult> results = new(approvedRules.Count);
        RunningTotals totals = new();

        for (int index = 0; index < approvedRules.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RuleScanResult scan = approvedRules[index];
            progress?.Report(new CleanProgress(
                index, approvedRules.Count, scan.Rule.Id, totals.FilesProcessed, totals.BytesFreed));

            RuleCleanResult result = scan.Rule.IsSpecial
                ? await CleanSpecialAsync(scan, dryRun, cancellationToken).ConfigureAwait(false)
                : await Task.Run(
                    () => CleanRule(scan, dryRun, progress, index, approvedRules.Count, totals, cancellationToken),
                    cancellationToken).ConfigureAwait(false);

            results.Add(result);

            progress?.Report(new CleanProgress(
                index + 1, approvedRules.Count, scan.Rule.Id, totals.FilesProcessed, totals.BytesFreed));
        }

        CleanResult cleanResult = new(results);
        _logger.Information(
            "Clean finished ({Mode}): {Bytes} byte(s) freed, {Removed} file(s) removed, {Skipped} skipped",
            dryRun ? "dry run" : "live",
            cleanResult.TotalBytesFreed,
            cleanResult.TotalFilesRemoved,
            cleanResult.TotalFilesSkipped);

        return cleanResult;
    }

    private RuleCleanResult CleanRule(
        RuleScanResult scan,
        bool dryRun,
        IProgress<CleanProgress>? progress,
        int ruleIndex,
        int totalRules,
        RunningTotals totals,
        CancellationToken cancellationToken)
    {
        List<FileCleanOutcome> outcomes = new(scan.Findings.Count);
        List<string> notes = [];
        long bytesFreed = 0;
        int removed = 0;
        int skipped = 0;
        int processed = 0;

        foreach (ScanFinding finding in scan.Findings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileCleanOutcome outcome = ProcessFinding(scan, finding, dryRun);
            outcomes.Add(outcome);
            processed++;
            totals.FilesProcessed++;

            if (outcome.Kind == FileOutcomeKind.Skipped)
            {
                skipped++;
                _logger.Warning("Skipped '{Path}': {Reason}", outcome.Path, outcome.Reason);
            }
            else
            {
                removed++;
                bytesFreed += outcome.SizeBytes;
                totals.BytesFreed += outcome.SizeBytes;

                if (outcome.Kind == FileOutcomeKind.Deleted)
                {
                    _logger.Debug("Deleted '{Path}' ({Size} bytes)", outcome.Path, outcome.SizeBytes);
                }
            }

            if (processed % ProgressInterval == 0)
            {
                progress?.Report(new CleanProgress(
                    ruleIndex, totalRules, scan.Rule.Id, totals.FilesProcessed, totals.BytesFreed));
            }
        }

        _logger.Information(
            "Rule '{RuleId}' ({Mode}): {Removed} file(s) worth {Bytes} byte(s), {Skipped} skipped",
            scan.Rule.Id,
            dryRun ? "dry run" : "live",
            removed,
            bytesFreed,
            skipped);

        return new RuleCleanResult(scan.Rule, bytesFreed, removed, skipped, outcomes, notes);
    }

    private FileCleanOutcome ProcessFinding(RuleScanResult scan, ScanFinding finding, bool dryRun)
    {
        if (!string.Equals(finding.RuleId, scan.Rule.Id, StringComparison.Ordinal))
        {
            return Skipped(finding, "the finding does not belong to this rule");
        }

        if (Directory.Exists(finding.Path))
        {
            return Skipped(finding, "the entry is a directory, not a file");
        }

        if (!File.Exists(finding.Path))
        {
            return Skipped(finding, "the file no longer exists");
        }

        string? root = FindContainingRoot(scan.Roots, finding.Path);
        if (root is null)
        {
            return Skipped(finding, "the file is no longer inside any root declared by this rule");
        }

        PathValidationResult validation = _validator.ValidateFile(finding.Path, root);
        if (!validation.IsAllowed)
        {
            return Skipped(finding, validation.Reason!);
        }

        long sizeBytes = GetSizeBytes(finding.Path) ?? finding.SizeBytes;

        if (dryRun)
        {
            return new FileCleanOutcome(finding.Path, sizeBytes, FileOutcomeKind.WouldDelete, null);
        }

        try
        {
            File.Delete(finding.Path);
            return new FileCleanOutcome(finding.Path, sizeBytes, FileOutcomeKind.Deleted, null);
        }
        catch (IOException ex)
        {
            return Skipped(finding, $"the file is locked or in use ({ex.GetType().Name}: {ex.Message})");
        }
        catch (UnauthorizedAccessException)
        {
            return Skipped(finding, "access was denied — the file may be in use, read-only, or WinClean lacks permission");
        }
    }

    private async Task<RuleCleanResult> CleanSpecialAsync(
        RuleScanResult scan,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        Rule rule = scan.Rule;

        if (!string.Equals(rule.SpecialTarget, SpecialTargets.RecycleBin, StringComparison.OrdinalIgnoreCase))
        {
            string reason = $"unknown specialTarget '{rule.SpecialTarget}'";
            _logger.Warning("Skipped rule '{RuleId}': {Reason}", rule.Id, reason);
            return new RuleCleanResult(
                rule, 0, 0, 1, [new FileCleanOutcome(RecycleBinLabel, 0, FileOutcomeKind.Skipped, reason)], []);
        }

        if (dryRun)
        {
            return new RuleCleanResult(
                rule,
                scan.TotalBytes,
                scan.FileCount,
                0,
                [new FileCleanOutcome(RecycleBinLabel, scan.TotalBytes, FileOutcomeKind.WouldDelete, null)],
                ["Size is the estimate recorded at scan time."]);
        }

        // Estimate first: after emptying there is nothing left to measure.
        RecycleBinEstimate? estimate =
            await Task.Run(() => _recycleBinEstimator.GetEstimate(), cancellationToken).ConfigureAwait(false);

        long bytes = estimate?.TotalBytes ?? scan.TotalBytes;
        int itemCount = estimate?.ItemCount ?? scan.FileCount;

        string? error = await Task.Run(() => _recycleBinEmptier.Empty(), cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            _logger.Warning("Could not empty the Recycle Bin: {Error}", error);
            return new RuleCleanResult(
                rule,
                0,
                0,
                1,
                [new FileCleanOutcome(RecycleBinLabel, 0, FileOutcomeKind.Skipped, error)],
                []);
        }

        _logger.Information(
            "Emptied the Recycle Bin via the Shell API: ~{Bytes} byte(s), ~{Count} item(s) (pre-empty estimate)",
            bytes,
            itemCount);

        return new RuleCleanResult(
            rule,
            bytes,
            itemCount,
            0,
            [new FileCleanOutcome(RecycleBinLabel, bytes, FileOutcomeKind.Deleted, null)],
            ["Size is a pre-empty estimate from the Shell API."]);
    }

    /// <summary>Finds the most specific declared root that contains the finding, or null when none does.</summary>
    private static string? FindContainingRoot(IReadOnlyList<string> roots, string path)
    {
        string? best = null;
        foreach (string root in roots)
        {
            if (PathValidator.IsInsideRoot(path, root) &&
                (best is null || root.Length > best.Length))
            {
                best = root;
            }
        }

        return best;
    }

    private static long? GetSizeBytes(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static FileCleanOutcome Skipped(ScanFinding finding, string reason) =>
        new(finding.Path, finding.SizeBytes, FileOutcomeKind.Skipped, reason);

    private sealed class RunningTotals
    {
        public int FilesProcessed;
        public long BytesFreed;
    }
}
