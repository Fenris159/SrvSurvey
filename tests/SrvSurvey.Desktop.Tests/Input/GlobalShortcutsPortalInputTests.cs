using System.Collections.Concurrent;
using SrvSurvey.Desktop.Input;
using Tmds.DBus;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class GlobalShortcutsPortalInputTests
{
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
        await input.OpenSettingsAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await input.OpenSettingsAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, settingsOpened);
        Assert.Equal(1, sessionsOpened);
        Assert.True(input.IsRunning);
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
        await input.OpenSettingsAsync();
        Assert.Contains("could not open", input.SettingsStatus);
        Assert.True(input.CanOpenSettings);
        Assert.True(input.IsRunning);
        Assert.False(session.Disposed);
    }

    /// <summary>Supplies isolated portal and desktop settings dependencies so tests cannot open host windows.</summary>
    private static GlobalShortcutsPortalInput CreateInput(
        Func<CancellationToken, Task<IPortalShortcutSession>> openSession,
        TimeSpan? retryDelay = null,
        Func<CancellationToken, Task<bool>>? openLegacySettings = null
    ) => new(openSession, retryDelay, openLegacySettings ?? (_ => Task.FromResult(false)));

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
        int received = 0;
        input.ActionTriggered += (_, _) => received++;
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(input.CanHandleAllBindings);
        session.ChangeBindings();
        Assert.False(input.IsRunning);
        Assert.False(input.CanHandleAllBindings);
        session.Emit("toggleOverlayInteraction");
        Assert.Equal(0, received);
        session.ChangeBindings("toggleOverlayInteraction", "unrelated");
        Assert.True(input.IsRunning);
        Assert.True(input.CanHandleAllBindings);
        session.Emit("toggleOverlayInteraction");
        Assert.Equal(1, received);
        input.Update(Settings("O") with { KeyboardEnabled = false });
        await WaitAsync(() => session.Disposed);
        session.ChangeBindings("toggleOverlayInteraction");
        Assert.False(input.IsRunning);
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
        var actions = new ConcurrentQueue<GlobalInputAction>();
        input.ActionTriggered += (_, args) => actions.Enqueue(args.Action);
        input.Update(Settings("O"));
        await WaitAsync(() => input.IsRunning);
        Assert.True(sessions.TryPeek(out FakeSession? first));
        first.Emit("unknown");
        first.Emit("toggleOverlayInteraction");
        Assert.Equal(GlobalInputAction.ToggleOverlayInteraction, Assert.Single(actions));
        input.Update(Settings("O") with { ControllerEnabled = true });
        Assert.Single(sessions);
        first.Closed.TrySetResult();
        await WaitAsync(() => sessions.Count == 2 && input.IsRunning);
        Assert.True(first.Disposed);
        input.Update(Settings("X"));
        await WaitAsync(() => sessions.Count == 3 && input.IsRunning);
        FakeSession latest = sessions.Last();
        Assert.True(latest.ForceBind);
        input.Update(Settings("X") with { KeyboardEnabled = false });
        await WaitAsync(() => latest.Disposed);
        latest.Emit("toggleOverlayInteraction");
        Assert.Single(actions);
        await input.DisposeAsync();
        await input.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => input.Update(Settings("O")));
    }

    /// <summary>Retries an unavailable service silently and defers fresh approval rather than opening a late game-session dialog.</summary>
    [Fact]
    public async Task RetriesServiceFailureButStopsAfterPermissionDeclined()
    {
        int attempts = 0;
        var statuses = new ConcurrentQueue<string>();
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
        input.StatusChanged += (message, _) => statuses.Enqueue(message);
        input.Update(Settings("O"));
        await WaitAsync(() =>
            statuses.Any(message => message.Contains("approval is needed", StringComparison.Ordinal))
        );
        await Task.Delay(40);
        Assert.Equal(2, Volatile.Read(ref attempts));
        Assert.False(input.IsRunning);
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
        Assert.False(input.IsRunning);
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
        Assert.True(input.CanHandleAllBindings);
        first.Closed.TrySetResult();
        await WaitAsync(() => sessions.Count == 2 && input.IsRunning);
        Assert.False(sessions.Last().AllowPermissionPrompt);
        input.Update(Settings("X"));
        await WaitAsync(() => sessions.Count == 3 && input.IsRunning);
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
        Assert.False(input.IsRunning);
        Assert.False(input.CanHandleAllBindings);
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
        Assert.False(input.IsRunning);
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
        var statuses = new ConcurrentQueue<string>();
        input.StatusChanged += (message, _) => statuses.Enqueue(message);
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(input.CanOpenSettings);
        Assert.DoesNotContain(statuses, message => message.Contains('\n'));
        Assert.Equal(0, session.ConfigureCalls);
        Assert.Contains("Super+P", input.SettingsStatus);
        await input.OpenSettingsAsync();
        Assert.Equal(1, opened);
        Assert.Equal(1, session.ConfigureCalls);
        Assert.False(session.AllowPermissionPrompt);
        Assert.True(input.IsRunning);
        session.TriggerDescriptions = new Dictionary<string, string> { ["toggleOverlayInteraction"] = "Ctrl+X" };
        session.ChangeBindings("toggleOverlayInteraction");
        Assert.Contains("Ctrl+X", input.SettingsStatus);
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
        Assert.True(input.CanOpenSettings);
        Assert.Contains("approval is needed", input.SettingsStatus);
        Task opening = input.OpenSettingsAsync();
        await WaitAsync(() => second.Started);
        Assert.True(second.AllowPermissionPrompt);
        Assert.True(second.ForceBind);
        Assert.False(input.CanOpenSettings);
        Assert.Same(opening, input.OpenSettingsAsync());
        second.Approved.TrySetResult();
        await opening.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(input.IsRunning);
        Assert.True(input.CanOpenSettings);
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
        await input.OpenSettingsAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, sessions.Count);
        Assert.True(sessions.First().Disposed);
        Assert.Equal(1, sessions.First().ConfigureCalls);
        Assert.True(sessions.Last().AllowPermissionPrompt);
        Assert.True(input.IsRunning);
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
        Task opening = input.OpenSettingsAsync();
        await WaitAsync(() => existingSession ? first.ConfigureCalls == 1 : pending.Started);
        input.Update(Settings("O") with { KeyboardEnabled = false });
        await opening.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(input.CanOpenSettings);
        Assert.Equal("Desktop shortcuts: off.", input.SettingsStatus);
        await input.OpenSettingsAsync();
        Assert.False(input.IsRunning);
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
        await input.OpenSettingsAsync();
        await Task.Delay(40);
        Assert.Equal(configureFailure ? 1 : 2, attempts);
        Assert.Contains(configureFailure ? "could not open" : "declined", input.SettingsStatus);
        Assert.True(input.CanOpenSettings);
        await input.DisposeAsync();
        Assert.False(input.CanOpenSettings);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => input.OpenSettingsAsync());
    }

    /// <summary>An unavailable portal cannot open configuration and does not prevent other listeners from running.</summary>
    [Fact]
    public async Task UnavailablePortalDisablesSettingsButton()
    {
        await using GlobalShortcutsPortalInput input = CreateInput(_ => throw new IOException("absent"));
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(input.CanOpenSettings);
        await input.OpenSettingsAsync();
        Assert.Contains("unavailable", input.SettingsStatus);
    }

    /// <summary>Unsupported services stop polling and leave the ordinary desktop-hook status visible.</summary>
    [Theory]
    [InlineData("unsupported")]
    [InlineData("org.freedesktop.DBus.Error.ServiceUnknown")]
    [InlineData("org.freedesktop.DBus.Error.UnknownInterface")]
    public async Task UnsupportedPortalStopsRetriesWithoutReplacingHookStatus(string error)
    {
        int attempts = 0;
        var statuses = new ConcurrentQueue<string>();
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
        input.StatusChanged += (message, _) => statuses.Enqueue(message);
        input.Update(Settings("O"));
        await input.StartupReady.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(40);
        Assert.Equal(1, attempts);
        Assert.False(input.CanOpenSettings);
        Assert.Contains("unavailable", input.SettingsStatus);
        Assert.All(statuses, Assert.Empty);
    }

    /// <summary>Transient failures reconnect silently without obscuring the working desktop listener.</summary>
    [Fact]
    public async Task TransientPortalFailurePreservesHookStatus()
    {
        var statuses = new ConcurrentQueue<string>();
        int attempts = 0;
        await using GlobalShortcutsPortalInput input = CreateInput(
            _ =>
            {
                Interlocked.Increment(ref attempts);
                throw new IOException("temporary bus failure");
            },
            TimeSpan.FromMilliseconds(10)
        );
        input.StatusChanged += (message, _) => statuses.Enqueue(message);
        input.Update(Settings("O"));
        await WaitAsync(() => Volatile.Read(ref attempts) >= 2);
        Assert.All(statuses, Assert.Empty);
        Assert.Contains("unavailable", input.SettingsStatus);
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
