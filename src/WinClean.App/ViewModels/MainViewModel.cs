using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinClean.App.Services;

namespace WinClean.App.ViewModels;

/// <summary>
/// The application shell: owns the pages, drives sidebar navigation, and refreshes pages
/// that read live or persisted data when they are shown.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    public MainViewModel(
        CleanerViewModel cleaner,
        MemoryViewModel memory,
        HistoryViewModel history,
        SettingsViewModel settings,
        IElevationService elevationService)
    {
        Cleaner = cleaner;
        Memory = memory;
        History = history;
        Settings = settings;
        IsRunningElevated = elevationService.IsElevated;
        _currentPage = cleaner;
    }

    public CleanerViewModel Cleaner { get; }

    public MemoryViewModel Memory { get; }

    public HistoryViewModel History { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>True when the app runs with administrator rights (after an elevation restart).</summary>
    public bool IsRunningElevated { get; }

    public string WindowTitle => IsRunningElevated ? "WinClean (Administrator)" : "WinClean";

    [ObservableProperty]
    private ViewModelBase _currentPage;

    public bool IsCleanerActive => CurrentPage == Cleaner;

    public bool IsMemoryActive => CurrentPage == Memory;

    public bool IsHistoryActive => CurrentPage == History;

    public bool IsSettingsActive => CurrentPage == Settings;

    partial void OnCurrentPageChanged(ViewModelBase value)
    {
        OnPropertyChanged(nameof(IsCleanerActive));
        OnPropertyChanged(nameof(IsMemoryActive));
        OnPropertyChanged(nameof(IsHistoryActive));
        OnPropertyChanged(nameof(IsSettingsActive));

        // Pages that read live or persisted data refresh when they become visible.
        if (CurrentPage == Memory)
        {
            Memory.Refresh();
        }
        else if (CurrentPage == History)
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
            "memory" => Memory,
            "history" => History,
            "settings" => Settings,
            _ => Cleaner,
        };
    }
}
