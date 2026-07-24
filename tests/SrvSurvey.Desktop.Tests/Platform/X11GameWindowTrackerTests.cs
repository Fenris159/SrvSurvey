using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class X11GameWindowTrackerTests
{
    [Theory]
    [InlineData("elitedangerous64.exe", "Wine", null)]
    [InlineData(null, "EliteDangerous64.exe", "unrelated")]
    [InlineData(null, null, "Elite - Dangerous (CLIENT)")]
    [InlineData(null, null, "Elite Dangerous")]
    public void RecognizesEliteWineWindowIdentities(
        string? resourceName,
        string? resourceClass,
        string? title)
    {
        Assert.True(EliteGameWindowIdentity.MatchesX11(
            resourceName,
            resourceClass,
            title));
    }

    [Theory]
    [InlineData("steam", "Steam", "Elite Dangerous Community")]
    [InlineData("firefox", "Firefox", "Elite Dangerous - Wiki")]
    [InlineData(null, null, null)]
    public void RejectsUnrelatedX11Windows(
        string? resourceName,
        string? resourceClass,
        string? title)
    {
        Assert.False(EliteGameWindowIdentity.MatchesX11(
            resourceName,
            resourceClass,
            title));
    }
}
