namespace WinClean.Core.Rules;

/// <summary>Loads cleaning rules from rules.json.</summary>
public interface IRuleLoader
{
    /// <summary>
    /// Loads the user's rules file (%LOCALAPPDATA%\WinClean\rules.json) if present,
    /// otherwise the rules file bundled next to the executable.
    /// </summary>
    RuleLoadResult Load();

    /// <summary>Loads rules from a specific file.</summary>
    RuleLoadResult Load(string path);
}
