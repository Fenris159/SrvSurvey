using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

public static class OverlayLayoutCatalog
{
    public static IReadOnlyList<OverlayLayoutCategoryDefinition> Categories { get; } =
    [
        new(OverlayLayoutCategory.ExplorationAndNavigation, "Exploration & navigation"),
        new(OverlayLayoutCategory.BiologyAndSurface, "Biology & surface"),
        new(OverlayLayoutCategory.Guardian, "Guardian"),
        new(OverlayLayoutCategory.SitesAndQuests, "Sites & quests"),
        new(OverlayLayoutCategory.CombatAndColonization, "Combat & colonization"),
        new(OverlayLayoutCategory.StatusAndUtilities, "Status & utilities"),
    ];

    public static IReadOnlyList<OverlayLayoutDefinition> Supported { get; } =
    [
        Define("PlotBioStatus", "Biology sample status", "SystemSurvey.AutoShowBioStatus", OverlayLayoutCategory.BiologyAndSurface, new(480, 80), new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8)),
        Define("PlotBioSystem", "System biology", "SystemSurvey.AutoShowBioSystem", OverlayLayoutCategory.BiologyAndSurface, new(240, 200), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Bottom, 144)),
        Define("PlotBodyInfo", "Body information", "SystemSurvey.AutoShowBodyInfo", OverlayLayoutCategory.ExplorationAndNavigation, new(290, 280), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8, ShowInGalaxyMap: true)),
        Define("PlotBuildCommodities", "Colonization commodities", "Colonization.AutoShowCommodityOverlay", OverlayLayoutCategory.CombatAndColonization, new(270, 380), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8, ShowInGalaxyMap: true)),
        Define("PlotFlightWarning", "Flight warning", "SystemSurvey.AutoShowFlightWarnings", OverlayLayoutCategory.StatusAndUtilities, new(300, 80), new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 90)),
        Define("PlotFloatie", "Notifications", "Notifications.Enabled", OverlayLayoutCategory.StatusAndUtilities, new(200, 80), new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Bottom, 24, ShowInGalaxyMap: true)),
        Define("PlotFootCombat", "Ground combat", "Combat.AutoShowFootCombat", OverlayLayoutCategory.CombatAndColonization, new(160, 88), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8)),
        Define("PlotFSS", "FSS body feed", "SystemSurvey.AutoShowLastFssBody", OverlayLayoutCategory.ExplorationAndNavigation, new(310, 100), new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8)),
        Define("PlotFSSInfo", "FSS information", "SystemSurvey.AutoShowFssInfo", OverlayLayoutCategory.ExplorationAndNavigation, new(270, 400), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8, ShowInGalaxyMap: true)),
        Define("PlotGalMap", "Galaxy Map system intelligence", "GalaxyMap.AutoShow", OverlayLayoutCategory.ExplorationAndNavigation, new(240, 180), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8, ShowInGalaxyMap: true)),
        Define("PlotGrounded", "Surface survey", "SystemSurvey.AutoShowSurfaceRadar", OverlayLayoutCategory.BiologyAndSurface, new(320, 440), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Middle, 0)),
        Define("PlotGuardians", "Guardian site", "Guardian.EnableGuardianSites", OverlayLayoutCategory.Guardian, new(300, 400), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0)),
        Define("PlotGuardianStatus", "Guardian status", "Guardian.EnableGuardianSites", OverlayLayoutCategory.Guardian, new(380, 108), new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8)),
        Define("PlotGuardianSystem", "Guardian system", "Guardian.AutoShowGuardianSummary", OverlayLayoutCategory.Guardian, new(220, 96), new(LegacyHorizontalAnchor.Left, 10, LegacyVerticalAnchor.Top, 8)),
        Define("PlotHumanSite", "Human settlement", "HumanSite.AutoShow", OverlayLayoutCategory.SitesAndQuests, new(320, 440), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0)),
        Define("PlotJumpInfo", "Next-jump information", "JumpInfo.AutoShow", OverlayLayoutCategory.ExplorationAndNavigation, new(600, 100), new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8, ShowInGalaxyMap: true)),
        Define("PlotFleetCarrierRoute", "Fleet carrier route", "FleetCarrierRoute.IsActive", OverlayLayoutCategory.ExplorationAndNavigation, new(320, 400), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8)),
        Define("PlotRouteBio", "Route bodies", "Route.IsActive", OverlayLayoutCategory.ExplorationAndNavigation, new(260, 420), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8)),
        Define("PlotMassacre", "Massacre missions", "Combat.AutoShowMassacreMissions", OverlayLayoutCategory.CombatAndColonization, new(240, 200), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8)),
        Define("PlotMiniTrack", "Mini tracker", "SystemSurvey.AutoShowMiniTrack", OverlayLayoutCategory.BiologyAndSurface, new(240, 80), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8)),
        Define("PlotMultiGameCommander", "Multiple Commander indicator", "OverlayBehavior.HideMultiGameCommanderOverlay", OverlayLayoutCategory.StatusAndUtilities, new(340, 36), new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, -34)),
        Define("PlotPriorScans", "Prior scans", "SystemSurvey.AutoShowPriorScans", OverlayLayoutCategory.BiologyAndSurface, new(308, 300), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0)),
        Define("PlotPulse", "Journal activity and SCO status", "PulseOverlay.Enabled", OverlayLayoutCategory.StatusAndUtilities, new(32, 32), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Bottom, 8)),
        Define("PlotQuestMini", "Quest indicator", "QuestWorkspace.IsEnabled", OverlayLayoutCategory.SitesAndQuests, new(240, 200), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8)),
        Define("PlotRamTah", "Ram Tah guidance", "Guardian.AutoShowRamTah", OverlayLayoutCategory.Guardian, new(240, 224), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Middle, 0)),
        Define("PlotSphericalSearch", "Spherical search", "Search, BoxelSearch, or Route active", OverlayLayoutCategory.ExplorationAndNavigation, new(240, 240), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8, ShowInGalaxyMap: true)),
        Define("PlotStationInfo", "Station information", "StationInfo.AutoShow", OverlayLayoutCategory.CombatAndColonization, new(240, 300), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0, ShowInGalaxyMap: true)),
        // Live panel sizes to content (~104–210 tall) with DSS body chips and
        // remaining biological signals. Editor height must stay >= 50 so the
        // preview is not forced into the one-line compact stub.
        Define("PlotSysStatus", "System status", "SystemSurvey.AutoShowSystemStatus", OverlayLayoutCategory.ExplorationAndNavigation, new(170, 140), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Bottom, 44)),
        Define("PlotTrackTarget", "Ground target", "GroundTarget.ShouldShow", OverlayLayoutCategory.BiologyAndSurface, new(128, 108), new(LegacyHorizontalAnchor.Center, 480, LegacyVerticalAnchor.Top, 8)),
    ];

    public static IReadOnlyList<OverlayLayoutDefinition> ForCategory(
        OverlayLayoutCategory category)
    {
        return Supported
            .Where(definition => definition.Category == category)
            .ToArray();
    }

    public static OverlayLayoutDefinition GetRequired(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Supported.FirstOrDefault(definition => string.Equals(
            definition.Name,
            name,
            StringComparison.Ordinal))
            ?? throw new ArgumentOutOfRangeException(
                nameof(name),
                name,
                "No passive overlay is registered with this plotter name.");
    }

    private static OverlayLayoutDefinition Define(
        string name,
        string displayName,
        string configurationBinding,
        OverlayLayoutCategory category,
        OverlayLayoutPreview preview,
        OverlayLayoutAnchor anchor)
    {
        return new OverlayLayoutDefinition(
            name,
            displayName,
            configurationBinding,
            category,
            new PixelSize(preview.Width, preview.Height),
            new LegacyOverlayPlacement(
                anchor.Horizontal,
                anchor.HorizontalOffset,
                anchor.Vertical,
                anchor.VerticalOffset,
                null),
            anchor.ShowInGalaxyMap);
    }
}

internal readonly record struct OverlayLayoutPreview(int Width, int Height);

internal readonly record struct OverlayLayoutAnchor(
    LegacyHorizontalAnchor Horizontal,
    int HorizontalOffset,
    LegacyVerticalAnchor Vertical,
    int VerticalOffset,
    bool ShowInGalaxyMap = false);

public sealed record OverlayLayoutDefinition(
    string Name,
    string DisplayName,
    string ConfigurationBinding,
    OverlayLayoutCategory Category,
    PixelSize PreviewSize,
    LegacyOverlayPlacement DefaultPlacement,
    bool ShowInGalaxyMap);

public sealed record OverlayLayoutCategoryDefinition(
    OverlayLayoutCategory Category,
    string DisplayName)
{
    public override string ToString() => DisplayName;
}

public enum OverlayLayoutCategory
{
    ExplorationAndNavigation,
    BiologyAndSurface,
    Guardian,
    SitesAndQuests,
    CombatAndColonization,
    StatusAndUtilities,
}
