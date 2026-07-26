namespace SrvSurvey.Desktop.Platform.Overlay;

public static class OverlayLayoutCatalog
{
    public static IReadOnlyList<OverlayLayoutDefinition> Supported { get; } =
    [
        Define("PlotBioStatus", "Biology sample status", LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8),
        Define("PlotBioSystem", "System biology", LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Bottom, 144),
        Define("PlotBodyInfo", "Body information", LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotBuildCommodities", "Colonization commodities", LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotFlightWarning", "Flight warning", LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 90),
        Define("PlotFloatie", "Notifications", LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Bottom, 24),
        Define("PlotFootCombat", "Ground combat", LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotFSS", "FSS body feed", LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8),
        Define("PlotFSSInfo", "FSS information", LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotGalMap", "Galaxy Map system intelligence", LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotGrounded", "Surface survey", LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Middle, 0),
        Define("PlotGuardians", "Guardian site", LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0),
        Define("PlotGuardianSystem", "Guardian system", LegacyHorizontalAnchor.Left, 10, LegacyVerticalAnchor.Top, 8),
        Define("PlotHumanSite", "Human settlement", LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0),
        Define("PlotJumpInfo", "Next-jump information", LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8),
        Define("PlotMassacre", "Massacre missions", LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotMiniTrack", "Mini tracker", LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotMultiGameCommander", "Multiple Commander indicator", LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, -34),
        Define("PlotPriorScans", "Prior scans", LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0),
        Define("PlotPulse", "Journal activity and SCO status", LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Bottom, 8),
        Define("PlotQuestMini", "Quest indicator", LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotRamTah", "Ram Tah guidance", LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Middle, 0),
        Define("PlotSphericalSearch", "Spherical search", LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotStationInfo", "Station information", LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0),
        Define("PlotSysStatus", "System status", LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Bottom, 44),
        Define("PlotTrackTarget", "Ground target", LegacyHorizontalAnchor.Center, 480, LegacyVerticalAnchor.Top, 8),
    ];

    private static OverlayLayoutDefinition Define(
        string name,
        string displayName,
        LegacyHorizontalAnchor horizontal,
        int horizontalOffset,
        LegacyVerticalAnchor vertical,
        int verticalOffset)
    {
        return new OverlayLayoutDefinition(
            name,
            displayName,
            new LegacyOverlayPlacement(
                horizontal,
                horizontalOffset,
                vertical,
                verticalOffset,
                null));
    }
}

public sealed record OverlayLayoutDefinition(
    string Name,
    string DisplayName,
    LegacyOverlayPlacement DefaultPlacement);
