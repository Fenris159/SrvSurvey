using System.Text.RegularExpressions;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed partial class OverlayCoverageInventoryTests
{
    private static readonly IReadOnlyDictionary<string, string>
        PreviewProductionWindows = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["PlotBioStatus"] = "BiologyStatusOverlayWindow.axaml",
            ["PlotBioSystem"] = "BiologySurveyOverlayWindow.axaml",
            ["PlotBodyInfo"] = "BodyInformationOverlayWindow.axaml",
            ["PlotBuildCommodities"] = "ColonizationCommodityOverlayWindow.axaml",
            ["PlotFlightWarning"] = "FlightWarningOverlayWindow.axaml",
            ["PlotFloatie"] = "NotificationOverlayWindow.axaml",
            ["PlotFootCombat"] = "FootCombatOverlayWindow.axaml",
            ["PlotFSS"] = "LastFssBodyOverlayWindow.axaml",
            ["PlotFSSInfo"] = "FssInfoOverlayWindow.axaml",
            ["PlotGalMap"] = "GalaxyMapOverlayWindow.axaml",
            ["PlotGrounded"] = "SurfaceSurveyOverlayWindow.axaml",
            ["PlotGuardians"] = "GuardianOverlayWindow.axaml",
            ["PlotGuardianStatus"] = "GuardianStatusOverlayWindow.axaml",
            ["PlotGuardianSystem"] = "GuardianSystemOverlayWindow.axaml",
            ["PlotHumanSite"] = "HumanSiteOverlayWindow.axaml",
            ["PlotJumpInfo"] = "JumpInfoOverlayWindow.axaml",
            ["PlotFleetCarrierRoute"] = "FleetCarrierRouteOverlayWindow.axaml",
            ["PlotRouteBio"] = "RouteBioOverlayWindow.axaml",
            ["PlotMassacre"] = "MassacreMissionsOverlayWindow.axaml",
            ["PlotMiniTrack"] = "MiniTrackOverlayWindow.axaml",
            ["PlotMultiGameCommander"] = "MultiGameCommanderOverlayWindow.axaml",
            ["PlotPriorScans"] = "PriorScansOverlayWindow.axaml",
            ["PlotPulse"] = "PulseOverlayWindow.axaml",
            ["PlotQuestMini"] = "QuestIndicatorOverlayWindow.axaml",
            ["PlotRamTah"] = "RamTahOverlayWindow.axaml",
            ["PlotSphericalSearch"] = "SphericalSearchOverlayWindow.axaml",
            ["PlotStationInfo"] = "StationInfoOverlayWindow.axaml",
            ["PlotSysStatus"] = "SystemStatusOverlayWindow.axaml",
            ["PlotTrackTarget"] = "GroundTargetOverlayWindow.axaml",
        };

    private static readonly OverlayMapping[] Mappings =
    [
        Map("PlotBase", [
            "src/SrvSurvey.Desktop/Platform/Overlay/CombinedOverlayPresentationController.cs",
            "src/SrvSurvey.Desktop/Platform/Overlay/OverlayPresentationMode.cs",
            "src/SrvSurvey.Desktop/Platform/Overlay/OverlayPresentationSession.cs",
            "src/SrvSurvey.Desktop/Platform/Overlay/OverlayPlatformService.cs",
            "src/SrvSurvey.Desktop/Platform/Overlay/OverlayWindowRegistry.cs",
            "src/SrvSurvey.Desktop/Platform/Overlay/X11OverlayWindowManagerPolicy.cs",
        ], [
            "tests/SrvSurvey.Desktop.Tests/Platform/CombinedOverlayProjectionTests.cs",
            "tests/SrvSurvey.Desktop.Tests/Platform/OverlayPlatformCapabilitiesTests.cs",
            "tests/SrvSurvey.Desktop.Tests/Platform/OverlayPresentationModeSelectorTests.cs",
            "tests/SrvSurvey.Desktop.Tests/Platform/OverlayWindowPlacementTests.cs",
            "tests/SrvSurvey.Desktop.Tests/Platform/X11OverlayWindowManagerPolicyTests.cs",
        ]),
        Map("PlotBioStatus", [
            "src/SrvSurvey.Desktop/BiologyStatusOverlayWindow.axaml",
            "src/SrvSurvey.Desktop/BiologyStatusOverlayPresentation.axaml",
        ], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/BiologyStatusViewModelTests.cs",
        ]),
        Map("PlotBioSystem", [
            "src/SrvSurvey.Desktop/BiologySurveyOverlayWindow.axaml",
            "src/SrvSurvey.Desktop/BiologySurveyOverlayPresentation.axaml",
        ], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/SystemSurveyViewModelTests.cs",
        ]),
        Map("PlotBodyInfo", [
            "src/SrvSurvey.Desktop/BodyInformationOverlayWindow.axaml",
            "src/SrvSurvey.Desktop/BodyInformationOverlayPresentation.axaml",
        ], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/SystemSurveyViewModelTests.cs",
        ]),
        Map("PlotBuildCommodities", ["src/SrvSurvey.Desktop/ColonizationCommodityOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/ColonizationCommodityOverlayViewModelTests.cs",
        ]),
        Map("PlotFlightWarning", ["src/SrvSurvey.Desktop/FlightWarningOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/SystemSurveyViewModelTests.cs",
        ]),
        Map("PlotFloatie", ["src/SrvSurvey.Desktop/NotificationOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/NotificationViewModelTests.cs",
        ]),
        Map("PlotFootCombat", ["src/SrvSurvey.Desktop/FootCombatOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/CombatViewModelTests.cs",
        ]),
        Map("PlotFSS", ["src/SrvSurvey.Desktop/LastFssBodyOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/SystemSurveyViewModelTests.cs",
        ]),
        Map("PlotFSSInfo", ["src/SrvSurvey.Desktop/FssInfoOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/SystemSurveyViewModelTests.cs",
        ]),
        Map("PlotGalMap", ["src/SrvSurvey.Desktop/GalaxyMapOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/GalaxyMapOverlayViewModelTests.cs",
        ]),
        Map("PlotGrounded", ["src/SrvSurvey.Desktop/SurfaceSurveyOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/SurfaceSurveyOverlayViewModelTests.cs",
            "tests/SrvSurvey.Desktop.Tests/ViewModels/SurfaceSurveyViewModelTests.cs",
        ]),
        Map("PlotGuardians", ["src/SrvSurvey.Desktop/GuardianOverlayWindow.axaml", "src/SrvSurvey.Desktop/GuardianSiteOverlayPresentation.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/GuardianOverlayViewModelTests.cs",
            "tests/SrvSurvey.Desktop.Tests/ViewModels/GuardianViewModelTests.cs",
        ]),
        Map("PlotGuardianStatus", ["src/SrvSurvey.Desktop/GuardianStatusOverlayWindow.axaml", "src/SrvSurvey.Desktop/GuardianStatusOverlayPresentation.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/GuardianViewModelTests.cs",
        ]),
        Map("PlotGuardianSystem", ["src/SrvSurvey.Desktop/GuardianSystemOverlayWindow.axaml", "src/SrvSurvey.Desktop/GuardianSystemOverlayPresentation.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/GuardianViewModelTests.cs",
        ]),
        Map("PlotHumanSite", ["src/SrvSurvey.Desktop/HumanSiteOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/HumanSiteOverlayViewModelTests.cs",
            "tests/SrvSurvey.Desktop.Tests/ViewModels/HumanSiteViewModelTests.cs",
        ]),
        Map("PlotJumpInfo", ["src/SrvSurvey.Desktop/JumpInfoOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/JumpInfoOverlayViewModelTests.cs",
            "tests/SrvSurvey.Desktop.Tests/ViewModels/JumpInfoViewModelTests.cs",
        ]),
        Map("PlotFleetCarrierRoute", ["src/SrvSurvey.Desktop/FleetCarrierRouteOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/FleetCarrierRouteOverlayViewModelTests.cs",
            "tests/SrvSurvey.Desktop.Tests/ViewModels/FleetCarrierJumpCountdownTrackerTests.cs",
        ]),
        Map("PlotRouteBio", [
            "src/SrvSurvey.Desktop/RouteBioOverlayWindow.axaml",
            "src/SrvSurvey.Desktop/Controls/RouteBioTargetRow.axaml",
            "src/SrvSurvey.Desktop/Controls/RouteBioTargetList.axaml",
        ], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/RouteWorkspaceViewModelTests.cs",
            "tests/SrvSurvey.Desktop.Tests/ViewModels/OverlayPositionPreviewViewModelTests.cs",
        ]),
        Map("PlotMassacre", ["src/SrvSurvey.Desktop/MassacreMissionsOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/CombatViewModelTests.cs",
        ]),
        Map("PlotMiniTrack", ["src/SrvSurvey.Desktop/MiniTrackOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/SurfaceSurveyViewModelTests.cs",
        ]),
        Map("PlotMultiGameCommander", ["src/SrvSurvey.Desktop/MultiGameCommanderOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/CommanderInstancesViewModelTests.cs",
        ]),
        Map("PlotPriorScans", ["src/SrvSurvey.Desktop/PriorScansOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/PriorScansOverlayViewModelTests.cs",
        ]),
        Map("PlotPulse", ["src/SrvSurvey.Desktop/PulseOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/PulseOverlayViewModelTests.cs",
        ]),
        Map("PlotQuestMini", ["src/SrvSurvey.Desktop/QuestIndicatorOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/QuestIndicatorViewModelTests.cs",
        ]),
        Map("PlotRamTah", ["src/SrvSurvey.Desktop/RamTahOverlayWindow.axaml", "src/SrvSurvey.Desktop/RamTahOverlayPresentation.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/GuardianViewModelTests.cs",
            "tests/SrvSurvey.Desktop.Tests/ViewModels/RamTahViewModelTests.cs",
        ]),
        Map("PlotSphericalSearch", ["src/SrvSurvey.Desktop/SphericalSearchOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/SphericalSearchOverlayViewModelTests.cs",
        ]),
        Map("PlotStationInfo", ["src/SrvSurvey.Desktop/StationInfoOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/StationInfoViewModelTests.cs",
        ]),
        Map("PlotSysStatus", ["src/SrvSurvey.Desktop/SystemStatusOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/SystemSurveyViewModelTests.cs",
        ]),
        Map("PlotTrackers", [
            "src/SrvSurvey.Desktop/SurfaceSurveyOverlayWindow.axaml",
            "src/SrvSurvey.Desktop/MiniTrackOverlayWindow.axaml",
        ], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/SurfaceSurveyViewModelTests.cs",
        ]),
        Map("PlotTrackTarget", ["src/SrvSurvey.Desktop/GroundTargetOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/GroundTargetOverlayViewModelTests.cs",
            "tests/SrvSurvey.Desktop.Tests/ViewModels/GroundTargetViewModelTests.cs",
        ]),
    ];

    [Fact]
    public void InventoryContainsEverySupportedOverlayExactlyOnce()
    {
        Assert.Equal(31, Mappings.Length);
        Assert.Equal(
            Mappings.Length,
            Mappings.Select(mapping => mapping.ContractName).Distinct().Count());
    }

    [Fact]
    public void EverySupportedOverlayHasProductionAndAssertionEvidence()
    {
        var root = FindRepositoryRoot();
        foreach (var mapping in Mappings)
        {
            Assert.NotEmpty(mapping.ProductionFiles);
            Assert.NotEmpty(mapping.TestFiles);
            foreach (var path in mapping.ProductionFiles)
            {
                Assert.True(
                    File.Exists(Path.Combine(root, Native(path))),
                    $"Missing {mapping.ContractName} production evidence: {path}");
            }

            foreach (var path in mapping.TestFiles)
            {
                var absolutePath = Path.Combine(root, Native(path));
                Assert.True(
                    File.Exists(absolutePath),
                    $"Missing {mapping.ContractName} test evidence: {path}");
                Assert.Contains("Assert.", File.ReadAllText(absolutePath));
            }
        }
    }

    [Fact]
    public void EveryForcedPreviewMapsToAnExistingProductionWindow()
    {
        var root = FindRepositoryRoot();
        Assert.Equal(
            OverlayLayoutCatalog.Supported
                .Select(definition => definition.Name)
                .Order(StringComparer.Ordinal),
            PreviewProductionWindows.Keys.Order(StringComparer.Ordinal));
        foreach (var productionWindow in PreviewProductionWindows.Values)
        {
            Assert.True(
                File.Exists(Path.Combine(
                    root,
                    "src",
                    "SrvSurvey.Desktop",
                    productionWindow)),
                $"Missing production overlay for preview: {productionWindow}");
        }
    }

    [Fact]
    public void FixedRuntimeWidthsMatchTheirEditorPresentationWidths()
    {
        var root = FindRepositoryRoot();
        foreach (var pair in PreviewProductionWindows)
        {
            // Human-site dimensions are commander settings in the legacy app.
            if (pair.Key == "PlotHumanSite")
            {
                continue;
            }

            var markup = File.ReadAllText(Path.Combine(
                root,
                "src",
                "SrvSurvey.Desktop",
                pair.Value));
            // Content-driven WidthAndHeight hosts may set only a soft MinWidth
            // that is lower than the catalog anchor; only pin-check fixed hosts.
            if (markup.Contains(
                    "SizeToContent=\"WidthAndHeight\"",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var match = WindowWidthRegex().Match(markup);
            if (!match.Success)
            {
                continue;
            }

            var expected = OverlayLayoutCatalog.GetRequired(
                pair.Key).PreviewSize.Width;
            Assert.Equal(
                expected,
                int.Parse(
                    match.Groups["width"].Value,
                    System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [GeneratedRegex("""
        <Window[\s\S]*?\bWidth="(?<width>\d+)"
        """)]
    private static partial Regex WindowWidthRegex();

    [Fact]
    public void CommodityOverlayUsesLegacyContentDrivenHeight()
    {
        var root = FindRepositoryRoot();
        var markup = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            PreviewProductionWindows["PlotBuildCommodities"]));

        Assert.Contains("MinHeight=\"1\"", markup);
        Assert.Contains("MaxHeight=\"480\"", markup);
        Assert.Contains("SizeToContent=\"WidthAndHeight\"", markup);
    }

    [Fact]
    public void EveryIndividualRuntimeOverlayWindowIsAvailableInTheEditor()
    {
        var root = FindRepositoryRoot();
        var overlayDirectory = Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop");
        var containerWindows = new HashSet<string>(StringComparer.Ordinal)
        {
            "CombinedOverlayWindow.axaml",
            "GuardianZoomOverlayWindow.axaml",
            "StreamOverlayWindow.axaml",
        };
        var runtimePanels = Directory.GetFiles(
                overlayDirectory,
                "*OverlayWindow.axaml")
            .Select(Path.GetFileName)
            .Where(name => name is not null && !containerWindows.Contains(name))
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            PreviewProductionWindows.Values.Order(StringComparer.Ordinal),
            runtimePanels);
    }

    [Fact]
    public void EveryRuntimeOverlayUsesTheSharedLegacyPresentationPipeline()
    {
        var root = FindRepositoryRoot();
        var hostedWindowSource = File.ReadAllText(Path.Combine(
            root,
            Native(
                "src/SrvSurvey.Desktop/Platform/Overlay/HostedOverlayWindow.cs")));
        Assert.Contains("OverlayThemeResources.Apply(", hostedWindowSource);
        var coordinatorFiles = Directory.GetFiles(
                Path.Combine(
                    root,
                    Native("src/SrvSurvey.Desktop/Platform/Overlay")),
                "*Coordinator.cs")
            .Select(path => new
            {
                Path = path,
                Source = File.ReadAllText(path),
            })
            .ToArray();
        foreach (var definition in OverlayLayoutCatalog.Supported)
        {
            var owners = coordinatorFiles.Where(file =>
                file.Source.Contains(
                    $"\"{definition.Name}\"",
                    StringComparison.Ordinal));
            Assert.Contains(owners, owner => owner.Source.Contains(
                    "OverlayThemeResources.Apply(",
                    StringComparison.Ordinal)
                || owner.Source.Contains(
                    "PassiveOverlayWindowDefinition(",
                    StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("src/SrvSurvey.Desktop/BiologyStatusOverlayPresentation.axaml", "IsAnalyzed")]
    [InlineData("src/SrvSurvey.Desktop/BiologySurveyOverlayPresentation.axaml", "IsComplete")]
    [InlineData("src/SrvSurvey.Desktop/FssInfoOverlayPresentation.axaml", "AreBiologicalSignalsComplete")]
    [InlineData("src/SrvSurvey.Desktop/FssInfoOverlayPresentation.axaml", "AreGeologicalSignalsComplete")]
    [InlineData("src/SrvSurvey.Desktop/MassacreMissionsOverlayPresentation.axaml", "IsComplete")]
    public void CompletionStatesRemainVisiblyStruckThrough(
        string relativePath,
        string stateBinding)
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, Native(relativePath)));

        Assert.Contains($"IsVisible=\"{{Binding {stateBinding}}}\"", xaml);
        Assert.Contains("TextDecorations=\"Strikethrough\"", xaml);
    }

    [Fact]
    public void FssOverlayDoesNotReplaceBodyRowsWithAnArbitrarySummaryCap()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/FssInfoOverlayPresentation.axaml")));
        var viewModel = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/ViewModels/SystemSurveyViewModel.cs")));

        Assert.Contains("ItemsSource=\"{Binding Survey.FssBodies}\"", xaml);
        Assert.Contains("<ScrollViewer", xaml);
        Assert.DoesNotContain("DisplayedFssBodies", viewModel);
        Assert.DoesNotContain("MaximumDisplayedFssBodies", viewModel);
    }

    [Fact]
    public void PositionEditorUsesCategorizedForcedPreviewsAndExplicitCommitControls()
    {
        var root = FindRepositoryRoot();
        var settingsShell = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/Views/SettingsView.axaml")));
        var overlaySettings = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/Views/OverlaySettingsView.axaml")));
        var editor = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/OverlayPositionEditorWindow.axaml")));
        var preview = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/OverlayPositionPreviewWindow.axaml")));
        var interaction = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/ViewModels/OverlayInteractionViewModel.cs")));
        var editorHost = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/Platform/Overlay/OverlayPositionEditorHost.cs")));
        var themeResources = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/Platform/Overlay/OverlayThemeResources.cs")));

        Assert.Contains("Edit Overlay Positions", interaction);
        Assert.Contains("OverlayInteraction.ToggleCommand", overlaySettings);
        Assert.Contains("ItemsSource=\"{Binding Categories}\"", editor);
        Assert.Contains("SelectedItem=\"{Binding SelectedCategory, Mode=TwoWay}\"", editor);
        Assert.Contains("Command=\"{Binding SnapToCenterCommand}\"", editor);
        Assert.Contains("Content=\"&#x25CE;\"", editor);
        Assert.Contains(
            "Snap every overlay in this category to the center",
            editor);
        Assert.Contains("Command=\"{Binding SaveCommand}\"", editor);
        Assert.Contains("Content=\"&#x2713;\"", editor);
        Assert.Contains("Command=\"{Binding CancelCommand}\"", editor);
        Assert.Contains("Content=\"&#x00D7;\"", editor);
        Assert.True(
            editor.IndexOf(
                "Command=\"{Binding SnapToCenterCommand}\"",
                StringComparison.Ordinal)
            < editor.IndexOf(
                "Command=\"{Binding SaveCommand}\"",
                StringComparison.Ordinal));
        Assert.Contains("Text=\"{Binding Title}\"", preview);
        Assert.Contains("ItemsSource=\"{Binding Rows}\"", preview);
        Assert.Contains("Text=\"{Binding CompactText}\"", preview);
        Assert.Contains("Text=\"{Binding Footer}\"", preview);
        Assert.Contains("SizeToContent=\"WidthAndHeight\"", preview);
        // Shared runtime presentations host the real overlay templates; the
        // generic preview surface remains only as a chrome/fallback host.
        // Editor-only yellow folder tab labels every panel for identification.
        Assert.Contains("x:Name=\"PreviewBody\"", preview);
        Assert.Contains("x:Name=\"EditorFolderTab\"", preview);
        Assert.Contains("x:Name=\"EditorFolderTabLabel\"", preview);
        Assert.Contains("CornerRadius=\"7,7,0,0\"", preview);
        Assert.Contains("SIMULATED GAME STATE", preview);
        Assert.Contains("BorderThickness=\"2\"", preview);
        var runtimeFactory = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/Platform/Overlay/OverlayRuntimePresentationFactory.cs")));
        Assert.Contains("CreatePresentation", runtimeFactory);
        Assert.Contains("CreateEditorDataContext", runtimeFactory);
        Assert.Contains("BiologySurveyOverlayPresentation", runtimeFactory);
        Assert.Contains("RouteBioOverlayPresentation", runtimeFactory);
        var routePresentation = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/RouteBioOverlayPresentation.axaml")));
        Assert.Contains("RouteBioTargetList", routePresentation);
        Assert.Contains("BiologyRewardBandControl", File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/BiologySurveyOverlayPresentation.axaml"))));
        Assert.DoesNotContain("Save all", preview);
        Assert.DoesNotContain("OnSaveRequested", preview);
        Assert.DoesNotContain("OnCancelRequested", preview);
        Assert.DoesNotContain("ContextFlyout", preview);
        Assert.Contains(
            "PointerPressed=\"OnPreviewSurfacePointerPressed\"",
            preview);
        Assert.Contains("IsOverlaySettingsOpen", editor);
        Assert.Contains("UseGlobalOverlayOpacity", editor);
        Assert.Contains("SelectedOverlayOpacityPercent", editor);
        Assert.Contains("UseGlobalOverlayScale", editor);
        Assert.Contains("SelectedOverlayScaleOrdinal", editor);
        Assert.DoesNotContain("VisiblePreviewOverlays", editor);
        Assert.DoesNotContain("SelectedPreviewOverlay", editor);
        Assert.DoesNotContain("Text=\"Overlay panel\"", editor);
        Assert.Contains("toolbar.Activate()", editorHost);
        Assert.Contains("OverlayWindowPlacement.BottomCenter", editorHost);
        Assert.Contains("screen.WorkingArea", editorHost);
        Assert.Contains(
            "ManagedOverlayWindowDragSession.Begin(preview, eventArgs)",
            editorHost);
        Assert.Contains(
            "Right Click Panels to edit individual Opacity/Scale",
            editor);
        Assert.DoesNotContain("BringPreviewToFront", editorHost);
        Assert.DoesNotContain("ClampToHost", editorHost);
        Assert.True(
            editorHost.IndexOf(
                "preview.Show();",
                StringComparison.Ordinal)
            < editorHost.IndexOf(
                "preview.PositionChanged += OnPreviewPositionChanged;",
                StringComparison.Ordinal));
        Assert.Contains("SettingsRequested", editorHost);
        Assert.Contains("ApplySurfaceChrome", themeResources);
        Assert.Contains("ApplyLegacyPresentation", themeResources);
        Assert.Contains("NormalizeLegacyOverlayControl", themeResources);
        Assert.Contains(
            "surface.BorderThickness = new Thickness(isEditorPreview ? 2 : 0)",
            themeResources);
        Assert.Contains("surface.Padding = new Thickness(4)", themeResources);
        Assert.Contains("simulated game data", interaction);
        Assert.Contains("game.IsAvailable", interaction);
        Assert.Contains("? game.ClientBounds", interaction);
        Assert.Contains(": (PixelRect?)null", interaction);
        Assert.Contains("ToggleLiveOverlayInteraction", interaction);
        Assert.Contains(
            "SetRuntimeOverlaysVisibleDuringEditing(true)",
            interaction);
        Assert.Contains("RefreshPreviewPositions(editSession)", interaction);
        Assert.DoesNotContain(
            "Close the categorized overlay position editor before enabling interaction with live overlays.",
            interaction);
        Assert.Contains("<Expander", overlaySettings);
        Assert.Contains("Classes=\"theme-selector\"", settingsShell);
        Assert.Contains("Text=\"Theme selection\"", settingsShell);
        Assert.Contains("Header=\"Overlay Settings\"", settingsShell);
        Assert.Contains("<views:OverlaySettingsView", settingsShell);
        Assert.Contains("BorderThickness=\"1\"", overlaySettings);
        Assert.Contains(
            "Command=\"{Binding OverlayTheme.PreviewCommand}\"",
            settingsShell);
        Assert.Contains(
            "Text=\"Overlay theme presets and saved states\"",
            settingsShell);
        Assert.Contains(
            "ColumnDefinitions=\"64,128,*,118\"",
            settingsShell);
        Assert.Contains(
            "Slider.overlay-theme-opacity /template/ Thumb#thumb",
            settingsShell);
        Assert.Contains("Content=\"Load Defaults\"", settingsShell);
        Assert.Contains("Overlay Opacity Override", overlaySettings);
        Assert.Contains("OverlayLayout.SelectedOverlay", overlaySettings);
        Assert.Contains("OverlayLayout.SaveCommand", overlaySettings);
        Assert.DoesNotContain("HorizontalAnchorOptions", overlaySettings);
        Assert.DoesNotContain("VerticalAnchorOptions", overlaySettings);
        Assert.DoesNotContain("GalaxyMap.AutoShow", settingsShell);
        Assert.Contains("GalaxyMap.AutoShow", overlaySettings);
        Assert.DoesNotContain("Notifications.Enabled", settingsShell);
        Assert.Contains("Notifications.Enabled", overlaySettings);

        var routeOverlay = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/RouteBioOverlayWindow.axaml")));
        var routeOverlayPresentation = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/RouteBioOverlayPresentation.axaml")));
        Assert.Contains("RouteBioOverlayPresentation", routeOverlay);
        Assert.Contains("RouteBioTargetList", routeOverlayPresentation);
        Assert.Contains("Width=\"260\"", routeOverlay);
        Assert.Contains("Text=\"ROUTE BODIES\"", routeOverlayPresentation);
        Assert.Contains("Classes=\"overlay-header\"", routeOverlayPresentation);
        Assert.Contains(
            "Background=\"{DynamicResource RavenHeaderBrush}\"",
            routeOverlayPresentation);
        Assert.DoesNotContain("RavenWarningBrush", routeOverlayPresentation);
        Assert.DoesNotContain(
            "BorderBrush=\"{DynamicResource RavenWarningBrush}\"",
            routeOverlayPresentation);

        var biologyOverlay = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/BiologySurveyOverlayWindow.axaml")));
        var biologyPresentation = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/BiologySurveyOverlayPresentation.axaml")));
        Assert.Contains("Width=\"240\"", biologyOverlay);
        Assert.Contains("BiologySurveyOverlayPresentation", biologyOverlay);
        Assert.Contains(
            "Text=\"{Binding Survey.BiologySurveyDisplay.Title}\"",
            biologyPresentation);
        Assert.Contains("Classes=\"overlay-header\"", biologyPresentation);
        Assert.Contains(
            "Background=\"{DynamicResource RavenHeaderBrush}\"",
            biologyPresentation);
        Assert.DoesNotContain("RavenWarningBrush", biologyPresentation);
        Assert.Contains("Padding=\"4\"", biologyPresentation);
        Assert.Contains("BorderThickness=\"0\"", biologyPresentation);
        Assert.Contains("CornerRadius=\"5\"", biologyPresentation);
        Assert.DoesNotContain("EXOBIOLOGY SURVEY", biologyPresentation);
        Assert.DoesNotContain("RavenSurfaceBrush", biologyPresentation);
        Assert.DoesNotContain("Classes=\"badge\"", biologyPresentation);

        var routeTargetList = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/Controls/RouteBioTargetList.axaml")));
        var routeTargetListCode = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/Controls/RouteBioTargetList.axaml.cs")));
        var routeTargetRow = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/Controls/RouteBioTargetRow.axaml")));
        Assert.Contains("VerticalScrollBarVisibility=\"Hidden\"", routeTargetList);
        Assert.Contains("ScrollIndicator", routeTargetList);
        Assert.Contains("MaxVisibleItemCount = 3", routeTargetListCode);
        Assert.Contains("Classes=\"route-body-check\"", routeTargetRow);
        Assert.Contains("Width=\"12\"", routeTargetRow);
        Assert.Contains("Width=\"22\"", routeTargetRow);
        Assert.Contains("RavenPrimaryBrush", routeTargetRow);
        Assert.Contains("Background=\"Transparent\"", routeTargetRow);
    }

    private static OverlayMapping Map(
        string contractName,
        IReadOnlyList<string> productionFiles,
        IReadOnlyList<string> testFiles)
    {
        return new OverlayMapping(contractName, productionFiles, testFiles);
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

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string Native(string relativePath)
    {
        return relativePath.Replace('/', Path.DirectorySeparatorChar);
    }

    private sealed record OverlayMapping(
        string ContractName,
        IReadOnlyList<string> ProductionFiles,
        IReadOnlyList<string> TestFiles);
}
