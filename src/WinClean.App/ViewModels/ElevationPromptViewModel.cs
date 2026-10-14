using CommunityToolkit.Mvvm.Input;
using WinClean.App.Services;

namespace WinClean.App.ViewModels;

/// <summary>Drives the elevation prompt: explains which rules need admin rights and what each choice does.</summary>
public partial class ElevationPromptViewModel : ViewModelBase
{
    public ElevationPromptViewModel(IReadOnlyList<string> adminRuleNames)
    {
        AdminRuleNames = adminRuleNames;
    }

    public IReadOnlyList<string> AdminRuleNames { get; }

    /// <summary>Raised with the user's decision; the dialog service closes the window.</summary>
    public event Action<ElevationDecision>? CloseRequested;

    public string BodyText =>
        $"These enabled rules need administrator rights to scan and clean fully:{Environment.NewLine}{string.Join(Environment.NewLine, AdminRuleNames.Select(name => $"•  {name}"))}{Environment.NewLine}{Environment.NewLine}WinClean can restart as administrator — the current window closes. You can also continue without elevation: locations you lack access to are simply skipped and listed with the reason.";

    [RelayCommand]
    private void Elevate() => CloseRequested?.Invoke(ElevationDecision.Elevate);

    [RelayCommand]
    private void ContinueWithout() => CloseRequested?.Invoke(ElevationDecision.ContinueWithout);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(ElevationDecision.Cancel);
}
