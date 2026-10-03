using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform.Overlay;
using Tmds.DBus;

namespace SrvSurvey.Desktop.Input;

/// <summary>Registers shortcuts with the desktop's version-one-or-newer Global Shortcuts portal.</summary>
internal sealed class DbusGlobalShortcutSession : IPortalShortcutSession
{
    private const string Service = "org.freedesktop.portal.Desktop";
    private readonly Func<ObjectPath, IRequest> requestFactory;
    private readonly Func<ObjectPath, IShortcutPortalSession> sessionFactory;
    private readonly Func<ObjectPath, Task> closeSession;
    private readonly Action disposeConnection;
    private readonly string senderName;
    private readonly IGlobalShortcutsPortal portal;
    private readonly PortalShortcutRegistrationStore registrations;
    private readonly List<IDisposable> subscriptions = [];
    private readonly HashSet<string> held = [];
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock gate = new();
    private ObjectPath? sessionPath;
    private bool disposed;
    private IReadOnlyDictionary<string, string> triggerDescriptions = new Dictionary<string, string>();
    private Exception? sessionError;

    /// <summary>Owns the session bus connection and proxies its shortcut interface.</summary>
    internal DbusGlobalShortcutSession(Connection connection, string senderName)
        : this(
            connection.CreateProxy<IGlobalShortcutsPortal>(Service, "/org/freedesktop/portal/desktop"),
            senderName,
            path => connection.CreateProxy<IRequest>(Service, path),
            path => connection.CreateProxy<IShortcutPortalSession>(Service, path),
            path => connection.CreateProxy<ISession>(Service, path).CloseAsync(),
            connection.Dispose,
            new PortalShortcutRegistrationStore(
                Path.Combine(AppDataPaths.ResolveCurrent().ConfigDirectory, "wayland-shortcut-registration.json")
            )
        ) { }

    /// <summary>Supplies portal proxies and ownership for deterministic protocol and cancellation tests.</summary>
    internal DbusGlobalShortcutSession(
        IGlobalShortcutsPortal portal,
        string senderName,
        Func<ObjectPath, IRequest> requestFactory,
        Func<ObjectPath, IShortcutPortalSession> sessionFactory,
        Func<ObjectPath, Task> closeSession,
        Action disposeConnection,
        PortalShortcutRegistrationStore? registrations = null
    )
    {
        this.portal = portal;
        this.senderName = senderName;
        this.requestFactory = requestFactory;
        this.sessionFactory = sessionFactory;
        this.closeSession = closeSession;
        this.disposeConnection = disposeConnection;
        this.registrations = registrations ?? new PortalShortcutRegistrationStore();
    }

    public IReadOnlyDictionary<string, string> TriggerDescriptions => Volatile.Read(ref triggerDescriptions);

    public event Action<string>? Activated;
    public event Action<IReadOnlySet<string>>? BindingsChanged;

    /// <summary>Connects to the user's bus without loading compositor-specific libraries.</summary>
    public static async Task<IPortalShortcutSession> OpenAsync(CancellationToken token)
    {
        var connection = new Connection(Address.Session);
        try
        {
            ConnectionInfo info = await connection.ConnectAsync().WaitAsync(token).ConfigureAwait(false);
            await RegisterApplicationAsync(
                    connection.CreateProxy<IHostPortalRegistry>(Service, "/org/freedesktop/portal/desktop"),
                    token,
                    File.Exists("/.flatpak-info") || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNAP"))
                )
                .ConfigureAwait(false);
            return new DbusGlobalShortcutSession(connection, info.LocalName);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Identifies the host app before portal calls, retaining automatic identity on older or sandboxed portals.</summary>
    internal static async Task RegisterApplicationAsync(
        IHostPortalRegistry registry,
        CancellationToken token,
        bool sandboxed = false
    )
    {
        if (sandboxed)
        {
            return;
        }
        try
        {
            await registry
                .RegisterAsync("io.github.fenris159.SrvSurvey", new Dictionary<string, object>())
                .WaitAsync(token)
                .ConfigureAwait(false);
        }
        catch (DBusException exception)
            when (exception.ErrorName
                    is "org.freedesktop.DBus.Error.UnknownMethod"
                        or "org.freedesktop.DBus.Error.UnknownInterface"
                        or "org.freedesktop.portal.Error.NotAllowed"
            )
        {
            // Older desktops lack Registry; sandboxed apps already have their own portal identity.
        }
    }

    /// <summary>Checks capabilities, creates a session, and restores accepted bindings before asking for new ones.</summary>
    public async Task<IReadOnlySet<string>> BindAsync(
        IReadOnlyList<PortalShortcutBinding> bindings,
        bool forceBind,
        CancellationToken token,
        bool allowPermissionPrompt = true
    )
    {
        if (await GetVersionAsync(token).ConfigureAwait(false) < 1)
        {
            throw new NotSupportedException("Global Shortcuts portal version is unsupported.");
        }
        await CreateSessionAsync(token).ConfigureAwait(false);
        IDictionary<string, object> listed = await RequestAsync(
                handle =>
                    portal.ListShortcutsAsync(
                        sessionPath!.Value,
                        new Dictionary<string, object> { ["handle_token"] = handle }
                    ),
                token
            )
            .ConfigureAwait(false);
        IReadOnlySet<string> accepted = ReadIds(listed);
        bool needsBinding = registrations.HasRegistration
            ? !registrations.Matches(bindings)
            : !accepted.SetEquals(bindings.Select(binding => binding.Id));
        IDictionary<string, object> displayResult = listed;
        if (forceBind || needsBinding)
        {
            if (!allowPermissionPrompt)
            {
                throw new PortalShortcutPermissionDeferredException();
            }
            (string Id, IDictionary<string, object> Properties)[] shortcuts = bindings
                .Select(binding =>
                    (
                        binding.Id,
                        (IDictionary<string, object>)
                            new Dictionary<string, object>
                            {
                                ["description"] = binding.Description,
                                ["preferred_trigger"] = binding.Trigger,
                            }
                    )
                )
                .ToArray();
            IDictionary<string, object> bound = await RequestAsync(
                    handle =>
                        portal.BindShortcutsAsync(
                            sessionPath!.Value,
                            shortcuts,
                            string.Empty,
                            new Dictionary<string, object> { ["handle_token"] = handle }
                        ),
                    token
                )
                .ConfigureAwait(false);
            displayResult = bound;
            accepted = ReadIds(bound);
        }
        UpdateTriggerDescriptions(displayResult);
        registrations.Save(bindings);
        return accepted;
    }

    /// <summary>Creates and observes a shortcut session without asking the user to approve bindings.</summary>
    internal async Task CreateSessionAsync(CancellationToken token)
    {
        string sessionToken = "srvsurvey_" + Guid.NewGuid().ToString("N");
        IDictionary<string, object> created = await RequestAsync(
                handle =>
                    portal.CreateSessionAsync(
                        new Dictionary<string, object>
                        {
                            ["handle_token"] = handle,
                            ["session_handle_token"] = sessionToken,
                        }
                    ),
                token
            )
            .ConfigureAwait(false);
        sessionPath = created["session_handle"] switch
        {
            ObjectPath path => path,
            string path => new ObjectPath(path),
            _ => throw new InvalidDataException("The shortcut portal returned an invalid session."),
        };
        IShortcutPortalSession session = sessionFactory(sessionPath.Value);
        subscriptions.Add(await session.WatchClosedAsync(_ => closed.TrySetResult(), OnError).ConfigureAwait(false));
        subscriptions.Add(
            await portal
                .WatchActivatedAsync(signal => OnActivated(signal.Session, signal.Id), OnError)
                .ConfigureAwait(false)
        );
        subscriptions.Add(
            await portal
                .WatchDeactivatedAsync(signal => OnDeactivated(signal.Session, signal.Id), OnError)
                .ConfigureAwait(false)
        );
        subscriptions.Add(
            await portal
                .WatchShortcutsChangedAsync(signal => OnBindingsChanged(signal.Session, signal.Shortcuts), OnError)
                .ConfigureAwait(false)
        );
    }

    /// <summary>Probes the installed interface without opening a permission dialog.</summary>
    internal Task<uint> GetVersionAsync(CancellationToken token) => portal.GetAsync<uint>("version").WaitAsync(token);

    /// <summary>Opens GNOME's per-application settings when its older portal silently restores existing shortcut grants.</summary>
    internal static async Task<bool> OpenLegacySettingsAsync(
        CancellationToken token,
        string? desktop = null,
        Func<CancellationToken, Task>? launch = null
    )
    {
        token.ThrowIfCancellationRequested();
        if (
            !(desktop ?? Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty)
                .Split(':')
                .Contains("GNOME", StringComparer.OrdinalIgnoreCase)
        )
        {
            return false;
        }
        await (launch ?? OpenGnomeSettingsAsync)(token).ConfigureAwait(false);
        return true;
    }

    /// <summary>Activates GNOME Settings over D-Bus so packaged application libraries cannot interfere with its launch.</summary>
    private static async Task OpenGnomeSettingsAsync(CancellationToken token)
    {
        using var connection = new Connection(Address.Session);
        await connection.ConnectAsync().WaitAsync(token).ConfigureAwait(false);
        await ActivateGnomeSettingsAsync(
                connection.CreateProxy<IDesktopSettingsActions>("org.gnome.Settings", "/org/gnome/Settings"),
                token
            )
            .ConfigureAwait(false);
    }

    /// <summary>Selects SrvSurvey's Applications page, where GNOME exposes its approved application shortcuts.</summary>
    internal static Task ActivateGnomeSettingsAsync(IDesktopSettingsActions settings, CancellationToken token) =>
        settings
            .ActivateAsync(
                "launch-panel",
                [("applications", (object[])["io.github.fenris159.SrvSurvey"])],
                new Dictionary<string, object>()
            )
            .WaitAsync(token);

    /// <summary>Uses the version-two settings UI without replacing active bindings, falling back on older backends.</summary>
    public async Task<bool> TryConfigureAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed), this);
        if (sessionPath is not ObjectPath path || await GetVersionAsync(token).ConfigureAwait(false) < 2)
        {
            return false;
        }
        try
        {
            await portal
                .ConfigureShortcutsAsync(path, string.Empty, new Dictionary<string, object>())
                .WaitAsync(token)
                .ConfigureAwait(false);
            return true;
        }
        catch (DBusException exception)
            when (exception.ErrorName
                    is "org.freedesktop.DBus.Error.UnknownMethod"
                        or "org.freedesktop.DBus.Error.UnknownInterface"
            )
        {
            return false;
        }
    }

    /// <summary>Retains the compositor's readable key descriptions, which are not portable key-code assignments.</summary>
    private void UpdateTriggerDescriptions(IDictionary<string, object> result)
    {
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        if (
            result.TryGetValue("shortcuts", out object? value)
            && value is (string Id, IDictionary<string, object> Properties)[] entries
        )
        {
            foreach ((string Id, IDictionary<string, object> Properties) entry in entries)
            {
                if (entry.Properties.TryGetValue("trigger_description", out object? trigger) && trigger is string text)
                {
                    descriptions[entry.Id] = text;
                }
            }
        }
        Volatile.Write(ref triggerDescriptions, descriptions);
    }

    /// <summary>Reads accepted shortcut IDs without assuming the desktop accepted every requested chord.</summary>
    internal static IReadOnlySet<string> ReadIds(IDictionary<string, object> result) =>
        result.TryGetValue("shortcuts", out object? shortcuts)
        && shortcuts is (string Id, IDictionary<string, object> Properties)[] entries
            ? entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Subscribes before sending a request so an immediate response cannot be lost.</summary>
    private async Task<IDictionary<string, object>> RequestAsync(
        Func<string, Task<ObjectPath>> invoke,
        CancellationToken token
    )
    {
        string handle = "srvsurvey_" + Guid.NewGuid().ToString("N");
        string sender = senderName.TrimStart(':').Replace('.', '_');
        var expected = new ObjectPath($"/org/freedesktop/portal/desktop/request/{sender}/{handle}");
        IRequest request = requestFactory(expected);
        var response = new TaskCompletionSource<(uint Code, IDictionary<string, object> Results)>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Exception? requestError = null;
        using IDisposable subscription = await request
            .WatchResponseAsync(
                value => response.TrySetResult(value),
                exception =>
                {
                    requestError = exception;
                    response.TrySetResult((0, new Dictionary<string, object>()));
                }
            )
            .ConfigureAwait(false);
        try
        {
            if (await invoke(handle).WaitAsync(token).ConfigureAwait(false) != expected)
            {
                throw new InvalidDataException("The shortcut portal returned an unexpected request handle.");
            }
            (uint code, IDictionary<string, object> results) = await response
                .Task.WaitAsync(token)
                .ConfigureAwait(false);
            if (Volatile.Read(ref requestError) is Exception exception)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
            }
            return code == 0 ? results : throw new PortalShortcutPermissionException();
        }
        catch (OperationCanceledException)
        {
            try
            {
                await request
                    .CloseAsync()
                    .WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is DBusException or TimeoutException or IOException)
            {
                // Shutdown must complete even if the desktop service stopped responding.
            }
            throw;
        }
    }

    /// <summary>Routes only this session's first activation until the compositor reports release.</summary>
    private void OnActivated(ObjectPath session, string id)
    {
        lock (gate)
        {
            if (!disposed && session == sessionPath && held.Add(id))
            {
                Activated?.Invoke(id);
            }
        }
    }

    /// <summary>Releases a shortcut only in its originating session.</summary>
    private void OnDeactivated(ObjectPath session, string id)
    {
        lock (gate)
        {
            if (session == sessionPath)
            {
                held.Remove(id);
            }
        }
    }

    /// <summary>Updates this session's granted actions and releases stale held state after desktop configuration changes.</summary>
    private void OnBindingsChanged(ObjectPath session, (string Id, IDictionary<string, object> Properties)[] shortcuts)
    {
        lock (gate)
        {
            if (!disposed && session == sessionPath)
            {
                held.Clear();
                UpdateTriggerDescriptions(new Dictionary<string, object> { ["shortcuts"] = shortcuts });
                BindingsChanged?.Invoke(shortcuts.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal));
            }
        }
    }

    /// <summary>Wakes the owner on service or signal-subscription failure so it can reconnect.</summary>
    private void OnError(Exception exception)
    {
        Interlocked.CompareExchange(ref sessionError, exception, null);
        closed.TrySetResult();
    }

    /// <summary>Waits for session loss or a settings/shutdown cancellation.</summary>
    public async Task WaitForCloseAsync(CancellationToken token)
    {
        await closed.Task.WaitAsync(token).ConfigureAwait(false);
        if (Volatile.Read(ref sessionError) is Exception exception)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    /// <summary>Closes permission state before releasing signal subscriptions and the bus connection.</summary>
    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
        }
        try
        {
            if (sessionPath is ObjectPath path)
            {
                await closeSession(path)
                    .WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is DBusException or TimeoutException or IOException)
        {
            // A desktop restart can remove the session before cleanup reaches it.
        }
        finally
        {
            foreach (IDisposable subscription in subscriptions)
            {
                subscription.Dispose();
            }
            disposeConnection();
        }
    }
}

/// <summary>Activates the host desktop's existing settings application without spawning a packaged-library child process.</summary>
[DBusInterface("org.gtk.Actions")]
internal interface IDesktopSettingsActions : IDBusObject
{
    /// <summary>Requests a settings panel with the desktop's supported action parameters.</summary>
    Task ActivateAsync(string action, object[] parameters, IDictionary<string, object> platformData);
}

/// <summary>Associates this connection with the installed SrvSurvey desktop entry on host portals.</summary>
[DBusInterface("org.freedesktop.host.portal.Registry")]
internal interface IHostPortalRegistry : IDBusObject
{
    /// <summary>Registers the application ID before any other portal calls use this connection.</summary>
    Task RegisterAsync(string appId, IDictionary<string, object> options);
}

/// <summary>Matches the portable GlobalShortcuts D-Bus interface, including version-one signals.</summary>
[DBusInterface("org.freedesktop.portal.GlobalShortcuts")]
internal interface IGlobalShortcutsPortal : IDBusObject
{
    /// <summary>Reads the advertised interface version.</summary>
    Task<T> GetAsync<T>(string property);

    /// <summary>Requests a new shortcut session.</summary>
    Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);

    /// <summary>Lists restored shortcut bindings.</summary>
    Task<ObjectPath> ListShortcutsAsync(ObjectPath session, IDictionary<string, object> options);

    /// <summary>Requests user-approved shortcuts for this session.</summary>
    Task<ObjectPath> BindShortcutsAsync(
        ObjectPath session,
        (string Id, IDictionary<string, object> Properties)[] shortcuts,
        string parentWindow,
        IDictionary<string, object> options
    );

    /// <summary>Opens the desktop's settings UI for an existing session on portal version two.</summary>
    Task ConfigureShortcutsAsync(ObjectPath session, string parentWindow, IDictionary<string, object> options);

    /// <summary>Watches compositor shortcut presses.</summary>
    Task<IDisposable> WatchActivatedAsync(
        Action<(ObjectPath Session, string Id, ulong Timestamp, IDictionary<string, object> Options)> handler,
        Action<Exception> onError
    );

    /// <summary>Watches compositor shortcut releases.</summary>
    Task<IDisposable> WatchDeactivatedAsync(
        Action<(ObjectPath Session, string Id, ulong Timestamp, IDictionary<string, object> Options)> handler,
        Action<Exception> onError
    );

    /// <summary>Watches compositor edits to this session's registered shortcuts.</summary>
    Task<IDisposable> WatchShortcutsChangedAsync(
        Action<(ObjectPath Session, (string Id, IDictionary<string, object> Properties)[] Shortcuts)> handler,
        Action<Exception> onError
    );
}

/// <summary>Matches the shortcut session's Closed signal.</summary>
[DBusInterface("org.freedesktop.portal.Session")]
internal interface IShortcutPortalSession : IDBusObject
{
    /// <summary>Watches the compositor closing this session.</summary>
    Task<IDisposable> WatchClosedAsync(Action<IDictionary<string, object>> handler, Action<Exception> onError);
}
