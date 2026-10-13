namespace WinClean.Core.Models;

/// <summary>
/// A single cleanable target, always loaded from rules.json and never hard-coded in code.
/// </summary>
public sealed record Rule
{
    /// <summary>Unique, stable identifier (used by settings and history).</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name shown in the review screen.</summary>
    public required string Name { get; init; }

    /// <summary>Grouping for the category tree (e.g. "System", "Browsers").</summary>
    public required string Category { get; init; }

    /// <summary>Plain-language explanation of what would be deleted.</summary>
    public required string Description { get; init; }

    /// <summary>Risk badge shown to the user before anything is deleted.</summary>
    public required RiskLevel RiskLevel { get; init; }

    /// <summary>True when the rule's roots typically require elevation (e.g. under %WINDIR%).</summary>
    public required bool RequiresAdmin { get; init; }

    /// <summary>
    /// Absolute root folders to scan. Environment variables are expanded by the loader.
    /// A '*' segment matches any single directory name and is expanded at scan time.
    /// </summary>
    public required IReadOnlyList<string> Paths { get; init; }

    /// <summary>File name pattern, e.g. "*.tmp". Never contains path separators.</summary>
    public string FilePattern { get; init; } = "*";

    /// <summary>Also match files in subfolders of each root.</summary>
    public required bool Recursive { get; init; }

    /// <summary>When set, the rule is handled by a built-in special handler instead of path scanning (e.g. "RecycleBin").</summary>
    public string? SpecialTarget { get; init; }

    /// <summary>True when the rule is handled by a special handler rather than by its paths.</summary>
    public bool IsSpecial => !string.IsNullOrEmpty(SpecialTarget);
}
