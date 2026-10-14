using System.Globalization;

namespace WinClean.Core.Special;

/// <summary>
/// Estimates the Recycle Bin size through the Windows Shell API (Shell.Application, ssfBitBucket).
/// WinClean only ever reads the bin this way; emptying happens via the Shell API in the cleaner
/// engine — the bin's on-disk folders are never touched manually.
/// </summary>
public sealed class ShellRecycleBinEstimator : IRecycleBinEstimator
{
    private const int SsfBitBucket = 0x0A;

    public RecycleBinEstimate? GetEstimate()
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return null;
            }

            dynamic shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Shell.Application could not be created.");
            dynamic folder = shell.Namespace(SsfBitBucket);
            if (folder is null)
            {
                return null;
            }

            long totalBytes = 0;
            int itemCount = 0;
            foreach (dynamic item in folder.Items())
            {
                itemCount++;
                totalBytes += GetItemSize(item);
            }

            return new RecycleBinEstimate(totalBytes, itemCount);
        }
        catch
        {
            // Estimation must never break a scan; a null estimate is surfaced as a note instead.
            return null;
        }
    }

    private static long GetItemSize(dynamic item)
    {
        try
        {
            object? extended = item.ExtendedProperty("System.Size");
            if (extended is string text &&
                long.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out long parsed))
            {
                return parsed;
            }
        }
        catch
        {
            // Not every item exposes ExtendedProperty; fall through to Size.
        }

        try
        {
            return Convert.ToInt64(item.Size, CultureInfo.CurrentCulture);
        }
        catch
        {
            return 0;
        }
    }
}
