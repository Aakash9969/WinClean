namespace WinClean.Core.Models;

/// <summary>
/// How risky it is to clean a rule's targets. Drives the risk badge shown in the review screen.
/// </summary>
public enum RiskLevel
{
    /// <summary>Cache-like data that applications rebuild automatically; deleting it cannot break anything.</summary>
    Safe,

    /// <summary>Normally harmless to delete, but can briefly slow things down or discard useful diagnostics.</summary>
    Moderate,

    /// <summary>Power users only: a higher chance that software on the machine misses these files.</summary>
    Advanced,
}
