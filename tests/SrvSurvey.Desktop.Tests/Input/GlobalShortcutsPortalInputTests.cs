using System.Collections.Concurrent;
using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class GlobalShortcutsPortalInputTests
{
    /// <summary>Respects desktop-side revocation and later reapproval without opening another permission request.</summary>
    [Fact]
    public async Task DesktopBindingChangesUpdateAvailabilityAndActions()
    {
        var session = new FakeSession();
        await using var input = new GlobalShortcutsPortalInput(_ => Task.FromResult<IPortalShortcutSession>(session));
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
        await using var input = new GlobalShortcutsPortalInput(
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
        await using var input = new GlobalShortcutsPortalInput(
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

    /// <summary>Completes startup after a refusal and avoids repeating the user's declined permission request.</summary>
    [Fact]
    public async Task DecliningStartupApprovalIsNotRetried()
    {
        int attempts = 0;
        await using var input = new GlobalShortcutsPortalInput(
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

    /// <summary>Requests approval once at startup, waits for its answer before focus handoff, and reconnects silently.</summary>
    [Fact]
    public async Task StartupApprovalBarrierAndSilentReconnect()
    {
        var sessions = new ConcurrentQueue<FakeSession>();
        var first = new FakeSession { Pending = true };
        await using var input = new GlobalShortcutsPortalInput(
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
        Assert.True(first.AllowPermissionPrompt);
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
        await using var input = new GlobalShortcutsPortalInput(_ =>
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
        await using var input = new GlobalShortcutsPortalInput(_ => Task.FromResult<IPortalShortcutSession>(session));
        input.Update(Settings("O"));
        await WaitAsync(() => session.Started);
        input.Update(Settings("O") with { KeyboardEnabled = false });
        await WaitAsync(() => session.Disposed);
        Assert.False(input.IsRunning);
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
