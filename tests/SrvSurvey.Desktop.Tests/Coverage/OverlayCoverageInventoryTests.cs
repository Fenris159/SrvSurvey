using System.Text.RegularExpressions;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed partial class OverlayCoverageInventoryTests
{
    private static readonly IReadOnlyDictionary<string, string> PreviewProductionWindows = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
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
        ["PlotMiningNotifications"] = "MiningActivityOverlayWindow.axaml",
        ["PlotMiningCargo"] = "MiningCargoOverlayWindow.axaml",
        ["PlotMiningReference"] = "MiningReferenceOverlayWindow.axaml",
        ["PlotMiningFiregroups"] = "MiningActivityOverlayWindow.axaml",
        ["PlotMiningWarning"] = "MiningWarningOverlayWindow.axaml",
        ["PlotSurfaceMining"] = "SurfaceMiningOverlayWindow.axaml",
        ["PlotMineMap"] = "MineMapOverlayWindow.axaml",
        ["PlotSurfaceMiningSurvey"] = "SurfaceMiningSurveyOverlayWindow.axaml",
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
        Map(
            "PlotBase",
            [
                "src/SrvSurvey.Desktop/Platform/Overlay/CombinedOverlayPresentationController.cs",
                "src/SrvSurvey.Desktop/Platform/Overlay/OverlayPresentationMode.cs",
                "src/SrvSurvey.Desktop/Platform/Overlay/OverlayPresentationSession.cs",
                "src/SrvSurvey.Desktop/Platform/Overlay/OverlayPlatformService.cs",
                "src/SrvSurvey.Desktop/Platform/Overlay/OverlayWindowRegistry.cs",
                "src/SrvSurvey.Desktop/Platform/Overlay/X11OverlayWindowManagerPolicy.cs",
            ],
            [
                "tests/SrvSurvey.Desktop.Tests/Platform/CombinedOverlayProjectionTests.cs",
                "tests/SrvSurvey.Desktop.Tests/Platform/OverlayPlatformCapabilitiesTests.cs",
                "tests/SrvSurvey.Desktop.Tests/Platform/OverlayPresentationModeSelectorTests.cs",
                "tests/SrvSurvey.Desktop.Tests/Platform/OverlayWindowPlacementTests.cs",
                "tests/SrvSurvey.Desktop.Tests/Platform/X11OverlayWindowManagerPolicyTests.cs",
            ]
        ),
        Map(
            "PlotBioStatus",
            [
                "src/SrvSurvey.Desktop/BiologyStatusOverlayWindow.axaml",
                "src/SrvSurvey.Desktop/BiologyStatusOverlayPresentation.axaml",
            ],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/BiologyStatusViewModelTests.cs"]
        ),
        Map(
            "PlotBioSystem",
            [
                "src/SrvSurvey.Desktop/BiologySurveyOverlayWindow.axaml",
                "src/SrvSurvey.Desktop/BiologySurveyOverlayPresentation.axaml",
            ],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/SystemSurveyViewModelTests.cs"]
        ),
        Map(
            "PlotBodyInfo",
            [
                "src/SrvSurvey.Desktop/BodyInformationOverlayWindow.axaml",
                "src/SrvSurvey.Desktop/BodyInformationOverlayPresentation.axaml",
            ],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/SystemSurveyViewModelTests.cs"]
        ),
        Map(
            "PlotBuildCommodities",
            ["src/SrvSurvey.Desktop/ColonizationCommodityOverlayWindow.axaml"],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/ColonizationCommodityOverlayViewModelTests.cs"]
        ),
        Map(
            "PlotFlightWarning",
            ["src/SrvSurvey.Desktop/FlightWarningOverlayWindow.axaml"],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/SystemSurveyViewModelTests.cs"]
        ),
        Map(
            "PlotFloatie",
            ["src/SrvSurvey.Desktop/NotificationOverlayWindow.axaml"],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/NotificationViewModelTests.cs"]
        ),
        Map(
            "PlotFootCombat",
            ["src/SrvSurvey.Desktop/FootCombatOverlayWindow.axaml"],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/CombatViewModelTests.cs"]
        ),
        Map(
            "PlotFSS",
            ["src/SrvSurvey.Desktop/LastFssBodyOverlayWindow.axaml"],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/SystemSurveyViewModelTests.cs"]
        ),
        Map(
            "PlotFSSInfo",
            ["src/SrvSurvey.Desktop/FssInfoOverlayWindow.axaml"],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/SystemSurveyViewModelTests.cs"]
        ),
        Map(
            "PlotGalMap",
            ["src/SrvSurvey.Desktop/GalaxyMapOverlayWindow.axaml"],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/GalaxyMapOverlayViewModelTests.cs"]
        ),
        Map(
            "PlotGrounded",
            ["src/SrvSurvey.Desktop/SurfaceSurveyOverlayWindow.axaml"],
            [
                "tests/SrvSurvey.Desktop.Tests/ViewModels/SurfaceSurveyOverlayViewModelTests.cs",
                "tests/SrvSurvey.Desktop.Tests/ViewModels/SurfaceSurveyViewModelTests.cs",
            ]
        ),
        Map(
            "PlotGuardians",
            [
                "src/SrvSurvey.Desktop/GuardianOverlayWindow.axaml",
                "src/SrvSurvey.Desktop/GuardianSiteOverlayPresentation.axaml",
            ],
            [
                "tests/SrvSurvey.Desktop.Tests/ViewModels/GuardianOverlayViewModelTests.cs",
                "tests/SrvSurvey.Desktop.Tests/ViewModels/GuardianViewModelTests.cs",
            ]
        ),
        Map(
            "PlotGuardianStatus",
            [
                "src/SrvSurvey.Desktop/GuardianStatusOverlayWindow.axaml",
                "src/SrvSurvey.Desktop/GuardianStatusOverlayPresentation.axaml",
            ],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/GuardianViewModelTests.cs"]
        ),
        Map(
            "PlotGuardianSystem",
            [
                "src/SrvSurvey.Desktop/GuardianSystemOverlayWindow.axaml",
                "src/SrvSurvey.Desktop/GuardianSystemOverlayPresentation.axaml",
            ],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/GuardianViewModelTests.cs"]
        ),
        Map(
            "PlotHumanSite",
            ["src/SrvSurvey.Desktop/HumanSiteOverlayWindow.axaml"],
            [
                "tests/SrvSurvey.Desktop.Tests/ViewModels/HumanSiteOverlayViewModelTests.cs",
                "tests/SrvSurvey.Desktop.Tests/ViewModels/HumanSiteViewModelTests.cs",
            ]
        ),
        Map(
            "PlotJumpInfo",
            ["src/SrvSurvey.Desktop/JumpInfoOverlayWindow.axaml"],
            [
                "tests/SrvSurvey.Desktop.Tests/ViewModels/JumpInfoOverlayViewModelTests.cs",
                "tests/SrvSurvey.Desktop.Tests/ViewModels/JumpInfoViewModelTests.cs",
            ]
        ),
        Map(
            "PlotFleetCarrierRoute",
            ["src/SrvSurvey.Desktop/FleetCarrierRouteOverlayWindow.axaml"],
            [
                "tests/SrvSurvey.Desktop.Tests/ViewModels/FleetCarrierRouteOverlayViewModelTests.cs",
                "tests/SrvSurvey.Desktop.Tests/ViewModels/FleetCarrierJumpCountdownTrackerTests.cs",
            ]
        ),
        Map(
            "PlotRouteBio",
            [
                "src/SrvSurvey.Desktop/RouteBioOverlayWindow.axaml",
                "src/SrvSurvey.Desktop/Controls/RouteBioTargetRow.axaml",
                "src/SrvSurvey.Desktop/Controls/RouteBioTargetList.axaml",
            ],
            [
                "tests/SrvSurvey.Desktop.Tests/ViewModels/RouteWorkspaceViewModelTests.cs",
                "tests/SrvSurvey.Desktop.Tests/ViewModels/OverlayPositionPreviewViewModelTests.cs",
            ]
        ),
        Map(
            "PlotMassacre",
            ["src/SrvSurvey.Desktop/MassacreMissionsOverlayWindow.axaml"],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/CombatViewModelTests.cs"]
        ),
        Map(
            "PlotMiningNotifications",
            [
                "src/SrvSurvey.Desktop/MiningActivityOverlayWindow.axaml",
                "src/SrvSurvey.Desktop/MiningActivityOverlayPresentation.axaml",
            ],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/MiningWorkspaceViewModelTests.cs"]
        ),
        Map(
            "PlotMiningCargo",
            [
                "src/SrvSurvey.Desktop/MiningCargoOverlayWindow.axaml",
                "src/SrvSurvey.Desktop/MiningCargoOverlayPresentation.axaml",
            ],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/MiningWorkspaceViewModelTests.cs"]
        ),
        Map(
            "PlotMiningFiregroups",
            [
                "src/SrvSurvey.Desktop/MiningActivityOverlayWindow.axaml",
                "src/SrvSurvey.Desktop/MiningActivityOverlayPresentation.axaml",
            ],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/MiningWorkspaceViewModelTests.cs"]
        ),
        Map(
            "PlotMiningWarning",
            [
                "src/SrvSurvey.Desktop/MiningWarningOverlayWindow.axaml",
                "src/SrvSurvey.Desktop/MiningWarningOverlayPresentation.axaml",
            ],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/SurfaceMiningViewModelTests.cs"]
        ),
        Map(
            "PlotSurfaceMining",
            [
                "src/SrvSurvey.Desktop/SurfaceMiningOverlayWindow.axaml",
                "src/SrvSurvey.Desktop/SurfaceMiningOverlayPresentation.axaml",
            ],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/SurfaceMiningViewModelTests.cs"]
        ),
        Map(
            "PlotMineMap",
            [
                "src/SrvSurvey.Desktop/MineMapOverlayWindow.axaml",
                "src/SrvSurvey.Desktop/MineMapOverlayPresentation.axaml",
            ],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/MineMapViewModelTests.cs"]
        ),
        Map(
            "PlotSurfaceMiningSurvey",
            [
                "src/SrvSurvey.Desktop/SurfaceMiningSurveyOverlayWindow.axaml",
                "src/SrvSurvey.Desktop/SurfaceMiningSurveyOverlayPresentation.axaml",
            ],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/MineMapViewModelTests.cs"]
        ),
        Map(
            "PlotMiningReference",
            [
                "src/SrvSurvey.Desktop/MiningReferenceOverlayWindow.axaml",
                "src/SrvSurvey.Desktop/MiningReferenceOverlayPresentation.axaml",
            ],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/MineMapViewModelTests.cs"]
        ),
        Map(
            "PlotMiniTrack",
            ["src/SrvSurvey.Desktop/MiniTrackOverlayWindow.axaml"],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/SurfaceSurveyViewModelTests.cs"]
        ),
        Map(
            "PlotMultiGameCommander",
            ["src/SrvSurvey.Desktop/MultiGameCommanderOverlayWindow.axaml"],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/CommanderInstancesViewModelTests.cs"]
        ),
        Map(
            "PlotPriorScans",
            ["src/SrvSurvey.Desktop/PriorScansOverlayWindow.axaml"],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/PriorScansOverlayViewModelTests.cs"]
        ),
        Map(
            "PlotPulse",
            ["src/SrvSurvey.Desktop/PulseOverlayWindow.axaml"],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/PulseOverlayViewModelTests.cs"]
        ),
        Map(
            "PlotQuestMini",
            ["src/SrvSurvey.Desktop/QuestIndicatorOverlayWindow.axaml"],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/QuestIndicatorViewModelTests.cs"]
        ),
        Map(
            "PlotRamTah",
            [
                "src/SrvSurvey.Desktop/RamTahOverlayWindow.axaml",
                "src/SrvSurvey.Desktop/RamTahOverlayPresentation.axaml",
            ],
            [
                "tests/SrvSurvey.Desktop.Tests/ViewModels/GuardianViewModelTests.cs",
                "tests/SrvSurvey.Desktop.Tests/ViewModels/RamTahViewModelTests.cs",
            ]
        ),
        Map(
            "PlotSphericalSearch",
            ["src/SrvSurvey.Desktop/SphericalSearchOverlayWindow.axaml"],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/SphericalSearchOverlayViewModelTests.cs"]
        ),
        Map(
            "PlotStationInfo",
            ["src/SrvSurvey.Desktop/StationInfoOverlayWindow.axaml"],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/StationInfoViewModelTests.cs"]
        ),
        Map(
            "PlotSysStatus",
            ["src/SrvSurvey.Desktop/SystemStatusOverlayWindow.axaml"],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/SystemSurveyViewModelTests.cs"]
        ),
        Map(
            "PlotTrackers",
            [
                "src/SrvSurvey.Desktop/SurfaceSurveyOverlayWindow.axaml",
                "src/SrvSurvey.Desktop/MiniTrackOverlayWindow.axaml",
            ],
            ["tests/SrvSurvey.Desktop.Tests/ViewModels/SurfaceSurveyViewModelTests.cs"]
        ),
        Map(
            "PlotTrackTarget",
            ["src/SrvSurvey.Desktop/GroundTargetOverlayWindow.axaml"],
            [
                "tests/SrvSurvey.Desktop.Tests/ViewModels/GroundTargetOverlayViewModelTests.cs",
                "tests/SrvSurvey.Desktop.Tests/ViewModels/GroundTargetViewModelTests.cs",
            ]
        ),
    ];

    [Fact]
    public void InventoryContainsEverySupportedOverlayExactlyOnce()
    {
        Assert.Equal(39, Mappings.Length);
        Assert.Equal(Mappings.Length, Mappings.Select(mapping => mapping.ContractName).Distinct().Count());
    }

    [Fact]
    public void EverySupportedOverlayHasProductionAndAssertionEvidence()
    {
        string root = FindRepositoryRoot();
        foreach (OverlayMapping mapping in Mappings)
        {
            Assert.NotEmpty(mapping.ProductionFiles);
            Assert.NotEmpty(mapping.TestFiles);
            foreach (string path in mapping.ProductionFiles)
            {
                Assert.True(
                    File.Exists(Path.Combine(root, Native(path))),
                    $"Missing {mapping.ContractName} production evidence: {path}"
                );
            }

            foreach (string path in mapping.TestFiles)
            {
                string absolutePath = Path.Combine(root, Native(path));
                Assert.True(File.Exists(absolutePath), $"Missing {mapping.ContractName} test evidence: {path}");
                Assert.Contains("Assert.", File.ReadAllText(absolutePath));
            }
        }
    }

    [Fact]
    public void EveryForcedPreviewMapsToAnExistingProductionWindow()
    {
        string root = FindRepositoryRoot();
        Assert.Equal(
            OverlayLayoutCatalog.Supported.Select(definition => definition.Name).Order(StringComparer.Ordinal),
            PreviewProductionWindows.Keys.Order(StringComparer.Ordinal)
        );
        foreach (string productionWindow in PreviewProductionWindows.Values)
        {
            Assert.True(
                File.Exists(Path.Combine(root, "src", "SrvSurvey.Desktop", productionWindow)),
                $"Missing production overlay for preview: {productionWindow}"
            );
        }
    }

    [Fact]
    public void FixedRuntimeWidthsMatchTheirEditorPresentationWidths()
    {
        string root = FindRepositoryRoot();
        foreach (KeyValuePair<string, string> pair in PreviewProductionWindows)
        {
            // Human-site dimensions are commander settings in the legacy app.
            if (pair.Key == "PlotHumanSite")
            {
                continue;
            }

            string markup = File.ReadAllText(Path.Combine(root, "src", "SrvSurvey.Desktop", pair.Value));
            // Content-driven WidthAndHeight hosts may set only a soft MinWidth
            // that is lower than the catalog anchor; only pin-check fixed hosts.
            if (markup.Contains("SizeToContent=\"WidthAndHeight\"", StringComparison.Ordinal))
            {
                continue;
            }

            Match match = WindowWidthRegex().Match(markup);
            if (!match.Success)
            {
                continue;
            }

            int expected = OverlayLayoutCatalog.GetRequired(pair.Key).PreviewSize.Width;
            Assert.Equal(
                expected,
                int.Parse(match.Groups["width"].Value, global::System.Globalization.CultureInfo.InvariantCulture)
            );
        }
    }

    [GeneratedRegex(
        """
            <Window[\s\S]*?\bWidth="(?<width>\d+)"
            """
    )]
    private static partial Regex WindowWidthRegex();

    [Fact]
    public void CommodityOverlayUsesLegacyContentDrivenHeight()
    {
        string root = FindRepositoryRoot();
        string markup = File.ReadAllText(
            Path.Combine(root, "src", "SrvSurvey.Desktop", PreviewProductionWindows["PlotBuildCommodities"])
        );

        Assert.Contains("MinHeight=\"1\"", markup);
        Assert.Contains("SizeToContent=\"WidthAndHeight\"", markup);
        Assert.DoesNotContain("MaxHeight=\"480\"", markup);
    }

    [Fact]
    public void EveryIndividualRuntimeOverlayWindowIsAvailableInTheEditor()
    {
        string root = FindRepositoryRoot();
        string overlayDirectory = Path.Combine(root, "src", "SrvSurvey.Desktop");
        var containerWindows = new HashSet<string>(StringComparer.Ordinal)
        {
            "CombinedOverlayWindow.axaml",
            "GuardianZoomOverlayWindow.axaml",
            "MineMapZoomOverlayWindow.axaml",
            "SurfaceMiningAlignmentOverlayWindow.axaml",
            "StreamOverlayWindow.axaml",
        };
        string[] runtimePanels = Directory
            .GetFiles(overlayDirectory, "*OverlayWindow.axaml")
            .Select(Path.GetFileName)
            .Where(name => name is not null && !containerWindows.Contains(name))
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            PreviewProductionWindows.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
            runtimePanels
        );
    }

    [Fact]
    public void EveryRuntimeOverlayUsesTheSharedLegacyPresentationPipeline()
    {
        string root = FindRepositoryRoot();
        string hostedWindowSource = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/Platform/Overlay/HostedOverlayWindow.cs"))
        );
        Assert.Contains("OverlayThemeResources.Apply(", hostedWindowSource);
        var coordinatorFiles = Directory
            .GetFiles(Path.Combine(root, Native("src/SrvSurvey.Desktop/Platform/Overlay")), "*Coordinator.cs")
            .Select(path => new { Path = path, Source = File.ReadAllText(path) })
            .ToArray();
        foreach (OverlayLayoutDefinition definition in OverlayLayoutCatalog.Supported)
        {
            var owners = coordinatorFiles.Where(file =>
                file.Source.Contains($"\"{definition.Name}\"", StringComparison.Ordinal)
            );
            Assert.Contains(
                owners,
                owner =>
                    owner.Source.Contains("OverlayThemeResources.Apply(", StringComparison.Ordinal)
                    || owner.Source.Contains("PassiveOverlayWindowDefinition(", StringComparison.Ordinal)
            );
        }
    }

    [Theory]
    [InlineData("src/SrvSurvey.Desktop/BiologyStatusOverlayPresentation.axaml", "IsAnalyzed")]
    [InlineData("src/SrvSurvey.Desktop/BiologySurveyOverlayPresentation.axaml", "IsComplete")]
    [InlineData("src/SrvSurvey.Desktop/FssInfoOverlayPresentation.axaml", "AreBiologicalSignalsComplete")]
    [InlineData("src/SrvSurvey.Desktop/FssInfoOverlayPresentation.axaml", "AreGeologicalSignalsComplete")]
    [InlineData("src/SrvSurvey.Desktop/MassacreMissionsOverlayPresentation.axaml", "IsComplete")]
    public void CompletionStatesRemainVisiblyStruckThrough(string relativePath, string stateBinding)
    {
        string root = FindRepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(root, Native(relativePath)));

        Assert.Contains($"IsVisible=\"{{Binding {stateBinding}}}\"", xaml);
        Assert.Contains("TextDecorations=\"Strikethrough\"", xaml);
    }

    [Fact]
    public void FssOverlayDoesNotReplaceBodyRowsWithAnArbitrarySummaryCap()
    {
        string root = FindRepositoryRoot();
        string xaml = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/FssInfoOverlayPresentation.axaml"))
        );
        string viewModel = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/ViewModels/SystemSurveyViewModel.cs"))
        );

        Assert.Contains("ItemsSource=\"{Binding Survey.FssBodies}\"", xaml);
        Assert.Contains("<ScrollViewer", xaml);
        Assert.DoesNotContain("DisplayedFssBodies", viewModel);
        Assert.DoesNotContain("MaximumDisplayedFssBodies", viewModel);
    }

    [Fact]
    public void PositionEditorUsesCategorizedForcedPreviewsAndExplicitCommitControls()
    {
        string root = FindRepositoryRoot();
        string themeShell = File.ReadAllText(Path.Combine(root, Native("src/SrvSurvey.Desktop/Views/ThemeView.axaml")));
        string overlaySettings = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/Views/OverlaySettingsView.axaml"))
        );
        string editor = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/OverlayPositionEditorWindow.axaml"))
        );
        string preview = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/OverlayPositionPreviewWindow.axaml"))
        );
        string interaction = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/ViewModels/OverlayInteractionViewModel.cs"))
        );
        string editorHost = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/Platform/Overlay/OverlayPositionEditorHost.cs"))
        );
        string themeResources = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/Platform/Overlay/OverlayThemeResources.cs"))
        );

        Assert.Contains("Edit Overlay Positions", interaction);
        Assert.Contains("OverlayInteraction.ToggleCommand", overlaySettings);
        Assert.Contains("ItemsSource=\"{Binding Categories}\"", editor);
        Assert.Contains("Text=\"{Binding SelectedCategory.DisplayName}\"", editor);
        Assert.Contains("Command=\"{Binding ToggleCategoryMenuCommand}\"", editor);
        Assert.Contains("Command=\"{Binding SnapToCenterCommand}\"", editor);
        Assert.Contains("Content=\"&#x25CE;\"", editor);
        Assert.Contains("Snap every overlay in this category to the center", editor);
        Assert.Contains("Command=\"{Binding SaveCommand}\"", editor);
        Assert.Contains("Content=\"&#x2713;\"", editor);
        Assert.Contains("Command=\"{Binding CancelCommand}\"", editor);
        Assert.Contains("Content=\"&#x00D7;\"", editor);
        Assert.True(
            editor.IndexOf("Command=\"{Binding SnapToCenterCommand}\"", StringComparison.Ordinal)
                < editor.IndexOf("Command=\"{Binding SaveCommand}\"", StringComparison.Ordinal)
        );
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
        string runtimeFactory = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/Platform/Overlay/OverlayRuntimePresentationFactory.cs"))
        );
        Assert.Contains("CreatePresentation", runtimeFactory);
        Assert.Contains("CreateEditorDataContext", runtimeFactory);
        Assert.Contains("BiologySurveyOverlayPresentation", runtimeFactory);
        Assert.Contains("RouteBioOverlayPresentation", runtimeFactory);
        string routePresentation = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/RouteBioOverlayPresentation.axaml"))
        );
        Assert.Contains("RouteBioTargetList", routePresentation);
        Assert.Contains(
            "BiologyRewardBandControl",
            File.ReadAllText(Path.Combine(root, Native("src/SrvSurvey.Desktop/BiologySurveyOverlayPresentation.axaml")))
        );
        Assert.DoesNotContain("Save all", preview);
        Assert.DoesNotContain("OnSaveRequested", preview);
        Assert.DoesNotContain("OnCancelRequested", preview);
        Assert.DoesNotContain("ContextFlyout", preview);
        Assert.Contains("PointerPressed=\"OnPreviewSurfacePointerPressed\"", preview);
        Assert.Contains("IsOverlaySettingsOpen", editor);
        Assert.Contains("UseGlobalOverlayOpacity", editor);
        Assert.Contains("SelectedOverlayOpacityPercent", editor);
        Assert.Contains("UseGlobalOverlayScale", editor);
        Assert.Contains("SelectedOverlayScalePercent", editor);
        Assert.Contains("ToggleTypographySettingsCommand", editor);
        Assert.Contains("ResetTypographyCommand", editor);
        Assert.Contains("ResetOverlaySizeCommand", editor);
        Assert.Contains("TypographyRoles", editor);
        Assert.Contains("x:Name=\"TypographySettingsPanel\"", editor);
        Assert.Contains("x:Name=\"PanelResizeThumb\"", preview);
        Assert.DoesNotContain("<Popup PlacementTarget=\"{Binding #TypographyButton}\"", editor);
        Assert.True(
            editor.IndexOf("x:Name=\"TypographySettingsPanel\"", StringComparison.Ordinal)
                < editor.IndexOf("x:Name=\"OverlaySettingsPanel\"", StringComparison.Ordinal)
        );
        Assert.Contains("overlay-category", editor);
        Assert.Contains("x:Name=\"OverlayCategoryMenu\"", editor);
        Assert.Contains("ToggleCategoryMenuCommand", editor);
        Assert.Contains("SelectCategoryCommand", editor);
        Assert.DoesNotContain("UpwardComboBox", editor);
        Assert.DoesNotContain("<Popup", editor);
        Assert.True(
            editor.IndexOf("x:Name=\"OverlayCategoryMenu\"", StringComparison.Ordinal)
                < editor.IndexOf("x:Name=\"EditorToolbarPanel\"", StringComparison.Ordinal)
        );
        Assert.DoesNotContain("VisiblePreviewOverlays", editor);
        Assert.DoesNotContain("SelectedPreviewOverlay", editor);
        Assert.DoesNotContain("Text=\"Overlay panel\"", editor);
        Assert.Contains("toolbar.Activate()", editorHost);
        Assert.Contains("OverlayWindowPlacement.BottomCenter", editorHost);
        Assert.Contains("screen.WorkingArea", editorHost);
        Assert.Contains("ManagedOverlayWindowDragSession.Begin(preview, eventArgs)", editorHost);
        Assert.Contains("Right-click panels to edit opacity, panel scale, and text scale", editor);
        Assert.DoesNotContain("BringPreviewToFront", editorHost);
        Assert.DoesNotContain("ClampToHost", editorHost);
        Assert.True(
            editorHost.IndexOf("preview.Show();", StringComparison.Ordinal)
                < editorHost.IndexOf("preview.PositionChanged += OnPreviewPositionChanged;", StringComparison.Ordinal)
        );
        Assert.Contains("SettingsRequested", editorHost);
        Assert.Contains("ApplySurfaceChrome", themeResources);
        Assert.Contains("ApplyLegacyPresentation", themeResources);
        Assert.Contains("NormalizeLegacyOverlayControl", themeResources);
        Assert.Contains("surface.BorderThickness = new Thickness(isEditorPreview ? 2 : 0)", themeResources);
        Assert.Contains("surface.Padding = new Thickness(4)", themeResources);
        Assert.Contains("simulated game data", interaction);
        Assert.Contains("game.IsAvailable", interaction);
        Assert.Contains("? game.ClientBounds", interaction);
        Assert.Contains(": (PixelRect?)null", interaction);
        Assert.Contains("ToggleLiveOverlayInteraction", interaction);
        Assert.Contains("SetRuntimeOverlaysVisibleDuringEditing(true)", interaction);
        Assert.Contains("RefreshPreviewPositions(editSession)", interaction);
        Assert.DoesNotContain(
            "Close the categorized overlay position editor before enabling interaction with live overlays.",
            interaction
        );
        Assert.Contains("<Expander", overlaySettings);
        Assert.Contains("Classes=\"theme-selector\"", themeShell);
        Assert.Contains("Text=\"Theme\"", themeShell);
        Assert.Contains("Header=\"Overlay Settings\"", themeShell);
        Assert.Contains("<views:OverlaySettingsView", themeShell);
        Assert.Contains("BorderThickness=\"1\"", overlaySettings);
        Assert.Contains("Command=\"{Binding OverlayTheme.PreviewCommand}\"", themeShell);
        Assert.Contains("Text=\"Overlay theme presets and saved states\"", themeShell);
        Assert.Contains("SharedSizeGroup=\"OverlayThemeColorName\"", themeShell);
        Assert.Contains("Grid.IsSharedSizeScope=\"True\"", themeShell);
        Assert.Contains("Slider.overlay-theme-opacity /template/ Thumb#thumb", themeShell);
        Assert.Contains("Content=\"Load Defaults\"", themeShell);
        Assert.Contains("Overlay Opacity Override", overlaySettings);
        Assert.Contains("OverlayLayout.SelectedOverlay", overlaySettings);
        Assert.Contains("OverlayLayout.SaveCommand", overlaySettings);
        Assert.DoesNotContain("HorizontalAnchorOptions", overlaySettings);
        Assert.DoesNotContain("VerticalAnchorOptions", overlaySettings);
        Assert.DoesNotContain("GalaxyMap.AutoShow", themeShell);
        Assert.Contains("GalaxyMap.AutoShow", overlaySettings);
        Assert.DoesNotContain("Notifications.Enabled", themeShell);
        Assert.Contains("Notifications.Enabled", overlaySettings);

        string routeOverlay = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/RouteBioOverlayWindow.axaml"))
        );
        string routeOverlayPresentation = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/RouteBioOverlayPresentation.axaml"))
        );
        Assert.Contains("RouteBioOverlayPresentation", routeOverlay);
        Assert.Contains("RouteBioTargetList", routeOverlayPresentation);
        Assert.Contains("Width=\"260\"", routeOverlay);
        Assert.Contains("Text=\"ROUTE BODIES\"", routeOverlayPresentation);
        Assert.Contains("Classes=\"overlay-header type-header\"", routeOverlayPresentation);
        Assert.Contains("Background=\"{DynamicResource RavenHeaderBrush}\"", routeOverlayPresentation);
        Assert.DoesNotContain("RavenWarningBrush", routeOverlayPresentation);
        Assert.DoesNotContain("BorderBrush=\"{DynamicResource RavenWarningBrush}\"", routeOverlayPresentation);

        string biologyOverlay = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/BiologySurveyOverlayWindow.axaml"))
        );
        string biologyPresentation = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/BiologySurveyOverlayPresentation.axaml"))
        );
        Assert.Contains("Width=\"240\"", biologyOverlay);
        Assert.Contains("BiologySurveyOverlayPresentation", biologyOverlay);
        Assert.Contains("Text=\"{Binding Survey.BiologySurveyDisplay.Title}\"", biologyPresentation);
        Assert.Contains("Classes=\"overlay-header type-header\"", biologyPresentation);
        Assert.Contains("Background=\"{DynamicResource RavenHeaderBrush}\"", biologyPresentation);
        Assert.DoesNotContain("RavenWarningBrush", biologyPresentation);
        Assert.Contains("Padding=\"4\"", biologyPresentation);
        Assert.Contains("BorderThickness=\"0\"", biologyPresentation);
        Assert.Contains("CornerRadius=\"5\"", biologyPresentation);
        Assert.DoesNotContain("EXOBIOLOGY SURVEY", biologyPresentation);
        Assert.DoesNotContain("RavenSurfaceBrush", biologyPresentation);
        Assert.DoesNotContain("Classes=\"badge\"", biologyPresentation);

        string routeTargetList = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/Controls/RouteBioTargetList.axaml"))
        );
        string routeTargetListCode = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/Controls/RouteBioTargetList.axaml.cs"))
        );
        string routeTargetRow = File.ReadAllText(
            Path.Combine(root, Native("src/SrvSurvey.Desktop/Controls/RouteBioTargetRow.axaml"))
        );
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
        IReadOnlyList<string> testFiles
    )
    {
        return new OverlayMapping(contractName, productionFiles, testFiles);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SrvSurvey.slnx")))
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
        IReadOnlyList<string> TestFiles
    );
}
