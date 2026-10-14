using CommunityToolkit.Mvvm.ComponentModel;

namespace WinClean.App.ViewModels;

/// <summary>A category node in the review tree, with a tri-state checkbox that selects all its rules.</summary>
public partial class CategoryViewModel : ObservableObject
{
    private readonly Action _selectionChanged;
    private bool _updatingFromChildren;

    public CategoryViewModel(string name, Action selectionChanged)
    {
        Name = name;
        _selectionChanged = selectionChanged;
    }

    public string Name { get; }

    public List<RuleViewModel> Rules { get; } = [];

    public long TotalBytes => Rules.Sum(rule => rule.TotalBytes);

    public int TotalFiles => Rules.Sum(rule => rule.FileCount);

    [ObservableProperty]
    private bool? _isChecked = true;

    partial void OnIsCheckedChanged(bool? value)
    {
        if (_updatingFromChildren)
        {
            return;
        }

        bool target = value == true;
        foreach (RuleViewModel rule in Rules)
        {
            rule.IsChecked = target;
        }

        _selectionChanged();
    }

    /// <summary>Re-derives the tri-state from the rule checkboxes, without re-applying them.</summary>
    public void RefreshFromChildren()
    {
        bool? state = Rules.All(rule => rule.IsChecked) ? true
            : Rules.All(rule => !rule.IsChecked) ? false
            : null;

        if (state == IsChecked)
        {
            return;
        }

        _updatingFromChildren = true;
        IsChecked = state;
        _updatingFromChildren = false;
    }
}
