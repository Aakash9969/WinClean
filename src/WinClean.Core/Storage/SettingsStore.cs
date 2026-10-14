using System.Text.Json;

namespace WinClean.Core.Storage;

/// <summary>
/// JSON-backed settings storage. Reads are defensive: a missing or corrupt file yields defaults
/// instead of throwing, so a bad file can never stop the app from starting. Writes are atomic.
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;

    public SettingsStore()
        : this(path: null)
    {
    }

    public SettingsStore(string? path)
    {
        _path = path ?? AppDataPaths.SettingsPath;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }
}
