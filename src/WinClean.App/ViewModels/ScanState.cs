namespace WinClean.App.ViewModels;

/// <summary>The UI states of the Cleaner page.</summary>
public enum ScanState
{
    /// <summary>Nothing scanned yet — show the scan call to action.</summary>
    Idle,

    /// <summary>A scan is running.</summary>
    Scanning,

    /// <summary>Scan finished — show the review tree.</summary>
    Review,
}
