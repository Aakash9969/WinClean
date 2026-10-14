namespace WinClean.Core.Special;

/// <summary>Total size and item count of everything currently in the Recycle Bin.</summary>
public sealed record RecycleBinEstimate(long TotalBytes, int ItemCount);

/// <summary>
/// Sizes the Recycle Bin without ever deleting from it. Production uses the Windows Shell API;
/// tests use a fake so no real system location is touched.
/// </summary>
public interface IRecycleBinEstimator
{
    /// <summary>Current estimate, or null when the Recycle Bin could not be read.</summary>
    RecycleBinEstimate? GetEstimate();
}
