using System.Runtime.InteropServices;
using System.Text;

namespace WinClean.Core.Paths;

/// <summary>Small Win32 interop helpers used by the path safety layer.</summary>
internal static class NativeMethods
{
    private const uint ShortPathBufferLength = 1024;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string longPath, StringBuilder shortPath, uint bufferLength);

    /// <summary>
    /// Returns the 8.3 short form of an existing path (e.g. C:\PROGRA~1), or null when it cannot
    /// be determined. Used so the blocklist also catches rules written with short-name aliases.
    /// </summary>
    public static string? TryGetShortPath(string longPath)
    {
        try
        {
            StringBuilder buffer = new((int)ShortPathBufferLength);
            uint length = GetShortPathNameW(longPath, buffer, ShortPathBufferLength);
            if (length == 0 || length >= ShortPathBufferLength)
            {
                return null;
            }

            string shortPath = buffer.ToString();
            return string.Equals(shortPath, longPath, StringComparison.OrdinalIgnoreCase)
                ? null
                : shortPath;
        }
        catch
        {
            return null;
        }
    }
}
