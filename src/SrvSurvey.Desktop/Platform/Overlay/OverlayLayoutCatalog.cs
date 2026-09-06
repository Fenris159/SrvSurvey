using Avalonia;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform.Overlay;

public static class OverlayLayoutCatalog
{
    public static IReadOnlyList<OverlayLayoutCategoryDefinition> Categories { get; } =
    [
        new(OverlayLayoutCategory.ExplorationAndNavigation, "Exploration & navigation"),
        new(OverlayLayoutCategory.Mining, "Mining"),
        new(OverlayLayoutCategory.BiologyAndSurface, "Biology & surface"),
        new(OverlayLayoutCategory.Guardian, "Guardian"),
        new(OverlayLayoutCategory.SitesAndQuests, "Sites & quests"),
        new(OverlayLayoutCategory.CombatAndColonization, "Combat & colonization"),
        new(OverlayLayoutCategory.StatusAndUtilities, "Status & utilities"),
    ];

    public static IReadOnlyList<OverlayLayoutDefinition> Supported { get; } =
    [
        Define("PlotBioStatus", "Biology sample status", "SystemSurvey.AutoShowBioStatus", OverlayLayoutCategory.BiologyAndSurface, OverlaySettingsCategory.Exobiology, new(260, 80), new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8)),
        // Keep the legacy bottom-based initial location, but top-anchor custom
        // moves because the three shared Biology states have different heights.
        Define("PlotBioSystem", "System biology", "SystemSurvey.AutoShowBioSystem", OverlayLayoutCategory.BiologyAndSurface, OverlaySettingsCategory.Exobiology, new(240, 200), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Bottom, 144, MoveVerticalAnchor: LegacyVerticalAnchor.Top)),
        Define("PlotBodyInfo", "Body information", "SystemSurvey.AutoShowBodyInfo", OverlayLayoutCategory.ExplorationAndNavigation, OverlaySettingsCategory.Exploration, new(260, 280), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8, ShowInGalaxyMap: true)),
        Define("PlotBuildCommodities", "Colonization commodities", "Colonization.AutoShowCommodityOverlay", OverlayLayoutCategory.CombatAndColonization, OverlaySettingsCategory.Colonization, new(270, 380), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8, ShowInGalaxyMap: true)),
        Define("PlotFlightWarning", "Flight warning", "SystemSurvey.AutoShowFlightWarnings", OverlayLayoutCategory.StatusAndUtilities, OverlaySettingsCategory.Exploration, new(300, 80), new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 90)),
        Define("PlotFloatie", "Notifications", "Notifications.Enabled", OverlayLayoutCategory.StatusAndUtilities, OverlaySettingsCategory.Global, new(160, 80), new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Bottom, 24, ShowInGalaxyMap: true, MoveVerticalAnchor: LegacyVerticalAnchor.Top)),
        Define("PlotFootCombat", "Ground combat", "Combat.AutoShowFootCombat", OverlayLayoutCategory.CombatAndColonization, OverlaySettingsCategory.Quests, new(160, 88), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8)),
        Define("PlotFSS", "FSS body feed", "SystemSurvey.AutoShowLastFssBody", OverlayLayoutCategory.ExplorationAndNavigation, OverlaySettingsCategory.Exploration, new(240, 100), new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8)),
        Define("PlotFSSInfo", "FSS information", "SystemSurvey.AutoShowFssInfo", OverlayLayoutCategory.ExplorationAndNavigation, OverlaySettingsCategory.Exploration, new(270, 400), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8, ShowInGalaxyMap: true)),
        Define("PlotGalMap", "Galaxy Map system intelligence", "GalaxyMap.AutoShow", OverlayLayoutCategory.ExplorationAndNavigation, OverlaySettingsCategory.Exploration, new(240, 180), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8, ShowInGalaxyMap: true)),
        Define("PlotMiningWarning", "Mining rig range warning", "Mining.ShouldShowRigWarning", OverlayLayoutCategory.Mining, OverlaySettingsCategory.Mining, new(300, 80), new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 90)),
        Define("PlotSurfaceMining", "Surface mining", "Mining.ShouldShow", OverlayLayoutCategory.Mining, OverlaySettingsCategory.Mining, new(320, 530), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Middle, 0, MoveVerticalAnchor: LegacyVerticalAnchor.Top)),
        Define("PlotGrounded", "Surface survey", "SystemSurvey.AutoShowSurfaceRadar", OverlayLayoutCategory.BiologyAndSurface, OverlaySettingsCategory.Exobiology, new(320, 440), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Middle, 0, MoveVerticalAnchor: LegacyVerticalAnchor.Top)),
        Define("PlotGuardians", "Guardian site", "Guardian.EnableGuardianSites", OverlayLayoutCategory.Guardian, OverlaySettingsCategory.Guardian, new(300, 400), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0, MoveVerticalAnchor: LegacyVerticalAnchor.Top)),
        Define("PlotGuardianStatus", "Guardian status", "Guardian.EnableGuardianSites", OverlayLayoutCategory.Guardian, OverlaySettingsCategory.Guardian, new(260, 108), new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8)),
        Define("PlotGuardianSystem", "Guardian system", "Guardian.AutoShowGuardianSummary", OverlayLayoutCategory.Guardian, OverlaySettingsCategory.Guardian, new(190, 96), new(LegacyHorizontalAnchor.Left, 10, LegacyVerticalAnchor.Top, 8)),
        Define("PlotHumanSite", "Human settlement", "HumanSite.AutoShow", OverlayLayoutCategory.SitesAndQuests, OverlaySettingsCategory.Quests, new(260, 440), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0, MoveVerticalAnchor: LegacyVerticalAnchor.Top)),
        Define("PlotJumpInfo", "Next-jump information", "JumpInfo.AutoShow", OverlayLayoutCategory.ExplorationAndNavigation, OverlaySettingsCategory.Travel, new(600, 100), new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8, ShowInGalaxyMap: true)),
        Define("PlotFleetCarrierRoute", "Fleet carrier route", "FleetCarrierRoute.IsActive", OverlayLayoutCategory.ExplorationAndNavigation, OverlaySettingsCategory.Travel, new(260, 400), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8)),
        Define("PlotRouteBio", "Route bodies", "Route.IsActive", OverlayLayoutCategory.ExplorationAndNavigation, OverlaySettingsCategory.Travel, new(260, 420), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8)),
        Define("PlotMassacre", "Massacre missions", "Combat.AutoShowMassacreMissions", OverlayLayoutCategory.CombatAndColonization, OverlaySettingsCategory.Quests, new(190, 200), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8)),
        Define("PlotMiniTrack", "Mini tracker", "SystemSurvey.AutoShowMiniTrack", OverlayLayoutCategory.BiologyAndSurface, OverlaySettingsCategory.Exobiology, new(190, 80), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8)),
        Define("PlotMultiGameCommander", "Multiple Commander indicator", "OverlayBehavior.HideMultiGameCommanderOverlay", OverlayLayoutCategory.StatusAndUtilities, OverlaySettingsCategory.Global, new(190, 36), new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, -34)),
        Define("PlotPriorScans", "Prior scans", "SystemSurvey.AutoShowPriorScans", OverlayLayoutCategory.BiologyAndSurface, OverlaySettingsCategory.Exobiology, new(308, 300), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0, MoveVerticalAnchor: LegacyVerticalAnchor.Top)),
        Define("PlotPulse", "Journal activity and SCO status", "PulseOverlay.Enabled", OverlayLayoutCategory.StatusAndUtilities, OverlaySettingsCategory.Global, new(32, 32), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Bottom, 8)),
        Define("PlotQuestMini", "Quest indicator", "QuestWorkspace.IsEnabled", OverlayLayoutCategory.SitesAndQuests, OverlaySettingsCategory.Quests, new(220, 200), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8)),
        Define("PlotRamTah", "Ram Tah guidance", "Guardian.AutoShowRamTah", OverlayLayoutCategory.Guardian, OverlaySettingsCategory.Guardian, new(190, 224), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Middle, 0, MoveVerticalAnchor: LegacyVerticalAnchor.Top)),
        Define("PlotSphericalSearch", "Spherical search", "Search, BoxelSearch, or Route active", OverlayLayoutCategory.ExplorationAndNavigation, new OverlayLayoutSettingsCategories(OverlaySettingsCategory.Boxel, [OverlaySettingsCategory.Exploration, OverlaySettingsCategory.Travel]), new(240, 240), new(LegacyHorizontalAnchor.Right, 8, LegacyVerticalAnchor.Top, 8, ShowInGalaxyMap: true)),
        Define("PlotStationInfo", "Station information", "StationInfo.AutoShow", OverlayLayoutCategory.CombatAndColonization, OverlaySettingsCategory.Travel, new(220, 300), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Middle, 0, ShowInGalaxyMap: true, MoveVerticalAnchor: LegacyVerticalAnchor.Top)),
        // Live/editor panel sizes to the shared content (up to 220 wide) with
        // DSS body chips and remaining biological signals. Keep the legacy
        // bottom-based initial location, but top-anchor custom moves because
        // the panel height changes with its current system-status content.
        Define("PlotSysStatus", "System status", "SystemSurvey.AutoShowSystemStatus", OverlayLayoutCategory.ExplorationAndNavigation, OverlaySettingsCategory.Exploration, new(140, 140), new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Bottom, 44, MoveVerticalAnchor: LegacyVerticalAnchor.Top)),
        Define("PlotTrackTarget", "Ground target", "GroundTarget.ShouldShow", OverlayLayoutCategory.BiologyAndSurface, OverlaySettingsCategory.Exobiology, new(128, 108), new(LegacyHorizontalAnchor.Center, 480, LegacyVerticalAnchor.Top, 8)),
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
        return Find(new OverlayId(name))
            ?? throw new ArgumentOutOfRangeException(
                nameof(name),
                name,
                "No passive overlay is registered with this plotter name.");
    }

    internal static OverlayLayoutDefinition GetRequired(OverlayId id)
    {
        return Find(id)
            ?? throw new ArgumentOutOfRangeException(
                nameof(id),
                id,
                "No passive overlay is registered with this identity.");
    }

    private static OverlayLayoutDefinition? Find(OverlayId id) =>
        Supported.FirstOrDefault(definition => definition.Id == id);

    private static OverlayLayoutDefinition Define(
        string name,
        string displayName,
        string configurationBinding,
        OverlayLayoutCategory category,
        OverlayLayoutSettingsCategories settingsCategories,
        OverlayLayoutPreview preview,
        OverlayLayoutAnchor anchor)
    {
        return new OverlayLayoutDefinition(
            name,
            displayName,
            configurationBinding,
            category,
            settingsCategories.All,
            new PixelSize(preview.Width, preview.Height),
            new LegacyOverlayPlacement(
                anchor.Horizontal,
                anchor.HorizontalOffset,
                anchor.Vertical,
                anchor.VerticalOffset,
                null),
            anchor.ShowInGalaxyMap,
            anchor.MoveVerticalAnchor ?? anchor.Vertical);
    }
}

internal readonly record struct OverlayLayoutPreview(int Width, int Height);

internal readonly record struct OverlayLayoutSettingsCategories(
    OverlaySettingsCategory Primary,
    IReadOnlyList<OverlaySettingsCategory>? Additional = null)
{
    internal IReadOnlyList<OverlaySettingsCategory> All => Additional is null
        ? [Primary]
        : [Primary, .. Additional];

    public static implicit operator OverlayLayoutSettingsCategories(
        OverlaySettingsCategory category) => new(category);
}

internal readonly record struct OverlayLayoutAnchor(
    LegacyHorizontalAnchor Horizontal,
    int HorizontalOffset,
    LegacyVerticalAnchor Vertical,
    int VerticalOffset,
    bool ShowInGalaxyMap = false,
    LegacyVerticalAnchor? MoveVerticalAnchor = null);

public sealed record OverlayLayoutDefinition(
    string Name,
    string DisplayName,
    string ConfigurationBinding,
    OverlayLayoutCategory Category,
    IReadOnlyList<OverlaySettingsCategory> SettingsCategories,
    PixelSize PreviewSize,
    LegacyOverlayPlacement DefaultPlacement,
    bool ShowInGalaxyMap,
    LegacyVerticalAnchor MoveVerticalAnchor)
{
    internal OverlayId Id { get; } = new(Name);
}

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
    Mining,
}
