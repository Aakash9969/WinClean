using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinClean.App.ViewModels;

/// <summary>
/// The application shell: owns the pages, drives sidebar navigation, and refreshes pages
/// that read persisted data when they are shown.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    public MainViewModel(CleanerViewModel cleaner, HistoryViewModel history, SettingsViewModel settings)
    {
        Cleaner = cleaner;
        History = history;
        Settings = settings;
        _currentPage = cleaner;
    }

    public CleanerViewModel Cleaner { get; }

    public HistoryViewModel History { get; }

    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private ViewModelBase _currentPage;

    public bool IsCleanerActive => CurrentPage == Cleaner;

    public bool IsHistoryActive => CurrentPage == History;

    public bool IsSettingsActive => CurrentPage == Settings;

    partial void OnCurrentPageChanged(ViewModelBase value)
    {
        OnPropertyChanged(nameof(IsCleanerActive));
        OnPropertyChanged(nameof(IsHistoryActive));
        OnPropertyChanged(nameof(IsSettingsActive));

        // Pages that read persisted data refresh when they become visible.
        if (CurrentPage == History)
        {
            History.Refresh();
        }
        else if (CurrentPage == Settings)
        {
            Settings.Refresh();
        }
    }

    [RelayCommand]
    private void Navigate(string? target)
    {
        CurrentPage = target switch
        {
            "history" => History,
            "settings" => Settings,
            _ => Cleaner,
        };
    }
}
