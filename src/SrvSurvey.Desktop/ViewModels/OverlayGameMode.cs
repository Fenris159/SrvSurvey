using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.ViewModels;

internal enum OverlayGameMode
{
    Offline,
    InternalPanel,
    ExternalPanel,
    CommsPanel,
    RolePanel,
    StationServices,
    GalaxyMap,
    SystemMap,
    Orrery,
    Fss,
    Saa,
    Codex,
    FsdJumping,
    InFighter,
    InSrv,
    InTaxi,
    OnFootInStation,
    OnFoot,
    SuperCruising,
    GlideMode,
    Landed,
    Docked,
    Flying,
    Unknown,
}

internal static class OverlayGameModeResolver
{
    public static OverlayGameMode Resolve(
        EliteStatus? status,
        bool isFsdJumping = false,
        string? musicTrack = null)
    {
        if (status is null)
        {
            return OverlayGameMode.Offline;
        }

        if (TryResolveGuiFocus(status.GuiFocus, out var guiMode))
        {
            return guiMode;
        }

        if (isFsdJumping || status.Flags.HasFlag(StatusFlags.FsdJump))
        {
            return OverlayGameMode.FsdJumping;
        }

        if (TryResolveMusicTrack(musicTrack, out var musicMode))
        {
            return musicMode;
        }

        return ResolvePhysicalMode(status);
    }

    private static bool TryResolveGuiFocus(
        GuiFocus focus,
        out OverlayGameMode mode)
    {
        mode = focus switch
        {
            GuiFocus.NoFocus => OverlayGameMode.Unknown,
            GuiFocus.InternalPanel => OverlayGameMode.InternalPanel,
            GuiFocus.ExternalPanel => OverlayGameMode.ExternalPanel,
            GuiFocus.CommsPanel => OverlayGameMode.CommsPanel,
            GuiFocus.RolePanel => OverlayGameMode.RolePanel,
            GuiFocus.StationServices => OverlayGameMode.StationServices,
            GuiFocus.GalaxyMap => OverlayGameMode.GalaxyMap,
            GuiFocus.SystemMap => OverlayGameMode.SystemMap,
            GuiFocus.Orrery => OverlayGameMode.Orrery,
            GuiFocus.Fss => OverlayGameMode.Fss,
            GuiFocus.Saa => OverlayGameMode.Saa,
            GuiFocus.Codex => OverlayGameMode.Codex,
            _ => OverlayGameMode.Unknown,
        };
        return focus != GuiFocus.NoFocus;
    }

    private static bool TryResolveMusicTrack(
        string? musicTrack,
        out OverlayGameMode mode)
    {
        if (string.Equals(musicTrack, "GalaxyMap", StringComparison.Ordinal))
        {
            mode = OverlayGameMode.GalaxyMap;
            return true;
        }

        if (string.Equals(musicTrack, "SystemMap", StringComparison.Ordinal))
        {
            mode = OverlayGameMode.SystemMap;
            return true;
        }

        mode = OverlayGameMode.Unknown;
        return false;
    }

    private static OverlayGameMode ResolvePhysicalMode(EliteStatus status)
    {
        var vehicle = ResolveVehicle(status);
        if (vehicle == OverlayVehicle.Fighter)
        {
            return OverlayGameMode.InFighter;
        }

        if (vehicle == OverlayVehicle.Srv)
        {
            return OverlayGameMode.InSrv;
        }

        if (vehicle == OverlayVehicle.Taxi)
        {
            return OverlayGameMode.InTaxi;
        }

        if (status.OnFootInStation)
        {
            return OverlayGameMode.OnFootInStation;
        }

        if (status.OnFootOnPlanet)
        {
            return OverlayGameMode.OnFoot;
        }

        if (status.Flags.HasFlag(StatusFlags.Supercruise))
        {
            return OverlayGameMode.SuperCruising;
        }

        if (status.GlideMode)
        {
            return OverlayGameMode.GlideMode;
        }

        if (status.Landed)
        {
            return OverlayGameMode.Landed;
        }

        if (status.Docked)
        {
            return OverlayGameMode.Docked;
        }

        if (vehicle == OverlayVehicle.MainShip)
        {
            return OverlayGameMode.Flying;
        }

        return status.Flags == StatusFlags.None
            && status.Flags2 == StatusFlags2.None
                ? OverlayGameMode.Offline
                : OverlayGameMode.Unknown;
    }

    private static OverlayVehicle ResolveVehicle(EliteStatus status)
    {
        if (status.InMainShip)
        {
            return OverlayVehicle.MainShip;
        }

        if (status.InFighter)
        {
            return OverlayVehicle.Fighter;
        }

        if (status.InSrv)
        {
            return OverlayVehicle.Srv;
        }

        if (status.OnFoot)
        {
            return OverlayVehicle.Foot;
        }

        return status.InTaxi ? OverlayVehicle.Taxi : OverlayVehicle.Unknown;
    }

    private enum OverlayVehicle
    {
        Unknown,
        MainShip,
        Fighter,
        Srv,
        Foot,
        Taxi,
    }
}
