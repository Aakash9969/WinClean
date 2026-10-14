using Serilog;
using WinClean.Core.Cleaning;
using WinClean.Core.Models;
using WinClean.Core.Paths;
using WinClean.Core.Scanning;
using WinClean.Core.Special;

namespace WinClean.Tests;

/// <summary>
/// Tests for <see cref="Cleaner"/>: dry-run safety, delete-time validation, skip handling and
/// summaries. Fixtures live in a temp folder; locked files are held open with FileShare.None
/// instead of relying on system files being in use.
/// </summary>
public sealed class CleanerTests : IDisposable
{
    /// <summary>A logger with no sinks — silently discards everything (Serilog 4 removed NullLogger).</summary>
    private static readonly ILogger SilentLogger = new LoggerConfiguration().CreateLogger();

    private readonly TempFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private Cleaner MakeCleaner(
        PathValidator? validator = null,
        FakeRecycleBinEstimator? estimator = null,
        FakeRecycleBinEmptier? emptier = null) =>
        new(
            validator ?? new PathValidator([]),
            emptier ?? new FakeRecycleBinEmptier(),
            estimator ?? new FakeRecycleBinEstimator(),
            SilentLogger);

    /// <summary>Builds a RuleScanResult manually, as if a scan had just produced it.</summary>
    private static RuleScanResult ScanOf(Rule rule, IReadOnlyList<string> roots, params (string Path, long Size)[] files) =>
        new(
            rule,
            files.Sum(file => file.Size),
            files.Length,
            files.Select(file => new ScanFinding(rule.Id, file.Path, file.Size)).ToList(),
            roots,
            []);

    [Fact]
    public async Task Clean_DryRun_DeletesNothingButEstimatesBytes()
    {
        string root = _fixture.CreateDirectory("root");
        string fileA = _fixture.CreateFile(@"root\a.tmp", 100);
        string fileB = _fixture.CreateFile(@"root\b.tmp", 50);
        Rule rule = TestRules.Make("r", [root]);
        RuleScanResult scan = ScanOf(rule, [root], (fileA, 100), (fileB, 50));

        CleanResult result = await MakeCleaner().CleanAsync([scan], dryRun: true);

        // Nothing may be deleted in a dry run.
        Assert.True(File.Exists(fileA));
        Assert.True(File.Exists(fileB));

        Assert.Equal(150, result.TotalBytesFreed);
        Assert.Equal(2, result.TotalFilesRemoved);
        Assert.Equal(0, result.TotalFilesSkipped);
        Assert.All(result.Rules.Single().Outcomes, outcome => Assert.Equal(FileOutcomeKind.WouldDelete, outcome.Kind));
    }

    [Fact]
    public async Task Clean_DeletesApprovedFiles_AndFreesSpace()
    {
        string root = _fixture.CreateDirectory("root");
        string fileA = _fixture.CreateFile(@"root\a.tmp", 100);
        string fileB = _fixture.CreateFile(@"root\b.tmp", 50);
        Rule rule = TestRules.Make("r", [root]);
        RuleScanResult scan = ScanOf(rule, [root], (fileA, 100), (fileB, 50));

        CleanResult result = await MakeCleaner().CleanAsync([scan], dryRun: false);

        Assert.False(File.Exists(fileA));
        Assert.False(File.Exists(fileB));
        Assert.Equal(150, result.TotalBytesFreed);
        Assert.Equal(2, result.TotalFilesRemoved);
        Assert.Equal(0, result.TotalFilesSkipped);
        Assert.Empty(result.SkippedOutcomes);
        Assert.All(result.Rules.Single().Outcomes, outcome => Assert.Equal(FileOutcomeKind.Deleted, outcome.Kind));
    }

    [Fact]
    public async Task Clean_LockedFile_IsSkippedWithReason_AndNeverForceDeleted()
    {
        string root = _fixture.CreateDirectory("root");
        string lockedPath = _fixture.CreateFile(@"root\locked.tmp", 100);
        string freePath = _fixture.CreateFile(@"root\free.tmp", 50);

        using (new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Rule rule = TestRules.Make("r", [root]);
            RuleScanResult scan = ScanOf(rule, [root], (lockedPath, 100), (freePath, 50));

            CleanResult result = await MakeCleaner().CleanAsync([scan], dryRun: false);

            Assert.True(File.Exists(lockedPath), "A locked file must never be force-deleted.");
            Assert.False(File.Exists(freePath));

            FileCleanOutcome skipped = Assert.Single(result.SkippedOutcomes);
            Assert.Equal(lockedPath, skipped.Path);
            Assert.NotNull(skipped.Reason);
            Assert.Contains("locked or in use", skipped.Reason, StringComparison.Ordinal);

            Assert.Equal(50, result.TotalBytesFreed);
            Assert.Equal(1, result.TotalFilesRemoved);
            Assert.Equal(1, result.TotalFilesSkipped);
        }
    }

    [Fact]
    public async Task Clean_FileOutsideDeclaredRoots_IsSkippedAtDeleteTime()
    {
        string root = _fixture.CreateDirectory("root");
        string elsewhere = _fixture.CreateDirectory("elsewhere");
        string sneaky = _fixture.CreateFile(@"elsewhere\sneaky.tmp", 100);
        Rule rule = TestRules.Make("r", [root]);

        // The finding claims a file that is not inside any root the rule declared.
        RuleScanResult scan = ScanOf(rule, [root], (sneaky, 100));

        CleanResult result = await MakeCleaner().CleanAsync([scan], dryRun: false);

        Assert.True(File.Exists(sneaky));
        FileCleanOutcome skipped = Assert.Single(result.SkippedOutcomes);
        Assert.Contains("no longer inside", skipped.Reason, StringComparison.Ordinal);
        Assert.Equal(0, result.TotalBytesFreed);
    }

    [Fact]
    public async Task Clean_FileInBlocklistedRoot_IsSkippedAtDeleteTime()
    {
        // Simulates the blocklist being tightened (or rules.json edited) between scan and clean:
        // findings that once looked fine must be refused at delete time.
        string root = _fixture.CreateDirectory("root");
        string file = _fixture.CreateFile(@"root\a.tmp", 100);
        Rule rule = TestRules.Make("r", [root]);
        RuleScanResult scan = ScanOf(rule, [root], (file, 100));

        PathValidator validator = new([new BlockedRoot(root, BlockKind.Tree)]);
        CleanResult result = await MakeCleaner(validator: validator).CleanAsync([scan], dryRun: false);

        Assert.True(File.Exists(file));
        FileCleanOutcome skipped = Assert.Single(result.SkippedOutcomes);
        Assert.Contains("protected location", skipped.Reason, StringComparison.Ordinal);
        Assert.Equal(0, result.TotalBytesFreed);
    }

    [Fact]
    public async Task Clean_FileBehindJunction_IsSkippedAndTargetUntouched()
    {
        string root = _fixture.CreateDirectory("root");
        string outside = _fixture.CreateDirectory("outside");
        string outsideFile = _fixture.CreateFile(@"outside\j.tmp", 100);
        _fixture.CreateJunction(@"root\junction", outside);

        Rule rule = TestRules.Make("r", [root]);
        string throughJunction = Path.Combine(root, "junction", "j.tmp");
        RuleScanResult scan = ScanOf(rule, [root], (throughJunction, 100));

        CleanResult result = await MakeCleaner().CleanAsync([scan], dryRun: false);

        Assert.True(File.Exists(outsideFile), "The junction target must never be deleted.");
        FileCleanOutcome skipped = Assert.Single(result.SkippedOutcomes);
        Assert.Contains("symlink or junction", skipped.Reason, StringComparison.Ordinal);
        Assert.Equal(0, result.TotalBytesFreed);
    }

    [Fact]
    public async Task Clean_MissingFile_IsSkippedWithReason()
    {
        string root = _fixture.CreateDirectory("root");
        string gone = Path.Combine(root, "gone.tmp");
        Rule rule = TestRules.Make("r", [root]);
        RuleScanResult scan = ScanOf(rule, [root], (gone, 100));

        CleanResult result = await MakeCleaner().CleanAsync([scan], dryRun: false);

        FileCleanOutcome skipped = Assert.Single(result.SkippedOutcomes);
        Assert.Contains("no longer exists", skipped.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clean_FindingFromAnotherRule_IsSkipped()
    {
        string root = _fixture.CreateDirectory("root");
        string file = _fixture.CreateFile(@"root\a.tmp", 100);
        Rule ruleA = TestRules.Make("rule-a", [root]);
        Rule ruleB = TestRules.Make("rule-b", [root]);

        // A finding stamped with rule A's id handed to rule B's scan result.
        RuleScanResult scan = new(
            ruleB, 100, 1, [new ScanFinding(ruleA.Id, file, 100)], [root], []);

        CleanResult result = await MakeCleaner().CleanAsync([scan], dryRun: false);

        Assert.True(File.Exists(file));
        FileCleanOutcome skipped = Assert.Single(result.SkippedOutcomes);
        Assert.Contains("does not belong", skipped.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clean_RecycleBinRule_CallsEmptierWithFreshEstimate()
    {
        Rule rule = TestRules.Make("recycle-bin", specialTarget: SpecialTargets.RecycleBin);
        RuleScanResult scan = new(rule, 500, 5, [], [], []);
        FakeRecycleBinEstimator estimator = new() { Estimate = new RecycleBinEstimate(400, 4) };
        FakeRecycleBinEmptier emptier = new();

        CleanResult result = await MakeCleaner(estimator: estimator, emptier: emptier).CleanAsync([scan], dryRun: false);

        Assert.True(emptier.WasCalled);
        Assert.Equal(400, result.TotalBytesFreed);
        Assert.Equal(4, result.TotalFilesRemoved);
        Assert.Empty(result.SkippedOutcomes);
    }

    [Fact]
    public async Task Clean_RecycleBinDryRun_DoesNotCallEmptier()
    {
        Rule rule = TestRules.Make("recycle-bin", specialTarget: SpecialTargets.RecycleBin);
        RuleScanResult scan = new(rule, 500, 5, [], [], []);
        FakeRecycleBinEmptier emptier = new();

        CleanResult result = await MakeCleaner(emptier: emptier).CleanAsync([scan], dryRun: true);

        Assert.False(emptier.WasCalled);
        Assert.Equal(500, result.TotalBytesFreed);
        Assert.Equal(5, result.TotalFilesRemoved);
        Assert.All(result.Rules.Single().Outcomes, outcome => Assert.Equal(FileOutcomeKind.WouldDelete, outcome.Kind));
    }

    [Fact]
    public async Task Clean_RecycleBinEmptierFails_IsReportedAsSkipped()
    {
        Rule rule = TestRules.Make("recycle-bin", specialTarget: SpecialTargets.RecycleBin);
        RuleScanResult scan = new(rule, 500, 5, [], [], []);
        FakeRecycleBinEmptier emptier = new() { ErrorResult = "the Shell API returned error 0x80070005" };

        CleanResult result = await MakeCleaner(emptier: emptier).CleanAsync([scan], dryRun: false);

        Assert.Equal(0, result.TotalBytesFreed);
        Assert.Equal(1, result.TotalFilesSkipped);
        FileCleanOutcome skipped = Assert.Single(result.SkippedOutcomes);
        Assert.Equal("the Shell API returned error 0x80070005", skipped.Reason);
    }

    [Fact]
    public async Task Clean_RecycleBinEstimatorUnavailable_FallsBackToScanTotals()
    {
        Rule rule = TestRules.Make("recycle-bin", specialTarget: SpecialTargets.RecycleBin);
        RuleScanResult scan = new(rule, 500, 5, [], [], []);
        FakeRecycleBinEstimator estimator = new() { Estimate = null };

        CleanResult result = await MakeCleaner(estimator: estimator).CleanAsync([scan], dryRun: false);

        Assert.Equal(500, result.TotalBytesFreed);
        Assert.Equal(5, result.TotalFilesRemoved);
    }

    [Fact]
    public async Task Clean_ReportsProgressPerRule()
    {
        string root1 = _fixture.CreateDirectory("root1");
        string fileA = _fixture.CreateFile(@"root1\a.tmp", 100);
        string root2 = _fixture.CreateDirectory("root2");
        string fileB = _fixture.CreateFile(@"root2\b.tmp", 50);

        ProgressRecorder<CleanProgress> recorder = new();
        RuleScanResult scan1 = ScanOf(TestRules.Make("r1", [root1]), [root1], (fileA, 100));
        RuleScanResult scan2 = ScanOf(TestRules.Make("r2", [root2]), [root2], (fileB, 50));

        CleanResult result = await MakeCleaner().CleanAsync([scan1, scan2], dryRun: false, recorder);

        Assert.Equal(150, result.TotalBytesFreed);
        Assert.Equal(4, recorder.Reports.Count);

        Assert.Equal(0, recorder.Reports[0].CompletedRules);
        Assert.Equal("r1", recorder.Reports[0].CurrentRuleId);
        Assert.Equal(2, recorder.Reports[0].TotalRules);

        Assert.Equal(1, recorder.Reports[1].CompletedRules);
        Assert.Equal(100, recorder.Reports[1].BytesFreedSoFar);
        Assert.Equal(1, recorder.Reports[1].FilesProcessed);

        Assert.Equal(2, recorder.Reports[3].CompletedRules);
        Assert.Equal(150, recorder.Reports[3].BytesFreedSoFar);
        Assert.Equal(2, recorder.Reports[3].FilesProcessed);
    }

    [Fact]
    public async Task Clean_CancellationTokenAlreadyCancelled_ThrowsOperationCanceled()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        string root = _fixture.CreateDirectory("root");
        RuleScanResult scan = ScanOf(TestRules.Make("r", [root]), [root]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => MakeCleaner().CleanAsync([scan], dryRun: false, progress: null, cts.Token));
    }

    [Fact]
    public async Task Clean_MultipleRules_SummaryAggregatesDeletedAndSkipped()
    {
        string root1 = _fixture.CreateDirectory("root1");
        string fileA = _fixture.CreateFile(@"root1\a.tmp", 100);
        string root2 = _fixture.CreateDirectory("root2");
        string fileB = _fixture.CreateFile(@"root2\b.tmp", 50);
        string root3 = _fixture.CreateDirectory("root3");
        string blocked = _fixture.CreateFile(@"root3\blocked.tmp", 25);

        PathValidator validator = new([new BlockedRoot(root3, BlockKind.Tree)]);
        RuleScanResult scan1 = ScanOf(TestRules.Make("r1", [root1]), [root1], (fileA, 100));
        RuleScanResult scan2 = ScanOf(TestRules.Make("r2", [root2]), [root2], (fileB, 50));
        RuleScanResult scan3 = ScanOf(TestRules.Make("r3", [root3]), [root3], (blocked, 25));

        CleanResult result = await MakeCleaner(validator: validator).CleanAsync([scan1, scan2, scan3], dryRun: false);

        Assert.Equal(150, result.TotalBytesFreed);
        Assert.Equal(2, result.TotalFilesRemoved);
        Assert.Equal(1, result.TotalFilesSkipped);
        Assert.True(File.Exists(blocked));
    }

    [Fact]
    public async Task Clean_EmptyApprovalList_ReturnsEmptySummary()
    {
        CleanResult result = await MakeCleaner().CleanAsync([], dryRun: false);

        Assert.Empty(result.Rules);
        Assert.Equal(0, result.TotalBytesFreed);
        Assert.Equal(0, result.TotalFilesRemoved);
        Assert.Equal(0, result.TotalFilesSkipped);
    }
}
