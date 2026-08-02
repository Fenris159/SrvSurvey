using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class OverlayCoverageInventoryTests
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
        Map("PlotBioStatus", ["src/SrvSurvey.Desktop/BiologyStatusOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/BiologyStatusViewModelTests.cs",
        ]),
        Map("PlotBioSystem", ["src/SrvSurvey.Desktop/BiologySurveyOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/SystemSurveyViewModelTests.cs",
        ]),
        Map("PlotBodyInfo", ["src/SrvSurvey.Desktop/BodyInformationOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/SystemSurveyViewModelTests.cs",
        ]),
        Map("PlotFlightWarning", ["src/SrvSurvey.Desktop/FlightWarningOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/SystemSurveyViewModelTests.cs",
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
        Map("PlotGuardians", ["src/SrvSurvey.Desktop/GuardianOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/GuardianOverlayViewModelTests.cs",
            "tests/SrvSurvey.Desktop.Tests/ViewModels/GuardianViewModelTests.cs",
        ]),
        Map("PlotGuardianStatus", ["src/SrvSurvey.Desktop/GuardianOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/GuardianViewModelTests.cs",
        ]),
        Map("PlotGuardianSystem", ["src/SrvSurvey.Desktop/GuardianSystemOverlayWindow.axaml"], [
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
        Map("PlotPriorScans", ["src/SrvSurvey.Desktop/PriorScansOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/PriorScansOverlayViewModelTests.cs",
        ]),
        Map("PlotRamTah", ["src/SrvSurvey.Desktop/RamTahOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/GuardianViewModelTests.cs",
            "tests/SrvSurvey.Desktop.Tests/ViewModels/RamTahViewModelTests.cs",
        ]),
        Map("PlotSphericalSearch", ["src/SrvSurvey.Desktop/SphericalSearchOverlayWindow.axaml"], [
            "tests/SrvSurvey.Desktop.Tests/ViewModels/SphericalSearchOverlayViewModelTests.cs",
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
        Assert.Equal(24, Mappings.Length);
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

    [Theory]
    [InlineData("src/SrvSurvey.Desktop/BiologyStatusOverlayWindow.axaml", "IsAnalyzed")]
    [InlineData("src/SrvSurvey.Desktop/BiologySurveyOverlayWindow.axaml", "IsComplete")]
    [InlineData("src/SrvSurvey.Desktop/FssInfoOverlayWindow.axaml", "AreBiologicalSignalsComplete")]
    [InlineData("src/SrvSurvey.Desktop/FssInfoOverlayWindow.axaml", "AreGeologicalSignalsComplete")]
    [InlineData("src/SrvSurvey.Desktop/MassacreMissionsOverlayWindow.axaml", "IsComplete")]
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
            Native("src/SrvSurvey.Desktop/FssInfoOverlayWindow.axaml")));
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

        Assert.Contains("Edit Overlay Positions", interaction);
        Assert.Contains("OverlayInteraction.ToggleCommand", overlaySettings);
        Assert.Contains("ItemsSource=\"{Binding Categories}\"", editor);
        Assert.Contains("SelectedItem=\"{Binding SelectedCategory, Mode=TwoWay}\"", editor);
        Assert.Contains("Command=\"{Binding SaveCommand}\"", editor);
        Assert.Contains("Content=\"&#x2713;\"", editor);
        Assert.Contains("Command=\"{Binding CancelCommand}\"", editor);
        Assert.Contains("Content=\"&#x00D7;\"", editor);
        Assert.Contains("Text=\"{Binding Title}\"", preview);
        Assert.Contains("ItemsSource=\"{Binding Rows}\"", preview);
        Assert.Contains("Text=\"{Binding CompactText}\"", preview);
        Assert.Contains("Text=\"{Binding Footer}\"", preview);
        Assert.Contains("SizeToContent=\"Height\"", preview);
        Assert.Contains("ItemsSource=\"{Binding RewardBands}\"", preview);
        Assert.Contains("BiologyRewardBandControl", preview);
        Assert.Contains("RouteBioTargetList", preview);
        Assert.Contains("SIMULATED GAME STATE", preview);
        Assert.Contains("BorderThickness=\"2\"", preview);
        Assert.Contains("simulated game data", interaction);
        Assert.Contains("game.IsAvailable", interaction);
        Assert.Contains("? game.ClientBounds", interaction);
        Assert.Contains(": (PixelRect?)null", interaction);
        Assert.Contains("ToggleLiveOverlayInteraction", interaction);
        Assert.Contains("<Expander", overlaySettings);
        Assert.Contains("Classes=\"theme-selector\"", settingsShell);
        Assert.Contains("Text=\"Theme selection\"", settingsShell);
        Assert.Contains("Header=\"Overlay Settings\"", settingsShell);
        Assert.Contains("<views:OverlaySettingsView", settingsShell);
        Assert.Contains("BorderThickness=\"1\"", overlaySettings);
        Assert.Contains(
            "Command=\"{Binding OverlayTheme.PreviewCommand}\"",
            settingsShell);
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
        Assert.Contains("RouteBioTargetList", routeOverlay);
        Assert.Contains("Width=\"220\"", routeOverlay);

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
