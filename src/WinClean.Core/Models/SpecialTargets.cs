namespace WinClean.Core.Models;

/// <summary>Well-known values for <see cref="Rule.SpecialTarget"/>.</summary>
public static class SpecialTargets
{
    /// <summary>
    /// Empty the Recycle Bin through the Windows Shell API. This is the only supported way to clean
    /// the Recycle Bin: WinClean never deletes the bin's on-disk folders manually.
    /// </summary>
    public const string RecycleBin = "RecycleBin";

    /// <summary>All known special target values, used in validation error messages.</summary>
    public static readonly IReadOnlyList<string> All = [RecycleBin];
}
