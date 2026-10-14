using System.Windows;
using WinClean.App.ViewModels;
using WinClean.App.Views;

namespace WinClean.App.Services;

/// <summary>Shows the styled WinClean dialogs. Views stay logic-free; the service owns the window lifecycle.</summary>
public sealed class DialogService : IDialogService
{
    public bool ConfirmClean(CleanConfirmation confirmation)
    {
        ConfirmCleanViewModel viewModel = new(confirmation);
        ConfirmCleanWindow window = new()
        {
            Owner = Application.Current.MainWindow,
            DataContext = viewModel,
        };

        bool confirmed = false;
        viewModel.CloseRequested += result =>
        {
            confirmed = result;
            window.Close();
        };

        window.ShowDialog();
        return confirmed;
    }
}
