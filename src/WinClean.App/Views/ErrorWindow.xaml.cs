using System.Windows;

namespace WinClean.App.Views;

/// <summary>
/// Theme-styled error dialog shown by the global exception handlers. Only view mechanics
/// live here; the error content is set by the caller.
/// </summary>
public partial class ErrorWindow : Window
{
    public ErrorWindow()
    {
        InitializeComponent();
    }

    public void SetError(string heading, string message, string details)
    {
        HeadingTextBlock.Text = heading;
        MessageTextBlock.Text = message;
        DetailsTextBox.Text = details;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnCopyDetailsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DetailsTextBox.Text);
        }
        catch
        {
            // Clipboard access can fail when the clipboard is held by another process.
        }
    }
}
