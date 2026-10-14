using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinClean.App.Services;
using WinClean.Core.Models;
using WinClean.Core.Rules;
using WinClean.Core.Storage;

namespace WinClean.App.ViewModels;

/// <summary>One rule on the Settings page with its enabled/disabled checkbox.</summary>
public partial class RuleSettingViewModel : ObservableObject
{
    private readonly Action _changed;

    public RuleSettingViewModel(Rule rule, bool isEnabled, Action changed)
    {
        Rule = rule;
        _changed = changed;
        _isEnabled = isEnabled;
    }

    public Rule Rule { get; }

    public string RuleId => Rule.Id;

    public string RuleName => Rule.Name;

    public string Category => Rule.Category;

    public RiskLevel RiskLevel => Rule.RiskLevel;

    public bool RequiresAdmin => Rule.RequiresAdmin;

    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value) => _changed();

    public string ToolTipText => $"{Rule.Description}{Environment.NewLine}{Environment.NewLine}Disabled rules are skipped entirely and never appear in the review.";
}

/// <summary>
/// The Settings page: the theme and which rules are enabled by default. Every change applies
/// immediately and is persisted to %LOCALAPPDATA%\WinClean\settings.json.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsStore _settingsStore;
    private readonly IRuleLoader _ruleLoader;
    private readonly IThemeService _themeService;

    public SettingsViewModel(ISettingsStore settingsStore, IRuleLoader ruleLoader, IThemeService themeService)
    {
        _settingsStore = settingsStore;
        _ruleLoader = ruleLoader;
        _themeService = themeService;
    }

    [ObservableProperty]
    private IReadOnlyList<RuleSettingViewModel> _rules = [];

    [ObservableProperty]
    private AppTheme _selectedTheme;

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        _themeService.Apply(value);
        PersistTheme(value);
    }

    [RelayCommand]
    public void Refresh()
    {
        AppSettings settings = _settingsStore.Load();
        SelectedTheme = ThemeService.ParseThemeName(settings.Theme);

        HashSet<string> disabled = new(settings.DisabledRuleIds, StringComparer.OrdinalIgnoreCase);
        Rules = [.. _ruleLoader.Load().Rules
            .Select(rule => new RuleSettingViewModel(rule, !disabled.Contains(rule.Id), OnRuleToggled))];
    }

    [RelayCommand]
    private void UseDarkTheme() => SelectedTheme = AppTheme.Dark;

    [RelayCommand]
    private void UseLightTheme() => SelectedTheme = AppTheme.Light;

    private void OnRuleToggled()
    {
        AppSettings settings = _settingsStore.Load();
        List<string> disabled = [.. settings.DisabledRuleIds];

        foreach (RuleSettingViewModel rule in Rules)
        {
            int existing = disabled.FindIndex(id => string.Equals(id, rule.RuleId, StringComparison.OrdinalIgnoreCase));
            if (!rule.IsEnabled && existing < 0)
            {
                disabled.Add(rule.RuleId);
            }
            else if (rule.IsEnabled && existing >= 0)
            {
                disabled.RemoveAt(existing);
            }
        }

        _settingsStore.Save(settings with { DisabledRuleIds = disabled });
    }

    private void PersistTheme(AppTheme theme)
    {
        AppSettings settings = _settingsStore.Load();
        _settingsStore.Save(settings with
        {
            Theme = theme == AppTheme.Light ? AppSettings.LightTheme : AppSettings.DarkTheme,
        });
    }
}
