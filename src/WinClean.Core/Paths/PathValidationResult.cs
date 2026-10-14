namespace WinClean.Core.Paths;

/// <summary>The verdict of the path safety gate, with a human-readable reason whenever access is refused.</summary>
public sealed record PathValidationResult(bool IsAllowed, string? Reason)
{
    /// <summary>The path passed every safety check.</summary>
    public static PathValidationResult Allowed { get; } = new(true, null);

    /// <summary>The path was refused; the reason explains which safety rule tripped it.</summary>
    public static PathValidationResult Denied(string reason) => new(false, reason);
}
