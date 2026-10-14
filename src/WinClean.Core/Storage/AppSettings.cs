namespace WinClean.Core.Storage;

/// <summary>
/// User settings: the selected theme and the rule ids that are turned off. Rule ids that are
/// absent from <see cref="DisabledRuleIds"/> are enabled by default, so newly added rules
/// participate in scans until the user turns them off.
/// </summary>
public sealed record AppSettings
{
    public const string DarkTheme = "Dark";
    public const string LightTheme = "Light";

    public string Theme { get; init; } = DarkTheme;

    public IReadOnlyList<string> DisabledRuleIds { get; init; } = [];
}
