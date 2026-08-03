using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class OverlayGameModeTests
{
    [Theory]
    [InlineData(GuiFocus.InternalPanel, 1)]
    [InlineData(GuiFocus.ExternalPanel, 2)]
    [InlineData(GuiFocus.RolePanel, 4)]
    [InlineData(GuiFocus.GalaxyMap, 6)]
    public void GuiFocusTakesPriorityOverPhysicalShipState(
        GuiFocus focus,
        int expected)
    {
        var status = new EliteStatus
        {
            Flags = StatusFlags.InMainShip | StatusFlags.Supercruise,
            GuiFocus = focus,
        };

        Assert.Equal((OverlayGameMode)expected, OverlayGameModeResolver.Resolve(status));
    }

    [Fact]
    public void PhysicalModeIsUsedWhenNoGuiHasFocus()
    {
        var status = new EliteStatus
        {
            Flags = StatusFlags.InMainShip | StatusFlags.Supercruise,
        };

        Assert.Equal(
            OverlayGameMode.SuperCruising,
            OverlayGameModeResolver.Resolve(status));
    }

    [Fact]
    public void FsdJumpIsResolvedAfterGuiFocus()
    {
        var status = new EliteStatus
        {
            Flags = StatusFlags.InMainShip | StatusFlags.FsdJump,
            GuiFocus = GuiFocus.SystemMap,
        };

        Assert.Equal(
            OverlayGameMode.SystemMap,
            OverlayGameModeResolver.Resolve(status));
        Assert.Equal(
            OverlayGameMode.FsdJumping,
            OverlayGameModeResolver.Resolve(status with
            {
                GuiFocus = GuiFocus.NoFocus,
            }));
    }

    [Fact]
    public void JournalMusicIsTheLegacyFallbackAfterGuiFocusAndFsdJump()
    {
        var status = new EliteStatus
        {
            Flags = StatusFlags.InMainShip,
        };

        Assert.Equal(
            OverlayGameMode.GalaxyMap,
            OverlayGameModeResolver.Resolve(
                status,
                musicTrack: "GalaxyMap"));
        Assert.Equal(
            OverlayGameMode.SystemMap,
            OverlayGameModeResolver.Resolve(
                status,
                musicTrack: "SystemMap"));
        Assert.Equal(
            OverlayGameMode.FsdJumping,
            OverlayGameModeResolver.Resolve(
                status,
                isFsdJumping: true,
                musicTrack: "GalaxyMap"));
        Assert.Equal(
            OverlayGameMode.InternalPanel,
            OverlayGameModeResolver.Resolve(
                status with { GuiFocus = GuiFocus.InternalPanel },
                musicTrack: "GalaxyMap"));
    }
}
