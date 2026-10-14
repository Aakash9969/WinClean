using Serilog;

namespace WinClean.Core.Logging;

/// <summary>Creates the rolling file logger used across WinClean.</summary>
public static class WinCleanLogger
{
    /// <summary>Rolling logs live in %LOCALAPPDATA%\WinClean\logs.</summary>
    public static string LogsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinClean",
        "logs");

    /// <summary>
    /// A daily rolling file logger: one file per day, at most 14 files retained, each capped
    /// at 10 MB. Every deletion and every skip is recorded, so the log doubles as an audit trail.
    /// </summary>
    public static ILogger CreateFileLogger()
    {
        Directory.CreateDirectory(LogsDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(LogsDirectory, "winclean-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
