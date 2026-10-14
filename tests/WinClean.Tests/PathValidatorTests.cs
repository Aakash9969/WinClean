using WinClean.Core.Paths;

namespace WinClean.Tests;

/// <summary>
/// Safety tests for <see cref="PathValidator"/>: path traversal, symlink/junction escape,
/// protected roots, and paths outside rule roots. Fixtures live in a temp folder; the default
/// blocklist is exercised with pure path strings, so no real system folder is ever touched.
/// </summary>
public sealed class PathValidatorTests : IDisposable
{
    private readonly TempFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static PathValidator MakeValidator(params (string Path, BlockKind Kind)[] blocked) =>
        new(blocked.Select(root => new BlockedRoot(root.Path, root.Kind)));

    [Fact]
    public void ValidateFile_InsideRuleRoot_IsAllowed()
    {
        string root = _fixture.CreateDirectory("root");
        string file = _fixture.CreateFile(@"root\a.tmp", 10);

        PathValidationResult result = MakeValidator().ValidateFile(file, root);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ValidateFile_WithDotDotTraversal_IsDenied()
    {
        string root = _fixture.CreateDirectory("root");

        // Lexically escapes the rule root; canonicalization lands outside it.
        string path = Path.Combine(root, "sub", "..", "..", "..", "Windows", "notepad.exe");

        PathValidationResult result = MakeValidator().ValidateFile(path, root);

        Assert.False(result.IsAllowed);
        Assert.Contains("outside", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateFile_OutsideRuleRoot_IsDenied()
    {
        string root = _fixture.CreateDirectory("root");
        string elsewhere = _fixture.CreateDirectory("elsewhere");
        string file = Path.Combine(elsewhere, "x.tmp");

        PathValidationResult result = MakeValidator().ValidateFile(file, root);

        Assert.False(result.IsAllowed);
        Assert.Contains("outside", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateFile_SiblingWithSamePrefix_IsDenied()
    {
        // "C:\...\RuleEvil\x.tmp" must not pass a naive prefix check against root "C:\...\Rule".
        string root = _fixture.CreateDirectory("Rule");
        string sibling = Path.Combine(Path.GetDirectoryName(root)!, "RuleEvil", "x.tmp");

        PathValidationResult result = MakeValidator().ValidateFile(sibling, root);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void ValidateFile_EqualToRuleRoot_IsDenied()
    {
        string root = _fixture.CreateDirectory("root");

        PathValidationResult result = MakeValidator().ValidateFile(root, root);

        Assert.False(result.IsAllowed);
        Assert.Contains("rule root itself", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateFile_InsideTreeBlockedRoot_IsDenied()
    {
        string protectedRoot = _fixture.CreateDirectory("Protected");
        string ruleRoot = Path.Combine(protectedRoot, "Sub");
        string file = Path.Combine(ruleRoot, "x.txt");

        PathValidationResult result = MakeValidator((protectedRoot, BlockKind.Tree))
            .ValidateFile(file, ruleRoot);

        Assert.False(result.IsAllowed);
        Assert.Contains("protected location", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateFile_BlockedRootItself_IsDenied()
    {
        string protectedRoot = _fixture.CreateDirectory("Protected");
        string parent = Path.GetDirectoryName(protectedRoot)!;

        PathValidationResult result = MakeValidator((protectedRoot, BlockKind.Tree))
            .ValidateFile(protectedRoot, parent);

        Assert.False(result.IsAllowed);
        Assert.Contains("protected location", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateFile_DirectChildOfRootOnlyBlock_IsDenied()
    {
        // The "user profile root" case: a rule pointing at %USERPROFILE% may never touch
        // files directly inside it (NTUSER.DAT and friends).
        string profile = _fixture.CreateDirectory("ProfileHome");
        string file = Path.Combine(profile, "NTUSER.DAT");

        PathValidationResult result = MakeValidator((profile, BlockKind.RootOnly))
            .ValidateFile(file, profile);

        Assert.False(result.IsAllowed);
        Assert.Contains("protected location", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateFile_DeepInsideRootOnlySubdirectory_IsAllowed()
    {
        // The "Prefetch" case: rule-declared folders deeper under a RootOnly-protected root
        // stay cleanable — only the protected root's direct children are off-limits.
        string profile = _fixture.CreateDirectory("ProfileHome");
        string ruleRoot = _fixture.CreateDirectory(@"ProfileHome\AppData\Local\TempLike");
        string file = _fixture.CreateFile(@"ProfileHome\AppData\Local\TempLike\x.tmp", 10);

        PathValidationResult result = MakeValidator((profile, BlockKind.RootOnly))
            .ValidateFile(file, ruleRoot);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ValidateFile_ThroughJunction_IsDenied()
    {
        string root = _fixture.CreateDirectory("root");
        string outside = _fixture.CreateDirectory("outside");
        _fixture.CreateFile(@"outside\e.tmp", 10);
        _fixture.CreateJunction(@"root\junction", outside);

        PathValidationResult result = MakeValidator()
            .ValidateFile(Path.Combine(root, "junction", "e.tmp"), root);

        Assert.False(result.IsAllowed);
        Assert.Contains("symlink or junction", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateFile_JunctionItself_IsDenied()
    {
        string root = _fixture.CreateDirectory("root");
        string outside = _fixture.CreateDirectory("outside");
        _fixture.CreateJunction(@"root\junction", outside);

        PathValidationResult result = MakeValidator()
            .ValidateFile(Path.Combine(root, "junction"), root);

        Assert.False(result.IsAllowed);
        Assert.Contains("symlink or junction", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateFile_MissingFile_CouldNotBeVerified_IsDenied()
    {
        string root = _fixture.CreateDirectory("root");

        PathValidationResult result = MakeValidator()
            .ValidateFile(Path.Combine(root, "ghost.tmp"), root);

        Assert.False(result.IsAllowed);
        Assert.Contains("could not be verified", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRoot_JunctionRoot_IsDenied()
    {
        string root = _fixture.CreateDirectory("root");
        string outside = _fixture.CreateDirectory("outside");
        _fixture.CreateJunction(@"root\junction", outside);

        PathValidationResult result = MakeValidator()
            .ValidateRoot(Path.Combine(root, "junction"));

        Assert.False(result.IsAllowed);
        Assert.Contains("symlink or junction", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRoot_InsideTreeBlockedRoot_IsDenied()
    {
        string protectedRoot = _fixture.CreateDirectory("Protected");

        PathValidationResult result = MakeValidator((protectedRoot, BlockKind.Tree))
            .ValidateRoot(Path.Combine(protectedRoot, "Sub"));

        Assert.False(result.IsAllowed);
        Assert.Contains("protected location", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRoot_BlockedRootItself_IsDenied()
    {
        string profile = _fixture.CreateDirectory("ProfileHome");

        PathValidationResult result = MakeValidator((profile, BlockKind.RootOnly))
            .ValidateRoot(profile);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void ValidateRoot_DirectChildOfRootOnlyBlock_IsAllowed()
    {
        // The "C:\Windows\Prefetch" case: a rule root that is a direct child of a
        // RootOnly-protected root may be scanned; only the protected level itself is off-limits.
        string profile = _fixture.CreateDirectory("ProfileHome");
        string ruleRoot = _fixture.CreateDirectory(@"ProfileHome\Prefetch");

        PathValidationResult result = MakeValidator((profile, BlockKind.RootOnly))
            .ValidateRoot(ruleRoot);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ValidateRoot_MissingRoot_CouldNotBeVerified_IsDenied()
    {
        string root = _fixture.CreateDirectory("root");

        PathValidationResult result = MakeValidator()
            .ValidateRoot(Path.Combine(root, "does-not-exist"));

        Assert.False(result.IsAllowed);
        Assert.Contains("could not be verified", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateFile_ShortNameAliasOfBlockedRoot_IsDenied()
    {
        // 8.3 aliases (C:\PROGRA~1-style) must not bypass the blocklist.
        string root = _fixture.CreateDirectory("AVeryLongFolderNameRoot");
        _fixture.CreateFile(@"AVeryLongFolderNameRoot\file.txt", 10);

        string? shortRoot = NativeMethods.TryGetShortPath(root);
        if (shortRoot is null)
        {
            return; // 8.3 aliases are disabled on this volume; nothing to verify.
        }

        PathValidator validator = MakeValidator((root, BlockKind.Tree));
        string shortFile = Path.Combine(shortRoot, "file.txt");

        PathValidationResult result = validator.ValidateFile(shortFile, shortRoot);

        Assert.False(result.IsAllowed);
        Assert.Contains("protected location", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultBlocklist_Documents_DeniedEvenWhenRulePointsThere()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string file = Path.Combine(documents, "important.docx");

        PathValidationResult result = PathValidator.CreateDefault()
            .ValidateFile(file, documents);

        Assert.False(result.IsAllowed);
        Assert.Contains("protected location", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultBlocklist_ProfileDirectChild_IsDenied()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string file = Path.Combine(profile, "NTUSER.DAT");

        PathValidationResult result = PathValidator.CreateDefault()
            .ValidateFile(file, profile);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void DefaultBlocklist_WindowsDirectChild_IsDenied()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string file = Path.Combine(windows, "MEMORY.DMP");

        PathValidationResult result = PathValidator.CreateDefault()
            .ValidateFile(file, windows);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void DefaultBlocklist_System32Tree_IsDenied()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string system32 = Path.Combine(windows, "System32");
        string file = Path.Combine(system32, "cmd.exe");

        PathValidationResult result = PathValidator.CreateDefault()
            .ValidateFile(file, system32);

        Assert.False(result.IsAllowed);
    }

    [Theory]
    [InlineData("ProgramFiles")]
    [InlineData("ProgramFiles(x86)")]
    public void DefaultBlocklist_ProgramFilesTrees_AreDenied(string environmentVariable)
    {
        string? programFiles = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            return; // Not present on this system (e.g. 32-bit OS).
        }

        string file = Path.Combine(programFiles, "SomeApp", "app.dll");

        PathValidationResult result = PathValidator.CreateDefault()
            .ValidateFile(file, programFiles);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void DefaultBlocklist_DeepInsideProfile_IsStillAllowed()
    {
        // The fixture lives under %TEMP% deep inside the profile: the profile-root block
        // must not over-block rule targets deeper in the tree.
        string file = _fixture.CreateFile("allowed.tmp", 1);

        PathValidationResult result = PathValidator.CreateDefault()
            .ValidateFile(file, _fixture.Root);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void DefaultBlocklist_RealTempRoot_IsAllowedForCleaning()
    {
        PathValidationResult result = PathValidator.CreateDefault()
            .ValidateRoot(Path.GetTempPath());

        Assert.True(result.IsAllowed);
    }
}
