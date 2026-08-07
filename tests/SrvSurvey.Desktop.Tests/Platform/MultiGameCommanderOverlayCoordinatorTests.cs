using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class MultiGameCommanderOverlayCoordinatorTests
{
    private static readonly GameWindowSnapshot VisibleGame = new(
        new nint(42),
        123,
        new PixelRect(100, 200, 1920, 1080),
        IsVisible: true,
        IsForeground: true);

    [Fact]
    public void VisibleForMultipleGamesWhileGameHasFocus()
    {
        Assert.True(ShouldShow(VisibleGame));
    }

    [Fact]
    public void VisibleForMultipleGamesWhileSrvSurveyHasFocus()
    {
        Assert.True(ShouldShow(
            VisibleGame with { IsForeground = false },
            isApplicationActive: true));
    }

    [Theory]
    [InlineData(false, false, false, true, true, true)]
    [InlineData(true, true, false, true, true, true)]
    [InlineData(true, false, true, true, true, true)]
    [InlineData(true, false, false, false, true, true)]
    [InlineData(true, false, false, true, false, true)]
    [InlineData(true, false, false, true, true, false)]
    public void HiddenWhenAnyRequiredConditionIsMissing(
        bool hasMultipleGameWindows,
        bool hideByPreference,
        bool isSuppressed,
        bool supportsPassiveOverlay,
        bool supportsClickThrough,
        bool supportsGameWindowTracking)
    {
        Assert.False(ShouldShow(
            VisibleGame,
            hasMultipleGameWindows,
            hideByPreference,
            isSuppressed,
            supportsPassiveOverlay,
            supportsClickThrough,
            supportsGameWindowTracking));
    }

    [Fact]
    public void HiddenForUnavailableMinimizedOrUnfocusedGame()
    {
        Assert.False(ShouldShow(GameWindowSnapshot.Unavailable));
        Assert.False(ShouldShow(VisibleGame with { IsVisible = false }));
        Assert.False(ShouldShow(VisibleGame with { IsForeground = false }));
    }

    private static bool ShouldShow(
        GameWindowSnapshot gameWindow,
        bool hasMultipleGameWindows = true,
        bool hideByPreference = false,
        bool isSuppressed = false,
        bool supportsPassiveOverlay = true,
        bool supportsClickThrough = true,
        bool supportsGameWindowTracking = true,
        bool isApplicationActive = false)
    {
        return MultiGameCommanderOverlayCoordinator.ShouldShow(
            new MultiGameOverlayVisibilityContext
    {
        HasMultipleGameWindows = hasMultipleGameWindows,
        HideByPreference = hideByPreference,
        IsSuppressed = isSuppressed,
        SupportsPassiveOverlay = supportsPassiveOverlay,
        SupportsClickThrough = supportsClickThrough,
        SupportsGameWindowTracking = supportsGameWindowTracking,
        GameWindow = gameWindow,
        IsApplicationActive = isApplicationActive
    });
    }
}
