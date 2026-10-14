namespace WinClean.Core.Paths;

/// <summary>
/// The safety gate. Nothing may be cleaned unless it passes this validator:
/// every path must canonically lie strictly inside a root declared by an enabled rule,
/// must not fall inside the hard blocklist (enforced even when a rule points there),
/// and must never involve a symlink or junction (reparse point) — neither as the file
/// itself nor anywhere between the rule root and the file.
/// </summary>
public sealed class PathValidator
{
    private readonly List<BlockedRoot> _blockedRoots = [];

    public PathValidator(IEnumerable<BlockedRoot> blockedRoots)
    {
        ArgumentNullException.ThrowIfNull(blockedRoots);

        foreach (BlockedRoot blocked in blockedRoots)
        {
            if (string.IsNullOrWhiteSpace(blocked.Path))
            {
                continue;
            }

            AddBlockedRoot(Canonicalize(blocked.Path), blocked.Kind);
        }
    }

    /// <summary>
    /// The hard blocklist, enforced even when a rule explicitly points at these locations:
    /// user profile root, Documents, Desktop, Pictures, Videos, Music, Downloads,
    /// C:\Windows root, System32, Program Files, Program Files (x86).
    /// </summary>
    public static IReadOnlyList<BlockedRoot> GetDefaultBlockedRoots()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        List<BlockedRoot> roots =
        [
            // The profile root itself: direct children (NTUSER.DAT and friends) are protected,
            // but rule-declared folders deeper inside the profile stay cleanable.
            new BlockedRoot(profile, BlockKind.RootOnly),
            new BlockedRoot(KnownFolder(Environment.SpecialFolder.MyDocuments, profile, "Documents"), BlockKind.Tree),
            new BlockedRoot(KnownFolder(Environment.SpecialFolder.Desktop, profile, "Desktop"), BlockKind.Tree),
            new BlockedRoot(KnownFolder(Environment.SpecialFolder.MyPictures, profile, "Pictures"), BlockKind.Tree),
            new BlockedRoot(KnownFolder(Environment.SpecialFolder.MyVideos, profile, "Videos"), BlockKind.Tree),
            new BlockedRoot(KnownFolder(Environment.SpecialFolder.MyMusic, profile, "Music"), BlockKind.Tree),
            new BlockedRoot(DownloadsFolder(profile), BlockKind.Tree),

            // The Windows root: files directly inside C:\Windows are protected, but rule-declared
            // subdirectories such as Prefetch, Temp or SoftwareDistribution\Download stay cleanable.
            new BlockedRoot(windows, BlockKind.RootOnly),
            new BlockedRoot(Path.Combine(windows, "System32"), BlockKind.Tree),
        ];

        string programFiles = Environment.GetEnvironmentVariable("ProgramFiles") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            roots.Add(new BlockedRoot(programFiles, BlockKind.Tree));
        }

        string programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            roots.Add(new BlockedRoot(programFilesX86, BlockKind.Tree));
        }

        return roots;
    }

    /// <summary>A validator with the default hard blocklist.</summary>
    public static PathValidator CreateDefault() => new(GetDefaultBlockedRoots());

    /// <summary>
    /// May WinClean clean inside this rule root? The root itself is never deleted — only its contents.
    /// </summary>
    public PathValidationResult ValidateRoot(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        string canonical = Canonicalize(root);

        PathValidationResult blocked = CheckBlocked(canonical, isFile: false);
        if (!blocked.IsAllowed)
        {
            return blocked;
        }

        return CheckReparsePoint(canonical);
    }

    /// <summary>
    /// May this file be cleaned, given the rule root it was found under? Refuses anything outside
    /// the rule root, anything in the blocklist, and anything involving a symlink or junction.
    /// </summary>
    public PathValidationResult ValidateFile(string path, string ruleRoot)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(ruleRoot);

        string canonicalRoot = Canonicalize(ruleRoot);
        string canonical = Canonicalize(path);

        if (string.Equals(canonical, canonicalRoot, StringComparison.OrdinalIgnoreCase))
        {
            return PathValidationResult.Denied($"'{canonical}' is the rule root itself; only its contents may be cleaned.");
        }

        if (!IsInside(canonical, canonicalRoot))
        {
            return PathValidationResult.Denied($"'{canonical}' lies outside the rule root '{canonicalRoot}'.");
        }

        PathValidationResult blocked = CheckBlocked(canonical, isFile: true);
        if (!blocked.IsAllowed)
        {
            return blocked;
        }

        PathValidationResult selfReparse = CheckReparsePoint(canonical);
        if (!selfReparse.IsAllowed)
        {
            return selfReparse;
        }

        // Never traverse a symlink or junction between the rule root and the file.
        for (string? directory = Path.GetDirectoryName(canonical);
             directory is not null
             && directory.Length > canonicalRoot.Length
             && !string.Equals(directory, canonicalRoot, StringComparison.OrdinalIgnoreCase);
             directory = Path.GetDirectoryName(directory))
        {
            PathValidationResult reparse = CheckReparsePoint(directory);
            if (!reparse.IsAllowed)
            {
                return reparse;
            }
        }

        return PathValidationResult.Allowed;
    }

    private void AddBlockedRoot(string canonicalPath, BlockKind kind)
    {
        _blockedRoots.Add(new BlockedRoot(canonicalPath, kind));

        // Also block the 8.3 alias form (e.g. C:\PROGRA~1) so short-name spelling cannot bypass the blocklist.
        string? shortPath = NativeMethods.TryGetShortPath(canonicalPath);
        if (shortPath is not null)
        {
            _blockedRoots.Add(new BlockedRoot(shortPath, kind));
        }
    }

    private PathValidationResult CheckBlocked(string canonical, bool isFile)
    {
        foreach (BlockedRoot blocked in _blockedRoots)
        {
            if (string.Equals(canonical, blocked.Path, StringComparison.OrdinalIgnoreCase))
            {
                return PathValidationResult.Denied($"'{canonical}' is the protected location '{blocked.Path}' and can never be cleaned.");
            }

            if (blocked.Kind == BlockKind.Tree && IsInside(canonical, blocked.Path))
            {
                return PathValidationResult.Denied($"'{canonical}' lies inside the protected location '{blocked.Path}'.");
            }

            if (isFile && blocked.Kind == BlockKind.RootOnly && IsDirectChild(canonical, blocked.Path))
            {
                return PathValidationResult.Denied($"'{canonical}' lies directly inside the protected location '{blocked.Path}'.");
            }
        }

        return PathValidationResult.Allowed;
    }

    /// <summary>
    /// Returns denied when the path is — or cannot be verified to be — a regular file or
    /// directory: symlinks and junctions are never followed and never deleted, and a path
    /// that cannot be read is never trusted.
    /// </summary>
    private static PathValidationResult CheckReparsePoint(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PathValidationResult.Denied($"'{path}' could not be verified (not accessible): {ex.Message}");
        }

        return attributes.HasFlag(FileAttributes.ReparsePoint)
            ? PathValidationResult.Denied($"'{path}' is a symlink or junction (reparse point); WinClean never follows or deletes these.")
            : PathValidationResult.Allowed;
    }

    private static string KnownFolder(Environment.SpecialFolder folder, string profile, string fallbackName)
    {
        string path = Environment.GetFolderPath(folder);
        return string.IsNullOrWhiteSpace(path) ? Path.Combine(profile, fallbackName) : path;
    }

    /// <summary>
    /// Resolves the real Downloads folder, honoring user relocation, via the shell known-folder
    /// registry key. Falls back to the default %USERPROFILE%\Downloads.
    /// </summary>
    private static string DownloadsFolder(string profile)
    {
        const string downloadsGuid = "{374DE290-123F-4565-9164-39C4925E467B}";
        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
            if (key?.GetValue(downloadsGuid) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return Environment.ExpandEnvironmentVariables(value);
            }
        }
        catch
        {
            // Fall back to the default location below.
        }

        return Path.Combine(profile, "Downloads");
    }

    private static string Canonicalize(string path)
    {
        string full = Path.GetFullPath(path);
        return TrimTrailingSeparators(full);
    }

    private static string TrimTrailingSeparators(string path)
    {
        // Keep drive roots like "C:\" intact; everything else loses its trailing separator.
        if (path.Length <= 3)
        {
            return path;
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>True when <paramref name="path"/> lies strictly beneath <paramref name="root"/>, comparing segments (not just prefixes) case-insensitively.</summary>
    private static bool IsInside(string path, string root)
    {
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool rootEndsWithSeparator =
            root.EndsWith(Path.DirectorySeparatorChar) ||
            root.EndsWith(Path.AltDirectorySeparatorChar);

        return rootEndsWithSeparator
            ? path.Length > root.Length
            : path.Length > root.Length + 1 &&
              (path[root.Length] == Path.DirectorySeparatorChar ||
               path[root.Length] == Path.AltDirectorySeparatorChar);
    }

    private static bool IsDirectChild(string path, string parent) =>
        Path.GetDirectoryName(path) is string directory &&
        string.Equals(directory, parent, StringComparison.OrdinalIgnoreCase);
}
