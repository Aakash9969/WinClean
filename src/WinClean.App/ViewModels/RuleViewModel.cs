using CommunityToolkit.Mvvm.ComponentModel;
using WinClean.Core.Models;
using WinClean.Core.Scanning;

namespace WinClean.App.ViewModels;

/// <summary>
/// A rule node in the review tree: checkbox, risk badge, admin badge, sizes — and, when
/// expanded, the exact files it found plus any scan notes. Checked state starts from the
/// rule's default selection (Safe rules preselected, risky targets not).
/// </summary>
public partial class RuleViewModel : ObservableObject
{
    /// <summary>Files listed under a rule before the tree starts scrolling; UI responsiveness guard.</summary>
    private const int MaxVisibleFiles = 500;

    private readonly CategoryViewModel _category;
    private readonly Action _selectionChanged;

    public RuleViewModel(RuleScanResult scanResult, CategoryViewModel category, Action selectionChanged)
    {
        ScanResult = scanResult;
        _category = category;
        _selectionChanged = selectionChanged;
        _isChecked = scanResult.Rule.CheckedByDefault;

        List<object> details = [];
        foreach (ScanFinding finding in ScanResult.Findings.Take(MaxVisibleFiles))
        {
            details.Add(new FileReviewItem(finding.Path, finding.SizeBytes));
        }

        if (ScanResult.Findings.Count > MaxVisibleFiles)
        {
            details.Add(new MoreFilesItem(ScanResult.Findings.Count - MaxVisibleFiles));
        }

        foreach (string note in ScanResult.Notes)
        {
            details.Add(new RuleNoteItem(note));
        }

        Details = details;
    }

    public Rule Rule => ScanResult.Rule;

    public RuleScanResult ScanResult { get; }

    /// <summary>Files (and notes) shown when this rule is expanded in the review tree.</summary>
    public IReadOnlyList<object> Details { get; }

    /// <summary>Two-way expansion state for the tree container.</summary>
    public bool IsExpanded { get; set; }

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
    private bool _isChecked;

    partial void OnIsCheckedChanged(bool value)
    {
        _category.RefreshFromChildren();
        _selectionChanged();
    }
}

/// <summary>One file that will be deleted when this rule is cleaned.</summary>
public sealed record FileReviewItem(string Path, long SizeBytes)
{
    public bool IsExpanded { get; set; }
}

/// <summary>Summary row when a rule found more files than the tree lists.</summary>
public sealed record MoreFilesItem(int Count)
{
    public bool IsExpanded { get; set; }
}

/// <summary>A scan-time note (e.g. a skipped path with its reason) shown under the rule.</summary>
public sealed record RuleNoteItem(string Text)
{
    public bool IsExpanded { get; set; }
}
