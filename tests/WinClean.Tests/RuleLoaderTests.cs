using WinClean.Core.Models;
using WinClean.Core.Rules;

namespace WinClean.Tests;

/// <summary>
/// Tests for <see cref="RuleLoader"/>. All fixtures are written into a temp folder and
/// removed afterwards; no real system folders are ever touched.
/// </summary>
public sealed class RuleLoaderTests : IDisposable
{
    private readonly string _fixtureDir;

    public RuleLoaderTests()
    {
        _fixtureDir = Path.Combine(Path.GetTempPath(), "WinCleanTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fixtureDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_fixtureDir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string WriteRules(string json)
    {
        string path = Path.Combine(_fixtureDir, "rules.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string Wrap(params string[] bodies) =>
        $$"""
        {
          "rules": [
            {{string.Join(",\n    ", bodies)}}
          ]
        }
        """;

    private const string ValidRule =
        """
        {
          "id": "test-rule",
          "name": "Test rule",
          "category": "Test",
          "description": "A test rule.",
          "riskLevel": "Safe",
          "requiresAdmin": false,
          "paths": [ "%TEMP%\\sub" ],
          "filePattern": "*.tmp",
          "recursive": true
        }
        """;

    [Fact]
    public void Load_BundledRules_LoadsAllInitialRulesWithoutErrors()
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, "rules.json");
        Assert.True(File.Exists(bundled), "rules.json should be copied to the test output directory.");

        RuleLoadResult result = new RuleLoader().Load(bundled);

        Assert.False(result.HasErrors);
        string[] expectedIds =
        [
            "user-temp",
            "windows-temp",
            "windows-update-cache",
            "prefetch",
            "thumbnail-cache",
            "crash-dumps",
            "windows-error-reports",
            "recycle-bin",
            "chrome-cache",
            "edge-cache",
            "firefox-cache",
        ];
        Assert.Equal(expectedIds.OrderBy(id => id, StringComparer.Ordinal),
            result.Rules.Select(rule => rule.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void Load_BundledRules_HasFullyExpandedAbsolutePaths()
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, "rules.json");

        RuleLoadResult result = new RuleLoader().Load(bundled);

        Assert.False(result.HasErrors);
        Assert.All(result.Rules, rule => Assert.All(rule.Paths, path => Assert.DoesNotContain("%", path)));
        Assert.All(result.Rules, rule => Assert.All(rule.Paths, path =>
            Assert.True(Path.IsPathFullyQualified(path), $"Path '{path}' should be absolute.")));
        Assert.All(result.Rules, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Category)));
    }

    [Fact]
    public void Load_BundledRules_RecycleBinIsASpecialTargetRule()
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, "rules.json");

        RuleLoadResult result = new RuleLoader().Load(bundled);

        Rule recycleBin = result.Rules.Single(rule => rule.Id == "recycle-bin");
        Assert.True(recycleBin.IsSpecial);
        Assert.Equal(SpecialTargets.RecycleBin, recycleBin.SpecialTarget);
        Assert.Empty(recycleBin.Paths);
    }

    [Fact]
    public void Load_ExpandsEnvironmentVariables()
    {
        string expectedRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("TEMP")
            ?? throw new InvalidOperationException("TEMP is not set."));
        string path = WriteRules(Wrap(ValidRule));

        RuleLoadResult result = new RuleLoader().Load(path);

        Assert.False(result.HasErrors);
        Rule rule = Assert.Single(result.Rules);
        string expanded = Assert.Single(rule.Paths);
        Assert.Equal(Path.Combine(expectedRoot, "sub"), expanded, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_MissingFile_ReportsError()
    {
        string path = Path.Combine(_fixtureDir, "missing.json");

        RuleLoadResult result = new RuleLoader().Load(path);

        Assert.Empty(result.Rules);
        RuleLoadError error = Assert.Single(result.Errors);
        Assert.Null(error.RuleId);
        Assert.Contains("not found", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_InvalidJson_ReportsError()
    {
        string path = WriteRules("{ this is not json ]");

        RuleLoadResult result = new RuleLoader().Load(path);

        Assert.Empty(result.Rules);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Load_UnknownRiskLevel_ReportsError()
    {
        string path = WriteRules(Wrap(ValidRule.Replace("\"riskLevel\": \"Safe\"", "\"riskLevel\": \"Extreme\"")));

        RuleLoadResult result = new RuleLoader().Load(path);

        Assert.Empty(result.Rules);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Load_MissingRequiredField_ReportsError()
    {
        string path = WriteRules(Wrap(ValidRule.Replace("\"description\": \"A test rule.\",", "")));

        RuleLoadResult result = new RuleLoader().Load(path);

        Assert.Empty(result.Rules);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Load_DuplicateRuleIds_ReportsErrorAndKeepsFirstRule()
    {
        string path = WriteRules(Wrap(ValidRule, ValidRule));

        RuleLoadResult result = new RuleLoader().Load(path);

        Rule rule = Assert.Single(result.Rules);
        RuleLoadError error = Assert.Single(result.Errors);
        Assert.Equal("test-rule", rule.Id);
        Assert.Equal("test-rule", error.RuleId);
        Assert.Contains("Duplicate", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RuleWithoutPaths_ReportsError()
    {
        string body =
            """
            {
              "id": "no-paths",
              "name": "No paths",
              "category": "Test",
              "description": "A rule without paths.",
              "riskLevel": "Safe",
              "requiresAdmin": false,
              "paths": [],
              "recursive": false
            }
            """;

        RuleLoadResult result = new RuleLoader().Load(WriteRules(Wrap(body)));

        Assert.Empty(result.Rules);
        RuleLoadError error = Assert.Single(result.Errors);
        Assert.Equal("no-paths", error.RuleId);
        Assert.Contains("at least one path", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RelativePath_ReportsError()
    {
        string body =
            """
            {
              "id": "relative-path",
              "name": "Relative path",
              "category": "Test",
              "description": "A rule with a relative path.",
              "riskLevel": "Safe",
              "requiresAdmin": false,
              "paths": [ "AppData\\Local\\Whatever" ],
              "recursive": false
            }
            """;

        RuleLoadResult result = new RuleLoader().Load(WriteRules(Wrap(body)));

        Assert.Empty(result.Rules);
        RuleLoadError error = Assert.Single(result.Errors);
        Assert.Contains("absolute", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_UndefinedEnvironmentVariable_ReportsError()
    {
        string body =
            """
            {
              "id": "undefined-var",
              "name": "Undefined variable",
              "category": "Test",
              "description": "A rule with an undefined environment variable.",
              "riskLevel": "Safe",
              "requiresAdmin": false,
              "paths": [ "%WINCLEAN_TESTS_UNDEFINED_7A3C%\\cache" ],
              "recursive": false
            }
            """;

        RuleLoadResult result = new RuleLoader().Load(WriteRules(Wrap(body)));

        Assert.Empty(result.Rules);
        RuleLoadError error = Assert.Single(result.Errors);
        Assert.Contains("environment variable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_FilePatternWithPathSeparator_ReportsError()
    {
        string body =
            """
            {
              "id": "bad-pattern",
              "name": "Bad pattern",
              "category": "Test",
              "description": "A pattern with a path separator.",
              "riskLevel": "Safe",
              "requiresAdmin": false,
              "paths": [ "%TEMP%" ],
              "filePattern": "sub\\*.tmp",
              "recursive": false
            }
            """;

        RuleLoadResult result = new RuleLoader().Load(WriteRules(Wrap(body)));

        Assert.Empty(result.Rules);
        RuleLoadError error = Assert.Single(result.Errors);
        Assert.Contains("path separators", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_FilePatternWithColon_ReportsError()
    {
        string body =
            """
            {
              "id": "colon-pattern",
              "name": "Colon pattern",
              "category": "Test",
              "description": "A pattern with a drive or stream specification.",
              "riskLevel": "Safe",
              "requiresAdmin": false,
              "paths": [ "%TEMP%" ],
              "filePattern": "file:name.tmp",
              "recursive": false
            }
            """;

        RuleLoadResult result = new RuleLoader().Load(WriteRules(Wrap(body)));

        Assert.Empty(result.Rules);
        RuleLoadError error = Assert.Single(result.Errors);
        Assert.Contains("plain file name pattern", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_UnknownSpecialTarget_ReportsError()
    {
        string body =
            """
            {
              "id": "weird-special",
              "name": "Weird special target",
              "category": "Test",
              "description": "A rule with an unknown specialTarget.",
              "riskLevel": "Safe",
              "requiresAdmin": false,
              "paths": [],
              "recursive": false,
              "specialTarget": "SomeOtherThing"
            }
            """;

        RuleLoadResult result = new RuleLoader().Load(WriteRules(Wrap(body)));

        Assert.Empty(result.Rules);
        RuleLoadError error = Assert.Single(result.Errors);
        Assert.Contains("specialTarget", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RecycleBinSpecialTarget_IsAcceptedWithoutPaths()
    {
        string body =
            """
            {
              "id": "bin",
              "name": "Recycle Bin",
              "category": "Test",
              "description": "Shell API handled target.",
              "riskLevel": "Moderate",
              "requiresAdmin": false,
              "paths": [],
              "recursive": false,
              "specialTarget": "RecycleBin"
            }
            """;

        RuleLoadResult result = new RuleLoader().Load(WriteRules(Wrap(body)));

        Assert.False(result.HasErrors);
        Rule rule = Assert.Single(result.Rules);
        Assert.True(rule.IsSpecial);
        Assert.Equal(SpecialTargets.RecycleBin, rule.SpecialTarget);
        Assert.Empty(rule.Paths);
        Assert.Equal("*", rule.FilePattern);
    }

    [Fact]
    public void Load_MissingFilePattern_DefaultsToStar()
    {
        string body =
            """
            {
              "id": "no-pattern",
              "name": "No pattern",
              "category": "Test",
              "description": "A rule without a filePattern.",
              "riskLevel": "Safe",
              "requiresAdmin": false,
              "paths": [ "%TEMP%" ],
              "recursive": false
            }
            """;

        RuleLoadResult result = new RuleLoader().Load(WriteRules(Wrap(body)));

        Assert.False(result.HasErrors);
        Rule rule = Assert.Single(result.Rules);
        Assert.Equal("*", rule.FilePattern);
    }

    [Fact]
    public void Load_MissingDefaultChecked_DerivesFromRiskLevel()
    {
        string moderate = ValidRule
            .Replace("\"id\": \"test-rule\"", "\"id\": \"moderate-rule\"")
            .Replace("\"riskLevel\": \"Safe\"", "\"riskLevel\": \"Moderate\"");

        RuleLoadResult result = new RuleLoader().Load(WriteRules(Wrap(ValidRule, moderate)));

        Assert.False(result.HasErrors);
        Assert.Equal(2, result.Rules.Count);
        Assert.True(result.Rules[0].CheckedByDefault);   // Safe -> preselected
        Assert.False(result.Rules[1].CheckedByDefault); // Moderate -> not preselected
    }

    [Fact]
    public void Load_ExplicitDefaultChecked_OverridesRiskLevel()
    {
        string body = ValidRule
            .Replace("\"riskLevel\": \"Safe\"", "\"riskLevel\": \"Moderate\"")
            .Replace("\"requiresAdmin\": false,", "\"requiresAdmin\": false,\n  \"defaultChecked\": true,");

        RuleLoadResult result = new RuleLoader().Load(WriteRules(Wrap(body)));

        Assert.False(result.HasErrors);
        Rule rule = Assert.Single(result.Rules);
        Assert.Equal(RiskLevel.Moderate, rule.RiskLevel);
        Assert.True(rule.CheckedByDefault);
    }

    [Fact]
    public void Load_BundledRules_DefaultSelectionFavorsSafeTargets()
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, "rules.json");

        RuleLoadResult result = new RuleLoader().Load(bundled);

        Assert.False(result.HasErrors);

        // Preselected: user temp, Windows temp, thumbnails, error reports, browser caches.
        string[] checkedByDefault = ["user-temp", "windows-temp", "thumbnail-cache", "windows-error-reports", "chrome-cache", "edge-cache", "firefox-cache"];
        Assert.All(checkedByDefault, id => Assert.True(
            result.Rules.Single(rule => rule.Id == id).CheckedByDefault,
            $"{id} should be checked by default"));

        // Not preselected: update cache, Prefetch, crash dumps, Recycle Bin.
        string[] uncheckedByDefault = ["windows-update-cache", "prefetch", "crash-dumps", "recycle-bin"];
        Assert.All(uncheckedByDefault, id => Assert.False(
            result.Rules.Single(rule => rule.Id == id).CheckedByDefault,
            $"{id} must not be checked by default"));
    }

    [Fact]
    public void UserRulesPath_LivesInLocalAppDataWinCleanFolder()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinClean",
            "rules.json");

        Assert.Equal(expected, RuleLoader.UserRulesPath, StringComparer.OrdinalIgnoreCase);
    }
}
