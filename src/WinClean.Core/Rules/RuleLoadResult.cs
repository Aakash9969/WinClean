using WinClean.Core.Models;

namespace WinClean.Core.Rules;

/// <summary>
/// The outcome of loading rules.json: the rules that are safe to use, plus any problems found.
/// Valid rules are returned even when some rules fail validation, so one bad edit never hides the rest.
/// </summary>
public sealed record RuleLoadResult(IReadOnlyList<Rule> Rules, IReadOnlyList<RuleLoadError> Errors)
{
    /// <summary>True when at least one problem was found.</summary>
    public bool HasErrors => Errors.Count > 0;
}
