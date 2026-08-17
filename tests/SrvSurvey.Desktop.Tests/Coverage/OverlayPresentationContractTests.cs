using SrvSurvey.Desktop.Platform.Overlay;
using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class OverlayPresentationContractTests
{
    private static readonly IReadOnlyDictionary<string, string> FixedHeaders =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BodyInformationOverlayPresentation.axaml"] = "BODY INFORMATION",
            ["FleetCarrierRouteOverlayPresentation.axaml"] = "FLEET CARRIER ROUTE",
            ["FlightWarningOverlayPresentation.axaml"] = "FLIGHT WARNING",
            ["FssInfoOverlayPresentation.axaml"] = "FSS SURVEY",
            ["GalaxyMapOverlayPresentation.axaml"] = "GALAXY MAP",
            ["GroundTargetOverlayPresentation.axaml"] = "SURFACE TARGET",
            ["HumanSiteOverlayPresentation.axaml"] = "HUMAN SETTLEMENT",
            ["JumpInfoOverlayPresentation.axaml"] = "NEXT JUMP",
            ["LastFssBodyOverlayPresentation.axaml"] = "LAST FSS SCAN",
            ["MassacreMissionsOverlayPresentation.axaml"] = "Massacre missions",
            ["PriorScansOverlayPresentation.axaml"] = "CANONN PRIOR SCANS",
            ["RouteBioOverlayPresentation.axaml"] = "Route bodies",
            ["SphericalSearchOverlayPresentation.axaml"] = "Search guidance",
            ["StationInfoOverlayPresentation.axaml"] = "STATION INFORMATION",
            ["SurfaceSurveyOverlayPresentation.axaml"] = "SURFACE SURVEY",
        };

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
            "ProgressText", "CompletionPercent", "MinWidth=\"0\"", "ClipToBounds=\"True\"", "ActiveSample", "Signals", "Warning", "Footer",
            "BiologyStatusOverlayPresentation",
        ]),
        Contract("PlotBioSystem", [
            "src/SrvSurvey.Desktop/BiologySurveyOverlayWindow.axaml",
            "src/SrvSurvey.Desktop/BiologySurveyOverlayPresentation.axaml",
        ], [
            "Bodies", "BodyIconAssetPath", "BundledAssetImageConverter", "RewardBands", "HasCanonnSignals", "CanonnLogoControl", "OrganismGroups", "Species", "VariantName", "PredictionMarkerToolTip", "RewardSummary", "FirstFootfallRewardSummary",
            "GeologicalSignals", "RadicoidaUnicaCountText",
            "BiologySurveyOverlayPresentation", "RavenBioConfirmedBrush",
            "RavenBioConfirmedDimBrush", "RavenBioPotentialBrush",
            "RavenBioConfirmedDimPotentialBrush",
            "RavenBioPredictionBrush", "RavenBioPredictionPotentialBrush",
            "RavenBioUnknownGlyphBrush", "RavenBioEmptyBrush",
            "RavenBioConfirmedEdgeBrush", "RavenBioPredictionEdgeBrush",
            "RavenBioGoldEdgeBrush", "RavenBioGalacticRegionEdgeBrush",
            "RavenBioGoldPotentialBrush", "RavenBioGoldDimPotentialBrush",
            "RavenBioConfirmedSegmentEdgeBrush",
            "RavenBioPredictionSegmentEdgeBrush",
            "RavenBioGoldSegmentEdgeBrush",
            "RavenBioGalacticRegionSegmentEdgeBrush",
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
            "FlightWarningText", "FlightWarningBrush", "FlightWarningDimBrush", "FlightWarningNote", "IsExtremeFlightWarning", "MinHeight=\"1\"", "SizeToContent=\"WidthAndHeight\"",
        ]),
        Contract("PlotFootCombat", ["src/SrvSurvey.Desktop/FootCombatOverlayWindow.axaml", "src/SrvSurvey.Desktop/FootCombatOverlayPresentation.axaml"], [
            "SettlementName", "FootCombatKills",
        ]),
        Contract("PlotFSS", ["src/SrvSurvey.Desktop/LastFssBodyOverlayWindow.axaml", "src/SrvSurvey.Desktop/LastFssBodyOverlayPresentation.axaml"], [
            "LastFssBodyName", "LastFssBodyDistance", "LastFssScanValue", "LastFssMappedValue",
            "LastFssSignalsText", "LastFssBiologyRewardBands", "LastFssBiologyRewardText", "FssTuningIndicator",
            "RavenBioConfirmedBrush", "RavenBioPredictionBrush",
            "RavenBioConfirmedDimPotentialBrush",
            "RavenBioUnknownGlyphBrush", "RavenBioEmptyBrush",
            "RavenBioConfirmedEdgeBrush", "RavenBioPredictionEdgeBrush",
            "RavenBioConfirmedSegmentEdgeBrush",
            "RavenBioPredictionSegmentEdgeBrush",
        ]),
        Contract("PlotFSSInfo", ["src/SrvSurvey.Desktop/FssInfoOverlayWindow.axaml", "src/SrvSurvey.Desktop/FssInfoOverlayPresentation.axaml"], [
            "SystemTitle", "ScanSummary", "FssFilterDescription", "FssBodies", "FssBodyListMaxHeight", "IsLandable",
            "ScanValue", "DssValue", "ShowSeparatorBeforeGeologicalSignals", "ShowSeparatorBeforeBiologicalSignals",
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
            "TargetName", "StarClass", "IsScoopableStarClass", "SCOOPABLE", "JumpProgress", "RouteLegs", "TotalDistance", "DiscoveryText",
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
            "CurrentBoxelName", "SystemProgress", "BoxelNextSystem", "BoxelClipboardStatus", "RouteNextHopName", "NextHopGuidance",
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
    public void DirectionalTrackersUseSharedVectorChevronsInsteadOfFontGlyphs()
    {
        var root = FindRepositoryRoot();
        var miniTrack = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "MiniTrackOverlayPresentation.axaml"));
        var surfaceSurvey = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "SurfaceSurveyOverlayPresentation.axaml"));
        var priorScans = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "PriorScansOverlayPresentation.axaml"));

        Assert.Contains("DirectionalChevronControl", miniTrack);
        Assert.Contains("IsFar=\"{Binding IsFarTarget}\"", miniTrack);
        Assert.DoesNotContain("&#x25B2;", miniTrack);

        Assert.Equal(
            2,
            surfaceSurvey.Split(
                "DirectionalChevronControl",
                StringSplitOptions.None).Length - 1);
        Assert.Contains("IsFar=\"{Binding IsFarTarget}\"", surfaceSurvey);
        Assert.DoesNotContain("&#x25B2;", surfaceSurvey);

        Assert.Contains("DirectionalChevronControl", priorScans);
        Assert.Contains("IsFar=\"{Binding IsFar}\"", priorScans);
        Assert.DoesNotContain("&#x25B2;", priorScans);
    }

    [Fact]
    public void GroundTargetUsesTheSharedRingedPointerDrawing()
    {
        var root = FindRepositoryRoot();
        var guidance = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "Controls",
            "GroundTargetGuidanceControl.cs"));
        var guidePreview = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "Controls",
            "GuideIconPreviewControl.cs"));

        Assert.Contains("RingedPointerDrawing.Draw", guidance);
        Assert.DoesNotContain("DrawVehicle", guidance);
        Assert.Contains("RingedPointerDrawing.Draw", guidePreview);
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
    [InlineData("PlotFSSInfo", "FssInfoOverlayPresentation.axaml", "FssInfoOverlayWindow.axaml", 270, true)]
    [InlineData("PlotFSS", "LastFssBodyOverlayPresentation.axaml", "LastFssBodyOverlayWindow.axaml", 240, true)]
    [InlineData("PlotBodyInfo", "BodyInformationOverlayPresentation.axaml", "BodyInformationOverlayWindow.axaml", 260, true)]
    [InlineData("PlotFleetCarrierRoute", "FleetCarrierRouteOverlayPresentation.axaml", "FleetCarrierRouteOverlayWindow.axaml", 260, true)]
    [InlineData("PlotGalMap", "GalaxyMapOverlayPresentation.axaml", "GalaxyMapOverlayWindow.axaml", 240, true)]
    [InlineData("PlotGuardianStatus", "GuardianStatusOverlayPresentation.axaml", "GuardianStatusOverlayWindow.axaml", 260, true)]
    [InlineData("PlotGuardianSystem", "GuardianSystemOverlayPresentation.axaml", "GuardianSystemOverlayWindow.axaml", 190, true)]
    [InlineData("PlotHumanSite", "HumanSiteOverlayPresentation.axaml", "HumanSiteOverlayWindow.axaml", 260, true)]
    [InlineData("PlotQuestMini", "QuestIndicatorOverlayPresentation.axaml", "QuestIndicatorOverlayWindow.axaml", 220, true)]
    [InlineData("PlotRamTah", "RamTahOverlayPresentation.axaml", "RamTahOverlayWindow.axaml", 190, true)]
    [InlineData("PlotStationInfo", "StationInfoOverlayPresentation.axaml", "StationInfoOverlayWindow.axaml", 220, true)]
    [InlineData("PlotRouteBio", "RouteBioOverlayPresentation.axaml", "RouteBioOverlayWindow.axaml", 260, false)]
    public void CompactPresentationsShareOneBoundedWidthWithTheirHosts(
        string plotterName,
        string presentationName,
        string windowName,
        int expectedWidth,
        bool isContentSized)
    {
        var root = FindRepositoryRoot();
        var desktop = Path.Combine(root, "src", "SrvSurvey.Desktop");
        var presentation = File.ReadAllText(Path.Combine(
            desktop,
            presentationName));
        var window = File.ReadAllText(Path.Combine(desktop, windowName));

        if (isContentSized)
        {
            Assert.Contains($"MaxWidth=\"{expectedWidth}\"", presentation);
            Assert.Contains("HorizontalAlignment=\"Left\"", presentation);
            Assert.Contains("MinWidth=\"1\"", window);
            Assert.Contains($"MaxWidth=\"{expectedWidth}\"", window);
        }
        else
        {
            Assert.Contains($"Width=\"{expectedWidth}\"", presentation);
            Assert.Contains($"MinWidth=\"{expectedWidth}\"", window);
        }
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
        Assert.Contains(
            "MaxHeight=\"{Binding Survey.FssBodyListMaxHeight}\"",
            fss);
        Assert.Contains("Padding=\"3\"", fss);
        Assert.Contains("RowSpacing=\"0\"", fss);
        Assert.Equal(
            2,
            fss.Split("Classes=\"overlay-divider\"", StringSplitOptions.None).Length - 1);
        var lowerDivider = XDocument.Parse(fss)
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Border"
                && element.Attribute("Classes")?.Value == "overlay-divider")
            .Last();
        Assert.Equal("StackPanel", lowerDivider.Parent?.Name.LocalName);
        Assert.Equal("0", lowerDivider.Parent?.Attribute("Spacing")?.Value);
        Assert.Equal(
            "ScrollViewer",
            lowerDivider.Parent?.Elements().First().Name.LocalName);
        Assert.DoesNotContain("<Border Height=\"48\"", fss);
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

    [Fact]
    public void LastFssBiologyPipsUseTheirStateFramesWithoutAGroupBorder()
    {
        var root = FindRepositoryRoot();
        var lastFss = XDocument.Load(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "LastFssBodyOverlayPresentation.axaml"));
        var rewardBands = lastFss
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ItemsControl"
                && element.Attribute("ItemsSource")?.Value
                    == "{Binding Survey.LastFssBiologyRewardBands}");
        var rewardBand = rewardBands
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "BiologyRewardBandControl");

        Assert.Equal("Grid", rewardBands.Parent?.Name.LocalName);
        Assert.Equal("{Binding IsPrediction}",
            rewardBand.Attribute("IsPrediction")?.Value);
        Assert.Equal("{DynamicResource RavenBioPredictionEdgeBrush}",
            rewardBand.Attribute("PredictionEdgeBrush")?.Value);
    }

    [Fact]
    public void BodyInformationBindingsUseTheNonNullDisplayProjection()
    {
        var root = FindRepositoryRoot();
        var bodyInfo = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "BodyInformationOverlayPresentation.axaml"));

        Assert.Contains("Survey.BodyInformationDisplay.", bodyInfo);
        Assert.DoesNotContain("Survey.BodyInformation.", bodyInfo);
    }

    [Fact]
    public void BodyInformationHeaderAndCompositionRowsRemainCompact()
    {
        var root = FindRepositoryRoot();
        var bodyInfo = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "BodyInformationOverlayPresentation.axaml"));

        Assert.Contains("Classes=\"badge overlay-state-pill\"", bodyInfo);
        Assert.Contains("Padding=\"7,1\"", bodyInfo);
        Assert.True(
            bodyInfo.IndexOf("BodyInformationDisplay.BodyClass", StringComparison.Ordinal) <
            bodyInfo.IndexOf("BodyInformationDisplay.Distance", StringComparison.Ordinal));
        Assert.Equal(
            2,
            bodyInfo.Split("MaxWidth=\"210\"", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void BiologyStatusBindingsUseTheNonNullActiveSampleProjection()
    {
        var root = FindRepositoryRoot();
        var biologyStatus = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "BiologyStatusOverlayPresentation.axaml"));

        Assert.Contains("ActiveSampleDisplay.", biologyStatus);
        Assert.DoesNotContain("ActiveSample.", biologyStatus);
    }

    [Fact]
    public void RequestedCompactRowsDoNotUsePanelFillingValueColumns()
    {
        var root = FindRepositoryRoot();
        var desktop = Path.Combine(root, "src", "SrvSurvey.Desktop");
        var bodyInfo = File.ReadAllText(Path.Combine(
            desktop,
            "BodyInformationOverlayPresentation.axaml"));
        var lastFss = File.ReadAllText(Path.Combine(
            desktop,
            "LastFssBodyOverlayPresentation.axaml"));
        var quest = File.ReadAllText(Path.Combine(
            desktop,
            "QuestIndicatorOverlayPresentation.axaml"));
        var station = File.ReadAllText(Path.Combine(
            desktop,
            "StationInfoOverlayPresentation.axaml"));
        var carrier = File.ReadAllText(Path.Combine(
            desktop,
            "FleetCarrierRouteOverlayPresentation.axaml"));
        var humanSite = File.ReadAllText(Path.Combine(
            desktop,
            "HumanSiteOverlayPresentation.axaml"));

        Assert.Contains("ColumnDefinitions=\"Auto,Auto,Auto\"", bodyInfo);
        Assert.DoesNotContain("Width=\"132\"", bodyInfo);
        Assert.Contains("Grid.Row=\"2\"", lastFss);
        Assert.Contains("LastFssBodyDistance", lastFss);
        Assert.Contains("LastFssBiologyRewardText", lastFss);
        Assert.Contains("Grid.Row=\"1\"", quest);
        Assert.Contains("UnreadMessageText", quest);
        Assert.Contains("ColumnDefinitions=\"Auto,Auto\"", station);
        Assert.Contains("HorizontalAlignment=\"Left\"", station);
        Assert.Contains("ColumnDefinitions=\"Auto,Auto\"", carrier);
        Assert.Contains("HorizontalAlignment=\"Left\"", carrier);
        Assert.Contains("<WrapPanel Orientation=\"Horizontal\">", humanSite);
    }

    [Fact]
    public void PriorScanStatePillsShareOneVisualContract()
    {
        var root = FindRepositoryRoot();
        var desktop = Path.Combine(root, "src", "SrvSurvey.Desktop");
        var priorScans = File.ReadAllText(Path.Combine(
            desktop,
            "PriorScansOverlayPresentation.axaml"));
        var ravenStyles = File.ReadAllText(Path.Combine(
            desktop,
            "Styles",
            "RavenStyles.axaml"));

        Assert.Equal(
            3,
            priorScans.Split("Classes=\"badge overlay-state-pill\"").Length - 1);
        Assert.Contains("Border.badge.overlay-state-pill", ravenStyles);
        Assert.Contains("Property=\"Width\" Value=\"66\"", ravenStyles);
        Assert.Contains("Property=\"Padding\" Value=\"8,3\"", ravenStyles);
    }

    [Fact]
    public void SystemBiologyUsesAnAnalyzedPillWithoutMutingVariantColors()
    {
        var root = FindRepositoryRoot();
        var desktop = Path.Combine(root, "src", "SrvSurvey.Desktop");
        var biologySurvey = File.ReadAllText(Path.Combine(
            desktop,
            "BiologySurveyOverlayPresentation.axaml"));
        var ravenStyles = File.ReadAllText(Path.Combine(
            desktop,
            "Styles",
            "RavenStyles.axaml"));

        Assert.Contains(
            "Classes=\"badge overlay-state-pill overlay-state-pill-compact\"",
            biologySurvey);
        Assert.Contains("IsVisible=\"{Binding IsAnalyzed}\"", biologySurvey);
        Assert.Contains("HorizontalAlignment=\"Left\"", biologySurvey);
        Assert.DoesNotContain("Opacity=\"{Binding RowOpacity}\"", biologySurvey);
        Assert.Contains(
            "Border.badge.overlay-state-pill.overlay-state-pill-compact",
            ravenStyles);
        Assert.Contains("Property=\"Width\" Value=\"42\"", ravenStyles);
    }

    [Fact]
    public void SystemBiologyUsesSharedBodyPipAndRewardColumns()
    {
        var root = FindRepositoryRoot();
        var desktop = Path.Combine(root, "src", "SrvSurvey.Desktop");
        var biologySurvey = File.ReadAllText(Path.Combine(
            desktop,
            "BiologySurveyOverlayPresentation.axaml"));
        var biologyWindow = File.ReadAllText(Path.Combine(
            desktop,
            "BiologySurveyOverlayWindow.axaml"));

        Assert.Contains("Grid.IsSharedSizeScope=\"True\"", biologySurvey);
        Assert.Contains("SharedSizeGroup=\"SystemBiologyBodyName\"", biologySurvey);
        Assert.Contains("SharedSizeGroup=\"SystemBiologyPips\"", biologySurvey);
        Assert.Contains("SharedSizeGroup=\"SystemBiologyReward\"", biologySurvey);
        Assert.Contains("<ItemsControl HorizontalAlignment=\"Left\"", biologySurvey);
        Assert.Contains("<controls:BiologyRewardBandGroupControl", biologySurvey);
        Assert.Contains("ItemsSource=\"{Binding SignalRewardBands}\"", biologySurvey);
        Assert.Contains("ItemsSource=\"{Binding AlternativeRewardBands}\"", biologySurvey);
        Assert.Contains(
            "Classes.highlight=\"{Binding IsRewardBandGroupHighlighted}\"",
            biologySurvey);
        Assert.Contains("Grid.Column=\"3\"", biologySurvey);
        Assert.Contains("TextAlignment=\"Left\"", biologySurvey);
        Assert.DoesNotContain("MaxWidth=\"76\"", biologySurvey);
        Assert.Contains("Padding=\"4\"", biologySurvey);
        Assert.Contains("MinWidth=\"1\"", biologyWindow);
        Assert.Contains("SizeToContent=\"WidthAndHeight\"", biologyWindow);
    }

    [Fact]
    public void OverlaySettingsDisableInactiveDssAndCanonnDependencies()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "Views",
            "OverlaySettingsView.axaml"));
        var controls = document.Descendants().ToArray();
        XElement BoundControl(string name, string property, string binding) =>
            controls.Single(element =>
                element.Name.LocalName == name
                && element.Attribute(property)?.Value.Contains(
                    binding,
                    StringComparison.Ordinal) == true);

        var distance = BoundControl(
            "NumericUpDown",
            "Value",
            "SystemSurvey.DssDistanceLimitLs");
        Assert.Equal(
            "{Binding SystemSurvey.SkipDistantDssCandidates}",
            distance.Parent?.Attribute("IsEnabled")?.Value);

        var priorScans = BoundControl(
            "CheckBox",
            "IsChecked",
            "SystemSurvey.AutoShowPriorScans");
        Assert.Equal(
            "{Binding SystemSurvey.UseExternalData}",
            priorScans.Parent?.Attribute("IsEnabled")?.Value);

        var radar = BoundControl(
            "CheckBox",
            "IsChecked",
            "SystemSurvey.ShowCanonnSignalsOnRadar");
        Assert.Equal(
            "{Binding SystemSurvey.AutoShowPriorScans}",
            radar.Parent?.Attribute("IsEnabled")?.Value);
        Assert.Equal(
            "{Binding SystemSurvey.UseExternalData}",
            radar.Parent?.Parent?.Attribute("IsEnabled")?.Value);

        var miniTrack = BoundControl(
            "CheckBox",
            "IsChecked",
            "SystemSurvey.AutoShowMiniTrack");
        var samplerGate = BoundControl(
            "CheckBox",
            "IsChecked",
            "SystemSurvey.ShowSurfaceRadarOnlyWhenGeneticSamplerDrawn");
        Assert.Same(miniTrack, samplerGate.ElementsBeforeSelf().Last());
        Assert.Equal(
            "{Binding SystemSurvey.AutoShowSurfaceRadar}",
            samplerGate.Attribute("IsEnabled")?.Value);
        var samplerGateLabel = Assert.Single(samplerGate.Elements());
        Assert.Equal("TextBlock", samplerGateLabel.Name.LocalName);
        Assert.Equal("Wrap", samplerGateLabel.Attribute("TextWrapping")?.Value);
        Assert.Equal(
            "Onfoot: Show only when Genetic Sampler is drawn.",
            samplerGateLabel.Attribute("Text")?.Value);
    }

    [Fact]
    public void ExobiologySettingsUseBalancedSeparatedSections()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "Views",
            "OverlaySettingsView.axaml"));
        var controls = document.Descendants().ToArray();
        XElement Named(string name) => controls.Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == name));
        XElement BoundCheckBox(string binding) => controls.Single(element =>
            element.Name.LocalName == "CheckBox"
            && element.Attribute("IsChecked")?.Value.Contains(
                binding,
                StringComparison.Ordinal) == true);

        var canonnGrid = Named("ExobiologyExternalDataGrid");
        var canonnRadar = BoundCheckBox(
            "SystemSurvey.ShowCanonnSignalsOnRadar");
        Assert.Same(canonnGrid.Elements().ElementAt(1), canonnRadar.Parent?.Parent);

        var surfaceRadarSeparator = Named("SurfaceRadarSeparator");
        var surfaceRadarPanel = Named("SurfaceRadarPanel");
        Assert.Same(surfaceRadarSeparator, surfaceRadarPanel.PreviousNode);
        Assert.Contains(
            BoundCheckBox("SystemSurvey.AutoShowSurfaceRadar"),
            surfaceRadarPanel.Descendants());
        Assert.Equal(
            2,
            surfaceRadarPanel.Elements()
                .Single(element => element.Name.LocalName == "Grid")
                .Elements()
                .Count());

        var rewardSeparator = Named("BiologyRewardSeparator");
        var rewardPanel = Named("BiologyRewardPanel");
        Assert.Same(rewardSeparator, rewardPanel.PreviousNode);
        var rewardHeading = rewardPanel.Elements().First();
        Assert.Equal("eyebrow", rewardHeading.Attribute("Classes")?.Value);
        Assert.Equal(
            "SPECIES REWARD GROUPS",
            rewardHeading.Attribute("Text")?.Value);
    }

    [Fact]
    public void GuardianSettingsUseBalancedWrappingColumns()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "Views",
            "OverlaySettingsView.axaml"));
        var controls = document.Descendants().ToArray();
        XElement Named(string name) => controls.Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == name));

        var primaryColumns = Named("GuardianPrimaryColumns");
        var settingsColumns = Named("GuardianSettingsColumns");
        Assert.Equal("*,*", primaryColumns.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("*,*", settingsColumns.Attribute("ColumnDefinitions")?.Value);

        var leftColumn = Named("GuardianLeftColumn");
        var rightColumn = Named("GuardianRightColumn");
        Assert.Equal(
            ["MAP ZOOM", "ALIGNMENT GUIDES"],
            leftColumn.Descendants()
                .Where(element => element.Attribute("Classes")?.Value == "eyebrow")
                .Select(element => element.Attribute("Text")?.Value));
        Assert.Equal(
            ["OVERLAY SIZE", "RUINS AERIAL ALTITUDES"],
            rightColumn.Descendants()
                .Where(element => element.Attribute("Classes")?.Value == "eyebrow")
                .Select(element => element.Attribute("Text")?.Value));

        var sizeSection = Named("GuardianOverlaySizeSection");
        var sizeSelector = Assert.Single(
            sizeSection.Elements(),
            element => element.Name.LocalName == "ComboBox");
        Assert.Equal(
            "{Binding Guardian.SelectedOverlaySize, Mode=TwoWay}",
            sizeSelector.Attribute("SelectedItem")?.Value);
        Assert.Equal("200", sizeSelector.Attribute("Width")?.Value);

        var guardianCard = Named("GuardianOverlayCard");
        var guardianCheckBoxes = guardianCard.Descendants().Where(element =>
            element.Name.LocalName == "CheckBox");
        Assert.All(guardianCheckBoxes, checkBox =>
        {
            var label = Assert.Single(checkBox.Elements());
            Assert.Equal("TextBlock", label.Name.LocalName);
            Assert.Equal("Wrap", label.Attribute("TextWrapping")?.Value);
        });
    }

    [Fact]
    public void CurrentCommanderValueIsVerticallyAlignedWithItsLabel()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "Views",
            "OverviewView.axaml"));
        var currentCommander = document.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value.Contains(
                "CommanderInstances.CurrentCommander",
                StringComparison.Ordinal) == true);

        Assert.Equal(
            "Center",
            currentCommander.Attribute("VerticalAlignment")?.Value);
        Assert.Equal(
            "Center",
            currentCommander.ElementsBeforeSelf()
                .Single()
                .Attribute("VerticalAlignment")?.Value);
    }

    [Fact]
    public void ExplorationTripMetricsUseLeftAlignedTwentyFortyFortyColumns()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "Views",
            "OverviewView.axaml"));
        var metrics = document.Descendants().Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == "ExplorationTripMetrics"));
        var columns = metrics.Elements()
            .Where(element => element.Name.LocalName == "StackPanel")
            .ToArray();

        Assert.Equal("1*,2*,2*", metrics.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal(3, columns.Length);
        Assert.All(columns, column =>
            Assert.Equal(
                "Stretch",
                column.Attribute("HorizontalAlignment")?.Value));
        Assert.All(
            columns.SelectMany(column => column.Elements()),
            text => Assert.Equal("Left", text.Attribute("TextAlignment")?.Value));
        Assert.All(
            columns.SelectMany(column => column.Elements())
                .Where(text => text.Attribute("Classes")?.Value == "metric"),
            metric => Assert.Equal(
                "CharacterEllipsis",
                metric.Attribute("TextTrimming")?.Value));
    }

    [Fact]
    public void OverlaySettingsPlaceLongNumericEditorsBelowTheirLabels()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "Views",
            "OverlaySettingsView.axaml"));
        var controls = document.Descendants().ToArray();
        XElement NumericEditor(string binding) => controls.Single(element =>
            element.Name.LocalName == "NumericUpDown"
            && element.Attribute("Value")?.Value.Contains(
                binding,
                StringComparison.Ordinal) == true);

        foreach (var binding in new[]
                 {
                     "SystemSurvey.FssBodyValueFloor",
                     "SystemSurvey.DssValueFloor",
                     "SystemSurvey.DssDistanceLimitLs",
                 })
        {
            var editor = NumericEditor(binding);
            Assert.Equal("150", editor.Attribute("Width")?.Value);
            Assert.Equal("Left", editor.Attribute("HorizontalAlignment")?.Value);
            Assert.Equal("StackPanel", editor.Parent?.Name.LocalName);
            Assert.Null(editor.Parent?.Attribute("Orientation"));
            Assert.Equal("TextBlock", editor.PreviousNode is XElement label
                ? label.Name.LocalName
                : null);
        }

        var bodyInformationExtension = NumericEditor(
            "SystemSurvey.BodyInformationPreviewExtensionSeconds");
        Assert.Equal(
            "150",
            bodyInformationExtension.Attribute("Width")?.Value);
        Assert.Equal(
            "Horizontal",
            bodyInformationExtension.Parent?.Attribute("Orientation")?.Value);
        Assert.Equal(
            "24,0,0,0",
            bodyInformationExtension.Parent?.Parent?.Attribute("Margin")?.Value);
        Assert.Equal(
            "Extend Body Information Preview by:",
            bodyInformationExtension.Parent?.Parent?.Elements().First()
                .Attribute("Text")?.Value);

        var extension = NumericEditor(
            "SystemSurvey.BodyPredictionPreviewExtensionSeconds");
        Assert.Equal("150", extension.Attribute("Width")?.Value);
        Assert.Equal("Horizontal", extension.Parent?.Attribute("Orientation")?.Value);
        Assert.Equal("24,0,0,0", extension.Parent?.Parent?.Attribute("Margin")?.Value);
        Assert.Equal(
            "Extend Body Predictions preview by:",
            extension.Parent?.Parent?.Elements().First().Attribute("Text")?.Value);

        var fssBodyCount = NumericEditor(
            "SystemSurvey.FssBodiesBeforeScrolling");
        Assert.Equal("150", fssBodyCount.Attribute("Width")?.Value);
        Assert.Equal("StackPanel", fssBodyCount.Parent?.Name.LocalName);
        Assert.Equal("28,0,0,0", fssBodyCount.Parent?.Attribute("Margin")?.Value);
        Assert.Equal(
            "# of bodies before scrolling:",
            fssBodyCount.Parent?.Elements().First().Attribute("Text")?.Value);

        var skipDistant = controls.Single(element =>
            element.Name.LocalName == "CheckBox"
            && element.Attribute("IsChecked")?.Value.Contains(
                "SystemSurvey.SkipDistantDssCandidates",
                StringComparison.Ordinal) == true);
        var showNonBodySignals = controls.Single(element =>
            element.Name.LocalName == "CheckBox"
            && element.Attribute("IsChecked")?.Value.Contains(
                "SystemSurvey.ShowNonBodySignals",
                StringComparison.Ordinal) == true);
        var distance = NumericEditor("SystemSurvey.DssDistanceLimitLs");
        var minimumValue = NumericEditor("SystemSurvey.DssValueFloor");
        var surveyStatusControls = skipDistant.Parent?.Elements().ToArray();
        Assert.NotNull(surveyStatusControls);
        Assert.True(Array.IndexOf(surveyStatusControls, skipDistant)
            < Array.IndexOf(surveyStatusControls, distance.Parent));
        Assert.True(Array.IndexOf(surveyStatusControls, distance.Parent)
            < Array.IndexOf(surveyStatusControls, minimumValue.Parent));
        Assert.True(Array.IndexOf(
            surveyStatusControls,
            minimumValue.Parent)
            < Array.IndexOf(surveyStatusControls, showNonBodySignals));
    }

    [Fact]
    public void CategoryOverlaySettingsKeepOwnedControlsOutOfCategoryPages()
    {
        var root = FindRepositoryRoot();
        var desktop = Path.Combine(root, "src", "SrvSurvey.Desktop");
        var mainWindow = File.ReadAllText(Path.Combine(
            desktop,
            "MainWindow.axaml"));
        var settings = File.ReadAllText(Path.Combine(
            desktop,
            "Views",
            "OverlaySettingsView.axaml"));
        var categoryWindow = File.ReadAllText(Path.Combine(
            desktop,
            "OverlayCategorySettingsWindow.axaml"));
        var colonization = File.ReadAllText(Path.Combine(
            desktop,
            "Views",
            "ColonizationView.axaml"));

        Assert.Contains("window_multiple_regular", mainWindow);
        Assert.Contains("HasOverlaySettings", mainWindow);
        Assert.Contains("OpenCategoryOverlaySettings_Click", mainWindow);
        Assert.Contains("Margin=\"32,16,32,32\"", categoryWindow);
        Assert.Contains("x:Name=\"ColonizationShoppingCard\"", settings);
        Assert.Contains("Colonization.AutoShowCommodityOverlay", settings);
        Assert.DoesNotContain(
            "Colonization.AutoShowCommodityOverlay",
            colonization);
        Assert.DoesNotContain("Colonisation projects", colonization);
    }

    [Fact]
    public void SharedPresentationsOwnCompactionMapSizingAndDividerBehavior()
    {
        var root = FindRepositoryRoot();
        var desktop = Path.Combine(root, "src", "SrvSurvey.Desktop");
        var notification = File.ReadAllText(Path.Combine(
            desktop,
            "NotificationOverlayPresentation.axaml"));
        var guardianSite = File.ReadAllText(Path.Combine(
            desktop,
            "GuardianSiteOverlayPresentation.axaml"));
        var commodities = File.ReadAllText(Path.Combine(
            desktop,
            "ColonizationCommodityOverlayPresentation.axaml"));
        var ravenStyles = File.ReadAllText(Path.Combine(
            desktop,
            "Styles",
            "RavenStyles.axaml"));
        var guardianStyles = File.ReadAllText(Path.Combine(
            desktop,
            "Styles",
            "GuardianLegacyOverlayStyles.axaml"));
        var pulseWindow = File.ReadAllText(Path.Combine(
            desktop,
            "PulseOverlayWindow.axaml"));
        var pulsePresentation = File.ReadAllText(Path.Combine(
            desktop,
            "PulseOverlayPresentation.axaml"));

        Assert.Contains("RowDefinitions=\"Auto,Auto\"", notification);
        Assert.Contains("Value=\"{Binding ProgressPercent}\"", notification);
        Assert.Contains("Width=\"{Binding Guardian.PreferredOverlayWidth}\"", guardianSite);
        Assert.Contains("Height=\"{Binding Guardian.PreferredOverlayHeight}\"", guardianSite);
        Assert.Contains("Classes.alternate=\"{Binding IsAlternateRow}\"", commodities);
        Assert.Contains("Border.overlay-divider", ravenStyles);
        Assert.Contains("HorizontalAlignment\" Value=\"Stretch\"", ravenStyles);
        Assert.Contains("Border.guardian-header-rule", guardianStyles);
        Assert.Contains("HorizontalAlignment\" Value=\"Stretch\"", guardianStyles);
        Assert.Contains("Width=\"32\"", pulseWindow);
        Assert.Contains("Height=\"32\"", pulseWindow);
        Assert.Contains("Width=\"32\"", pulsePresentation);
        Assert.Contains("Height=\"32\"", pulsePresentation);
    }

    private static PresentationContract Contract(
        string contractName,
        IReadOnlyList<string> productionFiles,
        IReadOnlyList<string> requiredTokens) => new(
            contractName,
            productionFiles,
        requiredTokens);

    [Fact]
    public void FixedNonGuardianHeadersUseTheSharedHeaderRole()
    {
        var desktop = Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop");

        foreach (var expected in FixedHeaders)
        {
            var document = XDocument.Load(Path.Combine(desktop, expected.Key));
            var header = document.Descendants()
                .Single(element =>
                    element.Name.LocalName == "TextBlock"
                    && string.Equals(
                        element.Attribute("Text")?.Value,
                        expected.Value,
                        StringComparison.Ordinal));

            Assert.Equal("overlay-header", header.Attribute("Classes")?.Value);
            Assert.Null(header.Attribute("Foreground"));
            Assert.Null(header.Attribute("FontSize"));
        }
    }

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
