using SharpHook.Data;
using SharpHook.Testing;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class GlobalKeyboardHookServiceTests
{
    [Fact]
    public void DispatchesConfiguredChordWhileApplicationIsActive()
    {
        using var testHook = new TestGlobalHook(TestThreadingMode.Simple)
        {
            EventMask = _ => EventMask.LeftAlt,
        };
        using var service = new GlobalKeyboardHookService(
            EnabledSettings(),
            OverlayHostKind.Windows,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            hookFactory: () => testHook);
        GlobalInputActionTriggeredEventArgs? triggered = null;
        service.ActionTriggered += (_, eventArgs) => triggered = eventArgs;

        service.Start();
        testHook.SimulateKeyRelease(KeyCode.VcX);

        Assert.Equal("Global keyboard input is active.", service.Status);
        Assert.NotNull(triggered);
        Assert.Equal(
            GlobalInputAction.ToggleAllVisibility,
            triggered.Action);
        Assert.Equal("ALT X", triggered.Chord);
    }

    [Fact]
    public void IgnoresChordOutsideApplicationAndGameContext()
    {
        using var testHook = new TestGlobalHook(TestThreadingMode.Simple)
        {
            EventMask = _ => EventMask.LeftAlt,
        };
        using var service = new GlobalKeyboardHookService(
            EnabledSettings(),
            OverlayHostKind.LinuxX11,
            new StubGameWindowTracker(),
            isApplicationActive: () => false,
            hookFactory: () => testHook);
        var triggerCount = 0;
        service.ActionTriggered += (_, _) => triggerCount++;

        service.Start();
        testHook.SimulateKeyRelease(KeyCode.VcX);

        Assert.Equal(0, triggerCount);
    }

    [Fact]
    public void DoesNotCreateHookOnUnsupportedHost()
    {
        var factoryCalls = 0;
        using var service = new GlobalKeyboardHookService(
            EnabledSettings(),
            OverlayHostKind.LinuxWayland,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            hookFactory: () =>
            {
                factoryCalls++;
                return new TestGlobalHook();
            });

        service.Start();

        Assert.Equal(0, factoryCalls);
        Assert.Equal(
            "Global keyboard input is unavailable on this platform.",
            service.Status);
    }

    private static GlobalInputSettings EnabledSettings()
    {
        var bindings = GlobalInputSettings.Default.Bindings.ToDictionary();
        bindings[GlobalInputAction.ToggleAllVisibility] = "ALT X";
        return GlobalInputSettings.Default with
        {
            KeyboardEnabled = true,
            Bindings = bindings,
        };
    }

    private sealed class StubGameWindowTracker : IGameWindowTracker
    {
        public GameWindowSnapshot GetSnapshot()
        {
            return GameWindowSnapshot.Unavailable;
        }

        public void Dispose()
        {
        }
    }
}
