namespace SrvSurvey.Desktop.ViewModels;

public static class GuideCatalog
{
    public static IReadOnlyList<GuideCategoryViewModel> Create()
    {
        return
        [
            new GuideCategoryViewModel(
                "getting-started",
                "01",
                "Getting started",
                "Connect SrvSurvey to the correct Commander and learn how its live workspaces and overlays respond to Elite Dangerous.",
                [
                    new GuideSectionViewModel(
                        "First launch",
                        "SrvSurvey reads Elite Dangerous Journal files. It does not need to modify the game installation.",
                        [
                            "Open Settings and confirm the journal folder. The default is discovered automatically on supported platforms.",
                            "Choose a preferred Commander when more than one profile appears in the same journal folder.",
                            "Keep SrvSurvey open while playing. The main workspace and context-sensitive overlays update from new journal events.",
                        ],
                        [
                            "Overview shows the active Commander, session, system, body, exploration totals, and unclaimed exobiology rewards.",
                            "If no Commander appears, use Diagnostics to inspect the chosen folder and newest Journal file.",
                        ]),
                    new GuideSectionViewModel(
                        "How the interface is organized",
                        "The numbered navigation follows the major activities in SrvSurvey; this Guides workspace is always available and does not depend on the game running.",
                        [],
                        [
                            "Exploration, Exobiology, Travel, Search, Guardian, Quests, and Colonisation contain activity-specific tools.",
                            "Diagnostics explains the live data source and provides repair, update, and journal-inspection tools.",
                            "Settings controls application behavior, overlay behavior, input bindings, privacy, migration, and appearance.",
                        ]),
                ],
                []),
            new GuideCategoryViewModel(
                "icons",
                "02",
                "Overlay icon glossary",
                "A visual reference for the symbols, markers, reward PIPs, and map shapes used by the in-game overlays.",
                [],
                []),
        ];
    }
}
