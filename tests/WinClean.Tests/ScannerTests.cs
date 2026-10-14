using WinClean.Core.Models;
using WinClean.Core.Paths;
using WinClean.Core.Scanning;
using WinClean.Core.Special;

namespace WinClean.Tests;

/// <summary>
/// Tests for <see cref="Scanner"/>. All fixtures live in a temp folder; the Recycle Bin is
/// represented by a fake estimator so no real system location is ever touched.
/// </summary>
public sealed class ScannerTests : IDisposable
{
    private readonly TempFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static Scanner MakeScanner(PathValidator? validator = null) =>
        new(validator ?? new PathValidator([]), new FakeRecycleBinEstimator());

    [Fact]
    public async Task Scan_RecursiveRule_SumsMatchingFileSizesOnly()
    {
        string root = _fixture.CreateDirectory("root");
        string fileA = _fixture.CreateFile(@"root\a.tmp", 100);
        string fileB = _fixture.CreateFile(@"root\b.tmp", 250);
        string fileC = _fixture.CreateFile(@"root\sub\c.tmp", 50);
        _fixture.CreateFile(@"root\sub\d.log", 999); // wrong extension: must be ignored

        string outside = _fixture.CreateDirectory("outside");
        string escapedFile = _fixture.CreateFile(@"outside\e.tmp", 4096);
        _fixture.CreateJunction(@"root\sub\junction", outside); // junction escape attempt

        ScanResult result = await MakeScanner().ScanAsync(
            [TestRules.Make("r", [root], "*.tmp", recursive: true)]);

        RuleScanResult ruleResult = Assert.Single(result.Rules);
        Assert.Empty(ruleResult.Notes);
        Assert.Equal(3, ruleResult.FileCount);
        Assert.Equal(400, ruleResult.TotalBytes);
        Assert.Equal([root], ruleResult.Roots);

        Assert.Equal(
            new[] { fileA, fileB, fileC }.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
            ruleResult.Findings.Select(finding => finding.Path).OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

        // The junction target must never be reachable.
        Assert.DoesNotContain(escapedFile, ruleResult.Findings.Select(finding => finding.Path));
        Assert.Equal(400, result.TotalBytes);
        Assert.Equal(3, result.TotalFiles);
    }

    [Fact]
    public async Task Scan_NonRecursiveRule_OnlyTopLevelFiles()
    {
        string root = _fixture.CreateDirectory("root");
        _fixture.CreateFile(@"root\a.tmp", 100);
        _fixture.CreateFile(@"root\b.tmp", 250);
        _fixture.CreateFile(@"root\sub\c.tmp", 50);

        ScanResult result = await MakeScanner().ScanAsync(
            [TestRules.Make("r", [root], "*.tmp", recursive: false)]);

        RuleScanResult ruleResult = Assert.Single(result.Rules);
        Assert.Equal(2, ruleResult.FileCount);
        Assert.Equal(350, ruleResult.TotalBytes);
    }

    [Fact]
    public async Task Scan_MissingRoot_AddsNoteInsteadOfFailing()
    {
        string missing = Path.Combine(_fixture.Root, "does-not-exist");

        ScanResult result = await MakeScanner().ScanAsync([TestRules.Make("r", [missing])]);

        RuleScanResult ruleResult = Assert.Single(result.Rules);
        Assert.Empty(ruleResult.Findings);
        string note = Assert.Single(ruleResult.Notes);
        Assert.Contains("not found", note, StringComparison.Ordinal);
        Assert.Empty(ruleResult.Roots);
    }

    [Fact]
    public async Task Scan_RootInsideBlocklistedTree_SkipsEverythingWithNote()
    {
        string protectedRoot = _fixture.CreateDirectory("protected");
        _fixture.CreateFile(@"protected\x.tmp", 10);

        PathValidator validator = new([new BlockedRoot(protectedRoot, BlockKind.Tree)]);

        ScanResult result = await new Scanner(validator, new FakeRecycleBinEstimator()).ScanAsync(
            [TestRules.Make("r", [protectedRoot], recursive: true)]);

        RuleScanResult ruleResult = Assert.Single(result.Rules);
        Assert.Equal(0, ruleResult.FileCount);
        Assert.Equal(0, ruleResult.TotalBytes);
        string note = Assert.Single(ruleResult.Notes);
        Assert.Contains("protected location", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_WildcardRoot_ExpandsAcrossProfiles()
    {
        _fixture.CreateFile(@"profiles\P1\cache\x.tmp", 100);
        _fixture.CreateFile(@"profiles\P2\cache\y.tmp", 200);
        _fixture.CreateFile(@"profiles\P3\other\z.tmp", 300); // no 'cache' folder: must be ignored

        ScanResult result = await MakeScanner().ScanAsync(
            [TestRules.Make("r", [Path.Combine(_fixture.Root, "profiles", "*", "cache")], "*.tmp")]);

        RuleScanResult ruleResult = Assert.Single(result.Rules);
        Assert.Equal(2, ruleResult.FileCount);
        Assert.Equal(300, ruleResult.TotalBytes);
        Assert.All(ruleResult.Findings, finding =>
            Assert.EndsWith("cache", Path.GetDirectoryName(finding.Path), StringComparison.OrdinalIgnoreCase));

        // The concrete expanded roots must be recorded for delete-time re-validation.
        Assert.Equal(2, ruleResult.Roots.Count);
        Assert.All(ruleResult.Roots, root =>
            Assert.EndsWith("cache", root, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Scan_WildcardRootWithoutMatches_AddsNote()
    {
        string pattern = Path.Combine(_fixture.Root, "profiles", "*", "cache");

        ScanResult result = await MakeScanner().ScanAsync([TestRules.Make("r", [pattern])]);

        RuleScanResult ruleResult = Assert.Single(result.Rules);
        Assert.Empty(ruleResult.Findings);
        string note = Assert.Single(ruleResult.Notes);
        Assert.Contains("No folders matched", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_CancellationTokenAlreadyCancelled_ThrowsOperationCanceled()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        string root = _fixture.CreateDirectory("root");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => MakeScanner().ScanAsync([TestRules.Make("r", [root])], progress: null, cts.Token));
    }

    [Fact]
    public async Task Scan_ReportsProgressPerRule()
    {
        string root1 = _fixture.CreateDirectory("root1");
        _fixture.CreateFile(@"root1\a.tmp", 100);
        string root2 = _fixture.CreateDirectory("root2");
        _fixture.CreateFile(@"root2\b.tmp", 50);

        ProgressRecorder<ScanProgress> recorder = new();

        ScanResult result = await MakeScanner().ScanAsync(
            [TestRules.Make("r1", [root1]), TestRules.Make("r2", [root2])],
            recorder);

        Assert.Equal(2, result.TotalFiles);
        Assert.Equal(4, recorder.Reports.Count);

        Assert.Equal(0, recorder.Reports[0].CompletedRules);
        Assert.Equal("r1", recorder.Reports[0].CurrentRuleId);
        Assert.Equal(2, recorder.Reports[0].TotalRules);

        Assert.Equal(1, recorder.Reports[1].CompletedRules);
        Assert.Equal(100, recorder.Reports[1].BytesFoundSoFar);

        Assert.Equal(1, recorder.Reports[2].CompletedRules);
        Assert.Equal("r2", recorder.Reports[2].CurrentRuleId);

        Assert.Equal(2, recorder.Reports[3].CompletedRules);
        Assert.Equal(150, recorder.Reports[3].BytesFoundSoFar);
        Assert.Equal(2, recorder.Reports[3].FilesFoundSoFar);
    }

    [Fact]
    public async Task Scan_CategoryTotals_AreAggregatedAcrossRules()
    {
        string root1 = _fixture.CreateDirectory("root1");
        _fixture.CreateFile(@"root1\a.tmp", 100);
        string root2 = _fixture.CreateDirectory("root2");
        _fixture.CreateFile(@"root2\b.tmp", 50);
        string root3 = _fixture.CreateDirectory("root3");
        _fixture.CreateFile(@"root3\c.tmp", 25);

        ScanResult result = await MakeScanner().ScanAsync(
        [
            TestRules.Make("r1", [root1], category: "System"),
            TestRules.Make("r2", [root2], category: "Browsers"),
            TestRules.Make("r3", [root3], category: "System"),
        ]);

        Assert.Equal(175, result.TotalBytes);
        Assert.Equal(3, result.TotalFiles);

        Assert.Equal(2, result.Categories.Count);
        CategorySummary system = result.Categories.Single(category => category.Name == "System");
        CategorySummary browsers = result.Categories.Single(category => category.Name == "Browsers");
        Assert.Equal(125, system.TotalBytes);
        Assert.Equal(2, system.FileCount);
        Assert.Equal(50, browsers.TotalBytes);
        Assert.Equal(1, browsers.FileCount);
    }

    [Fact]
    public async Task Scan_RecycleBinRule_UsesEstimatorTotals()
    {
        Rule rule = TestRules.Make("recycle-bin", specialTarget: SpecialTargets.RecycleBin);
        FakeRecycleBinEstimator estimator = new() { Estimate = new RecycleBinEstimate(123456, 7) };
        Scanner scanner = new(PathValidator.CreateDefault(), estimator);

        ScanResult result = await scanner.ScanAsync([rule]);

        RuleScanResult ruleResult = Assert.Single(result.Rules);
        Assert.Equal(123456, ruleResult.TotalBytes);
        Assert.Equal(7, ruleResult.FileCount);
        Assert.Empty(ruleResult.Findings);
        Assert.Empty(ruleResult.Roots);
        Assert.Empty(ruleResult.Notes);
        Assert.Equal(123456, result.TotalBytes);
        Assert.Equal(7, result.TotalFiles);
    }

    [Fact]
    public async Task Scan_RecycleBinEstimatorUnavailable_AddsNote()
    {
        Rule rule = TestRules.Make("recycle-bin", specialTarget: SpecialTargets.RecycleBin);
        FakeRecycleBinEstimator estimator = new() { Estimate = null };
        Scanner scanner = new(PathValidator.CreateDefault(), estimator);

        ScanResult result = await scanner.ScanAsync([rule]);

        RuleScanResult ruleResult = Assert.Single(result.Rules);
        Assert.Equal(0, ruleResult.TotalBytes);
        Assert.Equal(0, ruleResult.FileCount);
        string note = Assert.Single(ruleResult.Notes);
        Assert.Contains("could not be determined", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_EmptyRuleList_ReturnsEmptyResult()
    {
        ScanResult result = await MakeScanner().ScanAsync([]);

        Assert.Empty(result.Rules);
        Assert.Empty(result.Categories);
        Assert.Equal(0, result.TotalBytes);
        Assert.Equal(0, result.TotalFiles);
    }
}
