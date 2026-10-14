using System.Diagnostics;
using System.IO;
using System.Windows;
using WinClean.Core.Security;

namespace WinClean.App.Services;

/// <summary>
/// Reports whether WinClean runs elevated and can relaunch the app with admin rights.
/// Elevation is only ever requested when an enabled rule needs it — never at startup.
/// </summary>
public interface IElevationService
{
    /// <summary>True when the current process already runs elevated.</summary>
    bool IsElevated { get; }

    /// <summary>
    /// Relaunches WinClean elevated with the given arguments and shuts this instance down.
    /// Returns false when elevation was declined (UAC) or could not be started; the current
    /// instance then keeps running.
    /// </summary>
    bool TryRelaunchElevated(string arguments);
}

public sealed class ElevationService : IElevationService
{
    public bool IsElevated => AdminRights.IsCurrentProcessElevated();

    public bool TryRelaunchElevated(string arguments)
    {
        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            ProcessStartInfo startInfo = new(executablePath)
            {
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
            };

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            // The elevated instance is running; close this one.
            Application.Current.Shutdown();
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The user declined the UAC prompt — not an error, just "no".
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
