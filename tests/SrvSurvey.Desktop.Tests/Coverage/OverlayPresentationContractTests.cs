namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class OverlayPresentationContractTests
{
    private static readonly PresentationContract[] Contracts =
    [
        Contract("PlotBase", [
            "src/SrvSurvey.Desktop/Platform/Overlay/CombinedOverlayPresentationController.cs",
            "src/SrvSurvey.Desktop/Platform/Overlay/OverlayPresentationMode.cs",
            "src/SrvSurvey.Desktop/Platform/Overlay/OverlayPlatformService.cs",
            "src/SrvSurvey.Desktop/Platform/Overlay/OverlayWindowRegistry.cs",
        ], [
            "SupportsClickThrough",
            "SupportsGameWindowTracking",
            "Register",
            "MultipleWindows",
            "CombinedWindow",
            "SetInteractiveRegions",
        ]),
        Contract("PlotBioStatus", ["src/SrvSurvey.Desktop/BiologyStatusOverlayWindow.axaml"], [
            "ProgressText", "TrackedCompletionPercent", "ActiveSample", "Signals", "Warning", "Footer",
        ]),
        Contract("PlotBioSystem", ["src/SrvSurvey.Desktop/BiologySurveyOverlayWindow.axaml"], [
            "Bodies", "RewardBands", "HasCanonnSignals", "CanonnLogoControl", "Organisms", "RewardSummary", "FirstFootfallRewardSummary",
            "GeologicalSignals", "RadicoidaUnicaCountText",
        ]),
        Contract("PlotBodyInfo", ["src/SrvSurvey.Desktop/BodyInformationOverlayWindow.axaml"], [
            "BodyClass", "Distance", "ScanValue", "MappedValue", "Temperature", "Gravity", "Pressure",
            "BiologicalSignals", "GeologicalSignals", "Volcanism", "AtmosphereComposition", "Materials", "Rings",
        ]),
        Contract("PlotFlightWarning", ["src/SrvSurvey.Desktop/FlightWarningOverlayWindow.axaml"], [
            "FlightWarningText",
        ]),
        Contract("PlotFootCombat", ["src/SrvSurvey.Desktop/FootCombatOverlayWindow.axaml"], [
            "SettlementName", "FootCombatKills",
        ]),
        Contract("PlotFSS", ["src/SrvSurvey.Desktop/LastFssBodyOverlayWindow.axaml"], [
            "LastFssBodyName", "LastFssBodyDistance", "LastFssScanValue", "LastFssMappedValue",
            "LastFssSignalsText", "LastFssBiologyRewardBands", "LastFssBiologyRewardText", "FssTuningIndicator",
        ]),
        Contract("PlotFSSInfo", ["src/SrvSurvey.Desktop/FssInfoOverlayWindow.axaml"], [
            "SystemTitle", "ScanSummary", "FssFilterDescription", "FssBodies", "ScanValue", "DssValue",
            "BiologicalSignalsText", "GeologicalSignalsText", "TextDecorations=\"Strikethrough\"",
        ]),
        Contract("PlotGalMap", ["src/SrvSurvey.Desktop/GalaxyMapOverlayWindow.axaml"], [
            "PrimarySystemDisplay.DiscoveryText", "PrimarySystemDisplay.DiscoveredByText",
            "SecondarySystemDisplay.DiscoveryText", "SecondarySystemDisplay.DiscoveredByText",
            "RouteFooter", "Factions", "IsQuestTagged",
        ]),
        Contract("PlotGrounded", ["src/SrvSurvey.Desktop/SurfaceSurveyOverlayWindow.axaml"], [
            "BodyName", "HistoryText", "HeadingText", "RadarScaleText", "NavigationMarkers", "TrackerGroups",
        ]),
        Contract("PlotGuardians", ["src/SrvSurvey.Desktop/GuardianOverlayWindow.axaml", "src/SrvSurvey.Desktop/GuardianSiteOverlayPresentation.axaml"], [
            "ActiveMapSummary", "ActiveMapScaleText", "LiveMapPromptText", "TargetObeliskText",
            "guardian-panel", "ShowLegend=\"False\"", "RavenGuardian",
        ]),
        Contract("PlotGuardianStatus", ["src/SrvSurvey.Desktop/GuardianStatusOverlayWindow.axaml", "src/SrvSurvey.Desktop/GuardianStatusOverlayPresentation.axaml"], [
            "GuardianStatusTitle", "GuardianStatusDetail", "GuardianOriginFooter", "GuardianOnFootFooter",
            "GuardianStatusObeliskTitle", "GuardianStatusObeliskArtifacts",
            "GuardianStatusObeliskMissionStatus", "GuardianChoiceOneText",
            "GlideApproachText", "guardian-panel", "RavenGuardian",
        ]),
        Contract("PlotGuardianSystem", ["src/SrvSurvey.Desktop/GuardianSystemOverlayWindow.axaml", "src/SrvSurvey.Desktop/GuardianSystemOverlayPresentation.axaml"], [
            "CurrentSystemGuardianTitle", "CurrentSystemSites", "LegacyDisplayText", "LegacySurveyLine",
            "LegacyBlueprintLine", "LegacyExtraLine", "guardian-panel", "RavenGuardian",
        ]),
        Contract("PlotHumanSite", ["src/SrvSurvey.Desktop/HumanSiteOverlayWindow.axaml"], [
            "SiteName", "TemplateText", "FactionText", "DockingStatusText", "ApproachDistanceText",
            "CommanderPositionText", "MapProjection", "QuestMarkers", "QuestRoutes", "IsQuestTagged",
        ]),
        Contract("PlotJumpInfo", ["src/SrvSurvey.Desktop/JumpInfoOverlayWindow.axaml"], [
            "TargetName", "StarClass", "JumpProgress", "RouteLegs", "TotalDistance", "DiscoveryText",
            "TrafficText", "PointsOfInterestText", "DetailLines", "IsQuestTagged",
            "HasRouteGuidanceBadges", "HasRefuelGuidance", "HasNeutronGuidance",
            "Assets/Routes/refuel-star.png",
            "Assets/Routes/neutron-star.png",
        ]),
        Contract("PlotFleetCarrierRoute", ["src/SrvSurvey.Desktop/FleetCarrierRouteOverlayWindow.axaml"], [
            "HopProgress", "SystemName", "JumpSummary", "JumpsLeft",
            "FuelLeft", "TritiumInMarket", "JumpFuel", "IcyRingLabel",
            "HasRestockWarning", "RestockAmount", "CountdownTitle",
            "Countdown", "CountdownPhase", "CountdownPhaseTime",
        ]),
        Contract("PlotRouteBio", [
            "src/SrvSurvey.Desktop/RouteBioOverlayWindow.axaml",
            "src/SrvSurvey.Desktop/Controls/RouteBioTargetRow.axaml",
            "src/SrvSurvey.Desktop/Controls/RouteBioTargetList.axaml",
            "src/SrvSurvey.Desktop/ViewModels/RouteWorkspaceViewModel.cs",
        ], [
            "SystemName", "Targets", "IsCompleted", "CompletionLabel", "Species",
            "Subtype", "DistanceToArrival", "EstimatedScanValue", "EstimatedMappingValue",
            "EstimatedBiologyValue", "IsTerraformable", "BodyIconAssetPath",
            "CompactDetailSegments", "InlineSegments", "BundledAssetImageConverter",
        ]),
        Contract("PlotMassacre", ["src/SrvSurvey.Desktop/MassacreMissionsOverlayWindow.axaml"], [
            "TargetFaction", "MissionGiver", "RemainingText", "TextDecorations=\"Strikethrough\"",
        ]),
        Contract("PlotPriorScans", ["src/SrvSurvey.Desktop/PriorScansOverlayWindow.axaml"], [
            "DisplayName", "RewardText", "BearingText", "DistanceText", "ApproachText", "Targets", "ShowRadar",
        ]),
        Contract("PlotRamTah", ["src/SrvSurvey.Desktop/RamTahOverlayWindow.axaml", "src/SrvSurvey.Desktop/RamTahOverlayPresentation.axaml"], [
            "CurrentRamTahTitle", "CurrentRamTahLogs", "LogName", "Artifacts", "GuardianArtifactGlyphControl",
            "ObeliskNamesText", "Target obelisk A01: type .to A01 in chat",
            "guardian-panel", "RavenGuardian",
        ]),
        Contract("PlotSphericalSearch", ["src/SrvSurvey.Desktop/SphericalSearchOverlayWindow.axaml"], [
            "SphereCenterSystemName", "SphereDestinationSystemName", "DestinationDistance", "DestinationResult",
            "CurrentBoxelName", "SystemProgress", "BoxelNextSystem", "RouteNextHopName", "NextHopGuidance",
        ]),
        Contract("PlotSysStatus", ["src/SrvSurvey.Desktop/SystemStatusOverlayWindow.axaml"], [
            "SystemStatusText", "DssHeading", "DssBodies", "BiologicalHeading", "BiologicalBodies", "NonBodySignalsText",
        ]),
        Contract("PlotTrackers", [
            "src/SrvSurvey.Desktop/SurfaceSurveyOverlayWindow.axaml",
            "src/SrvSurvey.Desktop/MiniTrackOverlayWindow.axaml",
        ], ["TrackerGroups", "QuickTrackerGroups", "BearingText", "DistanceText", "Targets"]),
        Contract("PlotTrackTarget", ["src/SrvSurvey.Desktop/GroundTargetOverlayWindow.axaml"], [
            "TargetCoordinates", "TargetBearing", "RelativeHeading", "DistanceToTarget", "DescentAngle", "ApproachStatus",
        ]),
    ];

    [Fact]
    public void EveryOverlayHasItsInformationGroupsInProductionMarkup()
    {
        var root = FindRepositoryRoot();
        Assert.Equal(24, Contracts.Length);
        foreach (var contract in Contracts)
        {
            var production = string.Join(
                Environment.NewLine,
                contract.ProductionFiles.Select(path => File.ReadAllText(
                    Path.Combine(root, Native(path)))));
            foreach (var token in contract.RequiredTokens)
            {
                Assert.Contains(token, production, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void GuardianPresentationsUseDedicatedCompactVisualGrammar()
    {
        var root = FindRepositoryRoot();
        var presentations = new[]
        {
            "GuardianSiteOverlayPresentation.axaml",
            "GuardianStatusOverlayPresentation.axaml",
            "GuardianSystemOverlayPresentation.axaml",
            "RamTahOverlayPresentation.axaml",
        };

        foreach (var presentation in presentations)
        {
            var markup = File.ReadAllText(Path.Combine(
                root,
                "src",
                "SrvSurvey.Desktop",
                presentation));
            Assert.Contains("guardian-panel", markup);
            Assert.Contains("RavenGuardian", markup);
            Assert.DoesNotContain("LegacyOverlayBackgroundControl", markup);
            Assert.DoesNotContain("StripeBrush", markup);
            Assert.Contains("TextWrapping=\"Wrap\"", markup);
            Assert.DoesNotContain("TextTrimming=", markup);
            Assert.DoesNotContain("Classes=\"card", markup);
            Assert.DoesNotContain("Classes=\"badge", markup);
        }

        var styles = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "Styles",
            "GuardianLegacyOverlayStyles.axaml"));
        Assert.Contains("Assets/Fonts/Oxanium#Oxanium", styles);
        Assert.Contains("Assets/Fonts/Rajdhani#Rajdhani", styles);
        Assert.Contains("RavenGuardianHeaderBrush", styles);
        Assert.Contains("RavenGuardianPrimaryBrush", styles);
        Assert.Contains("RavenGuardianSecondaryBrush", styles);
        Assert.Contains("RavenGuardianMutedBrush", styles);
        Assert.Contains("guardian-panel", styles);
        Assert.Contains("guardian-title", styles);

        var ramTah = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "RamTahOverlayPresentation.axaml"));
        Assert.DoesNotContain("&lt;A01&gt;", ramTah);
    }

    private static PresentationContract Contract(
        string contractName,
        IReadOnlyList<string> productionFiles,
        IReadOnlyList<string> requiredTokens) => new(
            contractName,
            productionFiles,
            requiredTokens);

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "SrvSurvey.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root.");
    }

    private static string Native(string relativePath) =>
        relativePath.Replace('/', Path.DirectorySeparatorChar);

    private sealed record PresentationContract(
        string ContractName,
        IReadOnlyList<string> ProductionFiles,
        IReadOnlyList<string> RequiredTokens);
}
