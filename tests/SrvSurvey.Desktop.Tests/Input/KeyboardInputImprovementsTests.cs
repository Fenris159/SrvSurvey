using System.Diagnostics;
using SharpHook.Data;
using SharpHook.Testing;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class KeyboardInputImprovementsTests
{
    /// <summary>Forces one source for every action and context, without silently falling back to another listener.</summary>
    [Theory]
    [InlineData(KeyboardInputMode.Desktop, 0)]
    [InlineData(KeyboardInputMode.GameDisplay, 1)]
    [InlineData(KeyboardInputMode.WaylandPortal, 2)]
    public void ManualModeControlsAllShortcuts(KeyboardInputMode mode, int sourceValue)
    {
        var source = (KeyboardInputSource)sourceValue;
        var selector = new KeyboardInputSelector();
        selector.SetMode(mode);
        long start = Stopwatch.GetTimestamp();
        foreach (KeyboardInputSource candidate in Enum.GetValues<KeyboardInputSource>())
        {
            Assert.Equal(
                candidate == source,
                selector.TryAccept(candidate, GlobalInputAction.MapZoomIn, "+", start, learn: false)
            );
            Assert.Equal(
                candidate == source,
                selector.TryAccept(
                    candidate,
                    GlobalInputAction.ToggleOverlayInteraction,
                    "O",
                    start + Stopwatch.Frequency
                )
            );
        }
        selector.Reset();
        Assert.Equal(mode, selector.SelectedMode);
        selector.SetMode(KeyboardInputMode.Automatic);
        Assert.Null(selector.SelectedMode);
        Assert.True(selector.TryAccept(KeyboardInputSource.Desktop, GlobalInputAction.MapZoomIn, "+", start));
        Assert.Equal(KeyboardInputMode.Desktop, selector.SelectedMode);
        selector.SetMode((KeyboardInputMode)99);
        Assert.Null(selector.SelectedMode);
    }

    /// <summary>Duplicate, out-of-game, and partially granted reports cannot prove preferred input has failed.</summary>
    [Fact]
    public void RecoveryRequiresSeparatedInGameEvidenceAndStopsWhenPreferredWorks()
    {
        var selector = new KeyboardInputSelector();
        long start = Stopwatch.GetTimestamp();
        selector.TryAccept(KeyboardInputSource.Portal, GlobalInputAction.MapZoomIn, "+", start);
        Assert.False(selector.TryAccept(KeyboardInputSource.Desktop, GlobalInputAction.MapZoomIn, "+", start - 1));
        Assert.False(selector.TryAccept(KeyboardInputSource.Desktop, GlobalInputAction.MapZoomIn, "+", start + 1));
        long later = start + Stopwatch.Frequency;
        Assert.False(selector.TryAccept(KeyboardInputSource.Desktop, GlobalInputAction.MapZoomIn, "+", later));
        Assert.False(selector.TryAccept(KeyboardInputSource.Desktop, GlobalInputAction.MapZoomIn, "+", later + 1));
        Assert.True(
            selector.TryAccept(KeyboardInputSource.Desktop, GlobalInputAction.MapZoomIn, "+", later + 2, learn: false)
        );
        Assert.Equal(KeyboardInputMode.WaylandPortal, selector.SelectedMode);
        Assert.False(
            selector.TryAccept(
                KeyboardInputSource.NestedDisplay,
                GlobalInputAction.MapZoomIn,
                "+",
                later,
                canLearn: false
            )
        );
        Assert.True(
            selector.TryAccept(
                KeyboardInputSource.Portal,
                GlobalInputAction.MapZoomIn,
                "+",
                later + Stopwatch.Frequency
            )
        );
        Assert.False(
            selector.TryAccept(
                KeyboardInputSource.Desktop,
                GlobalInputAction.MapZoomIn,
                "+",
                later + 2 * Stopwatch.Frequency
            )
        );
        Assert.Equal(KeyboardInputMode.WaylandPortal, selector.SelectedMode);
        selector.Reset(KeyboardInputSource.Desktop);
        Assert.Equal(KeyboardInputMode.WaylandPortal, selector.SelectedMode);
        selector.Reset(KeyboardInputSource.Portal);
        Assert.Null(selector.SelectedMode);
    }

    /// <summary>Reset releases held key state and learned selection without re-registering portal permissions.</summary>
    [Fact]
    public async Task ServiceResetAndManualOverridePreserveBindingsAndApproval()
    {
        using var hook = new TestGlobalHook(TestThreadingMode.Simple);
        var portal = new Portal();
        GlobalInputSettings settings = GlobalInputSettings.Default with
        {
            KeyboardEnabled = true,
            KeyboardSource = KeyboardInputMode.WaylandPortal,
            Bindings = new Dictionary<GlobalInputAction, string> { [GlobalInputAction.MapZoomIn] = "O" },
        };
        await using var service = new GlobalKeyboardHookService(
            settings,
            OverlayHostKind.LinuxXWayland,
            new Tracker(),
            () => false,
            () => hook,
            additionalInput: new(PortalInput: portal, IsGameRunning: () => true)
        );
        int received = 0;
        service.ActionTriggered += (_, _) => received++;
        service.Start();
        hook.SimulateKeyPress(KeyCode.VcO);
        Assert.Equal(0, received);
        Assert.Contains("ignored", service.Diagnostics.LastInput);
        portal.Emit();
        Assert.Equal(1, received);
        Assert.Equal(KeyboardInputMode.WaylandPortal, service.Diagnostics.SelectedSource);
        int registrations = portal.Updates;
        service.ResetDetection();
        Assert.Equal(registrations, portal.Updates);
        Assert.Contains("reset", service.Diagnostics.LastInput);
        service.Update(settings with { KeyboardSource = KeyboardInputMode.Desktop });
        hook.SimulateKeyPress(KeyCode.VcO);
        Assert.Equal(2, received);
        Assert.Equal(KeyboardInputMode.Desktop, service.Diagnostics.SelectedSource);
        Assert.True(service.Diagnostics.DesktopAvailable);
        Assert.False(service.Diagnostics.GameDisplayAvailable);
        Assert.True(service.Diagnostics.PortalAvailable);
        Assert.Contains("focus confirmed", service.Diagnostics.FocusStatus);
    }

    /// <summary>A nested server's active window alone cannot prove desktop focus for a portal shortcut.</summary>
    [Fact]
    public async Task NestedFocusWithoutGameKeyDoesNotSelectPortal()
    {
        using var hook = new TestGlobalHook(TestThreadingMode.Simple);
        var portal = new Portal();
        var game = new IdleGameDisplay();
        await using var service = new GlobalKeyboardHookService(
            GlobalInputSettings.Default with
            {
                KeyboardEnabled = true,
            },
            OverlayHostKind.LinuxXWayland,
            new UnavailableTracker(),
            () => false,
            () => hook,
            additionalInput: new(GameKeyboardInput: game, PortalInput: portal, IsGameRunning: () => true)
        );
        int received = 0;
        service.ActionTriggered += (_, _) => received++;
        service.Start();
        await game.Read.Task.WaitAsync(TimeSpan.FromSeconds(2));
        portal.Emit();
        Assert.Equal(1, received);
        Assert.Null(service.Diagnostics.SelectedSource);
        Assert.Contains("unconfirmed", service.Diagnostics.FocusStatus);
    }

    /// <summary>Service discovery reports menu status but opens desktop settings only through the explicit handler.</summary>
    [Fact]
    public async Task ServiceExposesDesktopSettingsWithoutAutomaticOpening()
    {
        using var hook = new TestGlobalHook(TestThreadingMode.Simple);
        var portal = new Portal();
        await using var service = new GlobalKeyboardHookService(
            GlobalInputSettings.Default with
            {
                KeyboardEnabled = true,
            },
            OverlayHostKind.LinuxXWayland,
            new Tracker(),
            () => true,
            () => hook,
            additionalInput: new(PortalInput: portal)
        );
        int statuses = 0;
        service.StatusChanged += (_, _) => statuses++;
        service.Start();
        Assert.Equal(0, portal.SettingsCalls);
        Assert.True(service.Diagnostics.CanOpenDesktopShortcutSettings);
        Assert.Equal("Desktop menu active.", service.Diagnostics.DesktopShortcutSettingsStatus);
        await service.OpenDesktopShortcutSettingsAsync();
        Assert.Equal(1, portal.SettingsCalls);
        int before = statuses;
        string listenerStatus = service.Status;
        portal.NotifyMenuStatus();
        Assert.Equal(listenerStatus, service.Status);
        Assert.True(statuses > before);
        service.Update(GlobalInputSettings.Default with { KeyboardEnabled = false });
        Assert.Equal(1, portal.SettingsCalls);
    }

    /// <summary>Models nested focus without any actual keyboard input from that server.</summary>
    private sealed class IdleGameDisplay : IGameKeyboardInput
    {
        public TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Signals discovery and reports idle nested focus without a press.</summary>
        public GameKeyboardEventBatch ReadEvents(bool enabled)
        {
            Read.TrySetResult();
            return new(false, [], enabled);
        }

        /// <summary>No native resources are held by the fake input source.</summary>
        public void Dispose() { }
    }

    /// <summary>Models a native Wayland game whose desktop focus cannot be queried.</summary>
    private sealed class UnavailableTracker : IGameWindowTracker
    {
        /// <summary>Returns an unavailable desktop window snapshot.</summary>
        public GameWindowSnapshot GetSnapshot() => GameWindowSnapshot.Unavailable;

        /// <summary>No native resources are held by the fake tracker.</summary>
        public void Dispose() { }
    }

    /// <summary>Supplies confirmed desktop game focus.</summary>
    private sealed class Tracker : IGameWindowTracker
    {
        /// <summary>Returns a stable visible Elite window.</summary>
        public GameWindowSnapshot GetSnapshot() => new(1, 123, new Avalonia.PixelRect(0, 0, 100, 100), true, true);

        /// <summary>The fake tracker owns no native connection.</summary>
        public void Dispose() { }
    }

    /// <summary>Records portal updates while providing accepted shortcut signals.</summary>
    private sealed class Portal : IGlobalShortcutInput
    {
        public event EventHandler<GlobalInputActionTriggeredEventArgs>? ActionTriggered;
        public event Action<string, bool>? StatusChanged;
        public bool IsRunning => true;
        public int Updates { get; private set; }
        public bool CanOpenSettings => true;
        public string SettingsStatus => "Desktop menu active.";
        public int SettingsCalls { get; private set; }

        /// <summary>Records an explicit settings request.</summary>
        public Task OpenSettingsAsync()
        {
            SettingsCalls++;
            return Task.CompletedTask;
        }

        /// <summary>Reports capability changes without overwriting ordinary listener status.</summary>
        public void NotifyMenuStatus() => StatusChanged?.Invoke(string.Empty, true);

        /// <summary>Captures configuration updates and announces the connected source.</summary>
        public void Update(GlobalInputSettings settings)
        {
            Updates++;
            StatusChanged?.Invoke("Portal active", true);
        }

        /// <summary>Emits the test's approved action.</summary>
        public void Emit() => ActionTriggered?.Invoke(this, new(GlobalInputAction.MapZoomIn, "O"));

        /// <summary>No native state needs disposal.</summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
