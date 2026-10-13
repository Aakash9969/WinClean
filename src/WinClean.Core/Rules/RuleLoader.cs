using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WinClean.Core.Models;

namespace WinClean.Core.Rules;

/// <summary>
/// Loads and validates rules.json and expands environment variables in rule paths.
/// Validation is defensive: rules with missing ids, duplicate ids, non-absolute paths,
/// unresolvable environment variables or path-separator file patterns are rejected with
/// a descriptive error instead of being silently loaded.
/// </summary>
public sealed partial class RuleLoader : IRuleLoader
{
    private const string DefaultFilePattern = "*";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>The rules file shipped next to the executable.</summary>
    public static string BundledRulesPath => Path.Combine(AppContext.BaseDirectory, "rules.json");

    /// <summary>
    /// Optional user override. When this file exists it fully replaces the bundled rules,
    /// so new targets can be added by editing JSON without touching the install folder.
    /// </summary>
    public static string UserRulesPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinClean",
        "rules.json");

    public RuleLoadResult Load()
    {
        return File.Exists(UserRulesPath) ? Load(UserRulesPath) : Load(BundledRulesPath);
    }

    public RuleLoadResult Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            return new([], [new RuleLoadError(null, $"Rules file not found: '{path}'.")]);
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new([], [new RuleLoadError(null, $"Could not read rules file '{path}': {ex.Message}")]);
        }

        RulesDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<RulesDocument>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            return new([], [new RuleLoadError(null, $"Invalid JSON in rules file '{path}': {ex.Message}")]);
        }

        return ValidateAndExpand(document, path);
    }

    private static RuleLoadResult ValidateAndExpand(RulesDocument? document, string sourcePath)
    {
        List<Rule> rules = [];
        List<RuleLoadError> errors = [];

        if (document?.Rules is null)
        {
            errors.Add(new RuleLoadError(null, $"Rules file '{sourcePath}' does not contain a 'rules' array."));
            return new(rules, errors);
        }

        HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (Rule rule in document.Rules)
        {
            if (TryValidateAndExpand(rule, seenIds, out Rule? expanded, out string? error))
            {
                rules.Add(expanded!);
            }
            else
            {
                errors.Add(new RuleLoadError(rule.Id, error!));
            }
        }

        return new(rules, errors);
    }

    private static bool TryValidateAndExpand(Rule rule, HashSet<string> seenIds, out Rule? expanded, out string? error)
    {
        expanded = null;
        error = null;

        if (string.IsNullOrWhiteSpace(rule.Id))
        {
            error = "Rule is missing a non-empty 'id'.";
            return false;
        }

        if (!seenIds.Add(rule.Id))
        {
            error = $"Duplicate rule id '{rule.Id}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            error = $"Rule '{rule.Id}' is missing a non-empty 'name'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(rule.Category))
        {
            error = $"Rule '{rule.Id}' is missing a non-empty 'category'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(rule.Description))
        {
            error = $"Rule '{rule.Id}' is missing a non-empty 'description'.";
            return false;
        }

        if (rule.IsSpecial)
        {
            if (!string.Equals(rule.SpecialTarget, SpecialTargets.RecycleBin, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Rule '{rule.Id}' uses unknown specialTarget '{rule.SpecialTarget}'. Known values: {string.Join(", ", SpecialTargets.All)}.";
                return false;
            }

            // Special targets are handled by their built-in handler; paths are never used for them.
            expanded = rule with { Paths = [], FilePattern = DefaultFilePattern };
            return true;
        }

        if (rule.Paths is null || rule.Paths.Count == 0)
        {
            error = $"Rule '{rule.Id}' must declare at least one path.";
            return false;
        }

        string pattern = string.IsNullOrWhiteSpace(rule.FilePattern) ? DefaultFilePattern : rule.FilePattern;
        string? patternError = ValidateFilePattern(rule.Id, pattern);
        if (patternError is not null)
        {
            error = patternError;
            return false;
        }

        List<string> expandedPaths = [];
        foreach (string rawPath in rule.Paths)
        {
            if (!TryExpandPath(rule.Id, rawPath, out string? expandedPath, out string? pathError))
            {
                error = pathError;
                return false;
            }

            expandedPaths.Add(expandedPath!);
        }

        expanded = rule with { Paths = expandedPaths, FilePattern = pattern };
        return true;
    }

    /// <summary>
    /// A file pattern must be a plain file name pattern: separators, parent-directory segments
    /// and drive specifications are rejected so a pattern can never steer traversal outside a rule root.
    /// </summary>
    private static string? ValidateFilePattern(string ruleId, string pattern)
    {
        if (pattern.Contains(Path.DirectorySeparatorChar) || pattern.Contains(Path.AltDirectorySeparatorChar))
        {
            return $"Rule '{ruleId}' filePattern '{pattern}' must not contain path separators.";
        }

        if (pattern.Contains("..") || pattern.Contains(':'))
        {
            return $"Rule '{ruleId}' filePattern '{pattern}' must be a plain file name pattern such as '*.tmp'.";
        }

        return null;
    }

    private static bool TryExpandPath(string ruleId, string rawPath, out string? expandedPath, out string? error)
    {
        expandedPath = null;
        error = null;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            error = $"Rule '{ruleId}' has an empty path.";
            return false;
        }

        string expanded = rawPath;
        if (expanded.Contains('%'))
        {
            expanded = Environment.ExpandEnvironmentVariables(expanded);
            if (UnresolvedEnvironmentVariableRegex().IsMatch(expanded))
            {
                error = $"Rule '{ruleId}' path '{rawPath}' refers to an environment variable that is not defined.";
                return false;
            }
        }

        if (!Path.IsPathFullyQualified(expanded))
        {
            error = $"Rule '{ruleId}' path '{rawPath}' is not an absolute path.";
            return false;
        }

        try
        {
            expandedPath = Path.GetFullPath(expanded);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Rule '{ruleId}' path '{rawPath}' is not a valid Windows path: {ex.Message}";
            return false;
        }

        return true;
    }

    [GeneratedRegex("%[^%\\s]+%")]
    private static partial Regex UnresolvedEnvironmentVariableRegex();
}
