using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationCommodityOverlayCoordinatorTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-colonization-visibility-{Guid.NewGuid():N}"
    );

    /// <summary>Activating availability through either control keeps the real panel visible after undocking.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualAvailabilityActivationPersistsAfterUndocking(bool shortcut)
    {
        var registry = new OverlayWindowRegistry();
        OverlayPanelVisibilityViewModel availability = CreateAvailability(registry);
        OverlayPanelVisibilityEntryViewModel panel = availability.Panels.Single(item =>
            item.PlotterName == "PlotBuildCommodities"
        );
        panel.IsEnabled = false;
        var overlay = new ColonizationCommodityOverlayViewModel();
        overlay.Apply(
            Plan(),
            new EliteStatus { Flags = StatusFlags.Docked, GuiFocus = GuiFocus.StationServices },
            updatedHasMarketSinceDocking: true
        );
        using var host = new HostedOverlayTestHarness(registry);
        using var coordinator = new ColonizationCommodityOverlayCoordinator(overlay, host.Session);
        Assert.False(coordinator.IsVisible);

        if (shortcut)
        {
            coordinator.ToggleVisibility(availability);
        }
        else
        {
            panel.IsEnabled = true;
        }
        host.Tick();
        Assert.True(coordinator.IsVisible);

        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.InMainShip });
        host.Tick();
        Assert.True(coordinator.IsVisible);
        Assert.True(panel.IsEnabled);

        if (shortcut)
        {
            coordinator.ToggleVisibility(availability);
        }
        else
        {
            panel.IsEnabled = false;
        }
        host.Tick();
        Assert.False(coordinator.IsVisible);
        overlay.Apply(
            Plan(),
            new EliteStatus { Flags = StatusFlags.Docked, GuiFocus = GuiFocus.StationServices },
            updatedHasMarketSinceDocking: true
        );
        Assert.False(coordinator.IsVisible);
    }

    /// <summary>Checks that a direct manual request survives game-state changes independently of availability activation.</summary>
    [AvaloniaFact]
    public void DirectManualRequestSurvivesUndocking()
    {
        var registry = new OverlayWindowRegistry();
        OverlayPanelVisibilityViewModel availability = CreateAvailability(registry);
        var overlay = new ColonizationCommodityOverlayViewModel();
        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.InMainShip });
        using var host = new HostedOverlayTestHarness(registry);
        using var coordinator = new ColonizationCommodityOverlayCoordinator(overlay, host.Session);
        coordinator.ToggleVisibility(availability);
        Assert.True(coordinator.IsVisible);
        overlay.Apply(
            Plan(),
            new EliteStatus { Flags = StatusFlags.Docked, GuiFocus = GuiFocus.StationServices },
            updatedHasMarketSinceDocking: true
        );
        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.InMainShip });
        Assert.True(coordinator.IsVisible);
    }

    /// <summary>The automatic hiding preference must not prevent explicit manual display.</summary>
    [AvaloniaFact]
    public void ManualVisibilityDoesNotDependOnAutomaticSetting()
    {
        var registry = new OverlayWindowRegistry();
        OverlayPanelVisibilityViewModel availability = CreateAvailability(registry);
        availability.Toggle("PlotBuildCommodities");
        var overlay = new ColonizationCommodityOverlayViewModel();
        overlay.ApplyPreferences(ColonizationOverlayPreferences.Default with { AutoShow = false });
        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.InMainShip });
        using var host = new HostedOverlayTestHarness(registry);
        using var coordinator = new ColonizationCommodityOverlayCoordinator(overlay, host.Session);
        coordinator.ToggleVisibility(availability);
        Assert.True(coordinator.IsVisible);
    }

    /// <summary>Default automatic behavior resumes at docking without silently pinning every available panel at startup.</summary>
    [AvaloniaFact]
    public void AutomaticDockingAndManualSuppressionRemainIndependent()
    {
        var registry = new OverlayWindowRegistry();
        OverlayPanelVisibilityViewModel availability = CreateAvailability(registry);
        var overlay = new ColonizationCommodityOverlayViewModel();
        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.InMainShip });
        using var host = new HostedOverlayTestHarness(registry);
        using var coordinator = new ColonizationCommodityOverlayCoordinator(overlay, host.Session);
        Assert.False(coordinator.IsVisible);
        overlay.Apply(
            Plan(),
            new EliteStatus { Flags = StatusFlags.Docked, GuiFocus = GuiFocus.StationServices },
            updatedHasMarketSinceDocking: true
        );
        Assert.True(coordinator.IsVisible);
        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.InMainShip });
        Assert.False(coordinator.IsVisible);

        coordinator.ToggleVisibility(availability);
        coordinator.SetSuppressed(true);
        host.Tick();
        Assert.False(coordinator.IsVisible);
        coordinator.SetSuppressed(false);
        Assert.True(coordinator.IsVisible);
        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.InMainShip, GuiFocus = GuiFocus.ExternalPanel });
        Assert.True(coordinator.IsVisible);
        overlay.Apply(Plan(), null);
        Assert.False(coordinator.IsVisible);
        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.InMainShip });
        Assert.True(coordinator.IsVisible);

        coordinator.Dispose();
        coordinator.ToggleVisibility(availability);
        coordinator.SetSuppressed(true);
        availability.Toggle("PlotBuildCommodities");
        overlay.Apply(Plan(), null);
        Assert.False(coordinator.IsVisible);
    }

    /// <summary>Builds the same availability model used by the settings checkbox and shortcut dispatcher.</summary>
    private OverlayPanelVisibilityViewModel CreateAvailability(OverlayWindowRegistry registry)
    {
        string path = Path.Combine(directory, "ui.json");
        var input = new GlobalInputSettingsViewModel(
            new GlobalInputSettingsStore(path),
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows),
            new EmptyControllerDevices()
        );
        return new OverlayPanelVisibilityViewModel(new OverlayPanelVisibilitySettingsStore(path), input, registry);
    }

    /// <summary>Provides a loaded, unfinished commodity plan independently of Raven network access.</summary>
    private static ColonizationCommodityPlan Plan() =>
        new()
        {
            Title = "Test build",
            ProjectNames = ["Test build"],
            FleetCarriers = [],
            Rows =
            [
                new ColonizationCommodityPlanRow
                {
                    Commodity = "steel",
                    DisplayName = "Steel",
                    Category = "Metals",
                    Needed = 100,
                },
            ],
        };

    /// <summary>Removes the isolated availability settings after each scenario.</summary>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class EmptyControllerDevices : IControllerDeviceProvider
    {
        /// <summary>Avoids hardware discovery in deterministic keyboard and checkbox tests.</summary>
        public ControllerDeviceDiscoveryResult Discover() => new([], ErrorMessage: null);
    }
}
