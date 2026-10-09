using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class KeyboardInputSettingsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "srv-keyboard-settings-" + Guid.NewGuid());

    /// <summary>Persists one source per profile without changing shortcuts or another commander's preference.</summary>
    [Theory]
    [InlineData(KeyboardInputMode.Desktop)]
    [InlineData(KeyboardInputMode.GameDisplay)]
    [InlineData(KeyboardInputMode.WaylandPortal)]
    public void SourceSelectionPersistsAndResetPreservesBindings(KeyboardInputMode mode)
    {
        var store = new GlobalInputSettingsStore(Path.Combine(root, "first.json"));
        GlobalInputSettingsViewModel viewModel = Create(store);
        viewModel.KeyboardEnabled = true;
        viewModel.Bindings.Single(binding => binding.Definition.Action == GlobalInputAction.MapZoomIn).Chord = "CTRL O";
        viewModel.UpdateKeyboardDiagnostics(
            new(null, true, true, true, "Last shortcut received.", "Elite focus confirmed.", "Game display :2.")
        );
        viewModel.SelectedKeyboardSource = viewModel.KeyboardSourceOptions.Single(option => option.Mode == mode);
        Assert.Equal(mode, store.Load().KeyboardSource);
        Assert.Contains("Manual", viewModel.KeyboardSourceStatus);
        Assert.Equal(mode, Create(store).SelectedKeyboardSource.Mode);
        Assert.Equal(
            KeyboardInputMode.Automatic,
            new GlobalInputSettingsStore(Path.Combine(root, "second.json")).Load().KeyboardSource
        );
        int resets = 0;
        viewModel.KeyboardDetectionResetRequested += (_, _) => resets++;
        viewModel.ResetKeyboardDetectionCommand.Execute(null);
        viewModel.ResetKeyboardDetectionCommand.Execute(null);
        Assert.Equal(2, resets);
        Assert.Equal(KeyboardInputMode.Automatic, store.Load().KeyboardSource);
        Assert.Equal("CTRL O", store.Load().Bindings[GlobalInputAction.MapZoomIn]);
        Assert.True(store.Load().KeyboardEnabled);
    }

    /// <summary>Rejects unavailable choices and distinguishes requested overrides, learned sources, focus, and received input.</summary>
    [Fact]
    public void DiagnosticsDoNotChangeSavedSourceOrBindings()
    {
        var store = new GlobalInputSettingsStore(Path.Combine(root, "ui.json"));
        GlobalInputSettingsViewModel viewModel = Create(store);
        Assert.Contains("waiting", viewModel.KeyboardSourceStatus);
        viewModel.SelectedKeyboardSource = viewModel.KeyboardSourceOptions.Single(option =>
            option.Mode == KeyboardInputMode.WaylandPortal
        );
        Assert.Equal(KeyboardInputMode.Automatic, viewModel.CurrentSettings.KeyboardSource);
        viewModel.SelectedKeyboardSource = null!;
        viewModel.UpdateKeyboardDiagnostics(
            new(
                KeyboardInputMode.GameDisplay,
                true,
                true,
                false,
                "Map received.",
                "Focus unavailable.",
                "Game display :2."
            )
        );
        Assert.Contains("using Game display", viewModel.KeyboardSourceStatus);
        IReadOnlyList<KeyboardInputSourceOption> choices = viewModel.KeyboardSourceOptions;
        viewModel.UpdateKeyboardDiagnostics(
            new(
                KeyboardInputMode.GameDisplay,
                true,
                true,
                false,
                "Map received.",
                "Focus unavailable.",
                "Game display :2."
            )
        );
        Assert.Same(choices, viewModel.KeyboardSourceOptions);
        Assert.Equal("Focus unavailable.", viewModel.KeyboardFocusStatus);
        Assert.Equal("Map received.", viewModel.LastKeyboardInput);
        viewModel.SelectedKeyboardSource = viewModel.KeyboardSourceOptions.Single(option =>
            option.Mode == KeyboardInputMode.GameDisplay
        );
        KeyboardInputSourceOption selectedSource = viewModel.SelectedKeyboardSource;
        viewModel.SelectedKeyboardSource = selectedSource;
        Assert.Contains(":2", viewModel.KeyboardSourceDetails);
        viewModel.UpdateKeyboardDiagnostics(
            new(null, true, false, false, "Map received.", "Elite is not running.", "No game display.")
        );
        Assert.Contains("unavailable", viewModel.KeyboardSourceStatus);
        Assert.Equal(KeyboardInputMode.GameDisplay, store.Load().KeyboardSource);
    }

    /// <summary>Old and malformed profiles retain Automatic; defined preferences survive a normal save.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("99")]
    [InlineData("Unknown")]
    public void MissingOrInvalidSourceUsesAutomatic(string? value)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "ui.json");
        File.WriteAllText(
            path,
            new JsonObject { ["Input"] = new JsonObject { ["KeyboardSource"] = value } }.ToJsonString()
        );
        var store = new GlobalInputSettingsStore(path);
        Assert.Equal(KeyboardInputMode.Automatic, store.Load().KeyboardSource);
        store.Save(GlobalInputSettings.Default);
        Assert.Equal(KeyboardInputMode.Automatic, store.Load().KeyboardSource);
    }

    /// <summary>Menu wiring and status updates never invoke permission; only an explicit enabled button can open it.</summary>
    [Theory]
    [InlineData(OverlayHostKind.LinuxXWayland, true)]
    [InlineData(OverlayHostKind.LinuxWayland, true)]
    [InlineData(OverlayHostKind.LinuxX11, true)]
    [InlineData(OverlayHostKind.Windows, false)]
    public async Task DesktopSettingsButtonRequiresExplicitInput(OverlayHostKind host, bool supported)
    {
        var store = new GlobalInputSettingsStore(Path.Combine(root, "menu.json"));
        var viewModel = new GlobalInputSettingsViewModel(
            store,
            OverlayPlatformCapabilities.ForHost(host),
            new EmptyControllers()
        );
        Assert.Equal(supported, viewModel.IsDesktopShortcutSettingsVisible);
        Assert.Equal(
            supported ? "Desktop shortcuts: checking availability." : string.Empty,
            viewModel.DesktopShortcutSettingsStatus
        );
        int opened = 0;
        viewModel.SetDesktopShortcutSettingsHandler(() =>
        {
            opened++;
            return Task.CompletedTask;
        });
        viewModel.UpdateKeyboardDiagnostics(
            KeyboardInputHost.InitialDiagnostics(host) with
            {
                DesktopShortcutSettings = supported
                    ? new DesktopShortcutSettingsState(true, "Approval needed.", ["Overlay interaction: Super+O"])
                    : null,
            }
        );
        Assert.Equal(0, opened);
        Assert.Equal(supported, viewModel.IsDesktopShortcutSettingsVisible);
        Assert.False(viewModel.DesktopShortcutSettingsCommand.CanExecute(null));
        await viewModel.OpenDesktopShortcutSettingsAsync();
        viewModel.KeyboardEnabled = true;
        Assert.Equal(supported, viewModel.DesktopShortcutSettingsCommand.CanExecute(null));
        viewModel.DesktopShortcutSettingsCommand.Execute(null);
        Assert.Equal(supported ? 1 : 0, opened);
        Assert.Equal(supported ? "Approval needed." : string.Empty, viewModel.DesktopShortcutSettingsStatus);
        Assert.Equal(supported ? "Overlay interaction: Super+O" : string.Empty, viewModel.ApprovedDesktopShortcuts);
        Assert.Equal(supported, viewModel.HasApprovedDesktopShortcuts);
        viewModel.SetDesktopShortcutSettingsHandler(null);
        Assert.False(viewModel.CanOpenDesktopShortcutSettings);
    }

    /// <summary>Busy, canceled, and failing menu requests cannot spawn duplicate dialogs or unobserved exceptions.</summary>
    [Theory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("error")]
    public async Task DesktopSettingsButtonSerializesAndHandlesFailures(string outcome)
    {
        GlobalInputSettingsViewModel viewModel = Create(new GlobalInputSettingsStore(Path.Combine(root, "busy.json")));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.SetDesktopShortcutSettingsHandler(() => ready.Task);
        viewModel.KeyboardEnabled = true;
        viewModel.UpdateKeyboardDiagnostics(
            new(null, true, false, true, "", "", "")
            {
                DesktopShortcutSettings = new DesktopShortcutSettingsState(
                    true,
                    "Active.",
                    ["Overlay interaction: Super+O", "Map zoom in: Ctrl+Plus"]
                ),
            }
        );
        Assert.Equal("Overlay interaction: Super+O\nMap zoom in: Ctrl+Plus", viewModel.ApprovedDesktopShortcuts);
        Task opening = viewModel.OpenDesktopShortcutSettingsAsync();
        Assert.False(viewModel.CanOpenDesktopShortcutSettings);
        await viewModel.OpenDesktopShortcutSettingsAsync();
        if (outcome == "error")
        {
            ready.SetException(new IOException("portal failed"));
        }
        else if (outcome == "cancel")
        {
            ready.SetCanceled();
        }
        else
        {
            ready.SetResult();
        }
        await opening;
        Assert.True(viewModel.CanOpenDesktopShortcutSettings);
        Assert.Equal(
            outcome == "error",
            viewModel.DesktopShortcutSettingsStatus.Contains("could not open", StringComparison.Ordinal)
        );
        viewModel.UpdateKeyboardDiagnostics(
            new(null, true, false, true, "", "", "")
            {
                DesktopShortcutSettings = new DesktopShortcutSettingsState(
                    true,
                    "Active.",
                    ["Overlay interaction: Super+O", "Map zoom in: Ctrl+Plus"]
                ),
            }
        );
        Assert.Equal(
            outcome == "error",
            viewModel.DesktopShortcutSettingsStatus.Contains("could not open", StringComparison.Ordinal)
        );
        viewModel.UpdateKeyboardDiagnostics(
            new(null, true, false, false, "", "", "")
            {
                DesktopShortcutSettings = new DesktopShortcutSettingsState(false, "Unavailable.", []),
            }
        );
        Assert.Equal("Unavailable.", viewModel.DesktopShortcutSettingsStatus);
        Assert.False(viewModel.HasApprovedDesktopShortcuts);
        Assert.False(viewModel.CanOpenDesktopShortcutSettings);
    }

    /// <summary>Creates input settings without discovering physical controller devices.</summary>
    private static GlobalInputSettingsViewModel Create(GlobalInputSettingsStore store) =>
        new(store, OverlayPlatformCapabilities.ForHost(OverlayHostKind.LinuxXWayland), new EmptyControllers());

    /// <summary>Removes only this test's temporary profiles.</summary>
    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>Provides a deterministic empty controller inventory.</summary>
    private sealed class EmptyControllers : IControllerDeviceProvider
    {
        /// <summary>Reports no controllers without invoking SDL.</summary>
        public ControllerDeviceDiscoveryResult Discover() => new([], null);
    }
}
