using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class OverlayBehaviorViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-behavior-vm-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void SuitSuppressionRequiresOnFootAndTheMatchingPreference()
    {
        OverlayBehaviorViewModel viewModel = CreateViewModel();
        viewModel.HideInDominatorSuit = true;

        viewModel.UpdateContext(OdysseySuitType.Dominator, onFoot: false);
        Assert.False(viewModel.ShouldSuppressForSuit);

        viewModel.UpdateContext(OdysseySuitType.Dominator, onFoot: true);
        Assert.True(viewModel.ShouldSuppressForSuit);
        Assert.Equal("Dominator suit", viewModel.CurrentSuitText);

        viewModel.UpdateContext(OdysseySuitType.Maverick, onFoot: true);
        Assert.False(viewModel.ShouldSuppressForSuit);
        viewModel.HideInMaverickSuit = true;
        Assert.True(viewModel.ShouldSuppressForSuit);
    }

    [Fact]
    public void PassiveOverlayPreferencesPersist()
    {
        OverlayBehaviorViewModel viewModel = CreateViewModel();

        viewModel.KeepWhenGameLosesFocus = true;
        viewModel.HideMultiGameCommanderOverlay = true;
        viewModel.LockToMonitor = true;

        OverlayBehaviorPreferences persisted = new OverlayBehaviorSettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json")
        ).Load();
        Assert.True(persisted.KeepWhenGameLosesFocus);
        Assert.True(persisted.HideMultiGameCommanderOverlay);
        Assert.True(persisted.LockToMonitor);
    }

    [Fact]
    public void MonitorSelectionPersistsAndSurvivesDisconnectAndReconnect()
    {
        OverlayBehaviorViewModel viewModel = CreateViewModel();
        var primary = new ApplicationMonitorOption("DP-1", "Ultrawide (Primary)");
        var secondary = new ApplicationMonitorOption("DP-2", "Right display");
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        viewModel.SetAvailableMonitors([primary, secondary, secondary, new(null, "Invalid")]);
        Assert.Equal(3, viewModel.MonitorOptions.Count);
        viewModel.SelectedMonitor = secondary;

        Assert.Equal("DP-2", CreateViewModel().PreferredMonitorId);
        Assert.Contains(nameof(viewModel.SelectedMonitor), notifications);
        Assert.Contains(nameof(viewModel.PreferredMonitorId), notifications);
        viewModel.SetAvailableMonitors([primary]);
        Assert.Equal("DP-2", viewModel.SelectedMonitor.Id);
        Assert.Contains("not connected", viewModel.SelectedMonitor.DisplayName);
        Assert.Equal("DP-2", CreateViewModel().PreferredMonitorId);

        var reconnected = new ApplicationMonitorOption("DP-2", "Right display - moved above primary");
        viewModel.SetAvailableMonitors([primary, reconnected]);
        Assert.Same(reconnected, viewModel.SelectedMonitor);
        viewModel.SelectedMonitor = viewModel.MonitorOptions[0];
        Assert.Null(CreateViewModel().PreferredMonitorId);
    }

    [Fact]
    public void UnchangedOrNullSelectionDoesNotWriteSettings()
    {
        OverlayBehaviorViewModel viewModel = CreateViewModel();
        viewModel.SelectedMonitor = new ApplicationMonitorOption(null, "Automatic");
        viewModel.SelectedMonitor = null!;

        Assert.False(File.Exists(Path.Combine(temporaryDirectory, "ui-settings.json")));
        Assert.Null(viewModel.PreferredMonitorId);
        Assert.Throws<ArgumentNullException>(() => viewModel.SetAvailableMonitors(null!));
    }

    [Fact]
    public void MonitorSelectionRemainsActiveWhenSavingFails()
    {
        OverlayBehaviorViewModel viewModel = CreateViewModel();
        var monitor = new ApplicationMonitorOption("DP-1", "Ultrawide");
        viewModel.SetAvailableMonitors([monitor]);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        Directory.CreateDirectory(path);

        viewModel.SelectedMonitor = monitor;

        Assert.Equal("DP-1", viewModel.PreferredMonitorId);
        Assert.True(viewModel.HasSettingsStatus);
        Assert.Contains("could not be saved", viewModel.SettingsStatus);
        Directory.Delete(path);
        viewModel.SelectedMonitor = viewModel.MonitorOptions[0];
        Assert.False(viewModel.HasSettingsStatus);
    }

    [Fact]
    public void SessionSuppressionRequiresStatusCommanderAndActiveGameSession()
    {
        OverlayBehaviorViewModel viewModel = CreateViewModel();

        Assert.True(viewModel.ShouldSuppressForSession);

        viewModel.UpdateSessionContext(
            hasCurrentStatus: true,
            hasCurrentCommander: true,
            shutdown: false,
            atMainMenu: false
        );
        Assert.False(viewModel.ShouldSuppressForSession);

        viewModel.UpdateSessionContext(true, true, false, true);
        Assert.True(viewModel.ShouldSuppressForSession);

        viewModel.UpdateSessionContext(true, true, true, false);
        Assert.True(viewModel.ShouldSuppressForSession);

        viewModel.UpdateSessionContext(true, false, false, false);
        Assert.True(viewModel.ShouldSuppressForSession);

        viewModel.UpdateSessionContext(true, true, false, false, true);
        Assert.True(viewModel.ShouldSuppressForSession);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private OverlayBehaviorViewModel CreateViewModel()
    {
        Directory.CreateDirectory(temporaryDirectory);
        return new OverlayBehaviorViewModel(
            new OverlayBehaviorSettingsStore(Path.Combine(temporaryDirectory, "ui-settings.json"))
        );
    }
}
