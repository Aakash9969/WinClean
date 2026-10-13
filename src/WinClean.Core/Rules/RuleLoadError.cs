namespace WinClean.Core.Rules;

/// <summary>A problem found while loading rules.json. <see cref="RuleId"/> is null for file-level problems.</summary>
public sealed record RuleLoadError(string? RuleId, string Message);
