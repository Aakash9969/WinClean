using System.Diagnostics;

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
