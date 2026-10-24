using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class ProfileInventoryTests
{
    /// <summary>Reproduces Windows merging legacy routes into an existing Routes directory without changing any content.</summary>
    [Fact]
    public void WindowsDirectoryIdentityDoesNotDependOnOrdinalSortOrder()
    {
        string[] expected = ["Routes/F123", "guardian", "routes", "systems"];
        string[] actual = ["Routes", "Routes/F123", "guardian", "systems"];

        Assert.True(ProfileInventory.DirectoryPathsMatch(expected, actual, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Verifies that the comparison still rejects missing, additional, or case-distinct Linux directories.</summary>
    [Theory]
    [InlineData("guardian", "systems")]
    [InlineData("Routes", "routes")]
    public void DirectoryIdentityRejectsDifferentPathsOnCaseSensitivePlatforms(string expected, string actual)
    {
        Assert.False(ProfileInventory.DirectoryPathsMatch([expected], [actual], StringComparer.Ordinal));
    }

    /// <summary>Rejects changed Windows directory sets even when file inventories themselves are unchanged.</summary>
    [Theory]
    [InlineData(new[] { "Routes" }, new[] { "Routes", "injected" })]
    [InlineData(new[] { "Routes", "guardian" }, new[] { "Routes" })]
    [InlineData(new[] { "Routes", "guardian" }, new[] { "Routes", "injected" })]
    public void WindowsDirectoryIdentityRejectsMissingOrUnexpectedFolders(string[] expected, string[] actual)
    {
        Assert.False(ProfileInventory.DirectoryPathsMatch(expected, actual, StringComparer.OrdinalIgnoreCase));
    }
}
