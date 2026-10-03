using System.Diagnostics;

namespace SrvSurvey.Desktop.Input;

/// <summary>Owns an optional compositor shortcut session without blocking startup or the UI.</summary>
internal sealed class GlobalShortcutsPortalInput : IGlobalShortcutInput
{
    private readonly Lock gate = new();
    private readonly Func<CancellationToken, Task<IPortalShortcutSession>> openSession;
    private readonly TimeSpan retryDelay;
    private CancellationTokenSource? cancellation;
    private Task runTask = Task.CompletedTask;
    private readonly TaskCompletionSource startupReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IReadOnlyList<PortalShortcutBinding> bindings = [];
    private bool enabled;
    private bool configured;
    private bool forceNextBind;
    private bool disposed;
    private volatile bool running;
    private volatile bool handlesAllBindings;

    /// <summary>Creates a lazily opened portal session and a bounded reconnect interval.</summary>
    public GlobalShortcutsPortalInput(
        Func<CancellationToken, Task<IPortalShortcutSession>>? openSession = null,
        TimeSpan? retryDelay = null
    )
    {
        this.openSession = openSession ?? DbusGlobalShortcutSession.OpenAsync;
        this.retryDelay = retryDelay ?? TimeSpan.FromSeconds(10);
    }

    public event EventHandler<GlobalInputActionTriggeredEventArgs>? ActionTriggered;
    public event Action<string, bool>? StatusChanged;
    public bool IsRunning => running;
    public bool CanHandleAllBindings => handlesAllBindings;
    public Task StartupReady => startupReady.Task;

    /// <summary>Rebinds only for keyboard configuration changes and cancels any pending permission request.</summary>
    public void Update(GlobalInputSettings settings)
    {
        IReadOnlyList<PortalShortcutBinding> requested = PortalShortcutBinding.FromSettings(settings);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (configured && enabled == settings.KeyboardEnabled && bindings.SequenceEqual(requested))
            {
                return;
            }
            forceNextBind |= configured && !bindings.SequenceEqual(requested);
            bool allowPermissionPrompt = !configured;
            configured = true;
            bool forceBind = forceNextBind;
            enabled = settings.KeyboardEnabled;
            bindings = requested;
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            runTask = RunAfterAsync(
                runTask,
                settings.KeyboardEnabled ? requested : [],
                forceBind,
                allowPermissionPrompt,
                cancellation.Token
            );
        }
    }

    /// <summary>Serializes session replacement so old signals cannot activate new bindings.</summary>
    private async Task RunAfterAsync(
        Task previous,
        IReadOnlyList<PortalShortcutBinding> requested,
        bool forceBind,
        bool allowPermissionPrompt,
        CancellationToken token
    )
    {
        await previous.ConfigureAwait(false);
        while (requested.Count > 0 && !token.IsCancellationRequested)
        {
            try
            {
                await using IPortalShortcutSession session = await openSession(token).ConfigureAwait(false);
                IReadOnlySet<string> accepted = await session
                    .BindAsync(requested, forceBind, token, allowPermissionPrompt)
                    .ConfigureAwait(false);
                forceBind = false;
                lock (gate)
                {
                    if (!token.IsCancellationRequested)
                    {
                        forceNextBind = false;
                    }
                }
                await ObserveBoundSessionAsync(session, requested, accepted, token).ConfigureAwait(false);
            }
            catch (PortalShortcutPermissionDeferredException)
            {
                StatusChanged?.Invoke(
                    "Wayland shortcut approval is needed. Restart SrvSurvey to approve shortcuts at startup; existing keyboard listeners remain available.",
                    false
                );
                break;
            }
            catch (PortalShortcutPermissionException)
            {
                StatusChanged?.Invoke(
                    "Wayland shortcut permission was declined; existing keyboard listeners remain available.",
                    false
                );
                break;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Trace.TraceInformation("Global Shortcuts portal unavailable: {0}", exception.Message);
                StatusChanged?.Invoke("Global Shortcuts portal unavailable; using existing keyboard listeners.", false);
            }
            finally
            {
                startupReady.TrySetResult();
                allowPermissionPrompt = false;
                running = false;
                handlesAllBindings = false;
                StatusChanged?.Invoke(string.Empty, false);
            }
            try
            {
                await Task.Delay(retryDelay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
        }
        startupReady.TrySetResult();
    }

    /// <summary>Observes actions and desktop registration changes until an already-bound session closes.</summary>
    private async Task ObserveBoundSessionAsync(
        IPortalShortcutSession session,
        IReadOnlyList<PortalShortcutBinding> requested,
        IReadOnlySet<string> accepted,
        CancellationToken token
    )
    {
        string[] requestedIds = requested.Select(binding => binding.Id).ToArray();
        session.BindingsChanged += updated =>
        {
            if (!token.IsCancellationRequested)
            {
                Volatile.Write(ref accepted, updated);
                PublishAcceptedBindings(updated, requestedIds);
            }
        };
        session.Activated += id => OnActivation(id, requested, Volatile.Read(ref accepted), token);
        PublishAcceptedBindings(accepted, requestedIds);
        startupReady.TrySetResult();
        await session.WaitForCloseAsync(token).ConfigureAwait(false);
    }

    /// <summary>Rechecks actual desktop grants whenever the compositor changes registered shortcuts.</summary>
    private void PublishAcceptedBindings(IReadOnlySet<string> accepted, string[] requestedIds)
    {
        int count = requestedIds.Count(accepted.Contains);
        running = count > 0;
        handlesAllBindings = count == requestedIds.Length;
        StatusChanged?.Invoke($"Wayland portal shortcuts active: {count} of {requestedIds.Length} bindings.", running);
    }

    /// <summary>Routes only registered, accepted actions from the current uncanceled session.</summary>
    private void OnActivation(
        string id,
        IReadOnlyList<PortalShortcutBinding> requested,
        IReadOnlySet<string> accepted,
        CancellationToken token
    )
    {
        PortalShortcutBinding? binding = requested.FirstOrDefault(candidate => candidate.Id == id);
        if (!token.IsCancellationRequested && binding is not null && accepted.Contains(id))
        {
            ActionTriggered?.Invoke(this, new GlobalInputActionTriggeredEventArgs(binding.Action, binding.Chord));
        }
    }

    /// <summary>Stops pending permission dialogs and closes the portal before shutdown completes.</summary>
    public async ValueTask DisposeAsync()
    {
        Task pending;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            cancellation?.Cancel();
            pending = runTask;
        }
        await pending.ConfigureAwait(false);
        cancellation?.Dispose();
    }
}

/// <summary>Separates compositor registration and signals from shortcut selection and lifecycle policy.</summary>
internal interface IPortalShortcutSession : IAsyncDisposable
{
    event Action<string>? Activated;
    event Action<IReadOnlySet<string>>? BindingsChanged;

    /// <summary>Restores or requests the configured shortcuts and reports the IDs the compositor accepted.</summary>
    Task<IReadOnlySet<string>> BindAsync(
        IReadOnlyList<PortalShortcutBinding> bindings,
        bool forceBind,
        CancellationToken token,
        bool allowPermissionPrompt = true
    );

    /// <summary>Waits for a compositor disconnect or application cancellation.</summary>
    Task WaitForCloseAsync(CancellationToken token);
}

/// <summary>Distinguishes a user's refusal from a service failure that may be retried automatically.</summary>
public sealed class PortalShortcutPermissionException : Exception;

/// <summary>Defers new desktop approval to startup instead of interrupting an existing game session.</summary>
public sealed class PortalShortcutPermissionDeferredException : Exception;

/// <summary>Provides compositor-approved shortcut actions independently of raw keyboard hooks.</summary>
public interface IGlobalShortcutInput : IAsyncDisposable
{
    event EventHandler<GlobalInputActionTriggeredEventArgs>? ActionTriggered;
    event Action<string, bool>? StatusChanged;
    bool IsRunning { get; }
    bool CanHandleAllBindings => IsRunning;
    Task StartupReady => Task.CompletedTask;

    /// <summary>Applies keyboard enablement and shortcut bindings without restarting the application.</summary>
    void Update(GlobalInputSettings settings);
}
