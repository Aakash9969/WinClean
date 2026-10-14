using WinClean.Core.Models;

namespace WinClean.Core.Cleaning;

/// <summary>
/// Clean outcome for a single rule. In dry-run mode <see cref="BytesFreed"/> and
/// <see cref="FilesRemoved"/> describe what would happen, and <see cref="Outcomes"/>
/// carry <see cref="FileOutcomeKind.WouldDelete"/> entries instead of Deleted.
/// </summary>
public sealed record RuleCleanResult(
    Rule Rule,
    long BytesFreed,
    int FilesRemoved,
    int FilesSkipped,
    IReadOnlyList<FileCleanOutcome> Outcomes,
    IReadOnlyList<string> Notes);
