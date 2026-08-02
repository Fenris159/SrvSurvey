using SrvSurvey.Core.Routes;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Presentation;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>
/// A deterministic, editor-only Elite session used to make forced overlay
/// previews representative without reading or mutating commander data.
/// </summary>
internal sealed record OverlayPreviewSimulationState(
    string CommanderName,
    string CurrentSystem,
    string CurrentBody,
    string DestinationSystem,
    string StationName,
    string SettlementName,
    string GuardianSiteName,
    string ColonyProjectName)
{
    public static OverlayPreviewSimulationState Default { get; } = new(
        "CMDR Raven",
        "Synuefe NL-N C23-4",
        "Synuefe NL-N C23-4 B 3",
        "Synuefe EU-Q C21-10",
        "Raven Colonial Port",
        "Mitchell's Claim",
        "Ancient Ruins beta",
        "Raven's Reach");
}

internal static class OverlayPreviewSimulationProjector
{
    public static OverlayPreviewSimulationContent Project(
        OverlayLayoutDefinition definition,
        OverlayPreviewSimulationState state)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(state);

        return definition.Name switch
        {
            "PlotBioStatus" => Content(
                state.CurrentBody,
                "Bacterium sample active",
                "BIO SAMPLE 2 / 3",
                Row("Bacterium Acies", "SAMPLE 2 OF 3", 67),
                Row("Separation", "320 / 500 m", 64),
                Row("Temperature", "178-195 K"),
                Row("Signals", "6 biological")),
            "PlotBioSystem" => Content(
                state.CurrentSystem,
                "Bio signals: 12",
                "Rewards: 42.75 M - 106 M",
                BioRow("A4", "10.89 M - 34.34 M",
                    Known(1_000_000), Known(2_400_000),
                    Known(7_600_000, highlighted: true), Unknown()),
                BioRow("A5", "9.47 M - 31.54 M",
                    Known(2_200_000), Known(5_200_000),
                    Predicted(1_000_000, 9_400_000),
                    Known(13_000_000, highlighted: true)),
                BioRow("BC3", "20.7 M",
                    Known(4_100_000), Known(16_600_000)),
                BioRow("BC4", "1.69 M - 19.01 M",
                    Predicted(1_690_000, 19_010_000),
                    Known(7_600_000))),
            "PlotBodyInfo" => Content(
                state.CurrentBody,
                "High metal content world",
                "MAPPED VALUE 2.84 M CR",
                Row("Distance", "1,842 ls"),
                Row("Gravity", "2.84 g"),
                Row("Temperature", "187 K"),
                Row("Atmosphere", "Thin carbon dioxide"),
                Row("Signals", "6 biological | 2 geological"),
                Row("Materials", "Polonium 0.8% | Tellurium 1.2%")),
            "PlotBuildCommodities" => Content(
                state.ColonyProjectName,
                "Primary port - phase 2",
                "4,820 T REMAINING | FC 1,240 T | SHIP 192 T",
                Row("Steel", "2,450 need | 620 FC | 96 ship", 68),
                Row("Power generators", "840 need | 210 FC | 32 ship", 54),
                Row("Polymers", "610 need | 180 FC | 24 ship", 71),
                Row("Water purifiers", "420 need | 120 FC | 16 ship", 48),
                Row("CMM composites", "300 need | 80 FC | 12 ship", 61),
                Row("Emergency power cells", "200 need | 30 FC | 12 ship", 35)),
            "PlotFlightWarning" => Content(
                state.CurrentBody,
                "HIGH-GRAVITY APPROACH",
                "2.84 G | CHECK DESCENT RATE",
                Row("Warning", "High gravity - 2.84 g", 82, "⚠", OverlayPreviewGlyphTone.Danger)),
            "PlotFloatie" => Content(
                state.CommanderName,
                "Journal notification",
                "FIRST FOOTFALL CONFIRMED",
                Row("Discovery", "First footfall confirmed", glyph: "✓", glyphTone: OverlayPreviewGlyphTone.Success),
                Row("Codex", "Bacterium Acies recorded", glyph: "◆", glyphTone: OverlayPreviewGlyphTone.Information)),
            "PlotFootCombat" => Content(
                state.SettlementName,
                "High-intensity conflict zone",
                "6.42 M CR IN COMBAT BONDS",
                Row("Kills", "22 / 30", 73),
                Row("Side", "Federation"),
                Row("Opposition", "Blue Fortune Corp"),
                Row("Reinforcements", "2 waves remaining", 67)),
            "PlotFSS" => Content(
                $"{state.CurrentSystem} B 3",
                "High metal content world",
                "DSS 2.84 M CR | BIO 6 | GEO 2",
                Row("Scan value", "842,310 cr"),
                Row("Signals", "6 biological | 2 geological")),
            "PlotFSSInfo" => Content(
                state.CurrentSystem,
                "FSS 18 / 24 bodies - 75%",
                "6 BODIES REMAIN | EST. VALUE 18.4 M CR",
                Row("B 1", "Class II gas giant | 126,400 cr"),
                Row("B 2", "Rocky body | 18,220 cr"),
                Row("B 3", "HMC world | BIO 6 | GEO 2", 75, "☀", OverlayPreviewGlyphTone.Gold),
                Row("B 3 a", "Icy body | 12,840 cr"),
                Row("C 1", "Water world | 1.24 M cr", 100, "⚑", OverlayPreviewGlyphTone.Information),
                Row("C 2", "Terraformable HMC | 682,100 cr", 100, "✓", OverlayPreviewGlyphTone.Success)),
            "PlotGalMap" => Content(
                state.DestinationSystem,
                "Route and system intelligence",
                "QUEST DESTINATION | DATA UPDATED 2 M AGO",
                Row("Distance", "42.6 ly"),
                Row("Population", "18.2 million"),
                Row("Security", "Medium"),
                Row("Economy", "Industrial | Refinery"),
                Row("Points of interest", "2 stations | 1 settlement")),
            "PlotGrounded" => Content(
                state.CurrentBody,
                "Surface survey - heading 074 degrees",
                "3 TARGETS | RADAR 1.0 KM",
                Row("Bacterium Acies", "146 m | 068 degrees", 29, "►", OverlayPreviewGlyphTone.Information),
                Row("Tussock Capillum", "412 m | 091 degrees", 82, "⚐", OverlayPreviewGlyphTone.Gold),
                Row("Stratum Tectonicas", "1.24 km | 312 degrees"),
                Row("Ship", "860 m | 184 degrees"),
                Row("History", "12 samples | 4 species")),
            "PlotGuardians" => Content(
                state.GuardianSiteName,
                "Guardian ruins live map",
                "OBELISK B04 | RELIC + CASKET REQUIRED",
                Row("Survey", "18 / 27 points", 67),
                Row("Nearest", "Pylon P3 | 84 m", glyph: "►", glyphTone: OverlayPreviewGlyphTone.Information),
                Row("Obelisk", "B04 | Technology 06", glyph: "◆", glyphTone: OverlayPreviewGlyphTone.Gold),
                Row("Artifacts", "Relic 2 | Casket 1 | Orb 0"),
                Row("Mission", "12 / 28 logs", 43)),
            "PlotGuardianSystem" => Content(
                state.CurrentSystem,
                "Guardian sites in system",
                "3 SITES | 1 ACTIVE DESTINATION",
                Row("B 3 a", "Ancient Ruins beta | 18 / 27", 67),
                Row("B 3 b", "Guardian Structure | unvisited"),
                Row("C 1 a", "Ancient Ruins gamma | complete", 100, "✓", OverlayPreviewGlyphTone.Success)),
            "PlotHumanSite" => Content(
                state.SettlementName,
                "Military settlement - threat 2",
                "QUEST SITE | DOCKING GRANTED | 1.8 KM",
                Row("Faction", "Blue Fortune Corp | Anarchy"),
                Row("Layout", "Military M2 | aligned"),
                Row("Commander", "+42 m east | -18 m north"),
                Row("Landing pad", "Pad 02 | 164 m"),
                Row("Services", "Interstellar Factors")),
            "PlotJumpInfo" => Content(
                state.DestinationSystem,
                "K-class star | scoopable",
                "JUMP 4 / 9 | 138.7 LY REMAINING",
                Row("Destination", state.DestinationSystem, glyph: "►", glyphTone: OverlayPreviewGlyphTone.Primary),
                Row("Star", "K | scoopable", glyph: "☀", glyphTone: OverlayPreviewGlyphTone.Gold),
                Row("Traffic", "42 ships in 24 h")),
            "PlotFleetCarrierRoute" => Content(
                "Col 359 Sector EE-X b16-1",
                "Fleet carrier route",
                "HOP 2 / 46 | JUMP COOLDOWN 4:32",
                Row("Jump", "499.76 LY | 21,502.09 LY remaining"),
                Row("Tritium", "1,000 t fuel | 2,799 t market | 93 t jump"),
                Row("Ring", "Pristine icy ring", glyph: "◆", glyphTone: OverlayPreviewGlyphTone.Information),
                Row("Restock", "3,892 t required", glyph: "⚠", glyphTone: OverlayPreviewGlyphTone.Gold)),
            "PlotRouteBio" => Content(
                state.CurrentSystem,
                "Route body destinations",
                "1 / 5 BODIES COMPLETE",
                RouteBodyRow(new FollowRouteBioTarget(
                    "A 4",
                    4,
                    ["Tussock Stigmasis", "Recepta Conditivus"],
                    IsCompleted: true,
                    Subtype: "Rocky body",
                    DistanceToArrivalLs: 1245,
                    EstimatedBiologyValue: 27428800,
                    IsBiological: true)),
                RouteBodyRow(new FollowRouteBioTarget(
                    "A 5",
                    5,
                    ["Bacterium Acies", "Stratum Tectonicas"],
                    Subtype: "Earth-like world",
                    DistanceToArrivalLs: 2934,
                    EstimatedMappingValue: 625000,
                    EstimatedBiologyValue: 27428800,
                    IsBiological: true)),
                RouteBodyRow(new FollowRouteBioTarget(
                    "B 2",
                    8,
                    ["Cactoida Vermis", "Osseus Discus"],
                    Subtype: "High metal content world",
                    DistanceToArrivalLs: 4512,
                    EstimatedScanValue: 125000,
                    EstimatedBiologyValue: 27428800,
                    IsBiological: true)),
                RouteBodyRow(new FollowRouteBioTarget(
                    "C 1",
                    10,
                    ["Concha Aureolas", "Frutexa Metallicum"],
                    Subtype: "Water world",
                    DistanceToArrivalLs: 6870,
                    EstimatedMappingValue: 550000,
                    EstimatedBiologyValue: 14322000,
                    IsBiological: true)),
                RouteBodyRow(new FollowRouteBioTarget(
                    "C 2",
                    11,
                    ["Fonticulua Campestris"],
                    Subtype: "Icy body",
                    DistanceToArrivalLs: 9140,
                    EstimatedScanValue: 94000,
                    EstimatedBiologyValue: 7630000,
                    IsBiological: true))),
            "PlotMassacre" => Content(
                "Massacre missions",
                "3 active mission stacks",
                "22 / 45 KILLS | 41.8 M CR REWARD",
                Row("Blue Fortune Corp", "12 / 20 | Raven Colonial", 60),
                Row("Silver Legal Group", "8 / 15 | Allied Co-op", 53),
                Row("Crimson Raiders", "2 / 10 | System Authority", 20)),
            "PlotMiniTrack" => Content(
                state.CurrentBody,
                "Nearest surface targets",
                "HEADING 074 DEGREES",
                Row("Bacterium Acies", "146 m"),
                Row("Tussock Capillum", "412 m"),
                Row("Stratum Tectonicas", "1.24 km")),
            "PlotMultiGameCommander" => Content(
                "Multiple Elite clients",
                "2 commanders detected",
                "CTRL+ALT+W: NEXT",
                "2 COMMANDERS | CTRL+ALT+W",
                Row(state.CommanderName, "ACTIVE"),
                Row("CMDR Corvus", "BACKGROUND")),
            "PlotPriorScans" => Content(
                state.CurrentBody,
                "Canonn prior scans - 3 species",
                "LAST SYNC 2 M AGO | RADAR 1.0 KM",
                Row("Bacterium Acies", "7.62 M cr | active", 67),
                Row("Tussock Capillum", "19.01 M cr | analyzed", 100),
                Row("Stratum Tectonicas", "95.19 M cr | 1.24 km", 33),
                Row("Historical samples", "9 locations")),
            "PlotPulse" => Content(
                "Journal activity",
                "SCO cooling down",
                "SCO READY IN 4 S",
                "SCO",
                Row("SCO", "Cooling down", 60)),
            "PlotQuestMini" => Content(
                "Decrypt Guardian logs",
                "Ram Tah research mission",
                "12 / 28 LOGS | 2 LOCATIONS TRACKED",
                Row("Technology logs", "6 / 10", 60),
                Row("Culture logs", "4 / 8", 50),
                Row("Language logs", "2 / 10", 20),
                Row("Next site", "Ancient Ruins beta | 1.8 km", glyph: "◆", glyphTone: OverlayPreviewGlyphTone.Gold)),
            "PlotRamTah" => Content(
                "Decoding the Ancient Ruins",
                state.GuardianSiteName,
                "12 / 28 LOGS COMPLETE | 42.9%",
                Row("Technology 06", "B04 | Relic + Casket"),
                Row("Culture 04", "C02 | Orb + Tablet"),
                Row("Language 02", "A11 | Totem + Urn"),
                Row("Biology 08", "D07 | Relic + Orb")),
            "PlotSphericalSearch" => Content(
                "Galaxy Map search guidance",
                "Spherical search active",
                "BOXEL 42 / 128 | ROUTE HOP 4 / 9",
                Row("From", state.CurrentSystem),
                Row("To", state.DestinationSystem),
                Row("Distance", "42.6 ly | inside 50 ly limit"),
                Row("Boxel", "Eol Prou AA-A h | 42 visited", 33),
                Row("Next", "Eol Prou AA-A h23", glyph: "►", glyphTone: OverlayPreviewGlyphTone.Primary)),
            "PlotStationInfo" => Content(
                state.StationName,
                "Coriolis starport",
                "UPDATED 2 M AGO | QUEST LOCATION",
                Row("Largest pad", "Large"),
                Row("Economy", "High Tech | Industrial"),
                Row("Faction", "Raven Colonial Initiative"),
                Row("Services", "Shipyard | Outfitting | Vista", glyph: "✓", glyphTone: OverlayPreviewGlyphTone.Success),
                Row("Prohibited", "Narcotics | Slaves", glyph: "!", glyphTone: OverlayPreviewGlyphTone.Danger)),
            "PlotSysStatus" => Content(
                state.CurrentSystem,
                "System survey status",
                "FSS 18 / 24 | DSS 7 / 9 | BIO 6",
                "18/24 | BIO 6",
                Row("Survey", "FSS 18/24 | DSS 7/9", 75)),
            "PlotTrackTarget" => Content(
                "Ground target",
                "Biological sample location",
                "146 M | 068 DEGREES",
                Row("Bacterium Acies", "146 m | 068 degrees", 29, "►", OverlayPreviewGlyphTone.Information),
                Row("Latitude", "-18.4216"),
                Row("Longitude", "74.0921")),
            _ => throw new InvalidOperationException(
                $"No simulated preview state is defined for {definition.Name}.")
        };
    }

    private static OverlayPreviewSimulationContent Content(
        string subtitle,
        string context,
        string footer,
        params OverlayPositionPreviewRowViewModel[] rows)
    {
        return Content(subtitle, context, footer, string.Empty, rows);
    }

    private static OverlayPreviewSimulationContent Content(
        string subtitle,
        string context,
        string footer,
        string compactText,
        params OverlayPositionPreviewRowViewModel[] rows)
    {
        return new OverlayPreviewSimulationContent(
            subtitle,
            context,
            footer,
            compactText,
            rows);
    }

    private static OverlayPositionPreviewRowViewModel Row(
        string label,
        string value,
        double? progress = null,
        string glyph = "",
        OverlayPreviewGlyphTone glyphTone = OverlayPreviewGlyphTone.Primary)
    {
        return new OverlayPositionPreviewRowViewModel(
            label,
            value,
            progress,
            glyph,
            glyphTone);
    }

    private static OverlayPositionPreviewRowViewModel BioRow(
        string body,
        string reward,
        params BiologySignalRewardBandViewModel[] bands)
    {
        return new OverlayPositionPreviewRowViewModel(
            body,
            reward,
            Glyph: "☀",
            GlyphTone: OverlayPreviewGlyphTone.Gold,
            RewardBands: bands);
    }

    private static OverlayPositionPreviewRowViewModel RouteBodyRow(
        FollowRouteBioTarget target)
    {
        var item = new RouteBioTargetItemViewModel(0, 0, target);
        return new OverlayPositionPreviewRowViewModel(
            item.BodyName,
            item.CompactDetails,
            ShowCompletionCheckBox: true,
            IsCompleted: item.IsCompleted,
            BodyIconAssetPath: item.BodyIconAssetPath,
            BodyIconAccessibleName: item.BodyIconAccessibleName,
            RouteBody: item);
    }

    private static BiologySignalRewardBandViewModel Known(
        long reward,
        bool highlighted = false,
        bool dimmed = false) =>
        BiologySignalRewardBandViewModel.Known(
            reward,
            highlighted,
            dimmed,
            BiologyRewardThresholds.Default);

    private static BiologySignalRewardBandViewModel Predicted(
        long minimumReward,
        long maximumReward) =>
        BiologySignalRewardBandViewModel.Predicted(
            minimumReward,
            maximumReward,
            false,
            BiologyRewardThresholds.Default);

    private static BiologySignalRewardBandViewModel Unknown() =>
        BiologySignalRewardBandViewModel.Unknown(
            BiologyRewardThresholds.Default);
}

internal sealed record OverlayPreviewSimulationContent(
    string Subtitle,
    string Context,
    string Footer,
    string CompactText,
    IReadOnlyList<OverlayPositionPreviewRowViewModel> Rows);
