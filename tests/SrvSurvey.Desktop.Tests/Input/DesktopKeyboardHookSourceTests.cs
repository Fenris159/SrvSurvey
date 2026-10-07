using System.Diagnostics;
using SharpHook.Data;
using SharpHook.Testing;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Input;

/// <summary>Checks SharpHook events become chord activations and the native hook restarts without overlap.</summary>
public sealed class DesktopKeyboardHookSourceTests
{
    public static TheoryData<string, EventMask, KeyCode> ShortcutVariants =>
        new()
        {
            { "O", EventMask.None, KeyCode.VcO },
            { "SHIFT O", EventMask.LeftShift, KeyCode.VcO },
            { "ALT O", EventMask.LeftAlt, KeyCode.VcO },
            { "CTRL O", EventMask.LeftCtrl, KeyCode.VcO },
            { "ALT SHIFT O", EventMask.LeftAlt | EventMask.LeftShift, KeyCode.VcO },
            { "ALT CTRL O", EventMask.LeftAlt | EventMask.LeftCtrl, KeyCode.VcO },
            { "CTRL SHIFT O", EventMask.LeftCtrl | EventMask.LeftShift, KeyCode.VcO },
            { "ALT CTRL SHIFT O", EventMask.LeftAlt | EventMask.LeftCtrl | EventMask.LeftShift, KeyCode.VcO },
            { "X", EventMask.None, KeyCode.VcX },
            { "F2", EventMask.None, KeyCode.VcF2 },
            { "D1", EventMask.None, KeyCode.Vc1 },
        };

    public static TheoryData<string, KeyCode[]> ObservedModifierVariants =>
        new()
        {
            { "SHIFT O", [KeyCode.VcRightShift] },
            { "ALT O", [KeyCode.VcRightAlt] },
            { "CTRL O", [KeyCode.VcRightControl] },
            { "ALT SHIFT O", [KeyCode.VcRightAlt, KeyCode.VcRightShift] },
            { "ALT CTRL O", [KeyCode.VcRightAlt, KeyCode.VcRightControl] },
            { "CTRL SHIFT O", [KeyCode.VcRightControl, KeyCode.VcRightShift] },
            { "ALT CTRL SHIFT O", [KeyCode.VcRightAlt, KeyCode.VcRightControl, KeyCode.VcRightShift] },
        };

    [Theory]
    [MemberData(nameof(ShortcutVariants))]
    public async Task X11AutoRepeatDoesNotRetriggerChord(string chord, EventMask mask, KeyCode keyCode)
    {
        long eventTime = 1_000_000;
        using var hook = new TestGlobalHook(TestThreadingMode.Simple)
        {
            EventDateTime = _ => DateTimeOffset.UnixEpoch.AddMilliseconds(eventTime),
            EventMask = _ => mask,
        };
        await using DesktopKeyboardHookSource source = Start(
            OverlayHostKind.LinuxXWayland,
            hook,
            out RecordingKeyboardSink sink
        );

        hook.SimulateKeyPress(keyCode);
        sink.Clock += Stopwatch.Frequency / 2;
        eventTime += 500;
        hook.SimulateKeyRelease(keyCode);
        sink.Clock += Stopwatch.Frequency / 1000;
        eventTime += 1;
        hook.SimulateKeyPress(keyCode);
        Assert.Equal([chord], sink.Chords);

        eventTime += 30;
        hook.SimulateKeyRelease(keyCode);
        sink.Clock += Stopwatch.Frequency / 10;
        eventTime += 100;
        hook.SimulateKeyPress(keyCode);
        Assert.Equal([chord, chord], sink.Chords);
        Assert.All(sink.Activations, activation => Assert.Equal(KeyboardInputSource.Desktop, activation.Source));
        Assert.All(sink.Evidence, evidence => Assert.Equal(KeyboardFocusEvidence.DesktopWindow, evidence));
    }

    [Fact]
    public async Task DispatchesChordOnKeyPressEvenWhenModifiersAreReleasedBeforeLetter()
    {
        using var hook = new TestGlobalHook(TestThreadingMode.Simple)
        {
            EventMask = _ => EventMask.LeftAlt | EventMask.LeftShift,
        };
        await using DesktopKeyboardHookSource source = Start(
            OverlayHostKind.Windows,
            hook,
            out RecordingKeyboardSink sink
        );

        hook.SimulateKeyPress(KeyCode.VcO);
        hook.SimulateKeyPress(KeyCode.VcO);
        hook.EventMask = _ => EventMask.None;
        hook.SimulateKeyRelease(KeyCode.VcO);
        Assert.Equal(["ALT SHIFT O"], sink.Chords);

        hook.EventMask = _ => EventMask.LeftAlt | EventMask.LeftShift;
        hook.SimulateKeyPress(KeyCode.VcO);
        Assert.Equal(["ALT SHIFT O", "ALT SHIFT O"], sink.Chords);
    }

    [Fact]
    public async Task DispatchesFirstChordAfterIdleWhenPriorKeyReleaseWasMissed()
    {
        using var hook = new TestGlobalHook(TestThreadingMode.Simple);
        await using DesktopKeyboardHookSource source = Start(
            OverlayHostKind.LinuxXWayland,
            hook,
            out RecordingKeyboardSink sink
        );

        hook.SimulateKeyPress(KeyCode.VcO);
        hook.SimulateKeyPress(KeyCode.VcO);
        Assert.Single(sink.Chords);

        sink.Clock += Stopwatch.Frequency * 3;
        hook.SimulateKeyPress(KeyCode.VcO);
        Assert.Equal(2, sink.Chords.Count());
    }

    [Theory]
    [MemberData(nameof(ObservedModifierVariants))]
    public async Task X11ChordUsesObservedModifiersWhenLetterEventOmitsItsModifierMask(
        string chord,
        KeyCode[] modifiers
    )
    {
        long eventTime = 1_000_000;
        using var hook = new TestGlobalHook(TestThreadingMode.Simple)
        {
            EventDateTime = _ => DateTimeOffset.UnixEpoch.AddMilliseconds(eventTime),
        };
        await using DesktopKeyboardHookSource source = Start(
            OverlayHostKind.LinuxXWayland,
            hook,
            out RecordingKeyboardSink sink
        );

        foreach (KeyCode modifier in modifiers)
        {
            hook.SimulateKeyPress(modifier);
            sink.Clock += Stopwatch.Frequency;
            eventTime += 20;
        }
        hook.EventMask = _ => EventMask.NumLock;
        hook.SimulateKeyPress(KeyCode.VcO);
        Assert.Equal([chord], sink.Chords);

        eventTime += 20;
        hook.SimulateKeyRelease(KeyCode.VcO);
        foreach (KeyCode modifier in modifiers)
        {
            eventTime += 20;
            hook.SimulateKeyRelease(modifier);
        }
        sink.Clock += Stopwatch.Frequency / 10;
        eventTime += 20;
        hook.SimulateKeyPress(KeyCode.VcO);
        Assert.Equal([chord, "O"], sink.Chords);
    }

    [Fact]
    public async Task X11MissedModifierReleaseExpiresBeforeLaterPlainKey()
    {
        using var hook = new TestGlobalHook(TestThreadingMode.Simple);
        await using DesktopKeyboardHookSource source = Start(
            OverlayHostKind.LinuxXWayland,
            hook,
            out RecordingKeyboardSink sink
        );

        hook.SimulateKeyPress(KeyCode.VcRightAlt);
        hook.SimulateKeyPress(KeyCode.VcRightShift);
        sink.Clock += Stopwatch.Frequency * 11;
        hook.SimulateKeyPress(KeyCode.VcO);

        Assert.Equal(["O"], sink.Chords);
    }

    /// <summary>Keys typed in other applications are never tracked, while held modifiers still count once focus returns.</summary>
    [Fact]
    public async Task UnfocusedKeysAreNotTrackedAndSimulatedKeysAreIgnored()
    {
        using var hook = new TestGlobalHook(TestThreadingMode.Simple);
        await using DesktopKeyboardHookSource source = Start(
            OverlayHostKind.LinuxX11,
            hook,
            out RecordingKeyboardSink sink
        );

        sink.AllowsShortcuts = false;
        hook.SimulateKeyPress(KeyCode.VcRightAlt);
        hook.SimulateKeyPress(KeyCode.VcO);
        sink.AllowsShortcuts = true;
        hook.SimulateKeyPress(KeyCode.VcO);
        hook.EventMask = _ => EventMask.SimulatedEvent;
        sink.Clock += Stopwatch.Frequency * 3;
        hook.SimulateKeyPress(KeyCode.VcX);

        Assert.Equal(["ALT O"], sink.Chords);
    }

    /// <summary>Checks native Wayland uses optional portal support rather than starting an X11 desktop hook.</summary>
    [Theory]
    [InlineData(OverlayHostKind.LinuxWayland, "Wayland keyboard input is waiting for Global Shortcuts portal support.")]
    [InlineData(OverlayHostKind.Other, "Global keyboard input is unavailable on this platform.")]
    public async Task DoesNotCreateHookOnUnsupportedHost(OverlayHostKind host, string status)
    {
        int factoryCalls = 0;
        await using DesktopKeyboardHookSource source = Start(
            host,
            () =>
            {
                factoryCalls++;
                return new TestGlobalHook();
            },
            out RecordingKeyboardSink sink
        );

        Assert.Equal(0, factoryCalls);
        Assert.Equal(status, sink.Status);
        Assert.False(source.State.IsRunning);
    }

    [Fact]
    public async Task StartsThroughXWaylandAndReportsStopsAndRestarts()
    {
        var hooks = new List<TestGlobalHook>();
        await using DesktopKeyboardHookSource source = Start(
            OverlayHostKind.LinuxXWayland,
            () =>
            {
                var hook = new TestGlobalHook(TestThreadingMode.Simple);
                hooks.Add(hook);
                return hook;
            },
            out RecordingKeyboardSink sink
        );

        Assert.True(source.State.IsRunning);
        Assert.Equal("Global keyboard input is active.", sink.Status);
        hooks[0].Stop();
        await WaitForAsync(() => !source.State.IsRunning);
        Assert.Equal([KeyboardInputSource.Desktop], sink.Releases);
        Assert.Equal("Global keyboard input stopped.", sink.Status);

        source.Update(Settings() with { KeyboardEnabled = false });
        source.Update(Settings());
        await WaitForAsync(() => source.State.IsRunning);
        Assert.Equal(2, hooks.Count);
        source.Update(Settings() with { KeyboardEnabled = false });
        await WaitForAsync(() => hooks[1].IsDisposed);
        Assert.False(source.State.IsRunning);
        hooks.ForEach(hook => hook.Dispose());
    }

    [Fact]
    public async Task ReportsHookStartupFailure()
    {
        await using DesktopKeyboardHookSource source = Start(
            OverlayHostKind.LinuxX11,
            () => throw new InvalidOperationException("test failure"),
            out RecordingKeyboardSink sink
        );

        Assert.False(source.State.IsRunning);
        Assert.Equal("Global keyboard input could not start: test failure", sink.Status);
    }

    /// <summary>A disabled source never creates the native hook until keyboard input is enabled.</summary>
    [Fact]
    public async Task DisabledSourceWaitsForEnablement()
    {
        using var hook = new TestGlobalHook(TestThreadingMode.Simple);
        int factoryCalls = 0;
        await using var source = new DesktopKeyboardHookSource(
            OverlayHostKind.Windows,
            () =>
            {
                factoryCalls++;
                return hook;
            }
        );
        var sink = new RecordingKeyboardSink();
        source.Attach(sink);
        source.Start(Settings() with { KeyboardEnabled = false });
        Assert.Equal(0, factoryCalls);
        Assert.Empty(sink.Statuses);

        source.Update(Settings());
        Assert.Equal(1, factoryCalls);
        source.ResetDetection();
        hook.SimulateKeyPress(KeyCode.VcO);
        Assert.Equal(["O"], sink.Chords);
    }

    /// <summary>Repeat suppression survives rebinding, and every configurable action routes from the desktop hook.</summary>
    [Fact]
    public async Task X11AutoRepeatDoesNotRetriggerAnyConfigurableKeyboardAction()
    {
        long eventTime = 1_000_000;
        long timestamp = 0;
        using var hook = new TestGlobalHook(TestThreadingMode.Simple)
        {
            EventDateTime = _ => DateTimeOffset.UnixEpoch.AddMilliseconds(eventTime),
        };
        await using var service = new GlobalKeyboardHookService(
            Settings(),
            [new DesktopKeyboardHookSource(OverlayHostKind.LinuxXWayland, () => hook)],
            new TestGameWindowTracker(),
            () => true,
            timestampProvider: () => timestamp
        );
        var triggered = new List<GlobalInputAction>();
        service.ActionTriggered += (_, eventArgs) => triggered.Add(eventArgs.Action);
        service.Start();

        foreach (GlobalInputActionDefinition definition in GlobalInputActionCatalog.All)
        {
            var bindings = GlobalInputActionCatalog.All.ToDictionary(item => item.Action, _ => string.Empty);
            bindings[definition.Action] = "O";
            service.Update(GlobalInputSettings.Default with { KeyboardEnabled = true, Bindings = bindings });

            int before = triggered.Count;
            hook.SimulateKeyPress(KeyCode.VcO);
            timestamp += Stopwatch.Frequency / 2;
            eventTime += 500;
            hook.SimulateKeyRelease(KeyCode.VcO);
            timestamp += Stopwatch.Frequency / 1000;
            eventTime += 1;
            hook.SimulateKeyPress(KeyCode.VcO);
            Assert.True(triggered.Count == before + 1, $"Repeat retriggered {definition.Action}.");
            Assert.Equal(definition.Action, triggered[^1]);

            eventTime += 30;
            hook.SimulateKeyRelease(KeyCode.VcO);
            timestamp += Stopwatch.Frequency / 10;
            eventTime += 100;
            hook.SimulateKeyPress(KeyCode.VcO);
            Assert.True(triggered.Count == before + 2, $"Second tap missed {definition.Action}.");
            Assert.Equal(definition.Action, triggered[^1]);
            eventTime += 30;
            hook.SimulateKeyRelease(KeyCode.VcO);
            timestamp += Stopwatch.Frequency / 10;
            eventTime += 100;
        }
    }

    /// <summary>Blocks the key callback specifically, allowing background focus diagnostics to run before the event.</summary>
    [Fact]
    public async Task DisposalWaitsForInFlightEventBeforeDisposingTracker()
    {
        using var hook = new TestGlobalHook(TestThreadingMode.EventLoop) { EventMask = _ => EventMask.LeftAlt };
        var tracker = new TestGameWindowTracker();
        await using var service = new GlobalKeyboardHookService(
            EnabledSettings(),
            [new DesktopKeyboardHookSource(OverlayHostKind.LinuxX11, () => hook)],
            tracker,
            () =>
            {
                tracker.BlockSnapshots();
                return false;
            }
        );
        service.Start();

        hook.SimulateKeyPress(KeyCode.VcX);
        await tracker.SnapshotEntered.WaitAsync(TimeSpan.FromSeconds(2));
        Task disposal = service.DisposeAsync().AsTask();

        Assert.False(disposal.IsCompleted);
        Assert.False(tracker.IsDisposed);

        tracker.AllowSnapshot();
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(tracker.IsDisposed);
    }

    /// <summary>Prevents a replacement hook from starting until the original key callback and event loop finish.</summary>
    [Fact]
    public async Task RestartWaitsForPreviousEventLoopToStop()
    {
        using var firstHook = new TestGlobalHook(TestThreadingMode.EventLoop) { EventMask = _ => EventMask.LeftAlt };
        using var secondHook = new TestGlobalHook(TestThreadingMode.Simple);
        var tracker = new TestGameWindowTracker();
        var secondHookCreated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int factoryCalls = 0;
        await using var service = new GlobalKeyboardHookService(
            EnabledSettings(),
            [
                new DesktopKeyboardHookSource(
                    OverlayHostKind.LinuxX11,
                    () =>
                    {
                        if (Interlocked.Increment(ref factoryCalls) == 1)
                        {
                            return firstHook;
                        }

                        secondHookCreated.TrySetResult();
                        return secondHook;
                    }
                ),
            ],
            tracker,
            () =>
            {
                tracker.BlockSnapshots();
                return false;
            }
        );

        service.Start();
        firstHook.SimulateKeyPress(KeyCode.VcX);
        await tracker.SnapshotEntered.WaitAsync(TimeSpan.FromSeconds(2));
        service.Update(EnabledSettings() with { KeyboardEnabled = false });
        service.Update(EnabledSettings());

        Assert.Equal(1, Volatile.Read(ref factoryCalls));

        tracker.AllowSnapshot();
        await secondHookCreated.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, Volatile.Read(ref factoryCalls));
    }

    /// <summary>Starts an attached source that reports into a recording sink.</summary>
    private static DesktopKeyboardHookSource Start(
        OverlayHostKind host,
        TestGlobalHook hook,
        out RecordingKeyboardSink sink
    ) => Start(host, () => hook, out sink);

    private static DesktopKeyboardHookSource Start(
        OverlayHostKind host,
        Func<SharpHook.IGlobalHook> hookFactory,
        out RecordingKeyboardSink sink
    )
    {
        var source = new DesktopKeyboardHookSource(host, hookFactory);
        sink = new RecordingKeyboardSink();
        source.Attach(sink);
        source.Start(Settings());
        return source;
    }

    private static GlobalInputSettings Settings() => GlobalInputSettings.Default with { KeyboardEnabled = true };

    private static GlobalInputSettings EnabledSettings()
    {
        var bindings = GlobalInputSettings.Default.Bindings.ToDictionary();
        bindings[GlobalInputAction.ToggleAllVisibility] = "ALT X";
        return GlobalInputSettings.Default with { KeyboardEnabled = true, Bindings = bindings };
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
