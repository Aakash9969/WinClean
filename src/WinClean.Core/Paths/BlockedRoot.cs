namespace WinClean.Core.Paths;

/// <summary>
/// How strictly a blocked location is protected.
/// </summary>
public enum BlockKind
{
    /// <summary>
    /// The directory itself and everything beneath it is protected
    /// (Documents, Desktop, Pictures, Videos, Music, Downloads, System32, Program Files, Program Files (x86)).
    /// </summary>
    Tree,

    /// <summary>
    /// The directory itself and files directly inside it are protected, but rule-declared
    /// subdirectories remain cleanable (user profile root and C:\Windows root — this is what
    /// makes %WINDIR%\Prefetch or %WINDIR%\Temp legitimate rule targets while keeping
    /// NTUSER.DAT or C:\Windows\MEMORY.DMP untouchable).
    /// </summary>
    RootOnly,
}

/// <summary>A location WinClean refuses to touch, enforced even when a rule explicitly points at it.</summary>
public sealed record BlockedRoot(string Path, BlockKind Kind);
