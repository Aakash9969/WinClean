using CommunityToolkit.Mvvm.ComponentModel;
using WinClean.Core.Models;
using WinClean.Core.Scanning;

namespace WinClean.App.ViewModels;

/// <summary>A rule node in the review tree: checkbox, risk badge, admin badge, sizes and scan notes.</summary>
public partial class RuleViewModel : ObservableObject
{
    private readonly CategoryViewModel _category;
    private readonly Action _selectionChanged;

    public RuleViewModel(RuleScanResult scanResult, CategoryViewModel category, Action selectionChanged)
    {
        ScanResult = scanResult;
        _category = category;
        _selectionChanged = selectionChanged;
    }

    public Rule Rule => ScanResult.Rule;

    public RuleScanResult ScanResult { get; }

    public long TotalBytes => ScanResult.TotalBytes;

    public int FileCount => ScanResult.FileCount;

    public RiskLevel RiskLevel => Rule.RiskLevel;

    public bool RequiresAdmin => Rule.RequiresAdmin;

    public bool HasNotes => ScanResult.Notes.Count > 0;

    public int NoteCount => ScanResult.Notes.Count;

    public string ToolTipText =>
        ScanResult.Notes.Count == 0
            ? Rule.Description
            : $"{Rule.Description}{Environment.NewLine}{Environment.NewLine}Notes:{Environment.NewLine}{string.Join(Environment.NewLine, ScanResult.Notes)}";

    [ObservableProperty]
    private bool _isChecked = true;

    partial void OnIsCheckedChanged(bool value)
    {
        _category.RefreshFromChildren();
        _selectionChanged();
    }
}
