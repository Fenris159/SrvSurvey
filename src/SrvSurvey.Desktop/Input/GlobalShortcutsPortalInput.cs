using System.Diagnostics;
using Tmds.DBus;

namespace SrvSurvey.Desktop.Input;

/// <summary>Owns an optional compositor shortcut session without blocking startup or the UI.</summary>
internal sealed class GlobalShortcutsPortalInput : IGlobalShortcutInput
{
    private readonly Lock gate = new();
    private readonly Func<CancellationToken, Task<IPortalShortcutSession>> openSession;
    private readonly TimeSpan retryDelay;
    private readonly Func<CancellationToken, Task<bool>> openLegacySettings;
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
    private volatile bool settingsAvailable;
    private volatile bool openingSettings;
    private string settingsStatus = "Desktop shortcuts: checking availability.";
    private IPortalShortcutSession? activeSession;
    private Task settingsTask = Task.CompletedTask;

    /// <summary>Creates a lazily opened portal session and a bounded reconnect interval.</summary>
    public GlobalShortcutsPortalInput(
        Func<CancellationToken, Task<IPortalShortcutSession>>? openSession = null,
        TimeSpan? retryDelay = null,
        Func<CancellationToken, Task<bool>>? openLegacySettings = null
    )
    {
        this.openSession = openSession ?? DbusGlobalShortcutSession.OpenAsync;
        this.retryDelay = retryDelay ?? TimeSpan.FromSeconds(10);
        this.openLegacySettings =
            openLegacySettings ?? (token => DbusGlobalShortcutSession.OpenLegacySettingsAsync(token));
    }

    public event EventHandler<GlobalInputActionTriggeredEventArgs>? ActionTriggered;
    public event Action<string, bool>? StatusChanged;
    public bool IsRunning => running;
    public bool CanHandleAllBindings => handlesAllBindings;
    public Task StartupReady => startupReady.Task;
    public bool CanOpenSettings =>
        settingsAvailable
        && Volatile.Read(ref enabled)
        && Volatile.Read(ref bindings).Count > 0
        && !Volatile.Read(ref disposed)
        && !openingSettings;
    public string SettingsStatus => Volatile.Read(ref settingsStatus);

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
                false,
                cancellation.Token
            );
            if (!enabled)
            {
                SetSettingsStatus("Desktop shortcuts: off.", false);
            }
        }
    }

    /// <summary>Opens desktop configuration only on explicit request, using the current session where supported.</summary>
    public Task OpenSettingsAsync()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!settingsTask.IsCompleted)
            {
                return settingsTask;
            }
            if (!CanOpenSettings || bindings.Count == 0)
            {
                return Task.CompletedTask;
            }
            openingSettings = true;
            settingsTask = OpenSettingsCoreAsync(activeSession, forceNextBind, cancellation!.Token);
            return settingsTask;
        }
    }

    /// <summary>Opens supported portal or legacy desktop settings, requesting a new binding only when approval is needed.</summary>
    private async Task OpenSettingsCoreAsync(
        IPortalShortcutSession? session,
        bool needsBinding,
        CancellationToken token
    )
    {
        string previousStatus = SettingsStatus;
        SetSettingsStatus("Desktop shortcuts: opening settings...", true);
        try
        {
            if (
                session is not null
                && !needsBinding
                && (
                    await session.TryConfigureAsync(token).ConfigureAwait(false)
                    || await openLegacySettings(token).ConfigureAwait(false)
                )
            )
            {
                return;
            }
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate)
            {
                if (disposed || !enabled || token.IsCancellationRequested)
                {
                    return;
                }
                cancellation!.Cancel();
                cancellation.Dispose();
                cancellation = new CancellationTokenSource();
                runTask = RunAfterAsync(runTask, bindings, true, true, cancellation.Token, ready);
            }
            await ready.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Closing or disabling keyboard input cancels the explicit desktop request.
        }
        catch (Exception exception)
        {
            Trace.TraceInformation("Desktop shortcut settings could not open: {0}", exception.Message);
            SetSessionSettingsStatus(
                "Desktop shortcut settings could not open. Try again when the desktop portal is available.",
                settingsAvailable,
                token
            );
        }
        finally
        {
            openingSettings = false;
            if (SettingsStatus == "Desktop shortcuts: opening settings..." && !token.IsCancellationRequested)
            {
                SetSettingsStatus(running ? previousStatus : "Desktop shortcuts: inactive.", settingsAvailable);
            }
            StatusChanged?.Invoke(string.Empty, running);
        }
    }

    /// <summary>Publishes desktop capability and menu status separately from the ordinary keyboard listener.</summary>
    private void SetSettingsStatus(string message, bool available, bool reportStatus = true)
    {
        settingsAvailable = available;
        Volatile.Write(ref settingsStatus, message);
        StatusChanged?.Invoke(reportStatus ? message.Split('\n')[0] : string.Empty, running);
    }

    /// <summary>Serializes session replacement so old signals cannot activate new bindings.</summary>
    private async Task RunAfterAsync(
        Task previous,
        IReadOnlyList<PortalShortcutBinding> requested,
        bool forceBind,
        bool allowPermissionPrompt,
        CancellationToken token,
        TaskCompletionSource? ready = null
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
                ActivateSession(session, token);
                forceBind = false;
                await ObserveBoundSessionAsync(session, requested, accepted, ready, token).ConfigureAwait(false);
            }
            catch (PortalShortcutPermissionDeferredException)
            {
                SetSessionSettingsStatus(
                    "Wayland shortcut approval is needed. Click Desktop shortcut settings to approve; existing keyboard listeners remain available.",
                    true,
                    token
                );
                break;
            }
            catch (PortalShortcutPermissionException)
            {
                SetSessionSettingsStatus(
                    "Desktop shortcuts: inactive. Approval was declined; click Desktop shortcut settings to try again.",
                    true,
                    token
                );
                break;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (IsUnsupportedPortal(exception))
            {
                Trace.TraceInformation("Global Shortcuts portal unsupported: {0}", exception.Message);
                SetSessionSettingsStatus(
                    "Global Shortcuts portal unavailable on this desktop.",
                    false,
                    token,
                    reportStatus: false
                );
                break;
            }
            catch (Exception exception)
            {
                Trace.TraceInformation("Global Shortcuts portal unavailable: {0}", exception.Message);
                SetSessionSettingsStatus(
                    "Global Shortcuts portal unavailable; using existing keyboard listeners.",
                    false,
                    token,
                    reportStatus: false
                );
            }
            finally
            {
                startupReady.TrySetResult();
                allowPermissionPrompt = false;
                ReleaseSession(token);
                ready?.TrySetResult();
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
        ready?.TrySetResult();
    }

    /// <summary>Publishes only a current binding session and clears its pending registration change atomically.</summary>
    private void ActivateSession(IPortalShortcutSession session, CancellationToken token)
    {
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            forceNextBind = false;
            activeSession = session;
        }
    }

    /// <summary>Stops retries for desktops with no shortcut service or supported interface, retaining transient reconnects.</summary>
    private static bool IsUnsupportedPortal(Exception exception) =>
        exception
            is NotSupportedException
                or DBusException
                {
                    ErrorName: "org.freedesktop.DBus.Error.ServiceUnknown"
                        or "org.freedesktop.DBus.Error.UnknownInterface"
                };

    /// <summary>Clears a closed session and reports reconnecting without overwriting approval or disabled status.</summary>
    private void ReleaseSession(CancellationToken token)
    {
        lock (gate)
        {
            running = false;
            handlesAllBindings = false;
            activeSession = null;
            if (
                !token.IsCancellationRequested
                && SettingsStatus.StartsWith("Desktop shortcuts: active", StringComparison.Ordinal)
            )
            {
                SetSessionSettingsStatus("Desktop shortcuts: inactive. Reconnecting...", settingsAvailable, token);
            }
        }
        StatusChanged?.Invoke(string.Empty, false);
    }

    /// <summary>Observes actions and desktop registration changes until an already-bound session closes.</summary>
    private async Task ObserveBoundSessionAsync(
        IPortalShortcutSession session,
        IReadOnlyList<PortalShortcutBinding> requested,
        IReadOnlySet<string> accepted,
        TaskCompletionSource? ready,
        CancellationToken token
    )
    {
        session.BindingsChanged += updated =>
        {
            if (!token.IsCancellationRequested)
            {
                Volatile.Write(ref accepted, updated);
                PublishAcceptedBindings(session, updated, requested, token);
            }
        };
        session.Activated += id => OnActivation(id, requested, Volatile.Read(ref accepted), token);
        PublishAcceptedBindings(session, accepted, requested, token);
        ready?.TrySetResult();
        startupReady.TrySetResult();
        await session.WaitForCloseAsync(token).ConfigureAwait(false);
    }

    /// <summary>Ignores stale status from a canceled registration after settings or shutdown changes.</summary>
    private void SetSessionSettingsStatus(
        string message,
        bool available,
        CancellationToken token,
        bool reportStatus = true
    )
    {
        lock (gate)
        {
            if (!token.IsCancellationRequested && !disposed && enabled)
            {
                SetSettingsStatus(message, available, reportStatus);
            }
        }
    }

    /// <summary>Publishes actual desktop grants and their readable triggers without rewriting application key codes.</summary>
    private void PublishAcceptedBindings(
        IPortalShortcutSession session,
        IReadOnlySet<string> accepted,
        IReadOnlyList<PortalShortcutBinding> requested,
        CancellationToken token
    )
    {
        lock (gate)
        {
            if (token.IsCancellationRequested || disposed || !enabled)
            {
                return;
            }
            int count = requested.Count(binding => accepted.Contains(binding.Id));
            running = count > 0;
            handlesAllBindings = count == requested.Count;
            string triggers = string.Join(
                "\n",
                requested
                    .Where(binding => accepted.Contains(binding.Id))
                    .Select(binding =>
                        session.TriggerDescriptions.TryGetValue(binding.Id, out string? trigger)
                            ? $"{binding.Description}: {trigger}"
                            : $"{binding.Description}: approved by desktop (key not reported)"
                    )
            );
            string message =
                count > 0
                    ? $"Desktop shortcuts: active ({count} of {requested.Count} bindings).\n{triggers}"
                    : "Desktop shortcuts: inactive. Click Desktop shortcut settings to approve bindings.";
            SetSettingsStatus(message, true);
        }
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
            settingsAvailable = false;
            cancellation?.Cancel();
            pending = runTask;
        }
        await pending.ConfigureAwait(false);
        await settingsTask.ConfigureAwait(false);
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

    IReadOnlyDictionary<string, string> TriggerDescriptions => new Dictionary<string, string>();

    /// <summary>Opens desktop configuration on version two, returning false when a fresh binding request is needed.</summary>
    Task<bool> TryConfigureAsync(CancellationToken token) => Task.FromResult(false);
}

/// <summary>Distinguishes a user's refusal from a service failure that may be retried automatically.</summary>
public sealed class PortalShortcutPermissionException : Exception;

/// <summary>Defers new desktop approval until an explicit settings request.</summary>
public sealed class PortalShortcutPermissionDeferredException : Exception;

/// <summary>Provides compositor-approved shortcut actions independently of raw keyboard hooks.</summary>
public interface IGlobalShortcutInput : IAsyncDisposable
{
    event EventHandler<GlobalInputActionTriggeredEventArgs>? ActionTriggered;
    event Action<string, bool>? StatusChanged;
    bool IsRunning { get; }
    bool CanHandleAllBindings => IsRunning;
    Task StartupReady => Task.CompletedTask;
    bool CanOpenSettings => false;
    string SettingsStatus => "Desktop shortcut settings are unavailable.";

    /// <summary>Requests desktop shortcut configuration only in response to the settings button.</summary>
    Task OpenSettingsAsync() => Task.CompletedTask;

    /// <summary>Applies keyboard enablement and shortcut bindings without restarting the application.</summary>
    void Update(GlobalInputSettings settings);
}
