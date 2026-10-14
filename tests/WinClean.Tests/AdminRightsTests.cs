using WinClean.Core.Security;

namespace WinClean.Tests;

/// <summary>Elevation checks must never throw, whatever the test host's elevation level.</summary>
public sealed class AdminRightsTests
{
    [Fact]
    public void IsCurrentProcessElevated_ReturnsAValueWithoutThrowing()
    {
        // Both outcomes are valid depending on how the test host runs; the point is that the
        // Windows identity/principal checks complete cleanly either way.
        bool isElevated = AdminRights.IsCurrentProcessElevated();

        Assert.True(isElevated || !isElevated);
    }
}
