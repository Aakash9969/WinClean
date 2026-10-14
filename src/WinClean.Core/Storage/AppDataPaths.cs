namespace WinClean.Core.Storage;

/// <summary>Well-known locations for WinClean's local data under %LOCALAPPDATA%\WinClean.</summary>
public static class AppDataPaths
{
    public static string BaseDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinClean");

    public static string SettingsPath => Path.Combine(BaseDirectory, "settings.json");

    public static string HistoryPath => Path.Combine(BaseDirectory, "history.json");
}
