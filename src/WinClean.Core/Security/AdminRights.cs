namespace WinClean.Core.Security;

/// <summary>Elevation checks for the current Windows process.</summary>
public static class AdminRights
{
    /// <summary>True when the current process runs with an elevated (administrator) token.</summary>
    public static bool IsCurrentProcessElevated()
    {
        using System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        System.Security.Principal.WindowsPrincipal principal = new(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
