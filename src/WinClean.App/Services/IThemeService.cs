namespace WinClean.App.Services;

/// <summary>The app's visual theme.</summary>
public enum AppTheme
{
    Dark,
    Light,
}

/// <summary>Applies themes at runtime by swapping the palette dictionary.</summary>
public interface IThemeService
{
    /// <summary>The currently applied theme.</summary>
    AppTheme Current { get; }

    /// <summary>Applies the given theme immediately. Re-themes every open window.</summary>
    void Apply(AppTheme theme);
}
