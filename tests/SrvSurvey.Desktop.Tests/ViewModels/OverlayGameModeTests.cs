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

    [Theory]
    [InlineData(StatusFlags.InFighter, (int)OverlayGameMode.InFighter)]
    [InlineData(StatusFlags.InSrv, (int)OverlayGameMode.InSrv)]
    [InlineData(StatusFlags.Landed, (int)OverlayGameMode.Landed)]
    [InlineData(StatusFlags.Docked, (int)OverlayGameMode.Docked)]
    [InlineData(StatusFlags.InMainShip, (int)OverlayGameMode.Flying)]
    public void PhysicalVehicleAndShipStatesResolve(
        StatusFlags flags,
        int expected)
    {
        var status = new EliteStatus { Flags = flags };
        Assert.Equal(
            (OverlayGameMode)expected,
            OverlayGameModeResolver.Resolve(status));
    }

    [Fact]
    public void OnFootTaxiAndGlideModesResolveFromFlags2()
    {
        Assert.Equal(
            OverlayGameMode.InTaxi,
            OverlayGameModeResolver.Resolve(new EliteStatus
            {
                Flags2 = StatusFlags2.InTaxi,
            }));
        Assert.Equal(
            OverlayGameMode.OnFootInStation,
            OverlayGameModeResolver.Resolve(new EliteStatus
            {
                Flags2 = StatusFlags2.OnFoot | StatusFlags2.OnFootInStation,
            }));
        Assert.Equal(
            OverlayGameMode.OnFoot,
            OverlayGameModeResolver.Resolve(new EliteStatus
            {
                Flags2 = StatusFlags2.OnFoot
                    | StatusFlags2.OnFootOnPlanet
                    | StatusFlags2.OnFootExterior,
            }));
        Assert.Equal(
            OverlayGameMode.GlideMode,
            OverlayGameModeResolver.Resolve(new EliteStatus
            {
                Flags = StatusFlags.InMainShip,
                Flags2 = StatusFlags2.GlideMode,
            }));
    }

    [Fact]
    public void NullStatusIsOfflineAndEmptyFlagsRemainOffline()
    {
        Assert.Equal(
            OverlayGameMode.Offline,
            OverlayGameModeResolver.Resolve(null));
        Assert.Equal(
            OverlayGameMode.Offline,
            OverlayGameModeResolver.Resolve(new EliteStatus()));
    }
}
