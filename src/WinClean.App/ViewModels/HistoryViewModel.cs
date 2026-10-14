using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinClean.Core.Storage;

namespace WinClean.App.ViewModels;

/// <summary>One row on the History page: when the clean ran and what it achieved.</summary>
public partial class HistoryEntryViewModel : ObservableObject
{
    private readonly CleanHistoryEntry _entry;

    public HistoryEntryViewModel(CleanHistoryEntry entry)
    {
        _entry = entry;
    }

    public string WhenText => _entry.CompletedAtUtc.ToLocalTime().ToString("ddd, d MMM yyyy, HH:mm");

    public long TotalBytesFreed => _entry.TotalBytesFreed;

    public int TotalFilesRemoved => _entry.TotalFilesRemoved;

    public int TotalFilesSkipped => _entry.TotalFilesSkipped;

    public string SkippedNote => _entry.TotalFilesSkipped == 0
        ? "All selected files were removed."
        : $"{_entry.TotalFilesSkipped} file(s) were skipped — locked, protected or inaccessible (hover for details).";

    public string RulesToolTip => _entry.Rules.Count == 0
        ? "No rules were cleaned."
        : string.Join(Environment.NewLine, _entry.Rules.Select(rule =>
            $"{rule.RuleName}: {rule.BytesFreed:#,0} bytes, {rule.FilesRemoved:#,0} removed, {rule.FilesSkipped} skipped"));
}

/// <summary>The History page: every past clean, read from the local JSON history.</summary>
public partial class HistoryViewModel : ViewModelBase
{
    private readonly IHistoryStore _historyStore;

    public HistoryViewModel(IHistoryStore historyStore)
    {
        _historyStore = historyStore;
    }

    [ObservableProperty]
    private IReadOnlyList<HistoryEntryViewModel> _entries = [];

    [ObservableProperty]
    private bool _hasEntries;

    [RelayCommand]
    public void Refresh()
    {
        Entries = [.. _historyStore.Load().Select(entry => new HistoryEntryViewModel(entry))];
        HasEntries = Entries.Count > 0;
    }
}
