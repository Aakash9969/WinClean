using System.Diagnostics;
using WinClean.Core.Models;
using WinClean.Core.Special;

namespace WinClean.Tests;

/// <summary>
/// A disposable temp-folder fixture. Everything lives under %TEMP%\WinCleanTests and is removed
/// afterwards — real system folders are never touched. Junction targets are created inside the
/// same fixture so recursive cleanup can never escape the temp area.
/// </summary>
internal sealed class TempFixture : IDisposable
{
    public TempFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "WinCleanTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string CreateDirectory(string relativePath)
    {
        string path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public string CreateFile(string relativePath, int sizeBytes)
    {
        string path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    /// <summary>Creates a directory junction (mklink /J requires no elevation).</summary>
    public void CreateJunction(string relativeLinkPath, string targetPath)
    {
        string linkPath = Path.Combine(Root, relativeLinkPath);
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);

        using Process process = Process.Start(new ProcessStartInfo(
            "cmd.exe",
            $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Failed to start cmd.exe to create a junction.");

        process.WaitForExit();

        Assert.True(
            Directory.Exists(linkPath),
            $"Could not create junction '{linkPath}' -> '{targetPath}'. mklink exit code: {process.ExitCode}");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Builds Rule records for tests without touching rules.json.</summary>
internal static class TestRules
{
    public static Rule Make(
        string id,
        IReadOnlyList<string>? paths = null,
        string filePattern = "*",
        bool recursive = false,
        string category = "Test",
        string? specialTarget = null) =>
        new()
        {
            Id = id,
            Name = id,
            Category = category,
            Description = "Test rule.",
            RiskLevel = RiskLevel.Safe,
            RequiresAdmin = false,
            Paths = paths ?? [],
            FilePattern = filePattern,
            Recursive = recursive,
            SpecialTarget = specialTarget,
        };
}

/// <summary>Records every progress report so tests can assert on the sequence.</summary>
internal sealed class ProgressRecorder<T> : IProgress<T>
{
    public List<T> Reports { get; } = [];

    public void Report(T value) => Reports.Add(value);
}

/// <summary>Fake Recycle Bin estimator — no real system location is ever touched.</summary>
internal sealed class FakeRecycleBinEstimator : IRecycleBinEstimator
{
    public RecycleBinEstimate? Estimate { get; set; }

    public RecycleBinEstimate? GetEstimate() => Estimate;
}

/// <summary>Fake Recycle Bin emptier — records calls instead of emptying anything.</summary>
internal sealed class FakeRecycleBinEmptier : IRecycleBinEmptier
{
    public bool WasCalled { get; private set; }

    public string? ErrorResult { get; set; }

    public string? Empty()
    {
        WasCalled = true;
        return ErrorResult;
    }
}
