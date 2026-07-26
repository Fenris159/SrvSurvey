namespace SrvSurvey.Desktop.Tests.Parity;

public sealed class LegacyOverlayParityTests
{
    private static readonly OverlayMapping[] Mappings =
    [
        Map("PlotBase", [
            "src/SrvSurvey.Desktop/Platform/Overlay/OverlayPlatformService.cs",
            "src/SrvSurvey.Desktop/Platform/Overlay/OverlayWindowRegistry.cs",
        ], [
            "tests/SrvSurvey.Desktop.Tests/Platform/OverlayPlatformCapabilitiesTests.cs",
            "tests/SrvSurvey.Desktop.Tests/Platform/OverlayWindowPlacementTests.cs",
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
    public void MappingExactlyCoversEveryLegacyOverlayDesigner()
    {
        var root = FindRepositoryRoot();
        var actual = Directory.EnumerateFiles(
                Path.Combine(root, "SrvSurvey", "plotters"),
                "*.Designer.cs",
                SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var mapped = Mappings
            .Select(mapping => mapping.LegacyName + ".Designer.cs")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(22, actual.Length);
        Assert.Equal(actual, mapped);
    }

    [Fact]
    public void EveryLegacyOverlayHasProductionAndAssertionEvidence()
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
                    $"Missing {mapping.LegacyName} production evidence: {path}");
            }

            foreach (var path in mapping.TestFiles)
            {
                var absolutePath = Path.Combine(root, Native(path));
                Assert.True(
                    File.Exists(absolutePath),
                    $"Missing {mapping.LegacyName} test evidence: {path}");
                Assert.Contains("Assert.", File.ReadAllText(absolutePath));
            }
        }
    }

    [Theory]
    [InlineData("src/SrvSurvey.Desktop/BiologyStatusOverlayWindow.axaml", "IsAnalyzed")]
    [InlineData("src/SrvSurvey.Desktop/BiologySurveyOverlayWindow.axaml", "IsComplete")]
    [InlineData("src/SrvSurvey.Desktop/FssInfoOverlayWindow.axaml", "AreBiologicalSignalsComplete")]
    [InlineData("src/SrvSurvey.Desktop/FssInfoOverlayWindow.axaml", "AreGeologicalSignalsComplete")]
    [InlineData("src/SrvSurvey.Desktop/MassacreMissionsOverlayWindow.axaml", "IsComplete")]
    public void LegacyCompletionStatesRemainVisiblyStruckThrough(
        string relativePath,
        string stateBinding)
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, Native(relativePath)));

        Assert.Contains($"IsVisible=\"{{Binding {stateBinding}}}\"", xaml);
        Assert.Contains("TextDecorations=\"Strikethrough\"", xaml);
    }

    private static OverlayMapping Map(
        string legacyName,
        IReadOnlyList<string> productionFiles,
        IReadOnlyList<string> testFiles)
    {
        return new OverlayMapping(legacyName, productionFiles, testFiles);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PORTING_PLAN.md"))
                && Directory.Exists(Path.Combine(
                    current.FullName,
                    "SrvSurvey",
                    "plotters")))
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
        string LegacyName,
        IReadOnlyList<string> ProductionFiles,
        IReadOnlyList<string> TestFiles);
}
