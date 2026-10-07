using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Input;

/// <summary>Checks which keyboard sources and settings copy each host offers before and after listeners start.</summary>
public sealed class KeyboardInputHostTests
{
    [Theory]
    [InlineData(OverlayHostKind.Windows, true, false)]
    [InlineData(OverlayHostKind.LinuxX11, true, true)]
    [InlineData(OverlayHostKind.LinuxXWayland, true, true)]
    [InlineData(OverlayHostKind.LinuxWayland, true, true)]
    [InlineData(OverlayHostKind.Other, false, false)]
    public void HostDecidesKeyboardEnablementAndDesktopShortcutSettings(
        OverlayHostKind host,
        bool canEnable,
        bool offersSettings
    )
    {
        KeyboardInputDiagnostics diagnostics = KeyboardInputHost.InitialDiagnostics(host);

        Assert.Equal(canEnable, KeyboardInputHost.CanEnableKeyboard(OverlayPlatformCapabilities.ForHost(host)));
        Assert.Equal(offersSettings, diagnostics.DesktopShortcutSettings is not null);
        Assert.Equal("No configured shortcut received yet.", diagnostics.LastInput);
        Assert.Equal("Waiting for Elite Dangerous.", diagnostics.FocusStatus);
        Assert.Equal(
            offersSettings
                ? new DesktopShortcutSettingsState(false, "Desktop shortcuts: checking availability.", [])
                : null,
            diagnostics.DesktopShortcutSettings
        );
    }

    /// <summary>Linux desktops add game-display and portal sources ahead of the desktop hook; other hosts use the hook alone.</summary>
    [Theory]
    [InlineData(OverlayHostKind.Windows)]
    [InlineData(OverlayHostKind.LinuxX11)]
    [InlineData(OverlayHostKind.LinuxWayland)]
    public async Task CreatesSourcesInStartupOrderWithoutOpeningNativeConnections(OverlayHostKind host)
    {
        IReadOnlyList<IKeyboardActivationSource> sources = KeyboardInputHost.CreateSources(host, () => null);
        KeyboardInputSource[] expected =
            OperatingSystem.IsLinux() && host != OverlayHostKind.Windows
                ? [KeyboardInputSource.NestedDisplay, KeyboardInputSource.Portal, KeyboardInputSource.Desktop]
                : [KeyboardInputSource.Desktop];

        Assert.Equal(expected, sources.Select(source => source.Kind));
        Assert.All(sources, source => Assert.False(source.State.IsRunning));
        foreach (IKeyboardActivationSource source in sources)
        {
            await source.DisposeAsync();
        }

        var tracker = new TestGameWindowTracker();
        await using var service = new GlobalKeyboardHookService(
            GlobalInputSettings.Default,
            host,
            tracker,
            () => false
        );
        Assert.Equal(expected.Length > 1, service.Diagnostics.DesktopShortcutSettings is not null);
        Assert.False(service.IsRunning);
        await service.DisposeAsync();
        Assert.True(tracker.IsDisposed);
    }

    /// <summary>Every source choice stays listed with its availability and the copy shown in input settings.</summary>
    [Fact]
    public void SourceOptionsDescribeEveryChoice()
    {
        var diagnostics = new KeyboardInputDiagnostics(null, true, false, true, "", "", "Game display :2.");

        IReadOnlyList<KeyboardInputSourceOption> options = diagnostics.SourceOptions;

        Assert.Equal(
            ["Automatic (recommended)", "Desktop keyboard", "Game display", "Wayland portal"],
            options.Select(option => option.Label)
        );
        Assert.Equal([true, true, false, true], options.Select(option => option.IsAvailable));
        Assert.StartsWith("Game display :2. Uses the game's separate X11 display.", options[2].Details);
        Assert.Contains("Native Wayland may not expose which window has focus.", options[3].Details);
    }

    /// <summary>Equal approvals compare by content so repeated diagnostics do not refresh the settings view.</summary>
    [Fact]
    public void DesktopShortcutSettingsCompareApprovalsByContent()
    {
        var first = new DesktopShortcutSettingsState(true, "Active.", ["A: Super+A"]);
        var same = new DesktopShortcutSettingsState(true, "Active.", ["A: Super+A"]);

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, same with { ApprovedShortcuts = ["A: Super+B"] });
        Assert.NotEqual(first, same with { Status = "Inactive." });
        Assert.NotEqual(first, same with { CanOpen = false });
        Assert.False(first.Equals(null));
    }
}
