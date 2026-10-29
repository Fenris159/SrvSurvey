using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Combat;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationCommodityOverlayCoordinatorTests : IDisposable
{
    [AvaloniaFact]
    public async Task CombatReturnsWhenTheColonyPanelIsDeactivatedOrItsFocusedContentIsCleared()
    {
        var registry = new OverlayWindowRegistry();
        registry.SetPriorityFacts(OverlayPriorityFacts.HideCombatForColonization);
        OverlayPanelVisibilityViewModel availability = CreateAvailability(registry);
        var overlay = new ColonizationCommodityOverlayViewModel();
        overlay.ApplyPreferences(ColonizationOverlayPreferences.Default with { AutoShow = false });
        var status = new EliteStatus { Flags = StatusFlags.InMainShip };
        overlay.Apply(Plan(), status);
        using var host = new HostedOverlayTestHarness(registry);
        using var coordinator = new ColonizationCommodityOverlayCoordinator(overlay, host.Session);
        var combat = new CombatViewModel(
            new CombatSettingsStore(Path.Combine(directory, "combat.json")),
            new CommanderProfileStore(directory)
        );
        combat.AutoShowMassacreMissions = true;
        combat.SuppressForActiveBuildProjects = true;
        combat.LoadProfile(null, null, true, new CombatSnapshot([new(123, "Giver", "Enemy", null, 6, 6)]));
        await combat.ApplyUpdateAsync([], status, true);
        using var combatCoordinator = new CombatOverlayCoordinator(combat, host.Session);
        host.Tick();
        Assert.True(coordinator.IsVisible);
        Assert.False(combatCoordinator.IsMassacreVisible);
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"event":"Bounty","VictimFaction":"Enemy"}""",
                out JournalEventEnvelope? bounty,
                out _
            )
        );
        await combat.ApplyUpdateAsync([bounty!], status, true);
        Assert.Equal("1/6", Assert.Single(combat.MassacreMissions).ProgressText);
        coordinator.ToggleVisibility(availability);
        host.Tick();
        Assert.False(coordinator.IsVisible);
        Assert.True(combatCoordinator.IsMassacreVisible);
        coordinator.ToggleVisibility(availability);
        host.Tick();
        Assert.True(coordinator.IsVisible);
        Assert.False(combatCoordinator.IsMassacreVisible);
        overlay.Apply(Plan() with { ProjectNames = [], Rows = [] }, status);
        host.Tick();
        Assert.False(coordinator.IsVisible);
        Assert.True(combatCoordinator.IsMassacreVisible);
        Assert.Equal("1/6", Assert.Single(combat.MassacreMissions).ProgressText);
    }

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-colonization-visibility-{Guid.NewGuid():N}"
    );

    /// <summary>Restored settings keep both disabled switches hidden through docking and permit deliberate shortcut activation.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledAutomaticAndManualAvailabilityStayHiddenOnDockedStartup(bool docked)
    {
        CreateAvailability(new OverlayWindowRegistry())
            .Panels.Single(item => item.PlotterName == "PlotBuildCommodities")
            .IsEnabled = false;
        string path = Path.Combine(directory, "ui.json");
        new ColonizationSettingsStore(path).SaveOverlayPreferences(
            ColonizationOverlayPreferences.Default with
            {
                AutoShow = false,
            }
        );
        var registry = new OverlayWindowRegistry();
        OverlayPanelVisibilityViewModel availability = CreateAvailability(registry);
        OverlayPanelVisibilityEntryViewModel panel = availability.Panels.Single(item =>
            item.PlotterName == "PlotBuildCommodities"
        );
        var overlay = new ColonizationCommodityOverlayViewModel();
        overlay.ApplyPreferences(new ColonizationSettingsStore(path).LoadOverlayPreferences());
        overlay.Apply(Plan(), new EliteStatus { Flags = docked ? StatusFlags.Docked : StatusFlags.InMainShip });
        using var host = new HostedOverlayTestHarness(registry);
        using var coordinator = new ColonizationCommodityOverlayCoordinator(overlay, host.Session);

        Assert.False(panel.IsEnabled);
        Assert.False(coordinator.IsVisible);
        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.Docked });
        host.Tick();
        Assert.False(coordinator.IsVisible);
        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.InMainShip });
        Assert.False(coordinator.IsVisible);
        coordinator.ToggleVisibility(availability);
        host.Tick();
        Assert.True(panel.IsEnabled);
        Assert.True(coordinator.IsVisible);
        coordinator.ToggleVisibility(availability);
        host.Tick();
        Assert.False(panel.IsEnabled);
        Assert.False(coordinator.IsVisible);
    }

    /// <summary>Disabling automatic hiding uses saved availability and reacts to live settings changes without a shortcut.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PersistentDisplayRespectsAvailabilityAtStartupAndDuringSettingsChanges(bool initiallyEnabled)
    {
        CreateAvailability(new OverlayWindowRegistry())
            .Panels.Single(item => item.PlotterName == "PlotBuildCommodities")
            .IsEnabled = initiallyEnabled;
        var registry = new OverlayWindowRegistry();
        OverlayPanelVisibilityViewModel availability = CreateAvailability(registry);
        OverlayPanelVisibilityEntryViewModel panel = availability.Panels.Single(item =>
            item.PlotterName == "PlotBuildCommodities"
        );
        var overlay = new ColonizationCommodityOverlayViewModel();
        ColonizationOverlayPreferences preferences = ColonizationOverlayPreferences.Default with { AutoShow = false };
        overlay.ApplyPreferences(preferences);
        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.Docked });
        using var host = new HostedOverlayTestHarness(registry);
        using var coordinator = new ColonizationCommodityOverlayCoordinator(overlay, host.Session);

        Assert.Equal(initiallyEnabled, coordinator.IsVisible);
        panel.IsEnabled = true;
        host.Tick();
        Assert.True(coordinator.IsVisible);
        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.InMainShip });
        Assert.True(coordinator.IsVisible);
        panel.IsEnabled = false;
        host.Tick();
        Assert.False(coordinator.IsVisible);
        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.Docked });
        Assert.False(coordinator.IsVisible);
        overlay.ApplyPreferences(preferences with { AutoShow = true });
        Assert.True(coordinator.IsVisible);
        overlay.ApplyPreferences(preferences);
        Assert.False(coordinator.IsVisible);
    }

    /// <summary>Automatic docking display works while the always-on availability checkbox is disabled.</summary>
    [AvaloniaTheory]
    [InlineData(false, GuiFocus.NoFocus, false)]
    [InlineData(true, GuiFocus.NoFocus, false)]
    [InlineData(false, GuiFocus.StationServices, false)]
    [InlineData(true, GuiFocus.StationServices, false)]
    [InlineData(false, GuiFocus.StationServices, true)]
    [InlineData(true, GuiFocus.StationServices, true)]
    public void AutomaticDockingShowsPanelIndependentlyOfManualAvailability(
        bool manualAvailability,
        GuiFocus focus,
        bool hasMarket
    )
    {
        var registry = new OverlayWindowRegistry();
        OverlayPanelVisibilityViewModel availability = CreateAvailability(registry);
        OverlayPanelVisibilityEntryViewModel panel = availability.Panels.Single(item =>
            item.PlotterName == "PlotBuildCommodities"
        );
        panel.IsEnabled = manualAvailability;
        var overlay = new ColonizationCommodityOverlayViewModel();
        overlay.ApplyPreferences(ColonizationOverlayPreferences.Default with { AutoShow = true });
        overlay.Apply(
            Plan(),
            new EliteStatus { Flags = StatusFlags.Docked, GuiFocus = focus },
            updatedHasMarketSinceDocking: hasMarket
        );
        using var host = new HostedOverlayTestHarness(registry);
        using var coordinator = new ColonizationCommodityOverlayCoordinator(overlay, host.Session);

        Assert.True(overlay.ShouldAutoShow);
        Assert.True(coordinator.IsVisible);
        Assert.Equal(manualAvailability, panel.IsEnabled);

        registry.SetGlobalSuppression(manualSuppressed: true, suitSuppressed: false, sessionSuppressed: false);
        host.Tick();
        Assert.False(coordinator.IsVisible);
        registry.SetGlobalSuppression(manualSuppressed: false, suitSuppressed: false, sessionSuppressed: false);
        host.Tick();
        Assert.True(coordinator.IsVisible);

        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.InMainShip });
        host.Tick();
        Assert.False(coordinator.IsVisible);
    }

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
        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.InMainShip });
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
        Assert.Equal(!shortcut, coordinator.IsVisible);
    }

    /// <summary>A visibility shortcut can still explicitly hide automatic display until the next manual activation.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void VisibilityShortcutCanHideAndRestoreAutomaticDockingDisplay(bool manualAvailability)
    {
        var registry = new OverlayWindowRegistry();
        OverlayPanelVisibilityViewModel availability = CreateAvailability(registry);
        OverlayPanelVisibilityEntryViewModel panel = availability.Panels.Single(item =>
            item.PlotterName == "PlotBuildCommodities"
        );
        panel.IsEnabled = manualAvailability;
        var overlay = new ColonizationCommodityOverlayViewModel();
        var docked = new EliteStatus { Flags = StatusFlags.Docked };
        overlay.Apply(Plan(), docked);
        using var host = new HostedOverlayTestHarness(registry);
        using var coordinator = new ColonizationCommodityOverlayCoordinator(overlay, host.Session);
        Assert.True(coordinator.IsVisible);

        coordinator.ToggleVisibility(availability);
        host.Tick();
        Assert.False(coordinator.IsVisible);
        Assert.False(panel.IsEnabled);
        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.InMainShip });
        overlay.Apply(Plan(), docked);
        Assert.False(coordinator.IsVisible);

        coordinator.ToggleVisibility(availability);
        Assert.True(coordinator.IsVisible);
        Assert.True(panel.IsEnabled);
        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.InMainShip });
        Assert.True(coordinator.IsVisible);
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
        availability.Panels.Single(panel => panel.PlotterName == "PlotBuildCommodities").IsEnabled = true;
        var overlay = new ColonizationCommodityOverlayViewModel();
        overlay.ApplyPreferences(ColonizationOverlayPreferences.Default with { AutoShow = false });
        overlay.Apply(Plan(), new EliteStatus { Flags = StatusFlags.InMainShip });
        using var host = new HostedOverlayTestHarness(registry);
        using var coordinator = new ColonizationCommodityOverlayCoordinator(overlay, host.Session);
        Assert.True(coordinator.IsVisible);
        coordinator.ToggleVisibility(availability);
        Assert.False(coordinator.IsVisible);
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
