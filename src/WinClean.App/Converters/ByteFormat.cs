using System.Globalization;

namespace WinClean.App.Converters;

/// <summary>Shared byte formatting for bindings and status text: "0 B", "350 KB", "1.2 GB".</summary>
public static class ByteFormat
{
    public static string Bytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes.ToString("#,0", CultureInfo.InvariantCulture)} B"
            : $"{value.ToString("0.#", CultureInfo.InvariantCulture)} {units[unit]}";
    }
}
