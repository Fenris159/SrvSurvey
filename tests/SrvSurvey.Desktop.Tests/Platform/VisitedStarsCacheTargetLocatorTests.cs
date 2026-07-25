using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class VisitedStarsCacheTargetLocatorTests
{
    [Fact]
    public void ResolvesFrontierNumericProfileDirectory()
    {
        var local = Path.Combine("C:\\", "Users", "Drew", "AppData", "Local");

        var path = VisitedStarsCacheTargetLocator.ResolveWindows(local, "F123456");

        Assert.Equal(
            Path.Combine(
                Path.GetFullPath(local),
                "Frontier Developments",
                "Elite Dangerous",
                "123456",
                VisitedStarsCacheService.CacheFileName),
            path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Commander")]
    [InlineData("F12/34")]
    public void RejectsUnsafeFrontierIdentity(string frontierId)
    {
        Assert.Null(VisitedStarsCacheTargetLocator.ResolveWindows(
            Path.GetTempPath(),
            frontierId));
    }
}
