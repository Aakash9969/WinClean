namespace WinClean.Core.Cleaning;

/// <summary>What happened to one file during a clean.</summary>
public enum FileOutcomeKind
{
    /// <summary>Dry-run only: the file passed every safety check and would be deleted.</summary>
    WouldDelete,

    /// <summary>The file was deleted.</summary>
    Deleted,

    /// <summary>The file was refused or could not be deleted; <see cref="FileCleanOutcome.Reason"/> explains why.</summary>
    Skipped,
}

/// <summary>Per-file clean outcome, with the skip reason whenever nothing was deleted.</summary>
public sealed record FileCleanOutcome(string Path, long SizeBytes, FileOutcomeKind Kind, string? Reason)
{
    public bool IsSkipped => Kind == FileOutcomeKind.Skipped;
}
