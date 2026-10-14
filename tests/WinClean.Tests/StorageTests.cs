using WinClean.Core.Storage;

namespace WinClean.Tests;

/// <summary>
/// Tests for the JSON-backed settings and history stores. Fixtures live in a temp folder;
/// corrupt-file behavior is covered so a bad file can never break the app.
/// </summary>
public sealed class StorageTests : IDisposable
{
    private readonly TempFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private string StorePath(string name) => Path.Combine(_fixture.Root, name);

    [Fact]
    public void Settings_Load_MissingFile_ReturnsDefaults()
    {
        SettingsStore store = new(StorePath("missing-settings.json"));

        AppSettings settings = store.Load();

        Assert.Equal(AppSettings.DarkTheme, settings.Theme);
        Assert.Empty(settings.DisabledRuleIds);
    }

    [Fact]
    public void Settings_RoundTrips_ThemeAndDisabledRules()
    {
        string path = StorePath("settings.json");
        SettingsStore store = new(path);
        store.Save(new AppSettings
        {
            Theme = AppSettings.LightTheme,
            DisabledRuleIds = ["prefetch", "crash-dumps"],
        });

        AppSettings loaded = new SettingsStore(path).Load();

        Assert.Equal(AppSettings.LightTheme, loaded.Theme);
        Assert.Equal(["prefetch", "crash-dumps"], loaded.DisabledRuleIds);
    }

    [Fact]
    public void Settings_CorruptFile_ReturnsDefaultsInsteadOfThrowing()
    {
        string path = StorePath("settings.json");
        File.WriteAllText(path, "{ this is not json ]");

        AppSettings loaded = new SettingsStore(path).Load();

        Assert.Equal(AppSettings.DarkTheme, loaded.Theme);
        Assert.Empty(loaded.DisabledRuleIds);
    }

    [Fact]
    public void Settings_Save_CreatesMissingDirectories()
    {
        string path = Path.Combine(_fixture.Root, "deep", "nested", "settings.json");

        new SettingsStore(path).Save(new AppSettings { Theme = AppSettings.LightTheme });

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Settings_Save_LeavesNoTemporaryFileBehind()
    {
        string path = StorePath("settings.json");
        SettingsStore store = new(path);

        store.Save(new AppSettings());

        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void History_Load_MissingFile_ReturnsEmpty()
    {
        HistoryStore store = new(StorePath("missing-history.json"));

        Assert.Empty(store.Load());
    }

    [Fact]
    public void History_CorruptFile_ReturnsEmptyInsteadOfThrowing()
    {
        string path = StorePath("history.json");
        File.WriteAllText(path, "[ definitely not entries");

        Assert.Empty(new HistoryStore(path).Load());
    }

    [Fact]
    public void History_Append_PutsNewestFirst()
    {
        HistoryStore store = new(StorePath("history.json"));
        store.Append(MakeEntry(bytes: 100, ruleName: "first"));
        store.Append(MakeEntry(bytes: 200, ruleName: "second"));

        IReadOnlyList<CleanHistoryEntry> entries = store.Load();

        Assert.Equal(2, entries.Count);
        Assert.Equal(200, entries[0].TotalBytesFreed);
        Assert.Equal("second", entries[0].Rules[0].RuleName);
        Assert.Equal(100, entries[1].TotalBytesFreed);
    }

    [Fact]
    public void History_Append_RetainsAtMostMaxEntries()
    {
        HistoryStore store = new(StorePath("history.json"));

        for (int i = 0; i < HistoryStore.MaxEntries + 10; i++)
        {
            store.Append(MakeEntry(bytes: i, ruleName: $"entry-{i}"));
        }

        IReadOnlyList<CleanHistoryEntry> entries = store.Load();

        Assert.Equal(HistoryStore.MaxEntries, entries.Count);

        // Newest kept at the front, oldest evicted.
        Assert.Equal("entry-259", entries[0].Rules[0].RuleName);
        Assert.Equal($"entry-{HistoryStore.MaxEntries + 9}", entries[0].Rules[0].RuleName);
    }

    [Fact]
    public void History_RoundTrips_EntryFields()
    {
        string path = StorePath("history.json");
        CleanHistoryEntry entry = new(
            CompletedAtUtc: new DateTime(2026, 10, 14, 12, 30, 0, DateTimeKind.Utc),
            TotalBytesFreed: 123456,
            TotalFilesRemoved: 42,
            TotalFilesSkipped: 3,
            Rules:
            [
                new HistoryRuleEntry("user-temp", "User temporary files", 100000, 40, 1),
                new HistoryRuleEntry("recycle-bin", "Recycle Bin", 23456, 2, 2),
            ]);

        new HistoryStore(path).Append(entry);

        CleanHistoryEntry loaded = Assert.Single(new HistoryStore(path).Load());

        Assert.Equal(entry.CompletedAtUtc, loaded.CompletedAtUtc);
        Assert.Equal(123456, loaded.TotalBytesFreed);
        Assert.Equal(42, loaded.TotalFilesRemoved);
        Assert.Equal(3, loaded.TotalFilesSkipped);
        Assert.Equal(2, loaded.Rules.Count);
        Assert.Equal("user-temp", loaded.Rules[0].RuleId);
        Assert.Equal(100000, loaded.Rules[0].BytesFreed);
        Assert.Equal(2, loaded.Rules[1].FilesSkipped);
    }

    private static CleanHistoryEntry MakeEntry(long bytes, string ruleName) => new(
        CompletedAtUtc: DateTime.UtcNow,
        TotalBytesFreed: bytes,
        TotalFilesRemoved: 1,
        TotalFilesSkipped: 0,
        Rules: [new HistoryRuleEntry(ruleName, ruleName, bytes, 1, 0)]);
}
