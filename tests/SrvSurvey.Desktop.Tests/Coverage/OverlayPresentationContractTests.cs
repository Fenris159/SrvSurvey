using SrvSurvey.Desktop.Platform.Overlay;

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
        Contract("PlotBioStatus", [
            "src/SrvSurvey.Desktop/BiologyStatusOverlayWindow.axaml",
            "src/SrvSurvey.Desktop/BiologyStatusOverlayPresentation.axaml",
        ], [
            "ProgressText", "TrackedCompletionPercent", "ActiveSample", "Signals", "Warning", "Footer",
            "BiologyStatusOverlayPresentation",
        ]),
        Contract("PlotBioSystem", [
            "src/SrvSurvey.Desktop/BiologySurveyOverlayWindow.axaml",
            "src/SrvSurvey.Desktop/BiologySurveyOverlayPresentation.axaml",
        ], [
            "Bodies", "RewardBands", "HasCanonnSignals", "CanonnLogoControl", "Organisms", "RewardSummary", "FirstFootfallRewardSummary",
            "GeologicalSignals", "RadicoidaUnicaCountText",
            "BiologySurveyOverlayPresentation",
        ]),
        Contract("PlotBodyInfo", [
            "src/SrvSurvey.Desktop/BodyInformationOverlayWindow.axaml",
            "src/SrvSurvey.Desktop/BodyInformationOverlayPresentation.axaml",
        ], [
            "BodyClass", "Distance", "ScanValue", "MappedValue", "Temperature", "Gravity", "Pressure",
            "BiologicalSignals", "GeologicalSignals", "Volcanism", "AtmosphereComposition", "Materials", "Rings",
            "BodyInformationOverlayPresentation",
        ]),
        Contract("PlotFlightWarning", ["src/SrvSurvey.Desktop/FlightWarningOverlayWindow.axaml", "src/SrvSurvey.Desktop/FlightWarningOverlayPresentation.axaml"], [
            "FlightWarningText",
        ]),
        Contract("PlotFootCombat", ["src/SrvSurvey.Desktop/FootCombatOverlayWindow.axaml", "src/SrvSurvey.Desktop/FootCombatOverlayPresentation.axaml"], [
            "SettlementName", "FootCombatKills",
        ]),
        Contract("PlotFSS", ["src/SrvSurvey.Desktop/LastFssBodyOverlayWindow.axaml", "src/SrvSurvey.Desktop/LastFssBodyOverlayPresentation.axaml"], [
            "LastFssBodyName", "LastFssBodyDistance", "LastFssScanValue", "LastFssMappedValue",
            "LastFssSignalsText", "LastFssBiologyRewardBands", "LastFssBiologyRewardText", "FssTuningIndicator",
        ]),
        Contract("PlotFSSInfo", ["src/SrvSurvey.Desktop/FssInfoOverlayWindow.axaml", "src/SrvSurvey.Desktop/FssInfoOverlayPresentation.axaml"], [
            "SystemTitle", "ScanSummary", "FssFilterDescription", "FssBodies", "ScanValue", "DssValue",
            "BiologicalSignalsText", "GeologicalSignalsText", "IsSurfaceScanned", "SCANNED",
            "TextDecorations=\"Strikethrough\"",
        ]),
        Contract("PlotGalMap", ["src/SrvSurvey.Desktop/GalaxyMapOverlayWindow.axaml", "src/SrvSurvey.Desktop/GalaxyMapOverlayPresentation.axaml"], [
            "PrimarySystemDisplay.DiscoveryText", "PrimarySystemDisplay.DiscoveredByText",
            "SecondarySystemDisplay.DiscoveryText", "SecondarySystemDisplay.DiscoveredByText",
            "RouteFooter", "Factions", "IsQuestTagged",
        ]),
        Contract("PlotGrounded", ["src/SrvSurvey.Desktop/SurfaceSurveyOverlayWindow.axaml", "src/SrvSurvey.Desktop/SurfaceSurveyOverlayPresentation.axaml"], [
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
        Contract("PlotHumanSite", ["src/SrvSurvey.Desktop/HumanSiteOverlayWindow.axaml", "src/SrvSurvey.Desktop/HumanSiteOverlayPresentation.axaml"], [
            "SiteName", "TemplateText", "FactionText", "DockingStatusText", "ApproachDistanceText",
            "CommanderPositionText", "MapProjection", "QuestMarkers", "QuestRoutes", "IsQuestTagged",
        ]),
        Contract("PlotJumpInfo", ["src/SrvSurvey.Desktop/JumpInfoOverlayWindow.axaml", "src/SrvSurvey.Desktop/JumpInfoOverlayPresentation.axaml"], [
            "TargetName", "StarClass", "JumpProgress", "RouteLegs", "TotalDistance", "DiscoveryText",
            "TrafficText", "PointsOfInterestText", "DetailLines", "IsQuestTagged",
            "HasRouteGuidanceBadges", "HasRefuelGuidance", "HasNeutronGuidance",
            "Assets/Routes/refuel-star.png",
            "Assets/Routes/neutron-star.png",
        ]),
        Contract("PlotFleetCarrierRoute", ["src/SrvSurvey.Desktop/FleetCarrierRouteOverlayWindow.axaml", "src/SrvSurvey.Desktop/FleetCarrierRouteOverlayPresentation.axaml"], [
            "HopProgress", "SystemName", "JumpSummary", "JumpsLeft",
            "FuelLeft", "TritiumInMarket", "JumpFuel", "IcyRingLabel",
            "HasRestockWarning", "RestockAmount", "CountdownTitle",
            "Countdown", "CountdownPhase", "CountdownPhaseTime",
        ]),
        Contract("PlotRouteBio", [
            "src/SrvSurvey.Desktop/RouteBioOverlayWindow.axaml",
            "src/SrvSurvey.Desktop/RouteBioOverlayPresentation.axaml",
            "src/SrvSurvey.Desktop/Controls/RouteBioTargetRow.axaml",
            "src/SrvSurvey.Desktop/Controls/RouteBioTargetList.axaml",
            "src/SrvSurvey.Desktop/ViewModels/RouteWorkspaceViewModel.cs",
        ], [
            "SystemName", "Targets", "IsCompleted", "CompletionLabel", "Species",
            "Subtype", "DistanceToArrival", "EstimatedScanValue", "EstimatedMappingValue",
            "EstimatedBiologyValue", "IsTerraformable", "BodyIconAssetPath",
            "CompactDetailSegments", "InlineSegments", "BundledAssetImageConverter",
        ]),
        Contract("PlotMassacre", ["src/SrvSurvey.Desktop/MassacreMissionsOverlayWindow.axaml", "src/SrvSurvey.Desktop/MassacreMissionsOverlayPresentation.axaml"], [
            "TargetFaction", "MissionGiver", "RemainingText", "TextDecorations=\"Strikethrough\"",
        ]),
        Contract("PlotPriorScans", ["src/SrvSurvey.Desktop/PriorScansOverlayWindow.axaml", "src/SrvSurvey.Desktop/PriorScansOverlayPresentation.axaml"], [
            "DisplayName", "RewardText", "BearingText", "DistanceText", "ApproachText", "Targets", "ShowRadar",
        ]),
        Contract("PlotRamTah", ["src/SrvSurvey.Desktop/RamTahOverlayWindow.axaml", "src/SrvSurvey.Desktop/RamTahOverlayPresentation.axaml"], [
            "CurrentRamTahTitle", "CurrentRamTahLogs", "LogName", "Artifacts", "GuardianArtifactGlyphControl",
            "ObeliskNamesText", "Target obelisk A01: type .to A01 in chat",
            "guardian-panel", "RavenGuardian",
        ]),
        Contract("PlotSphericalSearch", ["src/SrvSurvey.Desktop/SphericalSearchOverlayWindow.axaml", "src/SrvSurvey.Desktop/SphericalSearchOverlayPresentation.axaml"], [
            "SphereCenterSystemName", "SphereDestinationSystemName", "DestinationDistance", "DestinationResult",
            "CurrentBoxelName", "SystemProgress", "BoxelNextSystem", "RouteNextHopName", "NextHopGuidance",
        ]),
        Contract("PlotSysStatus", [
            "src/SrvSurvey.Desktop/SystemStatusOverlayWindow.axaml",
            "src/SrvSurvey.Desktop/SystemStatusOverlayPresentation.axaml",
        ], [
            "SystemStatusText", "DssHeading", "DssBodies", "BiologicalHeading", "BiologicalBodies", "NonBodySignalsText",
            "SystemStatusOverlayPresentation",
        ]),
        Contract("PlotTrackers", [
            "src/SrvSurvey.Desktop/SurfaceSurveyOverlayWindow.axaml",
            "src/SrvSurvey.Desktop/SurfaceSurveyOverlayPresentation.axaml",
            "src/SrvSurvey.Desktop/MiniTrackOverlayWindow.axaml",
            "src/SrvSurvey.Desktop/MiniTrackOverlayPresentation.axaml",
        ], ["TrackerGroups", "QuickTrackerGroups", "BearingText", "DistanceText", "Targets"]),
        Contract("PlotTrackTarget", ["src/SrvSurvey.Desktop/GroundTargetOverlayWindow.axaml", "src/SrvSurvey.Desktop/GroundTargetOverlayPresentation.axaml"], [
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

    [Theory]
    [InlineData("PlotFSSInfo", "FssInfoOverlayPresentation.axaml", "FssInfoOverlayWindow.axaml", 270)]
    [InlineData("PlotFSS", "LastFssBodyOverlayPresentation.axaml", "LastFssBodyOverlayWindow.axaml", 310)]
    [InlineData("PlotBodyInfo", "BodyInformationOverlayPresentation.axaml", "BodyInformationOverlayWindow.axaml", 290)]
    [InlineData("PlotFleetCarrierRoute", "FleetCarrierRouteOverlayPresentation.axaml", "FleetCarrierRouteOverlayWindow.axaml", 320)]
    [InlineData("PlotRouteBio", "RouteBioOverlayPresentation.axaml", "RouteBioOverlayWindow.axaml", 260)]
    public void CompactPresentationsShareOneBoundedWidthWithTheirHosts(
        string plotterName,
        string presentationName,
        string windowName,
        int expectedWidth)
    {
        var root = FindRepositoryRoot();
        var desktop = Path.Combine(root, "src", "SrvSurvey.Desktop");
        var presentation = File.ReadAllText(Path.Combine(
            desktop,
            presentationName));
        var window = File.ReadAllText(Path.Combine(desktop, windowName));

        Assert.Contains($"Width=\"{expectedWidth}\"", presentation);
        Assert.Contains($"MinWidth=\"{expectedWidth}\"", window);
        Assert.Equal(
            expectedWidth,
            OverlayLayoutCatalog.GetRequired(plotterName).PreviewSize.Width);
    }

    [Fact]
    public void CompactOverlayDetailsWrapWithoutSplittingRouteGroups()
    {
        var root = FindRepositoryRoot();
        var desktop = Path.Combine(root, "src", "SrvSurvey.Desktop");
        var fss = File.ReadAllText(Path.Combine(
            desktop,
            "FssInfoOverlayPresentation.axaml"));
        var routeRow = File.ReadAllText(Path.Combine(
            desktop,
            "Controls",
            "RouteBioTargetRow.axaml"));

        Assert.Contains("FssFilterDescription", fss);
        Assert.Contains("TextWrapping=\"Wrap\"", fss);
        Assert.Contains("MaxHeight=\"216\"", fss);
        Assert.Contains("ItemsSource=\"{Binding InlineSegments}\"", routeRow);
        Assert.Contains("<WrapPanel Orientation=\"Horizontal\"", routeRow);
        Assert.DoesNotContain("MaxWidth=\"128\"", routeRow);
    }

    [Fact]
    public void CompactValueCellsAutoSizeAndTypographyUsesSemanticRoles()
    {
        var root = FindRepositoryRoot();
        var desktop = Path.Combine(root, "src", "SrvSurvey.Desktop");
        var lastFss = File.ReadAllText(Path.Combine(
            desktop,
            "LastFssBodyOverlayPresentation.axaml"));
        var bodyInfo = File.ReadAllText(Path.Combine(
            desktop,
            "BodyInformationOverlayPresentation.axaml"));
        var typography = File.ReadAllText(Path.Combine(
            desktop,
            "Styles",
            "OverlayTypographyStyles.axaml"));

        Assert.Contains("ColumnDefinitions=\"Auto,Auto\"", lastFss);
        Assert.Contains("ColumnDefinitions=\"Auto,Auto\"", bodyInfo);
        Assert.Contains("TextBlock.overlay-title", typography);
        Assert.Contains("TextBlock.overlay-value", typography);
        Assert.Contains("TextBlock.overlay-body", typography);
        Assert.Contains("TextBlock.overlay-detail", typography);
        Assert.Contains("TextBlock.overlay-caption", typography);
        Assert.DoesNotContain("TextBlock[FontSize=", typography);
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
