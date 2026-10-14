using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinClean.App.Converters;
using WinClean.Core.Rules;
using WinClean.Core.Scanning;

namespace WinClean.App.ViewModels;

/// <summary>
/// The Cleaner page: scans (read-only!), reports progress, and builds the review tree the user
/// approves. Deletion itself happens only through the clean flow, which requires an explicit
/// confirmation on the findings shown here.
/// </summary>
public partial class CleanerViewModel : ViewModelBase
{
    private readonly IRuleLoader _ruleLoader;
    private readonly IScanner _scanner;

    public CleanerViewModel(IRuleLoader ruleLoader, IScanner scanner)
    {
        _ruleLoader = ruleLoader;
        _scanner = scanner;
    }

    [ObservableProperty]
    private ScanState _state = ScanState.Idle;

    [ObservableProperty]
    private bool _hasScanned;

    [ObservableProperty]
    private bool _hasLoadErrors;

    [ObservableProperty]
    private string _loadErrorText = string.Empty;

    [ObservableProperty]
    private double _scanProgressPercent;

    [ObservableProperty]
    private string _scanStatusText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<CategoryViewModel> _categories = [];

    [ObservableProperty]
    private long _totalReclaimableBytes;

    [ObservableProperty]
    private long _selectedBytes;

    [ObservableProperty]
    private int _totalFilesFound;

    [ObservableProperty]
    private string _selectedSummaryText = string.Empty;

    partial void OnStateChanged(ScanState value) => ScanCommand.NotifyCanExecuteChanged();

    private bool CanExecuteScan() => State != ScanState.Scanning;

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanExecuteScan))]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        State = ScanState.Scanning;
        HasLoadErrors = false;
        LoadErrorText = string.Empty;
        ScanProgressPercent = 0;
        ScanStatusText = "Loading rules…";

        try
        {
            RuleLoadResult loaded = _ruleLoader.Load();
            if (loaded.HasErrors)
            {
                HasLoadErrors = true;
                LoadErrorText = string.Join(Environment.NewLine, loaded.Errors.Select(error =>
                    error.RuleId is null ? error.Message : $"Rule '{error.RuleId}': {error.Message}"));
            }

            if (loaded.Rules.Count == 0)
            {
                ScanStatusText = "No valid rules to scan — see the message above.";
                State = HasScanned ? ScanState.Review : ScanState.Idle;
                return;
            }

            // Progress<T> posts to the captured UI context, so the tree can be built safely below.
            Progress<ScanProgress> progress = new(ReportScanProgress);

            ScanResult result = await _scanner.ScanAsync(loaded.Rules, progress, cancellationToken);

            BuildReview(result);
            HasScanned = true;
            State = ScanState.Review;
        }
        catch (OperationCanceledException)
        {
            ScanStatusText = "Scan cancelled.";
            State = HasScanned ? ScanState.Review : ScanState.Idle;
        }
    }

    private void ReportScanProgress(ScanProgress value)
    {
        ScanProgressPercent = value.TotalRules == 0 ? 100 : value.CompletedRules * 100.0 / value.TotalRules;
        ScanStatusText = value.CurrentRuleId is null
            ? $"Scanning… {ByteFormat.Bytes(value.BytesFoundSoFar)} found so far"
            : $"Scanning: {value.CurrentRuleId}… {ByteFormat.Bytes(value.BytesFoundSoFar)} found so far";
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
        ScanStatusText = $"Found {ByteFormat.Bytes(result.TotalBytes)} across {result.TotalFiles} files. Review before cleaning.";
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
        SelectedSummaryText = $"{rules} rule(s) · {files} file(s)";
    }
}
