using System.Collections.Concurrent;
using System.Diagnostics;
using SrvSurvey.Desktop.Input;
using Tmds.DBus;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class GlobalShortcutsPortalInputTests
{
    private readonly RecordingKeyboardSink sink = new();

    /// <summary>Repeated clicks open legacy desktop settings when approved bindings are restored without an approval dialog.</summary>
    [Fact]
    public async Task ReopensLegacyDesktopSettingsWithoutRebinding()
    {
        int settingsOpened = 0;
        int sessionsOpened = 0;
        await using GlobalShortcutsPortalInput input = CreateInput(
            _ =>
            {
                sessionsOpened++;
                return Task.FromResult<IPortalShortcutSession>(new FakeSession());
            },
            openLegacySettings: _ =>
            {
                settingsOpened++;
                return Task.FromResult(true);
            }
        );
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, settingsOpened);
        await input.OpenDesktopShortcutSettingsAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await input.OpenDesktopShortcutSettingsAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, settingsOpened);
        Assert.Equal(1, sessionsOpened);
        Assert.True(input.State.IsRunning);
    }

    /// <summary>A failed legacy settings launch keeps the active session and allows the user to retry the button.</summary>
    [Fact]
    public async Task LegacySettingsFailureDoesNotReplaceApprovedSession()
    {
        var session = new FakeSession();
        await using GlobalShortcutsPortalInput input = CreateInput(
            _ => Task.FromResult<IPortalShortcutSession>(session),
            openLegacySettings: _ => throw new IOException("Settings unavailable")
        );
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        await input.OpenDesktopShortcutSettingsAsync();
        Assert.Contains("could not open", ShortcutUi(input).Status);
        Assert.True(ShortcutUi(input).CanOpen);
        Assert.True(input.State.IsRunning);
        Assert.False(session.Disposed);
    }

    /// <summary>Supplies isolated portal and desktop settings dependencies so tests cannot open host windows.</summary>
    private GlobalShortcutsPortalInput CreateInput(
        Func<CancellationToken, Task<IPortalShortcutSession>> openSession,
        TimeSpan? retryDelay = null,
        Func<CancellationToken, Task<bool>>? openLegacySettings = null
    )
    {
        var input = new GlobalShortcutsPortalInput(
            openSession,
            retryDelay,
            openLegacySettings ?? (_ => Task.FromResult(false))
        );
        input.Attach(sink);
        return input;
    }

    private static DesktopShortcutSettingsState ShortcutUi(GlobalShortcutsPortalInput input)
    {
        Assert.NotNull(input.State.DesktopShortcutSettings);
        return input.State.DesktopShortcutSettings;
    }

    /// <summary>Startup may restore approval but must never authorize the desktop to open a shortcut dialog.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupNeverOpensShortcutPermissionDialog(bool needsApproval)
    {
        var session = new FakeSession { Denied = needsApproval };
        await using GlobalShortcutsPortalInput input = CreateInput(_ =>
            Task.FromResult<IPortalShortcutSession>(session)
        );
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(session.AllowPermissionPrompt);
    }

    /// <summary>Respects desktop-side revocation and later reapproval without opening another permission request.</summary>
    [Fact]
    public async Task DesktopBindingChangesUpdateAvailabilityAndActions()
    {
        var session = new FakeSession();
        await using GlobalShortcutsPortalInput input = CreateInput(_ =>
            Task.FromResult<IPortalShortcutSession>(session)
        );
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(input.State.CanServeAllBindings);
        session.ChangeBindings();
        Assert.False(input.State.IsRunning);
        Assert.False(input.State.CanServeAllBindings);
        session.Emit("toggleOverlayInteraction");
        Assert.Empty(sink.Activations);
        session.ChangeBindings("toggleOverlayInteraction", "unrelated");
        Assert.True(input.State.IsRunning);
        Assert.True(input.State.CanServeAllBindings);
        session.Emit("toggleOverlayInteraction");
        Assert.Single(sink.Activations);
        input.Update(Settings("O") with { KeyboardEnabled = false });
        await WaitAsync(() => session.Disposed);
        session.ChangeBindings("toggleOverlayInteraction");
        Assert.False(input.State.IsRunning);
    }

    /// <summary>Maps application key names to standard XKB triggers and rejects controller or invalid bindings.</summary>
    [Theory]
    [InlineData("ALT SHIFT O", "ALT+SHIFT+o")]
    [InlineData("CTRL D1", "CTRL+1")]
    [InlineData("Backspace", "BackSpace")]
    [InlineData("OemOpenBrackets", "bracketleft")]
    [InlineData("NumPad1", "KP_1")]
    [InlineData("+", "equal")]
    [InlineData("B1", null)]
    [InlineData("nonsense", null)]
    [InlineData("CTRL CTRL O", null)]
    public void FormatsPortableBindings(string chord, string? expected)
    {
        IReadOnlyList<PortalShortcutBinding> bindings = PortalShortcutBinding.FromSettings(Settings(chord));
        Assert.Equal(expected, bindings.SingleOrDefault()?.Trigger);
    }

    /// <summary>Uses the same first-action-wins behavior as existing shortcut routing.</summary>
    [Fact]
    public void DuplicateChordsAreRegisteredOnce()
    {
        IReadOnlyList<PortalShortcutBinding> bindings = PortalShortcutBinding.FromSettings(
            Settings("O") with
            {
                Bindings = new Dictionary<GlobalInputAction, string>
                {
                    [GlobalInputAction.ToggleAllVisibility] = "O",
                    [GlobalInputAction.ToggleOverlayInteraction] = "O",
                },
            }
        );
        Assert.Equal(GlobalInputAction.ToggleAllVisibility, Assert.Single(bindings).Action);
        Assert.Empty(DbusGlobalShortcutSession.ReadIds(new Dictionary<string, object>()));
        Assert.Empty(DbusGlobalShortcutSession.ReadIds(new Dictionary<string, object> { ["shortcuts"] = 1 }));
    }

    /// <summary>Registers once, routes only accepted IDs, ignores controller-only edits, and reconnects after session loss.</summary>
    [Fact]
    public async Task RestoresBindingsAndReconnectsWithoutDuplicatingActions()
    {
        var sessions = new ConcurrentQueue<FakeSession>();
        await using GlobalShortcutsPortalInput input = CreateInput(
            _ =>
            {
                var session = new FakeSession();
                sessions.Enqueue(session);
                return Task.FromResult<IPortalShortcutSession>(session);
            },
            TimeSpan.FromMilliseconds(10)
        );
        input.Update(Settings("O"));
        await WaitAsync(() => input.State.IsRunning);
        Assert.True(sessions.TryPeek(out FakeSession? first));
        first.Emit("unknown");
        first.Emit("toggleOverlayInteraction");
        KeyboardActivation activation = Assert.Single(sink.Activations);
        Assert.Equal(KeyboardInputSource.Portal, activation.Source);
        Assert.Equal(GlobalInputAction.ToggleOverlayInteraction, activation.Action);
        Assert.Equal("O", activation.Chord);
        Assert.Equal([KeyboardFocusEvidence.Untracked], sink.Evidence);
        input.Update(Settings("O") with { ControllerEnabled = true });
        Assert.Single(sessions);
        first.Closed.TrySetResult();
        await WaitAsync(() => sessions.Count == 2 && input.State.IsRunning);
        Assert.True(first.Disposed);
        input.Update(Settings("X"));
        await WaitAsync(() => sessions.Count == 3 && input.State.IsRunning);
        FakeSession latest = sessions.Last();
        Assert.True(latest.ForceBind);
        input.Update(Settings("X") with { KeyboardEnabled = false });
        await WaitAsync(() => latest.Disposed);
        latest.Emit("toggleOverlayInteraction");
        Assert.Single(sink.Activations);
        await input.DisposeAsync();
        await input.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => input.Update(Settings("O")));
    }

    /// <summary>Retries an unavailable service silently and defers fresh approval rather than opening a late game-session dialog.</summary>
    [Fact]
    public async Task RetriesServiceFailureButStopsAfterPermissionDeclined()
    {
        int attempts = 0;
        await using GlobalShortcutsPortalInput input = CreateInput(
            _ =>
            {
                int attempt = Interlocked.Increment(ref attempts);
                if (attempt == 1)
                {
                    throw new IOException("bus absent");
                }
                return Task.FromResult<IPortalShortcutSession>(new FakeSession { Denied = true });
            },
            TimeSpan.FromMilliseconds(10)
        );
        input.Update(Settings("O"));
        await WaitAsync(() =>
            sink.Statuses.Any(message => message.Contains("approval is needed", StringComparison.Ordinal))
        );
        await Task.Delay(40);
        Assert.Equal(2, Volatile.Read(ref attempts));
        Assert.False(input.State.IsRunning);
        Assert.True(input.StartupReady.IsCompletedSuccessfully);
    }

    /// <summary>Completes silent discovery when approval is missing without retrying a permission request.</summary>
    [Fact]
    public async Task MissingApprovalIsNotRetriedAutomatically()
    {
        int attempts = 0;
        await using GlobalShortcutsPortalInput input = CreateInput(
            _ =>
            {
                attempts++;
                return Task.FromResult<IPortalShortcutSession>(new FakeSession { Denied = true });
            },
            TimeSpan.FromMilliseconds(10)
        );
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(40);
        Assert.Equal(1, attempts);
        Assert.False(input.State.IsRunning);
    }

    /// <summary>Waits for silent restoration before focus handoff and reconnects or changes bindings without permission dialogs.</summary>
    [Fact]
    public async Task SilentRestorationBarrierAndReconnect()
    {
        var sessions = new ConcurrentQueue<FakeSession>();
        var first = new FakeSession { Pending = true };
        await using GlobalShortcutsPortalInput input = CreateInput(
            _ =>
            {
                FakeSession next = sessions.IsEmpty ? first : new FakeSession();
                sessions.Enqueue(next);
                return Task.FromResult<IPortalShortcutSession>(next);
            },
            TimeSpan.FromMilliseconds(10)
        );
        input.Update(Settings("O"));
        await WaitAsync(() => first.Started);
        Assert.False(first.AllowPermissionPrompt);
        Assert.False(input.StartupReady.IsCompleted);
        first.Approved.TrySetResult();
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(input.State.CanServeAllBindings);
        first.Closed.TrySetResult();
        await WaitAsync(() => sessions.Count == 2 && input.State.IsRunning);
        Assert.False(sessions.Last().AllowPermissionPrompt);
        input.Update(Settings("X"));
        await WaitAsync(() => sessions.Count == 3 && input.State.IsRunning);
        Assert.False(sessions.Last().AllowPermissionPrompt);
    }

    /// <summary>Disabled keyboard input never delays startup focus or opens a portal session.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledInputCompletesStartupWithoutApproval(bool noBindings)
    {
        await using GlobalShortcutsPortalInput input = CreateInput(_ =>
            throw new InvalidOperationException("should not open")
        );
        input.Update(
            Settings("O") with
            {
                KeyboardEnabled = false,
                Bindings = noBindings ? new Dictionary<GlobalInputAction, string>() : Settings("O").Bindings,
            }
        );
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(input.State.IsRunning);
        Assert.False(input.State.CanServeAllBindings);
    }

    /// <summary>Cancels an outstanding authorization request and prevents stale session signals after rebinding.</summary>
    [Fact]
    public async Task CancelsOutstandingRegistrationOnDisable()
    {
        var session = new FakeSession { Pending = true };
        await using GlobalShortcutsPortalInput input = CreateInput(_ =>
            Task.FromResult<IPortalShortcutSession>(session)
        );
        input.Update(Settings("O"));
        await WaitAsync(() => session.Started);
        input.Update(Settings("O") with { KeyboardEnabled = false });
        await WaitAsync(() => session.Disposed);
        Assert.False(input.State.IsRunning);
    }

    /// <summary>Explicit configuration keeps active version-two bindings and shows desktop keys without changing requested keys.</summary>
    [Fact]
    public async Task OpensExistingDesktopMenuOnlyOnClick()
    {
        var session = new FakeSession
        {
            Configurable = true,
            TriggerDescriptions = new Dictionary<string, string> { ["toggleOverlayInteraction"] = "Super+P" },
        };
        int opened = 0;
        await using GlobalShortcutsPortalInput input = CreateInput(_ =>
        {
            opened++;
            return Task.FromResult<IPortalShortcutSession>(session);
        });
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(ShortcutUi(input).CanOpen);
        Assert.DoesNotContain(sink.Statuses, message => message.Contains('\n'));
        Assert.Equal(0, session.ConfigureCalls);
        Assert.Contains("Toggle live overlay interaction: Super+P", ShortcutUi(input).ApprovedShortcuts);
        await input.OpenDesktopShortcutSettingsAsync();
        Assert.Equal(1, opened);
        Assert.Equal(1, session.ConfigureCalls);
        Assert.False(session.AllowPermissionPrompt);
        Assert.True(input.State.IsRunning);
        session.TriggerDescriptions = new Dictionary<string, string> { ["toggleOverlayInteraction"] = "Ctrl+X" };
        session.ChangeBindings("toggleOverlayInteraction");
        Assert.Contains("Toggle live overlay interaction: Ctrl+X", ShortcutUi(input).ApprovedShortcuts);
        Assert.Equal("Desktop shortcuts: active (1 of 1 bindings).", ShortcutUi(input).Status);
    }

    /// <summary>Missing approval is requested only by the button, and a repeated click cannot create a second dialog.</summary>
    [Fact]
    public async Task ExplicitPermissionDoesNotRequireRestart()
    {
        var first = new FakeSession { Denied = true };
        var second = new FakeSession { Pending = true };
        var sessions = new Queue<FakeSession>([first, second]);
        await using GlobalShortcutsPortalInput input = CreateInput(_ =>
            Task.FromResult<IPortalShortcutSession>(sessions.Dequeue())
        );
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(ShortcutUi(input).CanOpen);
        Assert.Contains("approval is needed", ShortcutUi(input).Status);
        Task opening = input.OpenDesktopShortcutSettingsAsync();
        await WaitAsync(() => second.Started);
        Assert.True(second.AllowPermissionPrompt);
        Assert.True(second.ForceBind);
        Assert.False(ShortcutUi(input).CanOpen);
        Assert.Same(opening, input.OpenDesktopShortcutSettingsAsync());
        second.Approved.TrySetResult();
        await opening.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(input.State.IsRunning);
        Assert.True(ShortcutUi(input).CanOpen);
        Assert.True(first.Disposed);
    }

    /// <summary>Older desktops reopen a fresh explicitly authorized binding session instead of rebinding a used session.</summary>
    [Fact]
    public async Task VersionOneConfigurationReplacesSession()
    {
        var sessions = new ConcurrentQueue<FakeSession>();
        await using GlobalShortcutsPortalInput input = CreateInput(_ =>
        {
            var session = new FakeSession();
            sessions.Enqueue(session);
            return Task.FromResult<IPortalShortcutSession>(session);
        });
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        await input.OpenDesktopShortcutSettingsAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, sessions.Count);
        Assert.True(sessions.First().Disposed);
        Assert.Equal(1, sessions.First().ConfigureCalls);
        Assert.True(sessions.Last().AllowPermissionPrompt);
        Assert.True(input.State.IsRunning);
    }

    /// <summary>Disabling input cancels both explicit approval and configuration without enabling a stale menu.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisableCancelsExplicitDesktopRequest(bool existingSession)
    {
        var first = new FakeSession
        {
            Denied = !existingSession,
            Configurable = existingSession,
            ConfigurePending = existingSession,
        };
        var pending = new FakeSession { Pending = true };
        int count = 0;
        await using GlobalShortcutsPortalInput input = CreateInput(_ =>
            Task.FromResult<IPortalShortcutSession>(count++ == 0 ? first : pending)
        );
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        Task opening = input.OpenDesktopShortcutSettingsAsync();
        await WaitAsync(() => existingSession ? first.ConfigureCalls == 1 : pending.Started);
        input.Update(Settings("O") with { KeyboardEnabled = false });
        await opening.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(ShortcutUi(input).CanOpen);
        Assert.Equal("Desktop shortcuts: off.", ShortcutUi(input).Status);
        await input.OpenDesktopShortcutSettingsAsync();
        Assert.False(input.State.IsRunning);
    }

    /// <summary>Explicit refusals are not retried; protocol failures do not fall through to another permission request.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitRequestFailuresRemainSafe(bool configureFailure)
    {
        var session = new FakeSession { Denied = !configureFailure, ConfigureFails = configureFailure };
        int attempts = 0;
        await using GlobalShortcutsPortalInput input = CreateInput(
            _ =>
            {
                attempts++;
                return Task.FromResult<IPortalShortcutSession>(session);
            },
            TimeSpan.FromMilliseconds(10)
        );
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        await input.OpenDesktopShortcutSettingsAsync();
        await Task.Delay(40);
        Assert.Equal(configureFailure ? 1 : 2, attempts);
        Assert.Contains(configureFailure ? "could not open" : "declined", ShortcutUi(input).Status);
        Assert.True(ShortcutUi(input).CanOpen);
        await input.DisposeAsync();
        Assert.False(ShortcutUi(input).CanOpen);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => input.OpenDesktopShortcutSettingsAsync());
    }

    /// <summary>An unavailable portal cannot open configuration and does not prevent other listeners from running.</summary>
    [Fact]
    public async Task UnavailablePortalDisablesSettingsButton()
    {
        await using GlobalShortcutsPortalInput input = CreateInput(_ => throw new IOException("absent"));
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(ShortcutUi(input).CanOpen);
        await input.OpenDesktopShortcutSettingsAsync();
        Assert.Contains("unavailable", ShortcutUi(input).Status);
    }

    /// <summary>Unsupported services stop polling and leave the ordinary desktop-hook status visible.</summary>
    [Theory]
    [InlineData("unsupported")]
    [InlineData("org.freedesktop.DBus.Error.ServiceUnknown")]
    [InlineData("org.freedesktop.DBus.Error.UnknownInterface")]
    [InlineData("org.freedesktop.DBus.Error.UnknownMethod")]
    public async Task UnsupportedPortalStopsRetriesWithoutReplacingHookStatus(string error)
    {
        int attempts = 0;
        await using GlobalShortcutsPortalInput input = CreateInput(
            _ =>
            {
                attempts++;
                throw error == "unsupported"
                    ? new NotSupportedException("unsupported")
                    : new DBusException(error, "absent");
            },
            TimeSpan.FromMilliseconds(10)
        );
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(40);
        Assert.Equal(1, attempts);
        Assert.False(ShortcutUi(input).CanOpen);
        Assert.Contains("unavailable", ShortcutUi(input).Status);
        Assert.All(sink.Statuses, Assert.Empty);
    }

    /// <summary>Transient failures reconnect silently without obscuring the working desktop listener.</summary>
    [Fact]
    public async Task TransientPortalFailurePreservesHookStatus()
    {
        int attempts = 0;
        await using GlobalShortcutsPortalInput input = CreateInput(
            _ =>
            {
                Interlocked.Increment(ref attempts);
                throw new IOException("temporary bus failure");
            },
            TimeSpan.FromMilliseconds(10)
        );
        input.Update(Settings("O"));
        await WaitAsync(() => Volatile.Read(ref attempts) >= 2);
        Assert.All(sink.Statuses, Assert.Empty);
        Assert.Contains("unavailable", ShortcutUi(input).Status);
    }

    /// <summary>A missing host app registration cannot recover by repeating the same portal call.</summary>
    [Fact]
    public async Task MissingApplicationRegistrationStopsAutomaticRetries()
    {
        int attempts = 0;
        await using GlobalShortcutsPortalInput input = CreateInput(
            _ =>
            {
                Interlocked.Increment(ref attempts);
                throw new DBusException(
                    "org.freedesktop.portal.Error.Failed",
                    "Could not register app ID: App info not found for 'io.github.fenris159.SrvSurvey'"
                );
            },
            TimeSpan.FromMilliseconds(10)
        );
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(80);
        Assert.Equal(1, Volatile.Read(ref attempts));
        Assert.False(input.State.IsRunning);
        Assert.All(sink.Statuses, Assert.Empty);
    }

    /// <summary>Temporary outages remain recoverable but report one diagnostic per disconnected episode.</summary>
    [Fact]
    public async Task RepeatedFailuresLogOnceUntilThePortalRecovers()
    {
        string detail = "temporary shortcut portal failure " + Guid.NewGuid().ToString("N");
        using var recorder = new FailureLogRecorder(detail);
        Trace.Listeners.Add(recorder);
        try
        {
            int attempts = 0;
            var sessions = new ConcurrentQueue<FakeSession>();
            await using GlobalShortcutsPortalInput input = CreateInput(
                _ =>
                {
                    if (Interlocked.Increment(ref attempts) % 4 != 0)
                    {
                        throw new DBusException("org.freedesktop.portal.Error.Failed", detail);
                    }
                    var session = new FakeSession();
                    sessions.Enqueue(session);
                    return Task.FromResult<IPortalShortcutSession>(session);
                },
                TimeSpan.FromMilliseconds(10)
            );
            input.Update(Settings("O"));
            await WaitAsync(() => input.State.IsRunning);
            Assert.Equal(4, Volatile.Read(ref attempts));
            Assert.Single(recorder.Entries);
            sessions.Single().Closed.TrySetResult();
            await WaitAsync(() => sessions.Count == 2 && input.State.IsRunning);
            Assert.Equal(8, Volatile.Read(ref attempts));
            Assert.Equal(2, recorder.Entries.Count);
        }
        finally
        {
            Trace.Listeners.Remove(recorder);
        }
    }

    private sealed class FailureLogRecorder(string detail) : TraceListener
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (message?.Contains(detail, StringComparison.Ordinal) == true)
            {
                Entries.Enqueue(message);
            }
        }
    }

    /// <summary>Local setup failures stop retries, but repairing setup and re-enabling input can restore the session.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReenableRetriesApplicationRegistrationAfterRepair(bool localFailure)
    {
        int attempts = 0;
        await using GlobalShortcutsPortalInput input = CreateInput(
            _ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw localFailure
                        ? new PortalShortcutRegistrationException(new IOException("desktop entry unavailable"))
                        : new DBusException(
                            "org.freedesktop.portal.Error.Failed",
                            "App info not found for 'io.github.fenris159.SrvSurvey'"
                        );
                }
                return Task.FromResult<IPortalShortcutSession>(new FakeSession());
            },
            TimeSpan.FromMilliseconds(10)
        );
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(80);
        Assert.Equal(1, Volatile.Read(ref attempts));
        Assert.False(ShortcutUi(input).CanOpen);
        Assert.Contains("registration unavailable", ShortcutUi(input).Status);
        input.Update(Settings("O") with { KeyboardEnabled = false });
        input.Update(Settings("O"));
        await WaitAsync(() => input.State.IsRunning);
        Assert.Equal(2, Volatile.Read(ref attempts));
        Assert.True(input.State.CanServeAllBindings);
    }

    /// <summary>Creates one enabled test binding.</summary>
    private static GlobalInputSettings Settings(string chord) =>
        GlobalInputSettings.Default with
        {
            KeyboardEnabled = true,
            Bindings = new Dictionary<GlobalInputAction, string>
            {
                [GlobalInputAction.ToggleOverlayInteraction] = chord,
            },
        };

    /// <summary>Waits for a bounded, directly observable async lifecycle transition.</summary>
    private static async Task WaitAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    /// <summary>Emulates compositor acceptance, rejection, closure and pending permission.</summary>
    private sealed class FakeSession : IPortalShortcutSession
    {
        public event Action<string>? Activated;
        public event Action<IReadOnlySet<string>>? BindingsChanged;
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Approved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Denied { get; init; }
        public bool Configurable { get; init; }
        public bool ConfigureFails { get; init; }
        public bool ConfigurePending { get; init; }
        public int ConfigureCalls { get; private set; }
        public IReadOnlyDictionary<string, string> TriggerDescriptions { get; set; } = new Dictionary<string, string>();
        public bool Pending { get; init; }
        public bool Started { get; private set; }
        public bool ForceBind { get; private set; }
        public bool AllowPermissionPrompt { get; private set; }
        public bool Disposed { get; private set; }

        /// <summary>Emits a shortcut ID.</summary>
        public void Emit(string id) => Activated?.Invoke(id);

        /// <summary>Emits a desktop-side change to the accepted shortcut set.</summary>
        public void ChangeBindings(params string[] ids) =>
            BindingsChanged?.Invoke(ids.ToHashSet(StringComparer.Ordinal));

        /// <summary>Accepts a binding or waits for cancellation when permission is pending.</summary>
        public async Task<IReadOnlySet<string>> BindAsync(
            IReadOnlyList<PortalShortcutBinding> bindings,
            bool forceBind,
            CancellationToken token,
            bool allowPermissionPrompt = true
        )
        {
            Started = true;
            ForceBind = forceBind;
            AllowPermissionPrompt = allowPermissionPrompt;
            if (Denied)
            {
                if (!allowPermissionPrompt)
                {
                    throw new PortalShortcutPermissionDeferredException();
                }
                throw new PortalShortcutPermissionException();
            }
            if (Pending)
            {
                await Approved.Task.WaitAsync(token);
            }
            return bindings.Select(binding => binding.Id).ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>Emulates an existing-session settings menu, a pending request, or a protocol failure.</summary>
        public async Task<bool> TryConfigureAsync(CancellationToken token)
        {
            ConfigureCalls++;
            if (ConfigureFails)
            {
                throw new IOException("configuration unavailable");
            }
            if (ConfigurePending)
            {
                await Approved.Task.WaitAsync(token);
            }
            return Configurable;
        }

        /// <summary>Waits for simulated session closure.</summary>
        public Task WaitForCloseAsync(CancellationToken token) => Closed.Task.WaitAsync(token);

        /// <summary>Records disposal.</summary>
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
