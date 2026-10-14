namespace WinClean.Core.Special;

/// <summary>
/// Empties the Recycle Bin. The only supported implementation uses the Windows Shell API;
/// WinClean never deletes the bin's on-disk folders manually.
/// </summary>
public interface IRecycleBinEmptier
{
    /// <summary>Empties the Recycle Bin on all drives. Returns null on success, or an error message.</summary>
    string? Empty();
}
