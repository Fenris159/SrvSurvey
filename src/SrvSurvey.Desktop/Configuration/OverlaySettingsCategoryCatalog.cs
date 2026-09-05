namespace SrvSurvey.Desktop.Configuration;

public static class OverlaySettingsCategoryCatalog
{
    public static IReadOnlyList<OverlaySettingsCategoryDefinition> All { get; } =
    [
        new(
            OverlaySettingsCategory.Exploration,
            "exploration",
            "Exploration",
            "EXPLORATION",
            "Configure FSS, system survey, body information, and Galaxy Map overlays."),
        new(
            OverlaySettingsCategory.Exobiology,
            "exobiology",
            "Exobiology",
            "EXOBIOLOGY",
            "Configure biological survey, prior-scan, surface radar, and reward presentation overlays."),
        new(
            OverlaySettingsCategory.Travel,
            "travel",
            "Travel",
            "TRAVEL",
            "Configure next-jump and station-information overlays."),
        new(
            OverlaySettingsCategory.Boxel,
            "boxel",
            "Boxel",
            "BOXEL",
            "Configure Galaxy Map boxel guidance and completion notifications."),
        new(
            OverlaySettingsCategory.Mining,
            "mining",
            "Mining",
            "MINING",
            "Configure the Rhino surface mining overlay and rig location shortcuts."),
        new(
            OverlaySettingsCategory.Guardian,
            "guardian",
            "Guardian",
            "GUARDIAN",
            "Configure Guardian maps, system summaries, and Ram Tah overlays."),
        new(
            OverlaySettingsCategory.Quests,
            "quests",
            "Quests",
            "QUESTS",
            "Configure combat, mission, and human-settlement overlays."),
        new(
            OverlaySettingsCategory.Colonization,
            "colonisation",
            "Colonization",
            "COLONIZATION",
            "Configure the colonization commodity shopping overlay."),
    ];

    public static bool TryGet(
        string? navigationKey,
        out OverlaySettingsCategoryDefinition definition)
    {
        definition = All.FirstOrDefault(candidate => string.Equals(
            candidate.NavigationKey,
            navigationKey,
            StringComparison.Ordinal))!;
        return definition is not null;
    }
}

public enum OverlaySettingsCategory
{
    Global,
    Exploration,
    Exobiology,
    Travel,
    Boxel,
    Guardian,
    Quests,
    Colonization,
    Mining,
}

public sealed record OverlaySettingsCategoryDefinition(
    OverlaySettingsCategory Category,
    string NavigationKey,
    string DisplayName,
    string Eyebrow,
    string Description);
