using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

public static class OverlayLayoutCatalog
{
    public static IReadOnlyList<OverlayLayoutCategoryDefinition> Categories { get; } =
    [
        new(OverlayLayoutCategory.ExplorationAndNavigation, "Exploration & navigation"),
        new(OverlayLayoutCategory.BiologyAndSurface, "Biology & surface"),
        new(OverlayLayoutCategory.SitesAndQuests, "Sites & quests"),
        new(OverlayLayoutCategory.CombatAndColonization, "Combat & colonization"),
        new(OverlayLayoutCategory.StatusAndUtilities, "Status & utilities"),
    ];

    public static IReadOnlyList<OverlayLayoutDefinition> Supported { get; } =
    [
        Define("PlotBioStatus", "Biology sample status", "SystemSurvey.AutoShowBioStatus", OverlayLayoutCategory.BiologyAndSurface, 480, 80, LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8),
        Define("PlotBioSystem", "System biology", "SystemSurvey.AutoShowBioSystem", OverlayLayoutCategory.BiologyAndSurface, 200, 200, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Bottom, 144),
        Define("PlotBodyInfo", "Body information", "SystemSurvey.AutoShowBodyInfo", OverlayLayoutCategory.ExplorationAndNavigation, 320, 280, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8, showInGalaxyMap: true),
        Define("PlotBuildCommodities", "Colonization commodities", "Colonization.AutoShowCommodityOverlay", OverlayLayoutCategory.CombatAndColonization, 200, 400, LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8, showInGalaxyMap: true),
        Define("PlotFlightWarning", "Flight warning", "SystemSurvey.AutoShowFlightWarnings", OverlayLayoutCategory.StatusAndUtilities, 300, 80, LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 90),
        Define("PlotFloatie", "Notifications", "Notifications.Enabled", OverlayLayoutCategory.StatusAndUtilities, 200, 80, LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Bottom, 24, showInGalaxyMap: true),
        Define("PlotFootCombat", "Ground combat", "Combat.AutoShowFootCombat", OverlayLayoutCategory.CombatAndColonization, 180, 200, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotFSS", "FSS body feed", "SystemSurvey.AutoShowLastFssBody", OverlayLayoutCategory.ExplorationAndNavigation, 420, 100, LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8),
        Define("PlotFSSInfo", "FSS information", "SystemSurvey.AutoShowFssInfo", OverlayLayoutCategory.ExplorationAndNavigation, 300, 400, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8, showInGalaxyMap: true),
        Define("PlotGalMap", "Galaxy Map system intelligence", "GalaxyMap.AutoShow", OverlayLayoutCategory.ExplorationAndNavigation, 240, 180, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8, showInGalaxyMap: true),
        Define("PlotGrounded", "Surface survey", "SystemSurvey.AutoShowSurfaceRadar", OverlayLayoutCategory.BiologyAndSurface, 320, 440, LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Middle, 0),
        Define("PlotGuardians", "Guardian site", "Guardian.EnableGuardianSites", OverlayLayoutCategory.SitesAndQuests, 320, 440, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0),
        Define("PlotGuardianStatus", "Guardian status", "Guardian.EnableGuardianSites", OverlayLayoutCategory.SitesAndQuests, 500, 108, LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8),
        Define("PlotGuardianSystem", "Guardian system", "Guardian.AutoShowGuardianSummary", OverlayLayoutCategory.SitesAndQuests, 300, 200, LegacyHorizontalAnchor.Left, 10, LegacyVerticalAnchor.Top, 8),
        Define("PlotHumanSite", "Human settlement", "HumanSite.AutoShow", OverlayLayoutCategory.SitesAndQuests, 320, 440, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0),
        Define("PlotJumpInfo", "Next-jump information", "JumpInfo.AutoShow", OverlayLayoutCategory.ExplorationAndNavigation, 600, 100, LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8, showInGalaxyMap: true),
        Define("PlotFleetCarrierRoute", "Fleet carrier route", "FleetCarrierRoute.IsActive", OverlayLayoutCategory.ExplorationAndNavigation, 460, 400, LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotRouteBio", "Route bodies", "Route.IsActive", OverlayLayoutCategory.ExplorationAndNavigation, 220, 420, LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotMassacre", "Massacre missions", "Combat.AutoShowMassacreMissions", OverlayLayoutCategory.CombatAndColonization, 180, 200, LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotMiniTrack", "Mini tracker", "SystemSurvey.AutoShowMiniTrack", OverlayLayoutCategory.BiologyAndSurface, 240, 80, LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotMultiGameCommander", "Multiple Commander indicator", "OverlayBehavior.HideMultiGameCommanderOverlay", OverlayLayoutCategory.StatusAndUtilities, 340, 36, LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, -34),
        Define("PlotPriorScans", "Prior scans", "SystemSurvey.AutoShowPriorScans", OverlayLayoutCategory.BiologyAndSurface, 308, 300, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0),
        Define("PlotPulse", "Journal activity and SCO status", "PulseOverlay.Enabled", OverlayLayoutCategory.StatusAndUtilities, 32, 32, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Bottom, 8),
        Define("PlotQuestMini", "Quest indicator", "QuestWorkspace.IsEnabled", OverlayLayoutCategory.SitesAndQuests, 180, 200, LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotRamTah", "Ram Tah guidance", "Guardian.AutoShowRamTah", OverlayLayoutCategory.SitesAndQuests, 200, 280, LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Middle, 0),
        Define("PlotSphericalSearch", "Spherical search", "Search, BoxelSearch, or Route active", OverlayLayoutCategory.ExplorationAndNavigation, 240, 240, LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8, showInGalaxyMap: true),
        Define("PlotStationInfo", "Station information", "StationInfo.AutoShow", OverlayLayoutCategory.CombatAndColonization, 200, 300, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0, showInGalaxyMap: true),
        Define("PlotSysStatus", "System status", "SystemSurvey.AutoShowSystemStatus", OverlayLayoutCategory.ExplorationAndNavigation, 170, 40, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Bottom, 44),
        Define("PlotTrackTarget", "Ground target", "GroundTarget.ShouldShow", OverlayLayoutCategory.BiologyAndSurface, 128, 108, LegacyHorizontalAnchor.Center, 480, LegacyVerticalAnchor.Top, 8),
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
        int previewWidth,
        int previewHeight,
        LegacyHorizontalAnchor horizontal,
        int horizontalOffset,
        LegacyVerticalAnchor vertical,
        int verticalOffset,
        bool showInGalaxyMap = false)
    {
        return new OverlayLayoutDefinition(
            name,
            displayName,
            configurationBinding,
            category,
            new PixelSize(previewWidth, previewHeight),
            new LegacyOverlayPlacement(
                horizontal,
                horizontalOffset,
                vertical,
                verticalOffset,
                null),
            showInGalaxyMap);
    }
}

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
    SitesAndQuests,
    CombatAndColonization,
    StatusAndUtilities,
}
