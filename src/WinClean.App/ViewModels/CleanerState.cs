namespace WinClean.App.ViewModels;

/// <summary>The UI states of the Cleaner page.</summary>
public enum CleanerState
{
    /// <summary>Nothing scanned yet — show the scan call to action.</summary>
    Idle,

    /// <summary>A scan is running.</summary>
    Scanning,

    /// <summary>Scan finished — show the review tree for approval.</summary>
    Review,

    /// <summary>Deletion is running after the user confirmed the clean.</summary>
    Cleaning,

    /// <summary>Clean finished — show the summary.</summary>
    Summary,
}
