using System.Diagnostics;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Input;

/// <summary>Drives the keyboard activation policy with synthetic activations from fake desktop, game-display, and portal sources.</summary>
public sealed class GlobalKeyboardHookServiceTests
{
    private static readonly long Second = Stopwatch.Frequency;

    [Fact]
    public async Task DispatchesConfiguredChordWhileApplicationIsActive()
    {
        await using var input = new Harness(EnabledSettings()) { ApplicationActive = true };
        await input.StartAsync();

        input.Desktop.Report("ALT X");
        input.Desktop.Report("ALT Q");

        GlobalInputActionTriggeredEventArgs triggered = Assert.Single(input.Actions);
        Assert.Equal(GlobalInputAction.ToggleAllVisibility, triggered.Action);
        Assert.Equal("ALT X", triggered.Chord);
        Assert.Null(input.Service.Diagnostics.SelectedSource);
        Assert.Contains("SrvSurvey has focus", input.Service.Diagnostics.FocusStatus);
    }

    [Fact]
    public async Task IgnoresChordOutsideApplicationAndGameContextAndWhileDisabled()
    {
        await using var input = new Harness(EnabledSettings());
        await input.StartAsync();

        input.Desktop.Report("ALT X");
        input.Service.Update(EnabledSettings() with { KeyboardEnabled = false });
        input.ApplicationActive = true;
        input.Desktop.Report("ALT X");

        Assert.Empty(input.Actions);
        Assert.Equal(
            "Input detection reset. Use a shortcut with Elite Dangerous focused.",
            input.Service.Diagnostics.LastInput
        );
        Assert.Equal("Global keyboard input is disabled.", input.Service.Status);
    }

    /// <summary>A game display that reports Elite active proves focus even when the desktop tracker cannot.</summary>
    [Fact]
    public async Task GameDisplayForegroundConfirmsFocusAndSelectsThatSource()
    {
        await using var input = new Harness(OverlaySettings("O"));
        await input.StartAsync();
        input.GameDisplay.State = new KeyboardSourceState(true) { IsGameForeground = true };

        input.GameDisplay.Report("O");
        input.Desktop.Report("O");

        Assert.Single(input.Actions);
        Assert.Equal(KeyboardInputMode.GameDisplay, input.Service.Diagnostics.SelectedSource);
        Assert.Equal(
            "Last shortcut: Toggle live overlay interaction from Game display.",
            input.Service.Diagnostics.LastInput
        );
    }

    /// <summary>Checks native Wayland routes compositor shortcuts while Elite runs and honors text entry and enablement.</summary>
    [Fact]
    public async Task NativeWaylandRoutesApprovedShortcutWhileEliteRunsAndStopsWhenItExits()
    {
        await using var input = new Harness(GlobalInputSettings.Default with { KeyboardEnabled = true })
        {
            GameRunning = true,
        };
        await input.StartAsync();

        input.Portal.Report("O", GlobalInputAction.ToggleOverlayInteraction);
        Assert.Single(input.Actions);
        input.Suppress = true;
        input.Portal.Report("O", GlobalInputAction.ToggleOverlayInteraction);
        input.Suppress = false;
        input.GameRunning = false;
        input.Portal.Report("O", GlobalInputAction.ToggleOverlayInteraction);
        input.GameRunning = true;
        input.Service.Update(GlobalInputSettings.Default);
        input.Portal.Report("O", GlobalInputAction.ToggleOverlayInteraction);

        Assert.Single(input.Actions);
    }

    /// <summary>Application-only and unconfirmed native Wayland activations never preselect the source later used in-game.</summary>
    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(false, false, 0)]
    public async Task SelectsOnlyWhenEliteFocusIsConfirmed(bool applicationActive, bool gameRunning, int earlyCount)
    {
        await using var input = new Harness(OverlaySettings("O"))
        {
            ApplicationActive = applicationActive,
            GameRunning = gameRunning,
        };
        await input.StartAsync();

        input.Portal.Report("O", GlobalInputAction.ToggleOverlayInteraction);
        Assert.Equal(earlyCount, input.Actions.Count);
        input.Clock += Second;
        input.ApplicationActive = false;
        input.Tracker.Focused = true;
        input.Desktop.Report("O");

        Assert.Equal(earlyCount + 1, input.Actions.Count);
        Assert.Equal(KeyboardInputMode.Desktop, input.Service.Diagnostics.SelectedSource);
    }

    /// <summary>A nested server's active window alone cannot prove desktop focus for a portal shortcut.</summary>
    [Fact]
    public async Task NestedFocusWithoutGameKeyDoesNotSelectPortal()
    {
        await using var input = new Harness(OverlaySettings("O")) { GameRunning = true };
        await input.StartAsync();
        input.GameDisplay.State = new KeyboardSourceState(true) { IsGameForeground = true };

        input.Portal.Report("O", GlobalInputAction.ToggleOverlayInteraction);

        Assert.Single(input.Actions);
        Assert.Null(input.Service.Diagnostics.SelectedSource);
        Assert.Contains("unconfirmed", input.Service.Diagnostics.FocusStatus);
    }

    /// <summary>A recent confirmed game-display key lets the compositor's duplicate of that key prove focus.</summary>
    [Fact]
    public async Task RecentGameDisplayKeyCorroboratesPortalFocus()
    {
        await using var input = new Harness(OverlaySettings("O"));
        await input.StartAsync();
        input.GameDisplay.State = new KeyboardSourceState(true) { IsGameForeground = true };

        input.GameDisplay.Report("O");
        input.Clock += Second / 10;
        input.Portal.Report("O", GlobalInputAction.ToggleOverlayInteraction);
        Assert.Single(input.Actions);
        Assert.Equal(KeyboardInputMode.WaylandPortal, input.Service.Diagnostics.SelectedSource);

        input.Clock += Second;
        input.Portal.Report("O", GlobalInputAction.ToggleOverlayInteraction);
        Assert.Single(input.Actions);
    }

    /// <summary>Checks desktop and portal events cause a single action and a lost portal restores the desktop path.</summary>
    [Fact]
    public async Task PortalAndDesktopDeduplicateAndRecover()
    {
        await using var input = new Harness(OverlaySettings("O"));
        input.Tracker.Focused = true;
        await input.StartAsync();

        input.Desktop.Report("O");
        input.Portal.Report("O", GlobalInputAction.ToggleOverlayInteraction);
        Assert.Single(input.Actions);
        Assert.Equal(KeyboardInputMode.WaylandPortal, input.Service.Diagnostics.SelectedSource);

        input.Portal.Disconnect();
        input.Clock += Second / 10;
        input.Desktop.Report("O");

        Assert.Equal(2, input.Actions.Count);
        Assert.False(input.Service.Diagnostics.PortalAvailable);
    }

    /// <summary>A partially approved portal cannot lock out the raw source needed for the remaining app shortcuts.</summary>
    [Fact]
    public async Task PartialPortalApprovalDoesNotChooseTheGlobalSource()
    {
        await using var input = new Harness(TwoBindingSettings());
        input.Tracker.Focused = true;
        input.Portal.State = new KeyboardSourceState(true) { CanServeAllBindings = false };
        await input.StartAsync();

        input.Portal.Report("O", GlobalInputAction.ToggleOverlayInteraction);
        input.Clock += Second;
        input.Desktop.Report("P");

        Assert.Equal(
            [GlobalInputAction.ToggleOverlayInteraction, GlobalInputAction.MapZoomIn],
            input.Actions.Select(action => action.Action)
        );
        Assert.False(input.Service.Diagnostics.PortalAvailable);
    }

    /// <summary>An in-game activation chooses a shared provider for other shortcuts instead of learning one provider per action.</summary>
    [Fact]
    public async Task SelectedSourceAppliesToEveryKeyboardShortcut()
    {
        await using var input = new Harness(TwoBindingSettings());
        input.Tracker.Focused = true;
        await input.StartAsync();

        input.Portal.Report("O", GlobalInputAction.ToggleOverlayInteraction);
        input.Desktop.Report("P");
        Assert.Single(input.Actions);
        input.Portal.Report("P", GlobalInputAction.MapZoomIn);

        Assert.Equal(
            [GlobalInputAction.ToggleOverlayInteraction, GlobalInputAction.MapZoomIn],
            input.Actions.Select(action => action.Action)
        );
    }

    /// <summary>Checks one game-learned source governs every shortcut while application-only presses remain independent.</summary>
    [Fact]
    public async Task LearnsWorkingSourcesAndDoesNotReplayCrossSourcePresses()
    {
        await using var input = new Harness(TwoBindingSettings());
        input.Tracker.Focused = true;
        await input.StartAsync();

        Assert.True(input.Accepts(input.Desktop, "O"));
        Assert.False(input.Accepts(input.GameDisplay, "O", 1));
        Assert.False(input.Accepts(input.Portal, "O", 1, GlobalInputAction.ToggleOverlayInteraction));
        input.Clock += Second;
        Assert.False(input.Accepts(input.Desktop, "O"));
        Assert.True(input.Accepts(input.Portal, "O", 0, GlobalInputAction.ToggleOverlayInteraction));
        Assert.True(input.Accepts(input.Portal, "O", 1, GlobalInputAction.ToggleOverlayInteraction));
        Assert.False(input.Accepts(input.GameDisplay, "P"));
        Assert.True(input.Accepts(input.Portal, "P", 0, GlobalInputAction.MapZoomIn));
        input.ApplicationActive = true;
        Assert.True(input.Accepts(input.Desktop, "O", Second));
        input.ApplicationActive = false;
        input.Portal.Disconnect();
        Assert.True(input.Accepts(input.Desktop, "O", Second));
        Assert.Equal(KeyboardInputMode.Desktop, input.Service.Diagnostics.SelectedSource);
        input.Service.ResetDetection();
        Assert.Null(input.Service.Diagnostics.SelectedSource);
        Assert.Contains("reset", input.Service.Diagnostics.LastInput);
    }

    /// <summary>Recovers all shortcuts from a connected but silent preferred provider after repeated alternative presses.</summary>
    [Fact]
    public async Task SilentSelectedProviderAllowsRecoveryFromRepeatedAlternativePresses()
    {
        await using var input = new Harness(TwoBindingSettings());
        input.Tracker.Focused = true;
        await input.StartAsync();

        Assert.True(input.Accepts(input.Portal, "P", 0, GlobalInputAction.MapZoomIn));
        Assert.False(input.Accepts(input.Desktop, "P", Second));
        Assert.True(input.Accepts(input.Desktop, "P", Second));
        Assert.True(input.Accepts(input.Desktop, "O", Second));
        Assert.Equal(KeyboardInputMode.Desktop, input.Service.Diagnostics.SelectedSource);
    }

    /// <summary>Duplicate, out-of-game, and partially granted reports cannot prove preferred input has failed.</summary>
    [Fact]
    public async Task RecoveryRequiresSeparatedInGameEvidenceAndStopsWhenPreferredWorks()
    {
        await using var input = new Harness(TwoBindingSettings()) { Clock = 10 * Second };
        input.Tracker.Focused = true;
        await input.StartAsync();

        input.Accepts(input.Portal, "P", 0, GlobalInputAction.MapZoomIn);
        Assert.False(input.Accepts(input.Desktop, "P", -1));
        Assert.False(input.Accepts(input.Desktop, "P", 2));
        Assert.False(input.Accepts(input.Desktop, "P", Second));
        Assert.False(input.Accepts(input.Desktop, "P", 1));
        input.ApplicationActive = true;
        Assert.True(input.Accepts(input.Desktop, "P", 1));
        input.ApplicationActive = false;
        Assert.Equal(KeyboardInputMode.WaylandPortal, input.Service.Diagnostics.SelectedSource);
        input.GameDisplay.State = new KeyboardSourceState(true) { CanServeAllBindings = false };
        Assert.False(input.Accepts(input.GameDisplay, "P", -2));
        Assert.True(input.Accepts(input.Portal, "P", Second, GlobalInputAction.MapZoomIn));
        Assert.False(input.Accepts(input.Desktop, "P", Second));
        Assert.Equal(KeyboardInputMode.WaylandPortal, input.Service.Diagnostics.SelectedSource);
        input.Desktop.Disconnect();
        Assert.Equal(KeyboardInputMode.WaylandPortal, input.Service.Diagnostics.SelectedSource);
        input.Portal.Disconnect();
        Assert.Null(input.Service.Diagnostics.SelectedSource);
    }

    /// <summary>Compares duplicate reports only for the same shortcut, independent of the global source selection.</summary>
    [Theory]
    [InlineData("O", 0, false)]
    [InlineData("o", 0, false)]
    [InlineData("X", 0, true)]
    [InlineData("O", 1, true)]
    [InlineData("O", -1, true)]
    public async Task ComparesOnlyMatchingRecentActivations(string chord, int seconds, bool expected)
    {
        await using var input = new Harness(TwoBindingSettings()) { Clock = 2 * Second, ApplicationActive = true };
        await input.StartAsync();

        input.Accepts(input.Desktop, "O", 0, GlobalInputAction.MapZoomIn);

        Assert.Equal(expected, input.Accepts(input.Portal, chord, seconds * Second, GlobalInputAction.MapZoomIn));
    }

    /// <summary>Forces one source for every action and context, without silently falling back to another listener.</summary>
    [Theory]
    [InlineData(KeyboardInputMode.Desktop, 0)]
    [InlineData(KeyboardInputMode.GameDisplay, 1)]
    [InlineData(KeyboardInputMode.WaylandPortal, 2)]
    public async Task ManualModeControlsAllShortcuts(KeyboardInputMode mode, int sourceValue)
    {
        await using var input = new Harness(TwoBindingSettings() with { KeyboardSource = mode });
        await input.StartAsync();

        foreach (FakeKeyboardSource source in input.Sources)
        {
            input.ApplicationActive = true;
            Assert.Equal((int)source.Kind == sourceValue, input.Accepts(source, "P", Second));
            input.ApplicationActive = false;
            input.Tracker.Focused = true;
            Assert.Equal((int)source.Kind == sourceValue, input.Accepts(source, "O", Second));
            input.Tracker.Focused = false;
        }
        input.Service.ResetDetection();
        Assert.Equal(mode, input.Service.Diagnostics.SelectedSource);
        input.Service.Update(TwoBindingSettings());
        Assert.Null(input.Service.Diagnostics.SelectedSource);
        input.Service.Update(TwoBindingSettings() with { KeyboardSource = (KeyboardInputMode)99 });
        Assert.Null(input.Service.Diagnostics.SelectedSource);
    }

    /// <summary>Reset releases held key state and learned selection without re-registering desktop permissions.</summary>
    [Fact]
    public async Task ServiceResetAndManualOverridePreserveBindingsAndApproval()
    {
        GlobalInputSettings settings = MapSettings("O") with { KeyboardSource = KeyboardInputMode.WaylandPortal };
        await using var input = new Harness(settings) { GameRunning = true };
        input.Tracker.Focused = true;
        input.GameDisplay.State = new KeyboardSourceState(false);
        await input.StartAsync();

        input.Desktop.Report("O");
        Assert.Empty(input.Actions);
        Assert.Contains("ignored", input.Service.Diagnostics.LastInput);
        input.Portal.Report("O", GlobalInputAction.MapZoomIn);
        Assert.Single(input.Actions);
        Assert.Equal(KeyboardInputMode.WaylandPortal, input.Service.Diagnostics.SelectedSource);
        int updates = input.Portal.Updates;
        input.Service.ResetDetection();
        Assert.Equal(updates, input.Portal.Updates);
        Assert.All(input.Sources, source => Assert.Equal(1, source.Resets));
        Assert.Contains("reset", input.Service.Diagnostics.LastInput);
        input.Service.Update(settings with { KeyboardSource = KeyboardInputMode.Desktop });
        input.Clock += Second;
        input.Desktop.Report("O");

        Assert.Equal(2, input.Actions.Count);
        KeyboardInputDiagnostics diagnostics = input.Service.Diagnostics;
        Assert.Equal(KeyboardInputMode.Desktop, diagnostics.SelectedSource);
        Assert.True(diagnostics.DesktopAvailable);
        Assert.False(diagnostics.GameDisplayAvailable);
        Assert.True(diagnostics.PortalAvailable);
        Assert.Contains("focus confirmed", diagnostics.FocusStatus);
    }

    /// <summary>Source status and desktop settings surface structurally, and settings open only on explicit request.</summary>
    [Fact]
    public async Task ServiceExposesSourceStatusAndDesktopSettingsWithoutAutomaticOpening()
    {
        await using var input = new Harness(EnabledSettings());
        var offered = new DesktopShortcutSettingsState(true, "Desktop menu active.", ["Overlay interaction: Super+O"]);
        input.Portal.State = new KeyboardSourceState(true) { DesktopShortcutSettings = offered };
        input.GameDisplay.State = new KeyboardSourceState(true) { GameDisplay = new GameDisplayConnection(":2", 123) };
        int statuses = 0;
        input.Service.StatusChanged += (_, _) => statuses++;
        await input.StartAsync();

        Assert.Equal(0, input.Portal.SettingsRequests);
        Assert.Equal(offered, input.Service.Diagnostics.DesktopShortcutSettings);
        Assert.Equal("Game display :2.", input.Service.Diagnostics.GameDisplayStatus);
        await input.Service.OpenDesktopShortcutSettingsAsync();
        Assert.Equal(1, input.Portal.SettingsRequests);
        Assert.All(input.Sources, source => Assert.Equal(1, source.Starts));
        Assert.True(input.Service.IsRunning);

        int before = statuses;
        input.Portal.ReportStatus(string.Empty);
        Assert.Equal("Global keyboard input is ready to start.", input.Service.Status);
        Assert.True(statuses > before);
        input.Portal.ReportStatus("Portal active");
        Assert.Equal("Portal active", input.Service.Status);
        input.Service.Update(EnabledSettings() with { KeyboardEnabled = false });
        input.Portal.ReportStatus("Portal reconnecting");
        Assert.Equal("Global keyboard input is disabled.", input.Service.Status);
        Assert.All(input.Sources, source => Assert.Equal(1, source.Updates));
        Assert.Equal(1, input.Portal.SettingsRequests);
    }

    /// <summary>Startup focus waits for every source's silent discovery, and no settings source means no request.</summary>
    [Fact]
    public async Task StartupWaitsForEverySourceAndDisposesThemBeforeTheTracker()
    {
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var input = new Harness(EnabledSettings() with { KeyboardEnabled = false });
        input.Portal.StartupReady = restored.Task;
        await input.StartAsync();

        Task ready = input.Service.StartupReady;
        Assert.False(ready.IsCompleted);
        restored.SetResult();
        await ready.WaitAsync(TimeSpan.FromSeconds(2));
        await input.Service.OpenDesktopShortcutSettingsAsync();
        Assert.Equal(0, input.Portal.SettingsRequests);
        Assert.Equal("Global keyboard input is disabled.", input.Service.Status);

        await input.DisposeAsync();
        await input.Service.DisposeAsync();
        Assert.All(input.Sources, source => Assert.True(source.Disposed));
        Assert.True(input.Tracker.IsDisposed);
        Assert.Throws<ObjectDisposedException>(input.Service.Start);
        Assert.Throws<ObjectDisposedException>(() => input.Service.Update(EnabledSettings()));
    }

    /// <summary>A different Elite process or game display releases learned selection and every source's held keys.</summary>
    [Fact]
    public async Task GameChangeRelearnsSourceAndRefreshesFocusDiagnostics()
    {
        await using var input = new Harness(OverlaySettings("O"));
        input.Tracker.Focused = true;
        await input.StartAsync();
        Assert.Equal("Elite Dangerous focus confirmed.", input.Service.Diagnostics.FocusStatus);
        input.Desktop.Report("O");
        Assert.Equal(KeyboardInputMode.Desktop, input.Service.Diagnostics.SelectedSource);

        input.GameDisplay.State = new KeyboardSourceState(true) { GameDisplay = new GameDisplayConnection(":2", 7) };
        await WaitForAsync(() => input.Desktop.Resets > 0);

        Assert.Null(input.Service.Diagnostics.SelectedSource);
        Assert.All(input.Sources, source => Assert.Equal(1, source.Resets));
        input.Tracker.Snapshot = TestGameWindowTracker.Foreground with { IsForeground = false };
        await WaitForAsync(() => input.Service.Diagnostics.FocusStatus == "Elite Dangerous is not focused.");
        input.Tracker.Snapshot = GameWindowSnapshot.Unavailable;
        await WaitForAsync(() => input.Service.Diagnostics.FocusStatus.Contains("cannot be confirmed"));
        input.GameDisplay.State = new KeyboardSourceState(false);
        await WaitForAsync(() => input.Service.Diagnostics.FocusStatus == "Elite Dangerous is not running.");
    }

    private static GlobalInputSettings EnabledSettings() =>
        GlobalInputSettings.Default with
        {
            KeyboardEnabled = true,
            Bindings = new Dictionary<GlobalInputAction, string> { [GlobalInputAction.ToggleAllVisibility] = "ALT X" },
        };

    private static GlobalInputSettings OverlaySettings(string chord) =>
        GlobalInputSettings.Default with
        {
            KeyboardEnabled = true,
            Bindings = new Dictionary<GlobalInputAction, string>
            {
                [GlobalInputAction.ToggleOverlayInteraction] = chord,
            },
        };

    private static GlobalInputSettings MapSettings(string chord) =>
        GlobalInputSettings.Default with
        {
            KeyboardEnabled = true,
            Bindings = new Dictionary<GlobalInputAction, string> { [GlobalInputAction.MapZoomIn] = chord },
        };

    private static GlobalInputSettings TwoBindingSettings() =>
        GlobalInputSettings.Default with
        {
            KeyboardEnabled = true,
            Bindings = new Dictionary<GlobalInputAction, string>
            {
                [GlobalInputAction.ToggleOverlayInteraction] = "O",
                [GlobalInputAction.MapZoomIn] = "P",
            },
        };

    /// <summary>Waits for the once-per-second focus refresh without hanging a broken test.</summary>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    /// <summary>Owns one service with fake desktop, game-display, and portal sources plus controllable context.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        public Harness(GlobalInputSettings settings)
        {
            Service = new GlobalKeyboardHookService(
                settings,
                Sources,
                Tracker,
                () => ApplicationActive,
                new AdditionalKeyboardInput(IsGameRunning: () => GameRunning, SuppressShortcuts: () => Suppress),
                () => Clock
            );
            Service.ActionTriggered += (_, args) => Actions.Add(args);
        }

        public FakeKeyboardSource GameDisplay { get; } = new(KeyboardInputSource.NestedDisplay);
        public FakeKeyboardSource Portal { get; } = new(KeyboardInputSource.Portal);
        public FakeKeyboardSource Desktop { get; } = new(KeyboardInputSource.Desktop);
        public FakeKeyboardSource[] Sources => [GameDisplay, Portal, Desktop];
        public TestGameWindowTracker Tracker { get; } = new();
        public GlobalKeyboardHookService Service { get; }
        public List<GlobalInputActionTriggeredEventArgs> Actions { get; } = [];
        public long Clock { get; set; } = Second;
        public bool ApplicationActive { get; set; }
        public bool GameRunning { get; set; }
        public bool Suppress { get; set; }

        /// <summary>Starts the service and waits for its first focus refresh so later reports are not overwritten.</summary>
        public async Task StartAsync()
        {
            Service.Start();
            await WaitForAsync(() => Service.Diagnostics.FocusStatus != "Waiting for Elite Dangerous.");
        }

        /// <summary>Advances the clock, reports one activation, and returns whether it dispatched an action.</summary>
        public bool Accepts(FakeKeyboardSource source, string chord, long elapsed = 0, GlobalInputAction? action = null)
        {
            Clock += elapsed;
            int before = Actions.Count;
            source.Report(chord, action);
            return Actions.Count > before;
        }

        public ValueTask DisposeAsync() => Service.DisposeAsync();
    }
}
