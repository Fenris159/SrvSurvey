using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class OverlayPanelVisibilityViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-panel-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public void EveryPanelUsesCatalogedSettingsCategoriesAndOneUnboundShortcut()
    {
        var registry = new OverlayWindowRegistry();
        var viewModel = Create(registry);

        Assert.Equal(OverlayLayoutCatalog.Supported.Count, viewModel.Panels.Count);
        Assert.Equal(
            OverlayLayoutCatalog.Supported.Select(item => item.Name)
                .Order(StringComparer.Ordinal),
            viewModel.Panels.Select(item => item.PlotterName)
                .Order(StringComparer.Ordinal));
        Assert.All(viewModel.Panels, panel =>
        {
            Assert.True(panel.IsEnabled);
            Assert.Empty(panel.Shortcut.Chord);
            Assert.Equal(
                panel.PlotterName,
                panel.Shortcut.Definition.OverlayPlotterName);
        });
        Assert.Equal(
            [
                OverlaySettingsCategory.Boxel,
                OverlaySettingsCategory.Exploration,
                OverlaySettingsCategory.Travel,
            ],
            viewModel.Panels.Single(panel =>
                panel.PlotterName == "PlotSphericalSearch").SettingsCategories);
        Assert.Equal(
            [OverlaySettingsCategory.Travel],
            viewModel.Panels.Single(panel =>
                panel.PlotterName == "PlotStationInfo").SettingsCategories);
        Assert.Equal(
            [OverlaySettingsCategory.Global],
            viewModel.Panels.Single(panel =>
                panel.PlotterName == "PlotFloatie").SettingsCategories);
        Assert.All(
            Enum.GetValues<OverlaySettingsCategory>(),
            category => Assert.NotEmpty(viewModel.ForCategory(category)));
    }

    [Fact]
    public void TogglePersistsAndUpdatesRegistryAvailability()
    {
        var registry = new OverlayWindowRegistry();
        var viewModel = Create(registry);

        Assert.True(viewModel.Toggle("PlotGuardians"));
        Assert.False(registry.IsUserVisible("PlotGuardians"));
        Assert.False(viewModel.Panels.Single(panel =>
            panel.PlotterName == "PlotGuardians").IsEnabled);

        var reloaded = Create(new OverlayWindowRegistry());
        Assert.False(reloaded.Panels.Single(panel =>
            panel.PlotterName == "PlotGuardians").IsEnabled);
        Assert.False(viewModel.Toggle("PlotUnknown"));
    }

    [Fact]
    public void SettingsCategoriesPreserveEstablishedPanelGroups()
    {
        var expected = new Dictionary<OverlaySettingsCategory, string[]>
        {
            [OverlaySettingsCategory.Global] =
                ["PlotFloatie", "PlotMultiGameCommander", "PlotPulse"],
            [OverlaySettingsCategory.Exploration] =
            [
                "PlotBodyInfo",
                "PlotFlightWarning",
                "PlotFSS",
                "PlotFSSInfo",
                "PlotGalMap",
                "PlotSphericalSearch",
                "PlotSysStatus",
            ],
            [OverlaySettingsCategory.Exobiology] =
            [
                "PlotBioStatus",
                "PlotBioSystem",
                "PlotGrounded",
                "PlotMiniTrack",
                "PlotPriorScans",
                "PlotTrackTarget",
            ],
            [OverlaySettingsCategory.Travel] =
            [
                "PlotFleetCarrierRoute",
                "PlotJumpInfo",
                "PlotRouteBio",
                "PlotSphericalSearch",
                "PlotStationInfo",
            ],
            [OverlaySettingsCategory.Boxel] = ["PlotSphericalSearch"],
            [OverlaySettingsCategory.Guardian] =
            [
                "PlotGuardians",
                "PlotGuardianStatus",
                "PlotGuardianSystem",
                "PlotRamTah",
            ],
            [OverlaySettingsCategory.Quests] =
            [
                "PlotFootCombat",
                "PlotHumanSite",
                "PlotMassacre",
                "PlotQuestMini",
            ],
            [OverlaySettingsCategory.Colonization] =
                ["PlotBuildCommodities"],
        };
        var viewModel = Create(new OverlayWindowRegistry());

        foreach (var (category, plotterNames) in expected)
        {
            Assert.Equal(
                plotterNames.Order(StringComparer.Ordinal),
                viewModel.ForCategory(category)
                    .Select(panel => panel.PlotterName)
                    .Order(StringComparer.Ordinal));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private OverlayPanelVisibilityViewModel Create(OverlayWindowRegistry registry)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui.json");
        var input = new GlobalInputSettingsViewModel(
            new GlobalInputSettingsStore(path),
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows),
            new EmptyControllerDeviceProvider());
        return new OverlayPanelVisibilityViewModel(
            new OverlayPanelVisibilitySettingsStore(path),
            input,
            registry);
    }

    private sealed class EmptyControllerDeviceProvider
        : IControllerDeviceProvider
    {
        public ControllerDeviceDiscoveryResult Discover()
        {
            return new ControllerDeviceDiscoveryResult([], ErrorMessage: null);
        }
    }
}
