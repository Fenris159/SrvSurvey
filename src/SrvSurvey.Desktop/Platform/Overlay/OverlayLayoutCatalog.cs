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
        Define("PlotBioStatus", "Biology sample status", OverlayLayoutCategory.BiologyAndSurface, 480, 80, LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8),
        Define("PlotBioSystem", "System biology", OverlayLayoutCategory.BiologyAndSurface, 200, 200, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Bottom, 144),
        Define("PlotBodyInfo", "Body information", OverlayLayoutCategory.ExplorationAndNavigation, 320, 280, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotBuildCommodities", "Colonization commodities", OverlayLayoutCategory.CombatAndColonization, 200, 400, LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotFlightWarning", "Flight warning", OverlayLayoutCategory.StatusAndUtilities, 300, 80, LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 90),
        Define("PlotFloatie", "Notifications", OverlayLayoutCategory.StatusAndUtilities, 200, 80, LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Bottom, 24),
        Define("PlotFootCombat", "Ground combat", OverlayLayoutCategory.CombatAndColonization, 180, 200, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotFSS", "FSS body feed", OverlayLayoutCategory.ExplorationAndNavigation, 420, 100, LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8),
        Define("PlotFSSInfo", "FSS information", OverlayLayoutCategory.ExplorationAndNavigation, 300, 400, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotGalMap", "Galaxy Map system intelligence", OverlayLayoutCategory.ExplorationAndNavigation, 240, 180, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotGrounded", "Surface survey", OverlayLayoutCategory.BiologyAndSurface, 320, 440, LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Middle, 0),
        Define("PlotGuardians", "Guardian site", OverlayLayoutCategory.SitesAndQuests, 320, 440, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0),
        Define("PlotGuardianSystem", "Guardian system", OverlayLayoutCategory.SitesAndQuests, 300, 200, LegacyHorizontalAnchor.Left, 10, LegacyVerticalAnchor.Top, 8),
        Define("PlotHumanSite", "Human settlement", OverlayLayoutCategory.SitesAndQuests, 320, 440, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0),
        Define("PlotJumpInfo", "Next-jump information", OverlayLayoutCategory.ExplorationAndNavigation, 600, 100, LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8),
        Define("PlotMassacre", "Massacre missions", OverlayLayoutCategory.CombatAndColonization, 180, 200, LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotMiniTrack", "Mini tracker", OverlayLayoutCategory.BiologyAndSurface, 240, 80, LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotMultiGameCommander", "Multiple Commander indicator", OverlayLayoutCategory.StatusAndUtilities, 340, 36, LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, -34),
        Define("PlotPriorScans", "Prior scans", OverlayLayoutCategory.BiologyAndSurface, 308, 300, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0),
        Define("PlotPulse", "Journal activity and SCO status", OverlayLayoutCategory.StatusAndUtilities, 32, 32, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Bottom, 8),
        Define("PlotQuestMini", "Quest indicator", OverlayLayoutCategory.SitesAndQuests, 180, 200, LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotRamTah", "Ram Tah guidance", OverlayLayoutCategory.SitesAndQuests, 200, 280, LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Middle, 0),
        Define("PlotSphericalSearch", "Spherical search", OverlayLayoutCategory.ExplorationAndNavigation, 240, 240, LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8),
        Define("PlotStationInfo", "Station information", OverlayLayoutCategory.CombatAndColonization, 200, 300, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0),
        Define("PlotSysStatus", "System status", OverlayLayoutCategory.ExplorationAndNavigation, 170, 40, LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Bottom, 44),
        Define("PlotTrackTarget", "Ground target", OverlayLayoutCategory.BiologyAndSurface, 128, 108, LegacyHorizontalAnchor.Center, 480, LegacyVerticalAnchor.Top, 8),
    ];

    public static IReadOnlyList<OverlayLayoutDefinition> ForCategory(
        OverlayLayoutCategory category)
    {
        return Supported
            .Where(definition => definition.Category == category)
            .ToArray();
    }

    private static OverlayLayoutDefinition Define(
        string name,
        string displayName,
        OverlayLayoutCategory category,
        int previewWidth,
        int previewHeight,
        LegacyHorizontalAnchor horizontal,
        int horizontalOffset,
        LegacyVerticalAnchor vertical,
        int verticalOffset)
    {
        return new OverlayLayoutDefinition(
            name,
            displayName,
            category,
            new PixelSize(previewWidth, previewHeight),
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
    OverlayLayoutCategory Category,
    PixelSize PreviewSize,
    LegacyOverlayPlacement DefaultPlacement);

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
