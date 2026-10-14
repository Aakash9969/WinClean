using CommunityToolkit.Mvvm.Input;
using WinClean.App.Converters;
using WinClean.App.Services;

namespace WinClean.App.ViewModels;

/// <summary>Drives the confirm-clean dialog: shows exactly what will be deleted and requires an explicit decision.</summary>
public partial class ConfirmCleanViewModel : ViewModelBase
{
    public ConfirmCleanViewModel(CleanConfirmation confirmation)
    {
        Confirmation = confirmation;
    }

    public CleanConfirmation Confirmation { get; }

    /// <summary>Raised with the user's decision; the dialog service closes the window.</summary>
    public event Action<bool>? CloseRequested;

    public string HeadingText =>
        $"About {ByteFormat.Bytes(Confirmation.TotalBytes)} across {Confirmation.TotalFiles:#,0} files in {Confirmation.Rules.Count} rule(s) will be deleted permanently. This cannot be undone.";

    [RelayCommand]
    private void Confirm() => CloseRequested?.Invoke(true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
