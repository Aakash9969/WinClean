using System.Windows;
using WinClean.Core.Storage;

namespace WinClean.App.Services;

/// <summary>
/// Applies themes by swapping the palette resource dictionary. All palette references in the
/// Controls dictionary and the views are DynamicResources, so the whole app re-themes live.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private const string PaletteUriFormat = "pack://application:,,,/WinClean;component/Theme/Palette.{0}.xaml";

    public AppTheme Current { get; private set; } = AppTheme.Dark;

    public void Apply(AppTheme theme)
    {
        Current = theme;

        ResourceDictionary palette = new()
        {
            Source = new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, PaletteUriFormat, theme), UriKind.Absolute),
        };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        for (int index = 0; index < dictionaries.Count; index++)
        {
            if (dictionaries[index].Source?.OriginalString.Contains("Palette.", StringComparison.OrdinalIgnoreCase) == true)
            {
                dictionaries[index] = palette;
                return;
            }
        }

        // No palette merged yet (should not happen — App.xaml merges one) — put it in front.
        dictionaries.Insert(0, palette);
    }

    /// <summary>Parses a persisted theme name, falling back to Dark.</summary>
    public static AppTheme ParseThemeName(string? name) =>
        string.Equals(name, AppSettings.LightTheme, StringComparison.OrdinalIgnoreCase)
            ? AppTheme.Light
            : AppTheme.Dark;
}
