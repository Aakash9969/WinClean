namespace WinClean.Core.Storage;

/// <summary>Loads and saves the user's settings.</summary>
public interface ISettingsStore
{
    /// <summary>Returns stored settings, or defaults when nothing was saved yet or the file is unreadable.</summary>
    AppSettings Load();

    /// <summary>Saves settings atomically (temp file + rename) so a crash can never corrupt them.</summary>
    void Save(AppSettings settings);
}
