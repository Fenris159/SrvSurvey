using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class DesktopBehaviorViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-desktop-behavior-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public void StartupAndMinimizeFocusTheCurrentCommanderWindow()
    {
        var switcher = new RecordingSwitcher();
        var viewModel = CreateViewModel(switcher);

        Assert.True(viewModel.RequestStartupFocus());
        Assert.True(viewModel.RequestMinimizeFocus());

        Assert.Equal(2, switcher.CurrentActivationCount);
        Assert.Equal(0, switcher.NextActivationCount);
    }

    [Fact]
    public void OnlyLiveFsdJumpUsesOptionalFocusPolicy()
    {
        var switcher = new RecordingSwitcher();
        var viewModel = CreateViewModel(switcher);
        viewModel.FocusGameAfterFsdJump = true;
        var jump = Parse("{\"event\":\"FSDJump\"}");

        viewModel.ApplyJournalEvents([jump], isBootstrapRead: true);
        viewModel.ApplyJournalEvents(
            [Parse("{\"event\":\"Scan\"}")],
            isBootstrapRead: false);
        Assert.Equal(0, switcher.CurrentActivationCount);

        viewModel.ApplyJournalEvents([jump], isBootstrapRead: false);

        Assert.Equal(1, switcher.CurrentActivationCount);
        Assert.Equal(0, switcher.NextActivationCount);
    }

    [Fact]
    public void MissingGameWindowReportsNonFatalStatus()
    {
        var switcher = new RecordingSwitcher { Result = false };
        var viewModel = CreateViewModel(switcher);

        Assert.False(viewModel.RequestStartupFocus());

        Assert.Contains("no matching game window", viewModel.StatusMessage);
    }

    [Fact]
    public void ApplicationWindowPreferencesPersistAndSignalPlacementChange()
    {
        var viewModel = CreateViewModel(new RecordingSwitcher());
        var monitor = new ApplicationMonitorOption(
            "\\\\.\\DISPLAY2",
            "DISPLAY2 · 2560 × 1440 · 100%");
        viewModel.SetAvailableMonitors([monitor]);
        var changeCount = 0;
        viewModel.ApplicationWindowPreferencesChanged += (_, _) =>
            changeCount++;

        viewModel.SelectedMonitor = monitor;
        viewModel.SelectedApplicationWindowScale =
            ApplicationWindowScaleCatalog.All.Single(option =>
                option.Percent == 125);
        viewModel.RememberApplicationWindowPosition(
            new ApplicationWindowPosition(2100, 75, "\\\\.\\DISPLAY2"));

        Assert.Equal(2, changeCount);
        Assert.Same(monitor, viewModel.SelectedMonitor);
        Assert.Equal(125, viewModel.SelectedApplicationWindowScale.Percent);
        var saved = new DesktopBehaviorSettingsStore(Path.Combine(
            temporaryDirectory,
            "ui-settings.json")).Load();
        Assert.Equal("\\\\.\\DISPLAY2", saved.PreferredMonitorId);
        Assert.Equal(125, saved.ApplicationWindowScalePercent);
        Assert.Equal(
            new ApplicationWindowPosition(2100, 75, "\\\\.\\DISPLAY2"),
            saved.LastApplicationWindowPosition);
    }

    [Fact]
    public void ReducedMotionPreferencePersistsWithoutPlacementNotification()
    {
        var viewModel = CreateViewModel(new RecordingSwitcher());
        var placementChangeCount = 0;
        viewModel.ApplicationWindowPreferencesChanged += (_, _) =>
            placementChangeCount++;

        viewModel.ReduceMotion = true;

        Assert.True(viewModel.ReduceMotion);
        Assert.True(new DesktopBehaviorSettingsStore(Path.Combine(
            temporaryDirectory,
            "ui-settings.json")).Load().ReduceMotion);
        Assert.Equal(0, placementChangeCount);
    }

    [Fact]
    public void SavedMonitorFallsBackToAutomaticBeforeMonitorEnumeration()
    {
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        new DesktopBehaviorSettingsStore(path).Save(
            new DesktopBehaviorPreferences(
                true,
                true,
                false,
                false,
                "DP-2",
                100));
        var viewModel = CreateViewModel(new RecordingSwitcher());

        Assert.Same(ApplicationMonitorOption.Automatic, viewModel.SelectedMonitor);
    }

    [Fact]
    public void DisconnectedSavedMonitorRemainsSelectedWithFallbackLabel()
    {
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        new DesktopBehaviorSettingsStore(path).Save(
            new DesktopBehaviorPreferences(
                true,
                true,
                false,
                false,
                "DP-2",
                100));
        var viewModel = CreateViewModel(new RecordingSwitcher());

        viewModel.SetAvailableMonitors(
        [
            new ApplicationMonitorOption("DP-1", "DP-1 (Primary)"),
        ]);

        Assert.Equal("DP-2", viewModel.SelectedMonitor.Id);
        Assert.Contains("not connected", viewModel.SelectedMonitor.DisplayName);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private DesktopBehaviorViewModel CreateViewModel(
        IGameWindowSwitcher switcher)
    {
        return new DesktopBehaviorViewModel(
            new DesktopBehaviorSettingsStore(Path.Combine(
                temporaryDirectory,
                "ui-settings.json")),
            switcher);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out var result, out _));
        return result!;
    }

    private sealed class RecordingSwitcher : IGameWindowSwitcher
    {
        public bool Result { get; init; } = true;

        public int CurrentActivationCount { get; private set; }

        public int NextActivationCount { get; private set; }

        public int GetAvailableWindowCount() => 1;

        public bool TryActivateCurrent()
        {
            CurrentActivationCount++;
            return Result;
        }

        public bool TryActivateNext()
        {
            NextActivationCount++;
            return Result;
        }

        public void Dispose()
        {
        }
    }
}
