using System.Text.Json;

namespace WinClean.Core.Storage;

/// <summary>
/// JSON-backed clean history. Reads are defensive: a missing or corrupt file yields an empty
/// history instead of throwing. Writes are atomic.
/// </summary>
public sealed class HistoryStore : IHistoryStore
{
    public const int MaxEntries = 250;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;

    public HistoryStore()
        : this(path: null)
    {
    }

    public HistoryStore(string? path)
    {
        _path = path ?? AppDataPaths.HistoryPath;
    }

    public IReadOnlyList<CleanHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<CleanHistoryEntry>>(File.ReadAllText(_path), JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public void Append(CleanHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        List<CleanHistoryEntry> entries = [.. Load()];
        entries.Insert(0, entry);

        if (entries.Count > MaxEntries)
        {
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(entries, JsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }
}
