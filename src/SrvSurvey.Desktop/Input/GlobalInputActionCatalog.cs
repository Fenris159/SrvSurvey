namespace SrvSurvey.Desktop.Input;

public enum GlobalInputAction
{
    ToggleAllVisibility,
    ToggleOverlayInteraction,
    MapZoomIn,
    MapZoomOut,
    MapZoomAuto,
    MapBeHuge,
    ShowJumpInfo,
    CopyNextBoxel,
    PasteGalMap,
    ShowFssInfo,
    ShowBodyInfo,
    ShowStationInfo,
    ShowSystemNotes,
    ShowColonyShopping,
    RefreshColonyData,
    CollapseColonyData,
    NextWindow,
    StreamOne,
    AdjustVr,
    ResetVr,
    ToggleFirstFootfall,
    Track1,
    Track2,
    Track3,
    Track4,
    Track5,
    Track6,
    Track7,
    Track8,
    QuestShow,
    ToggleImageEmbed,
    ToggleBiologySampleStatusVisibility,
    ToggleSystemBiologyVisibility,
    ToggleBodyInformationVisibility,
    ToggleColonizationCommoditiesVisibility,
    ToggleFlightWarningVisibility,
    ToggleNotificationsVisibility,
    ToggleGroundCombatVisibility,
    ToggleFssBodyFeedVisibility,
    ToggleFssInformationVisibility,
    ToggleGalaxyMapSystemIntelligenceVisibility,
    ToggleSurfaceSurveyVisibility,
    ToggleGuardianSiteVisibility,
    ToggleGuardianStatusVisibility,
    ToggleGuardianSystemVisibility,
    ToggleHumanSettlementVisibility,
    ToggleNextJumpInformationVisibility,
    ToggleFleetCarrierRouteVisibility,
    ToggleRouteBodiesVisibility,
    ToggleMassacreMissionsVisibility,
    ToggleMiniTrackerVisibility,
    ToggleMultiCommanderIndicatorVisibility,
    TogglePriorScansVisibility,
    ToggleJournalActivityVisibility,
    ToggleQuestIndicatorVisibility,
    ToggleRamTahGuidanceVisibility,
    ToggleSphericalSearchVisibility,
    ToggleStationInformationVisibility,
    ToggleSystemStatusVisibility,
    ToggleGroundTargetVisibility,
    ToggleSurfaceMiningVisibility,
    MiningRig1,
    MiningRig2,
    MiningRig3,
    MiningRig4,
    MiningRig5,
    MiningRig6,
}

public sealed record GlobalInputActionDefinition(
    GlobalInputAction Action,
    string LegacyName,
    string DisplayName,
    string Description,
    string DefaultChord,
    string? OverlayPlotterName = null);

public static class GlobalInputActionCatalog
{
    public static IReadOnlyList<GlobalInputActionDefinition> All { get; } =
    [
        Define(GlobalInputAction.ToggleAllVisibility, "toggleAllVisibility", "Toggle overlays", "Hide or show all detached overlays.", "ALT F2"),
        Define(GlobalInputAction.ToggleOverlayInteraction, "toggleOverlayInteraction", "Toggle live overlay interaction", "Switch existing live overlays between passive click-through and clickable drag-to-position mode without opening the full editor.", "ALT SHIFT O"),
        Define(GlobalInputAction.MapZoomIn, "mapZoomIn", "Map zoom in", "Increase the active map scale.", "CTRL +"),
        Define(GlobalInputAction.MapZoomOut, "mapZoomOut", "Map zoom out", "Decrease the active map scale.", "CTRL -"),
        Define(GlobalInputAction.MapZoomAuto, "mapZoomAuto", "Automatic map zoom", "Restore automatic map scaling.", "CTRL SHIFT Backspace"),
        Define(GlobalInputAction.MapBeHuge, "mapBeHuge", "Toggle large map", "Switch the active map between normal and large layouts.", "CTRL Backspace"),
        Define(GlobalInputAction.ShowJumpInfo, "showJumpInfo", "Toggle jump information", "Show or hide route and next-jump information.", "ALT D"),
        Define(GlobalInputAction.CopyNextBoxel, "copyNextBoxel", "Copy next boxel system", "Copy the next boxel-search system while using the Galaxy Map.", "CTRL C"),
        Define(GlobalInputAction.PasteGalMap, "pasteGalMap", "Paste Galaxy Map target", "Enter the current route, boxel, or clipboard target in the Galaxy Map.", string.Empty),
        Define(GlobalInputAction.ShowFssInfo, "showFssInfo", "Toggle FSS information", "Show or hide the FSS information overlay.", "ALT F"),
        Define(GlobalInputAction.ShowBodyInfo, "showBodyInfo", "Toggle body information", "Show or hide information for the current body.", "ALT B"),
        Define(GlobalInputAction.ShowStationInfo, "showStationInfo", "Toggle station information", "Show or hide information for the current station.", "ALT I"),
        Define(GlobalInputAction.ShowSystemNotes, "showSystemNotes", "Show system notes", "Open notes for the current system.", "CTRL SHIFT N"),
        Define(GlobalInputAction.ShowColonyShopping, "showColonyShopping", "Toggle construction shopping", "Show or hide construction commodity requirements.", "ALT S"),
        Define(GlobalInputAction.RefreshColonyData, "refreshColonyData", "Refresh construction data", "Refresh active colonization project data.", "ALT CTRL S"),
        Define(GlobalInputAction.CollapseColonyData, "collapseColonyData", "Collapse construction data", "Collapse or expand construction commodity rows.", "ALT SHIFT S"),
        Define(GlobalInputAction.NextWindow, "nextWindow", "Next Elite window", "Switch overlay tracking to the next Elite process.", "ALT CTRL W"),
        Define(GlobalInputAction.StreamOne, "streamOne", "Toggle stream overlay", "Show or hide the dedicated stream overlay.", "ALT CTRL O"),
        Define(GlobalInputAction.AdjustVr, "adjustVR", "Adjust VR overlay", "Open VR overlay adjustment mode.", "ALT V"),
        Define(GlobalInputAction.ResetVr, "resetVR", "Reset VR orientation", "Reset the captured VR headset orientation.", string.Empty),
        Define(GlobalInputAction.ToggleFirstFootfall, "toggleFF", "Toggle first footfall", "Toggle first-footfall state for the current body.", string.Empty),
        DefineOverlayToggle(GlobalInputAction.ToggleSurfaceMiningVisibility, "toggleSurfaceMiningVisibility", "Surface mining", "PlotSurfaceMining"),
        Define(GlobalInputAction.MiningRig1, "miningRig1", "Mining rig 1", "Set or clear rig 1 at the Rhino deployment position.", "ALT 1"),
        Define(GlobalInputAction.MiningRig2, "miningRig2", "Mining rig 2", "Set or clear rig 2 at the Rhino deployment position.", "ALT 2"),
        Define(GlobalInputAction.MiningRig3, "miningRig3", "Mining rig 3", "Set or clear rig 3 at the Rhino deployment position.", "ALT 3"),
        Define(GlobalInputAction.MiningRig4, "miningRig4", "Mining rig 4", "Set or clear rig 4 at the Rhino deployment position.", "ALT 4"),
        Define(GlobalInputAction.MiningRig5, "miningRig5", "Mining rig 5", "Set or clear rig 5 at the Rhino deployment position.", "ALT 5"),
        Define(GlobalInputAction.MiningRig6, "miningRig6", "Mining rig 6", "Set or clear rig 6 at the Rhino deployment position.", "ALT 6"),
        Define(GlobalInputAction.Track1, "track1", "Track location 1", "Toggle surface bookmark number 1.", "ALT CTRL F1"),
        Define(GlobalInputAction.Track2, "track2", "Track location 2", "Toggle surface bookmark number 2.", "ALT CTRL F2"),
        Define(GlobalInputAction.Track3, "track3", "Track location 3", "Toggle surface bookmark number 3.", "ALT CTRL F3"),
        Define(GlobalInputAction.Track4, "track4", "Track location 4", "Toggle surface bookmark number 4.", "ALT CTRL F4"),
        Define(GlobalInputAction.Track5, "track5", "Track location 5", "Toggle surface bookmark number 5.", "ALT CTRL F5"),
        Define(GlobalInputAction.Track6, "track6", "Track location 6", "Toggle surface bookmark number 6.", "ALT CTRL F6"),
        Define(GlobalInputAction.Track7, "track7", "Track location 7", "Toggle surface bookmark number 7.", "ALT CTRL F7"),
        Define(GlobalInputAction.Track8, "track8", "Track location 8", "Toggle surface bookmark number 8.", "ALT CTRL F8"),
        Define(GlobalInputAction.QuestShow, "questShow", "Toggle quest communications", "Show or hide quest communications.", "ALT Q"),
        Define(GlobalInputAction.ToggleImageEmbed, "toggleImageEmbed", "Toggle screenshot data", "Enable or disable data banners on future screenshots.", "ALT CTRL I"),
        DefineOverlayToggle(GlobalInputAction.ToggleBiologySampleStatusVisibility, "toggleBiologySampleStatusVisibility", "Biology sample status", "PlotBioStatus"),
        DefineOverlayToggle(GlobalInputAction.ToggleSystemBiologyVisibility, "toggleSystemBiologyVisibility", "System biology", "PlotBioSystem"),
        DefineOverlayToggle(GlobalInputAction.ToggleBodyInformationVisibility, "toggleBodyInformationVisibility", "Body information", "PlotBodyInfo"),
        DefineOverlayToggle(GlobalInputAction.ToggleColonizationCommoditiesVisibility, "toggleColonizationCommoditiesVisibility", "Colonization commodities", "PlotBuildCommodities"),
        DefineOverlayToggle(GlobalInputAction.ToggleFlightWarningVisibility, "toggleFlightWarningVisibility", "Flight warning", "PlotFlightWarning"),
        DefineOverlayToggle(GlobalInputAction.ToggleNotificationsVisibility, "toggleNotificationsVisibility", "Notifications", "PlotFloatie"),
        DefineOverlayToggle(GlobalInputAction.ToggleGroundCombatVisibility, "toggleGroundCombatVisibility", "Ground combat", "PlotFootCombat"),
        DefineOverlayToggle(GlobalInputAction.ToggleFssBodyFeedVisibility, "toggleFssBodyFeedVisibility", "FSS body feed", "PlotFSS"),
        DefineOverlayToggle(GlobalInputAction.ToggleFssInformationVisibility, "toggleFssInformationVisibility", "FSS information", "PlotFSSInfo"),
        DefineOverlayToggle(GlobalInputAction.ToggleGalaxyMapSystemIntelligenceVisibility, "toggleGalaxyMapSystemIntelligenceVisibility", "Galaxy Map system intelligence", "PlotGalMap"),
        DefineOverlayToggle(GlobalInputAction.ToggleSurfaceSurveyVisibility, "toggleSurfaceSurveyVisibility", "Surface survey", "PlotGrounded"),
        DefineOverlayToggle(GlobalInputAction.ToggleGuardianSiteVisibility, "toggleGuardianSiteVisibility", "Guardian site", "PlotGuardians"),
        DefineOverlayToggle(GlobalInputAction.ToggleGuardianStatusVisibility, "toggleGuardianStatusVisibility", "Guardian status", "PlotGuardianStatus"),
        DefineOverlayToggle(GlobalInputAction.ToggleGuardianSystemVisibility, "toggleGuardianSystemVisibility", "Guardian system", "PlotGuardianSystem"),
        DefineOverlayToggle(GlobalInputAction.ToggleHumanSettlementVisibility, "toggleHumanSettlementVisibility", "Human settlement", "PlotHumanSite"),
        DefineOverlayToggle(GlobalInputAction.ToggleNextJumpInformationVisibility, "toggleNextJumpInformationVisibility", "Next-jump information", "PlotJumpInfo"),
        DefineOverlayToggle(GlobalInputAction.ToggleFleetCarrierRouteVisibility, "toggleFleetCarrierRouteVisibility", "Fleet carrier route", "PlotFleetCarrierRoute"),
        DefineOverlayToggle(GlobalInputAction.ToggleRouteBodiesVisibility, "toggleRouteBodiesVisibility", "Route bodies", "PlotRouteBio"),
        DefineOverlayToggle(GlobalInputAction.ToggleMassacreMissionsVisibility, "toggleMassacreMissionsVisibility", "Massacre missions", "PlotMassacre"),
        DefineOverlayToggle(GlobalInputAction.ToggleMiniTrackerVisibility, "toggleMiniTrackerVisibility", "Mini tracker", "PlotMiniTrack"),
        DefineOverlayToggle(GlobalInputAction.ToggleMultiCommanderIndicatorVisibility, "toggleMultiCommanderIndicatorVisibility", "Multiple Commander indicator", "PlotMultiGameCommander"),
        DefineOverlayToggle(GlobalInputAction.TogglePriorScansVisibility, "togglePriorScansVisibility", "Prior scans", "PlotPriorScans"),
        DefineOverlayToggle(GlobalInputAction.ToggleJournalActivityVisibility, "toggleJournalActivityVisibility", "Journal activity and SCO status", "PlotPulse"),
        DefineOverlayToggle(GlobalInputAction.ToggleQuestIndicatorVisibility, "toggleQuestIndicatorVisibility", "Quest indicator", "PlotQuestMini"),
        DefineOverlayToggle(GlobalInputAction.ToggleRamTahGuidanceVisibility, "toggleRamTahGuidanceVisibility", "Ram Tah guidance", "PlotRamTah"),
        DefineOverlayToggle(GlobalInputAction.ToggleSphericalSearchVisibility, "toggleSphericalSearchVisibility", "Spherical search", "PlotSphericalSearch"),
        DefineOverlayToggle(GlobalInputAction.ToggleStationInformationVisibility, "toggleStationInformationVisibility", "Station information", "PlotStationInfo"),
        DefineOverlayToggle(GlobalInputAction.ToggleSystemStatusVisibility, "toggleSystemStatusVisibility", "System status", "PlotSysStatus"),
        DefineOverlayToggle(GlobalInputAction.ToggleGroundTargetVisibility, "toggleGroundTargetVisibility", "Ground target", "PlotTrackTarget"),
    ];

    public static GlobalInputActionDefinition Get(GlobalInputAction action)
    {
        return All.Single(definition => definition.Action == action);
    }

    public static bool TryGetByLegacyName(
        string name,
        out GlobalInputActionDefinition? definition)
    {
        definition = All.FirstOrDefault(candidate => string.Equals(
            candidate.LegacyName,
            name,
            StringComparison.OrdinalIgnoreCase));
        return definition is not null;
    }

    public static bool TryGetOverlayPlotterName(
        GlobalInputAction action,
        out string plotterName)
    {
        plotterName = Get(action).OverlayPlotterName ?? string.Empty;
        return plotterName.Length > 0;
    }

    private static GlobalInputActionDefinition Define(
        GlobalInputAction action,
        string legacyName,
        string displayName,
        string description,
        string defaultChord)
    {
        return new GlobalInputActionDefinition(
            action,
            legacyName,
            displayName,
            description,
            defaultChord);
    }

    private static GlobalInputActionDefinition DefineOverlayToggle(
        GlobalInputAction action,
        string legacyName,
        string panelName,
        string plotterName)
    {
        return new GlobalInputActionDefinition(
            action,
            legacyName,
            $"Toggle {panelName} visibility",
            $"When off, the {panelName} panel is rendered inactive and is not visible until toggled on.",
            string.Empty,
            plotterName);
    }
}
