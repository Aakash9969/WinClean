namespace WinClean.Core.Storage;

/// <summary>Stores past cleans locally so the History page can show them.</summary>
public interface IHistoryStore
{
    /// <summary>Returns past cleans, newest first. Empty when nothing was cleaned yet or the file is unreadable.</summary>
    IReadOnlyList<CleanHistoryEntry> Load();

    /// <summary>Adds an entry at the front and trims the list to the retention limit.</summary>
    void Append(CleanHistoryEntry entry);
}
