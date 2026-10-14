using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinClean.App.Converters;
using WinClean.App.Services;
using WinClean.Core.Cleaning;
using WinClean.Core.Models;
using WinClean.Core.Rules;
using WinClean.Core.Scanning;
using WinClean.Core.Storage;
using ILogger = Serilog.ILogger;

namespace WinClean.App.ViewModels;

/// <summary>
/// The Cleaner page. Scan first (read-only), review, then clean: deletion runs only on the
/// checked rules, only after an explicit confirmation dialog, and every clean is written
/// to the local history. Elevation is requested only when enabled rules need it.
/// </summary>
public partial class CleanerViewModel : ViewModelBase
{
    private readonly IRuleLoader _ruleLoader;
    private readonly IScanner _scanner;
    private readonly ICleaner _cleaner;
    private readonly ISettingsStore _settingsStore;
    private readonly IHistoryStore _historyStore;
    private readonly IDialogService _dialogService;
    private readonly IElevationService _elevationService;
    private readonly ILogger _logger;

    public CleanerViewModel(
        IRuleLoader ruleLoader,
        IScanner scanner,
        ICleaner cleaner,
        ISettingsStore settingsStore,
        IHistoryStore historyStore,
        IDialogService dialogService,
        IElevationService elevationService,
        ILogger logger)
    {
        _ruleLoader = ruleLoader;
        _scanner = scanner;
        _cleaner = cleaner;
        _settingsStore = settingsStore;
        _historyStore = historyStore;
        _dialogService = dialogService;
        _elevationService = elevationService;
        _logger = logger;
    }

    [ObservableProperty]
    private CleanerState _state = CleanerState.Idle;

    [ObservableProperty]
    private bool _hasScanned;

    [ObservableProperty]
    private bool _hasLoadErrors;

    [ObservableProperty]
    private string _loadErrorText = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<CategoryViewModel> _categories = [];

    [ObservableProperty]
    private long _totalReclaimableBytes;

    [ObservableProperty]
    private long _selectedBytes;

    [ObservableProperty]
    private int _totalFilesFound;

    [ObservableProperty]
    private int _selectedFiles;

    [ObservableProperty]
    private int _selectedRuleCount;

    [ObservableProperty]
    private string _selectedSummaryText = string.Empty;

    [ObservableProperty]
    private CleanResult? _lastCleanResult;

    private int _filesToClean;

    partial void OnStateChanged(CleanerState value)
    {
        ScanCommand.NotifyCanExecuteChanged();
        CleanSelectedCommand.NotifyCanExecuteChanged();
    }

    private bool CanExecuteScan() => State is not CleanerState.Scanning and not CleanerState.Cleaning;

    private bool CanExecuteCleanSelected() => State == CleanerState.Review && SelectedRuleCount > 0;

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanExecuteScan))]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        State = CleanerState.Scanning;
        HasLoadErrors = false;
        LoadErrorText = string.Empty;
        ProgressPercent = 0;
        StatusText = "Loading rules…";

        try
        {
            RuleLoadResult loaded = _ruleLoader.Load();
            if (loaded.HasErrors)
            {
                HasLoadErrors = true;
                LoadErrorText = string.Join(Environment.NewLine, loaded.Errors.Select(error =>
                    error.RuleId is null ? error.Message : $"Rule '{error.RuleId}': {error.Message}"));
            }

            AppSettings settings = _settingsStore.Load();
            HashSet<string> disabled = new(settings.DisabledRuleIds, StringComparer.OrdinalIgnoreCase);
            List<Rule> enabledRules = [.. loaded.Rules.Where(rule => !disabled.Contains(rule.Id))];

            if (enabledRules.Count == 0)
            {
                ScanStatusResult("No enabled rules to scan — enable rules in Settings.");
                return;
            }

            // Admin rights are only requested when an enabled rule needs them — never at startup.
            if (!HandleElevationForScan(enabledRules))
            {
                return;
            }

            // Progress<T> posts to the captured UI context, so the tree can be built safely below.
            Progress<ScanProgress> progress = new(ReportScanProgress);

            ScanResult result = await _scanner.ScanAsync(enabledRules, progress, cancellationToken);

            BuildReview(result);
            HasScanned = true;
            State = CleanerState.Review;
        }
        catch (OperationCanceledException)
        {
            ScanStatusResult("Scan cancelled.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Scan failed unexpectedly");
            HasLoadErrors = true;
            LoadErrorText = $"The scan failed unexpectedly: {ex.Message}";
            ScanStatusResult("Scan failed.");
        }
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanExecuteCleanSelected))]
    private async Task CleanSelectedAsync(CancellationToken cancellationToken)
    {
        List<RuleScanResult> approved = [.. Categories.SelectMany(category => category.Rules)
            .Where(rule => rule.IsChecked)
            .Select(rule => rule.ScanResult)];
        if (approved.Count == 0)
        {
            return;
        }

        bool adminNote = approved.Any(scan => scan.Rule.RequiresAdmin) && !_elevationService.IsElevated;

        CleanConfirmation confirmation = new(
            SelectedBytes,
            SelectedFiles,
            adminNote,
            [.. approved.Select(scan => new CleanRuleLine(
                scan.Rule.Id,
                scan.Rule.Name,
                scan.Rule.RiskLevel,
                scan.Rule.RequiresAdmin,
                scan.TotalBytes,
                scan.FileCount))]);

        // Deletion only happens after the user reviews these exact rules and confirms.
        if (!_dialogService.ConfirmClean(confirmation))
        {
            return;
        }

        State = CleanerState.Cleaning;
        ProgressPercent = 0;
        _filesToClean = SelectedFiles;
        StatusText = "Preparing to clean…";

        try
        {
            Progress<CleanProgress> progress = new(ReportCleanProgress);

            CleanResult result = await _cleaner.CleanAsync(approved, dryRun: false, progress, cancellationToken);

            _historyStore.Append(ToHistoryEntry(result, DateTime.UtcNow));
            LastCleanResult = result;
            StatusText = $"Freed {ByteFormat.Bytes(result.TotalBytesFreed)} — {result.TotalFilesRemoved:#,0} file(s) removed, {result.TotalFilesSkipped} skipped.";
            State = CleanerState.Summary;
        }
        catch (OperationCanceledException)
        {
            ScanStatusResult("Clean cancelled partway. Run a scan to see the current state again.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Clean failed unexpectedly");
            HasLoadErrors = true;
            LoadErrorText = $"The clean failed unexpectedly: {ex.Message}";
            StatusText = "Clean failed.";
        }
    }

    [RelayCommand]
    private void CloseSummary() => State = CleanerState.Idle;

    /// <summary>
    /// Shows the elevation prompt when enabled rules need admin rights. Returns false when the
    /// scan should be aborted (user cancelled, or the app is relaunching elevated).
    /// </summary>
    private bool HandleElevationForScan(List<Rule> enabledRules)
    {
        List<Rule> adminRules = [.. enabledRules.Where(rule => rule.RequiresAdmin)];
        if (adminRules.Count == 0 || _elevationService.IsElevated)
        {
            return true;
        }

        ElevationDecision decision = _dialogService.PromptForElevation([.. adminRules.Select(rule => rule.Name)]);
        switch (decision)
        {
            case ElevationDecision.Cancel:
                ScanStatusResult("Scan cancelled.");
                return false;

            case ElevationDecision.Elevate:
                StatusText = "Restarting with administrator rights…";
                if (_elevationService.TryRelaunchElevated("--scan"))
                {
                    // This instance is shutting down; the elevated one auto-starts the scan.
                    return false;
                }

                StatusText = "Elevation was not granted — continuing without administrator rights.";
                break;
        }

        return true;
    }

    private void ScanStatusResult(string message)
    {
        StatusText = message;
        State = HasScanned ? CleanerState.Review : CleanerState.Idle;
    }

    private void ReportScanProgress(ScanProgress value)
    {
        ProgressPercent = value.TotalRules == 0 ? 100 : value.CompletedRules * 100.0 / value.TotalRules;
        StatusText = value.CurrentRuleId is null
            ? $"Scanning… {ByteFormat.Bytes(value.BytesFoundSoFar)} found so far"
            : $"Scanning: {value.CurrentRuleId}… {ByteFormat.Bytes(value.BytesFoundSoFar)} found so far";
    }

    private void ReportCleanProgress(CleanProgress value)
    {
        double percent = _filesToClean == 0
            ? 100
            : Math.Min(100, value.FilesProcessed * 100.0 / _filesToClean);

        ProgressPercent = percent;
        StatusText = value.CurrentRuleId is null
            ? $"Cleaning… {ByteFormat.Bytes(value.BytesFreedSoFar)} freed so far"
            : $"Cleaning {value.CurrentRuleId}… {ByteFormat.Bytes(value.BytesFreedSoFar)} freed so far";
    }

    private void BuildReview(ScanResult result)
    {
        List<CategoryViewModel> categories = [];
        foreach (IGrouping<string, RuleScanResult> grouping in
                 result.Rules.GroupBy(scan => scan.Rule.Category, StringComparer.OrdinalIgnoreCase))
        {
            CategoryViewModel category = new(grouping.Key, RecomputeSelection);
            foreach (RuleScanResult scanResult in grouping)
            {
                category.Rules.Add(new RuleViewModel(scanResult, category, RecomputeSelection));
            }

            categories.Add(category);
        }

        Categories = categories;
        TotalReclaimableBytes = result.TotalBytes;
        TotalFilesFound = result.TotalFiles;
        RecomputeSelection();
        StatusText = $"Found {ByteFormat.Bytes(result.TotalBytes)} across {result.TotalFiles:#,0} files. Review before cleaning.";
    }

    private void RecomputeSelection()
    {
        long bytes = 0;
        int files = 0;
        int rules = 0;
        foreach (CategoryViewModel category in Categories)
        {
            foreach (RuleViewModel rule in category.Rules)
            {
                if (!rule.IsChecked)
                {
                    continue;
                }

                bytes += rule.TotalBytes;
                files += rule.FileCount;
                rules++;
            }
        }

        SelectedBytes = bytes;
        SelectedFiles = files;
        SelectedRuleCount = rules;
        SelectedSummaryText = $"{rules} rule(s) · {files:#,0} file(s)";
        CleanSelectedCommand.NotifyCanExecuteChanged();
    }

    private static CleanHistoryEntry ToHistoryEntry(CleanResult result, DateTime completedAtUtc) => new(
        completedAtUtc,
        result.TotalBytesFreed,
        result.TotalFilesRemoved,
        result.TotalFilesSkipped,
        [.. result.Rules.Select(rule => new HistoryRuleEntry(
            rule.Rule.Id,
            rule.Rule.Name,
            rule.BytesFreed,
            rule.FilesRemoved,
            rule.FilesSkipped))]);
}
