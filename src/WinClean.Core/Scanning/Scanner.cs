using WinClean.Core.Models;
using WinClean.Core.Paths;
using WinClean.Core.Special;

namespace WinClean.Core.Scanning;

/// <summary>
/// Default <see cref="IScanner"/> implementation: async, cancellable, progress-reporting and
/// safety-checked. Every rule root and every candidate file is validated by <see cref="PathValidator"/>,
/// and symlinks/junctions are never followed during traversal.
/// </summary>
public sealed class Scanner : IScanner
{
    private readonly PathValidator _validator;
    private readonly IRecycleBinEstimator _recycleBinEstimator;

    public Scanner(PathValidator validator, IRecycleBinEstimator recycleBinEstimator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _recycleBinEstimator = recycleBinEstimator ?? throw new ArgumentNullException(nameof(recycleBinEstimator));
    }

    public async Task<ScanResult> ScanAsync(
        IReadOnlyList<Rule> rules,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);

        List<RuleScanResult> results = new(rules.Count);
        long bytesSoFar = 0;
        int filesSoFar = 0;

        for (int index = 0; index < rules.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Rule rule = rules[index];
            progress?.Report(new ScanProgress(index, rules.Count, rule.Id, bytesSoFar, filesSoFar));

            RuleScanResult result = rule.IsSpecial
                ? await ScanSpecialAsync(rule, cancellationToken).ConfigureAwait(false)
                : await Task.Run(() => ScanRule(rule, cancellationToken), cancellationToken).ConfigureAwait(false);

            results.Add(result);
            bytesSoFar += result.TotalBytes;
            filesSoFar += result.FileCount;

            progress?.Report(new ScanProgress(index + 1, rules.Count, rule.Id, bytesSoFar, filesSoFar));
        }

        return new ScanResult(results);
    }

    private async Task<RuleScanResult> ScanSpecialAsync(Rule rule, CancellationToken cancellationToken)
    {
        if (!string.Equals(rule.SpecialTarget, SpecialTargets.RecycleBin, StringComparison.OrdinalIgnoreCase))
        {
            return new RuleScanResult(rule, 0, 0, [], [], [$"Rule '{rule.Id}' uses unknown specialTarget '{rule.SpecialTarget}'."]);
        }

        RecycleBinEstimate? estimate =
            await Task.Run(() => _recycleBinEstimator.GetEstimate(), cancellationToken).ConfigureAwait(false);

        return estimate is null
            ? new RuleScanResult(rule, 0, 0, [], [], ["Recycle Bin size could not be determined (Shell API unavailable)."])
            : new RuleScanResult(rule, estimate.TotalBytes, estimate.ItemCount, [], [], []);
    }

    private RuleScanResult ScanRule(Rule rule, CancellationToken cancellationToken)
    {
        List<ScanFinding> findings = [];
        List<string> notes = [];

        List<string> roots = [];
        foreach (string pattern in rule.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            roots.AddRange(ExpandRoot(pattern));
        }

        if (roots.Count == 0 && rule.Paths.Count > 0)
        {
            notes.Add("No folders matched the configured paths.");
        }

        // Only roots that actually exist are recorded: the cleaner re-validates containment
        // against these at delete time, and nothing can have been found under a missing root.
        List<string> scannedRoots = [];
        foreach (string root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(root))
            {
                scannedRoots.Add(root);
            }

            ScanRoot(rule, root, findings, notes, cancellationToken);
        }

        return new RuleScanResult(rule, findings.Sum(finding => finding.SizeBytes), findings.Count, findings, scannedRoots, notes);
    }

    private void ScanRoot(
        Rule rule,
        string root,
        List<ScanFinding> findings,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            notes.Add($"Path not found: '{root}'.");
            return;
        }

        PathValidationResult rootValidation = _validator.ValidateRoot(root);
        if (!rootValidation.IsAllowed)
        {
            notes.Add($"Skipped '{root}': {rootValidation.Reason}");
            return;
        }

        EnumerationOptions options = new()
        {
            RecurseSubdirectories = rule.Recursive,
            IgnoreInaccessible = true,

            // Never follow and never return symlinks or junctions, at any depth.
            AttributesToSkip = FileAttributes.ReparsePoint,

            MatchType = MatchType.Win32,
        };

        try
        {
            foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles(rule.FilePattern, options))
            {
                cancellationToken.ThrowIfCancellationRequested();

                PathValidationResult validation = _validator.ValidateFile(file.FullName, root);
                if (!validation.IsAllowed)
                {
                    notes.Add($"Skipped '{file.FullName}': {validation.Reason}");
                    continue;
                }

                findings.Add(new ScanFinding(rule.Id, file.FullName, file.Length));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notes.Add($"Could not fully scan '{root}': {ex.Message}");
        }
    }

    /// <summary>
    /// Expands '*' segments in a rule path: "…\User Data\*\Cache" yields one concrete root per
    /// matching directory (for example one per browser profile). Patterns without wildcards pass
    /// through unchanged, even when the folder does not exist — missing folders are reported as notes.
    /// </summary>
    private static IEnumerable<string> ExpandRoot(string pattern)
    {
        if (!pattern.Contains('*'))
        {
            yield return pattern;
            yield break;
        }

        string? parent = Path.GetDirectoryName(pattern);
        string segment = Path.GetFileName(pattern);

        IEnumerable<string> parentPaths = parent is null ? [pattern] : ExpandRoot(parent);
        foreach (string parentPath in parentPaths)
        {
            if (!segment.Contains('*'))
            {
                yield return Path.Combine(parentPath, segment);
                continue;
            }

            string[] matches;
            try
            {
                matches = Directory.GetDirectories(parentPath, segment);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string match in matches)
            {
                yield return match;
            }
        }
    }
}
