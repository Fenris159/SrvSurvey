using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;
using Tmds.DBus;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class DbusGlobalShortcutSessionTests
{
    /// <summary>Uses application settings only on GNOME, never attempting a host launch during capability discovery.</summary>
    [Theory]
    [InlineData("ubuntu:GNOME", true)]
    [InlineData("gnome", true)]
    [InlineData("KDE", false)]
    [InlineData("X-Cinnamon", false)]
    [InlineData("", false)]
    public async Task OpensLegacySettingsOnlyOnGnome(string desktop, bool expected)
    {
        int launches = 0;
        bool opened = await DbusGlobalShortcutSession.OpenLegacySettingsAsync(
            CancellationToken.None,
            desktop,
            _ =>
            {
                launches++;
                return Task.CompletedTask;
            }
        );
        Assert.Equal(expected, opened);
        Assert.Equal(expected ? 1 : 0, launches);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DbusGlobalShortcutSession.OpenLegacySettingsAsync(canceled.Token, desktop)
        );
        if (expected)
        {
            await Assert.ThrowsAsync<IOException>(() =>
                DbusGlobalShortcutSession.OpenLegacySettingsAsync(
                    CancellationToken.None,
                    desktop,
                    _ => throw new IOException("Settings unavailable")
                )
            );
        }
    }

    /// <summary>Uses GNOME's application-panel action with SrvSurvey's canonical desktop ID and propagates launch failures.</summary>
    [Fact]
    public async Task GnomeSettingsActionTargetsThisApplication()
    {
        var settings = new FakeSettingsActions();
        await DbusGlobalShortcutSession.ActivateGnomeSettingsAsync(settings, CancellationToken.None);
        Assert.Equal(1, settings.Calls);
        settings.Failure = true;
        await Assert.ThrowsAsync<IOException>(() =>
            DbusGlobalShortcutSession.ActivateGnomeSettingsAsync(settings, CancellationToken.None)
        );
    }

    /// <summary>Opens configuration only on supported versions and preserves the restored session.</summary>
    [Theory]
    [InlineData(1u, false)]
    [InlineData(2u, true)]
    public async Task ConfiguresExistingSessionWhenSupported(uint version, bool expected)
    {
        var server = new FakePortal { Restored = true, Version = version };
        await using DbusGlobalShortcutSession session = server.CreateClient();
        Assert.False(await session.TryConfigureAsync(CancellationToken.None));
        await session.BindAsync(Bindings(), false, CancellationToken.None, false);
        Assert.Equal(expected, await session.TryConfigureAsync(CancellationToken.None));
        Assert.Equal(expected ? 1 : 0, server.ConfigureCalls);
        Assert.Equal(0, server.BindCalls);
        Assert.Equal("Alt+O", session.TriggerDescriptions["toggleOverlayInteraction"]);
        server.ChangeTrigger("Super+P");
        Assert.Equal("Super+P", session.TriggerDescriptions["toggleOverlayInteraction"]);
        server.ChangeBindings(server.SessionPath, "toggleOverlayInteraction");
        Assert.Empty(session.TriggerDescriptions);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.TryConfigureAsync(canceled.Token));
        await session.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.TryConfigureAsync(CancellationToken.None));
    }

    /// <summary>Falls back only for missing configuration support; other failures remain visible to the owner.</summary>
    [Theory]
    [InlineData("org.freedesktop.DBus.Error.UnknownMethod", true)]
    [InlineData("org.freedesktop.DBus.Error.UnknownInterface", true)]
    [InlineData("org.freedesktop.portal.Error.Failed", false)]
    public async Task ConfigurationFailureIsNotMistakenForUnsupportedPortal(string error, bool unsupported)
    {
        var server = new FakePortal
        {
            Restored = true,
            Version = 2,
            ConfigurationFailure = error,
        };
        await using DbusGlobalShortcutSession session = server.CreateClient();
        await session.BindAsync(Bindings(), false, CancellationToken.None, false);
        if (unsupported)
        {
            Assert.False(await session.TryConfigureAsync(CancellationToken.None));
        }
        else
        {
            await Assert.ThrowsAsync<DBusException>(() => session.TryConfigureAsync(CancellationToken.None));
        }
        Assert.Equal(1, server.ConfigureCalls);
        Assert.Equal(0, server.BindCalls);
    }

    /// <summary>Routes only this session's desktop edits and releases held state when grants change.</summary>
    [Fact]
    public async Task ObservesRegisteredShortcutChanges()
    {
        var server = new FakePortal { Restored = true };
        await using DbusGlobalShortcutSession session = server.CreateClient();
        await session.BindAsync(Bindings(), false, CancellationToken.None);
        IReadOnlySet<string>? changed = null;
        session.BindingsChanged += ids => changed = ids;
        int actions = 0;
        session.Activated += _ => actions++;
        server.Press(server.SessionPath);
        server.ChangeBindings(new ObjectPath("/other"), "other");
        Assert.Null(changed);
        server.ChangeBindings(server.SessionPath, "mapZoomIn");
        Assert.Contains("mapZoomIn", changed!);
        server.Press(server.SessionPath);
        Assert.Equal(2, actions);
        await session.DisposeAsync();
        server.ChangeBindings(server.SessionPath);
        Assert.Contains("mapZoomIn", changed!);
    }

    /// <summary>Restores desktop-approved bindings silently and defers a new permission request outside startup.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SilentReconnectDoesNotRequestApproval(bool restored)
    {
        var server = new FakePortal { Restored = restored };
        await using DbusGlobalShortcutSession session = server.CreateClient();
        if (restored)
        {
            Assert.Single(await session.BindAsync(Bindings(), false, CancellationToken.None, false));
        }
        else
        {
            await Assert.ThrowsAsync<PortalShortcutPermissionDeferredException>(() =>
                session.BindAsync(Bindings(), false, CancellationToken.None, false)
            );
        }
        Assert.Equal(0, server.BindCalls);
    }

    /// <summary>Remembers a completed partial approval while continuing to trust the desktop's actual accepted IDs.</summary>
    [Fact]
    public async Task RemembersPartialDesktopSelectionWithoutRepeatingApproval()
    {
        IReadOnlyList<PortalShortcutBinding> requested =
        [
            .. Bindings(),
            new("mapZoomIn", "Zoom", "p", "P", GlobalInputAction.MapZoomIn),
        ];
        var registration = new PortalShortcutRegistrationStore();
        registration.Save(requested);
        var server = new FakePortal { Restored = true };
        await using DbusGlobalShortcutSession session = server.CreateClient(registration);
        Assert.Single(await session.BindAsync(requested, false, CancellationToken.None, false));
        Assert.Equal(0, server.BindCalls);
        var noPermissions = new FakePortal();
        await using DbusGlobalShortcutSession denied = noPermissions.CreateClient(registration);
        Assert.Empty(await denied.BindAsync(requested, false, CancellationToken.None, false));
        Assert.Equal(0, noPermissions.BindCalls);
    }

    /// <summary>Detects changed key triggers after restarting, deferring their approval until the explicit settings request.</summary>
    [Fact]
    public async Task ChangedBindingsNeedExplicitApprovalEvenWhenActionIdsMatch()
    {
        var registration = new PortalShortcutRegistrationStore();
        registration.Save(Bindings());
        IReadOnlyList<PortalShortcutBinding> changed = [Bindings()[0] with { Trigger = "ALT+x", Chord = "ALT X" }];
        var server = new FakePortal { Restored = true };
        await using DbusGlobalShortcutSession deferred = server.CreateClient(registration);
        await Assert.ThrowsAsync<PortalShortcutPermissionDeferredException>(() =>
            deferred.BindAsync(changed, false, CancellationToken.None, false)
        );
        Assert.Equal(0, server.BindCalls);
        Assert.False(registration.Matches(changed));
        var restarted = new FakePortal { Restored = true, ExpectedTrigger = "ALT+x" };
        await using DbusGlobalShortcutSession startup = restarted.CreateClient(registration);
        await startup.BindAsync(changed, false, CancellationToken.None);
        Assert.Equal(1, restarted.BindCalls);
        Assert.True(registration.Matches(changed));
    }

    /// <summary>Stores stable binding metadata, tolerates malformed metadata and keeps a live registration after write failure.</summary>
    [Fact]
    public void RegistrationMetadataSurvivesRestartAndStorageErrors()
    {
        string root = Path.Combine(Path.GetTempPath(), "srv-shortcut-metadata-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "bindings.json");
            var registration = new PortalShortcutRegistrationStore(path);
            Assert.False(registration.HasRegistration);
            Assert.False(registration.Matches(Bindings()));
            registration.Save(Bindings());
            var reloaded = new PortalShortcutRegistrationStore(path);
            Assert.True(reloaded.HasRegistration);
            Assert.True(reloaded.Matches([Bindings()[0] with { Description = "Translated" }]));
            Assert.False(reloaded.Matches([Bindings()[0] with { Trigger = "x" }]));
            File.WriteAllText(path, "{\"Bindings\":4}");
            Assert.False(new PortalShortcutRegistrationStore(path).HasRegistration);
            File.WriteAllText(path, "malformed");
            Assert.False(new PortalShortcutRegistrationStore(path).HasRegistration);
            var unwritable = new PortalShortcutRegistrationStore(root);
            unwritable.Save(Bindings());
            Assert.True(unwritable.Matches(Bindings()));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>Uses the installed desktop ID and tolerates only missing or sandbox-restricted host registration.</summary>
    [Theory]
    [InlineData(null, true, false)]
    [InlineData("org.freedesktop.DBus.Error.UnknownMethod", true, false)]
    [InlineData("org.freedesktop.DBus.Error.UnknownInterface", true, false)]
    [InlineData("org.freedesktop.portal.Error.NotAllowed", true, false)]
    [InlineData("org.freedesktop.portal.Error.Failed", false, false)]
    [InlineData("org.freedesktop.portal.Error.Failed", true, true)]
    public async Task RegistersHostIdentityWithCompatibleFallbacks(string? error, bool accepted, bool sandboxed)
    {
        var registry = new FakeRegistry(error);
        int preparationCalls = 0;
        Exception? failure = await Record.ExceptionAsync(() =>
            DbusGlobalShortcutSession.RegisterApplicationAsync(
                registry,
                CancellationToken.None,
                sandboxed,
                _ =>
                {
                    preparationCalls++;
                    return Task.CompletedTask;
                }
            )
        );
        Assert.Equal(sandboxed ? null : "io.github.fenris159.SrvSurvey", registry.AppId);
        Assert.True(registry.Options is null || registry.Options.Count == 0);
        Assert.Equal(accepted, failure is null);
        Assert.Equal(sandboxed ? 0 : 1, preparationCalls);
    }

    /// <summary>Classifies file, permission and malformed-entry failures as permanent setup errors while preserving cancellation.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DesktopEntryFailureIsReportedBeforeAnyPortalRequest(int failureKind)
    {
        var registry = new FakeRegistry(null);
        Exception cause = failureKind switch
        {
            0 => new IOException("desktop entry unavailable"),
            1 => new UnauthorizedAccessException("read-only data directory"),
            _ => new InvalidDataException("desktop entry has no Desktop Entry section"),
        };
        PortalShortcutRegistrationException failure = await Assert.ThrowsAsync<PortalShortcutRegistrationException>(
            () =>
                DbusGlobalShortcutSession.RegisterApplicationAsync(
                    registry,
                    CancellationToken.None,
                    ensureDesktopEntry: _ => throw cause
                )
        );
        Assert.Same(cause, failure.InnerException);
        Assert.Null(registry.AppId);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DbusGlobalShortcutSession.RegisterApplicationAsync(
                registry,
                canceled.Token,
                ensureDesktopEntry: token => Task.FromCanceled(token)
            )
        );
        Assert.Null(registry.AppId);
    }

    /// <summary>An unintegrated AppImage has a discoverable identity before its host portal registration.</summary>
    [Fact]
    public async Task CreatesDesktopIdentityBeforeRegisteringWithPortal()
    {
        string root = Directory.CreateTempSubdirectory("SrvSurvey-portal-identity-").FullName;
        try
        {
            string applications = Path.Combine(root, "applications");
            string desktopFile = Path.Combine(applications, LinuxDesktopEntryRegistration.DesktopFileName);
            var registry = new FakeRegistry(null, desktopFile);
            await DbusGlobalShortcutSession.RegisterApplicationAsync(
                registry,
                CancellationToken.None,
                ensureDesktopEntry: token =>
                    LinuxDesktopEntryRegistration.EnsureAsync(
                        applications,
                        "/Applications/SrvSurvey.AppImage",
                        [],
                        token
                    )
            );
            Assert.Equal(LinuxDesktopEntryRegistration.ApplicationId, registry.AppId);
            string launcher = await File.ReadAllTextAsync(desktopFile);
            Assert.Contains("NoDisplay=true", launcher, StringComparison.Ordinal);
            Assert.Contains("TryExec=/Applications/SrvSurvey.AppImage", launcher, StringComparison.Ordinal);
            Assert.DoesNotContain("MimeType=", launcher, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Checks version-one registration, session filtering, hold-repeat suppression, and closure.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RegistersOrRestoresAndRoutesOnlyOwnSession(bool restored, bool force)
    {
        var server = new FakePortal { Restored = restored };
        await using DbusGlobalShortcutSession session = server.CreateClient();
        IReadOnlySet<string> accepted = await session.BindAsync(Bindings(), force, CancellationToken.None);
        Assert.Contains("toggleOverlayInteraction", accepted);
        Assert.Equal(!restored || force ? 1 : 0, server.BindCalls);
        int count = 0;
        session.Activated += _ => count++;
        server.Press(new ObjectPath("/other"));
        server.Release(new ObjectPath("/other"));
        server.Press(server.SessionPath);
        server.Press(server.SessionPath);
        Assert.Equal(1, count);
        server.Release(server.SessionPath);
        server.Press(server.SessionPath);
        Assert.Equal(2, count);
        Task closed = session.WaitForCloseAsync(CancellationToken.None);
        server.Close();
        await closed.WaitAsync(TimeSpan.FromSeconds(2));
        await session.DisposeAsync();
        Assert.True(server.ConnectionDisposed);
        Assert.Equal(1, server.SessionCloses);
    }

    /// <summary>Reports a session signal error to the reconnect owner and still releases the bus.</summary>
    [Fact]
    public async Task SignalFailureAndSessionCleanupAreSafe()
    {
        var server = new FakePortal { CloseFails = true };
        await using DbusGlobalShortcutSession session = server.CreateClient();
        await session.BindAsync(Bindings(), false, CancellationToken.None);
        server.SignalError(new IOException("desktop stopped"));
        await Assert.ThrowsAsync<IOException>(() => session.WaitForCloseAsync(CancellationToken.None));
        await session.DisposeAsync();
        Assert.True(server.ConnectionDisposed);
    }

    /// <summary>Rejects unsupported versions, canceled/denied authorization, invalid session data and mismatched handles.</summary>
    [Theory]
    [InlineData("version")]
    [InlineData("cancel")]
    [InlineData("deny")]
    [InlineData("session")]
    [InlineData("handle")]
    [InlineData("watcherror")]
    public async Task FailsInvalidPortalResponses(string failure)
    {
        var server = new FakePortal { Failure = failure };
        await using DbusGlobalShortcutSession session = server.CreateClient();
        Exception? exception = await Record.ExceptionAsync(() =>
            session.BindAsync(Bindings(), false, CancellationToken.None)
        );
        Assert.NotNull(exception);
        Assert.IsNotType<NullReferenceException>(exception);
    }

    /// <summary>Cancels an unanswered request and tells the desktop to close its dialog.</summary>
    [Fact]
    public async Task CancellationClosesPendingRequest()
    {
        var server = new FakePortal { Failure = "pending" };
        await using DbusGlobalShortcutSession session = server.CreateClient();
        using var cancellation = new CancellationTokenSource();
        Task operation = session.BindAsync(Bindings(), false, cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(1, server.RequestCloses);
    }

    /// <summary>Checks real portal identity and session creation without opening a permission dialog.</summary>
    [Fact]
    public async Task CanProbeInstalledPortalWithoutPermissionDialog()
    {
        if (
            !OperatingSystem.IsLinux()
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"))
        )
        {
            Assert.Skip("An installed session portal is not available on this test host.");
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await using IPortalShortcutSession session = await DbusGlobalShortcutSession.OpenAsync(timeout.Token);
            uint version = await ((DbusGlobalShortcutSession)session).GetVersionAsync(timeout.Token);
            Assert.True(version >= 1);
            await ((DbusGlobalShortcutSession)session).CreateSessionAsync(timeout.Token);
        }
        catch (DBusException exception)
            when (exception.ErrorName
                    is "org.freedesktop.DBus.Error.UnknownMethod"
                        or "org.freedesktop.DBus.Error.UnknownInterface"
                        or "org.freedesktop.DBus.Error.ServiceUnknown"
            )
        {
            Assert.Skip("This desktop does not expose the optional Global Shortcuts portal.");
        }
    }

    /// <summary>Creates a single normalized shortcut for protocol tests.</summary>
    private static IReadOnlyList<PortalShortcutBinding> Bindings() =>
        PortalShortcutBinding.FromSettings(
            GlobalInputSettings.Default with
            {
                Bindings = new Dictionary<GlobalInputAction, string>
                {
                    [GlobalInputAction.ToggleOverlayInteraction] = "ALT O",
                },
            }
        );

    /// <summary>Checks the native GNOME action signature without opening the user's settings during tests.</summary>
    private sealed class FakeSettingsActions : IDesktopSettingsActions
    {
        public ObjectPath ObjectPath => new("/org/gnome/Settings");
        public int Calls { get; private set; }
        public bool Failure { get; set; }

        /// <summary>Asserts the panel and application arguments and supplies controllable launch errors.</summary>
        public Task ActivateAsync(string action, object[] parameters, IDictionary<string, object> platformData)
        {
            Calls++;
            Assert.Equal("launch-panel", action);
            (string, object[]) panel = Assert.IsType<ValueTuple<string, object[]>>(Assert.Single(parameters));
            Assert.Equal("applications", panel.Item1);
            Assert.Equal("io.github.fenris159.SrvSurvey", Assert.Single(panel.Item2));
            Assert.Empty(platformData);
            return Failure ? Task.FromException(new IOException("Settings unavailable")) : Task.CompletedTask;
        }
    }

    /// <summary>Captures the connection identity request and emulates desktop compatibility failures.</summary>
    private sealed class FakeRegistry(string? error, string? requiredDesktopFile = null) : IHostPortalRegistry
    {
        public ObjectPath ObjectPath => new("/org/freedesktop/portal/desktop");
        public string? AppId { get; private set; }
        public IDictionary<string, object>? Options { get; private set; }

        /// <summary>Checks the application ID and supplies a requested D-Bus error.</summary>
        public Task RegisterAsync(string appId, IDictionary<string, object> options)
        {
            if (requiredDesktopFile is not null && !File.Exists(requiredDesktopFile))
            {
                throw new DBusException(
                    "org.freedesktop.portal.Error.Failed",
                    "Could not register app ID: App info not found for 'io.github.fenris159.SrvSurvey'"
                );
            }
            AppId = appId;
            Options = options;
            return error is null ? Task.CompletedTask : Task.FromException(new DBusException(error, "registration"));
        }
    }

    /// <summary>Implements the actual portal proxy contract with immediate replies and controllable signals.</summary>
    private sealed class FakePortal : IGlobalShortcutsPortal, IShortcutPortalSession
    {
        private readonly Dictionary<ObjectPath, FakeRequest> requests = [];
        private Action<(ObjectPath Session, string Id, ulong Timestamp, IDictionary<string, object> Options)>? pressed;
        private Action<(ObjectPath Session, string Id, ulong Timestamp, IDictionary<string, object> Options)>? released;
        private Action<IDictionary<string, object>>? closed;
        private Action<Exception>? error;
        private Action<(ObjectPath Session, (string Id, IDictionary<string, object> Properties)[] Shortcuts)>? changed;
        public ObjectPath ObjectPath => new("/org/freedesktop/portal/desktop");
        public ObjectPath SessionPath { get; } = new("/session/test");
        public bool Restored { get; init; }
        public uint Version { get; init; } = 1;
        public string? ConfigurationFailure { get; init; }
        public int ConfigureCalls { get; private set; }
        public bool CloseFails { get; init; }
        public string? Failure { get; init; }
        public string ExpectedTrigger { get; init; } = "ALT+o";
        public int BindCalls { get; private set; }
        public int SessionCloses { get; private set; }
        public int RequestCloses => requests.Values.Sum(request => request.CloseCalls);
        public bool ConnectionDisposed { get; private set; }

        /// <summary>Builds the production session with protocol-compatible test proxies.</summary>
        public DbusGlobalShortcutSession CreateClient(PortalShortcutRegistrationStore? registrations = null) =>
            new(
                this,
                ":1.2",
                path => requests[path] = new FakeRequest(path),
                _ => this,
                _ =>
                {
                    if (!ConnectionDisposed)
                    {
                        SessionCloses++;
                    }
                    return CloseFails ? Task.FromException(new IOException("gone")) : Task.CompletedTask;
                },
                () => ConnectionDisposed = true,
                registrations
            );

        /// <summary>Supplies the interface version.</summary>
        public Task<T> GetAsync<T>(string property) =>
            Task.FromResult((T)(object)(Failure == "version" ? 0u : Version));

        /// <summary>Returns a typed or deliberately malformed session handle.</summary>
        public Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options) =>
            Respond(
                options,
                new Dictionary<string, object> { ["session_handle"] = Failure == "session" ? 12 : SessionPath }
            );

        /// <summary>Restores saved IDs when requested by the test.</summary>
        public Task<ObjectPath> ListShortcutsAsync(ObjectPath session, IDictionary<string, object> options) =>
            Respond(options, Results(Restored));

        /// <summary>Accepts a binding and checks the standard preferred trigger sent by the application.</summary>
        public Task<ObjectPath> BindShortcutsAsync(
            ObjectPath session,
            (string Id, IDictionary<string, object> Properties)[] shortcuts,
            string parentWindow,
            IDictionary<string, object> options
        )
        {
            BindCalls++;
            Assert.Equal(ExpectedTrigger, Assert.Single(shortcuts).Properties["preferred_trigger"]);
            return Respond(options, Results(true));
        }

        /// <summary>Checks version-two configuration uses the current session and propagates protocol errors.</summary>
        public Task ConfigureShortcutsAsync(
            ObjectPath session,
            string parentWindow,
            IDictionary<string, object> options
        )
        {
            ConfigureCalls++;
            Assert.Equal(SessionPath, session);
            Assert.Empty(parentWindow);
            Assert.Empty(options);
            return ConfigurationFailure is null
                ? Task.CompletedTask
                : Task.FromException(new DBusException(ConfigurationFailure, "configuration"));
        }

        /// <summary>Constructs a portal response carrying an array of accepted shortcut tuples.</summary>
        private static Dictionary<string, object> Results(bool accepted) =>
            new Dictionary<string, object>
            {
                ["shortcuts"] = accepted
                    ? new (string, IDictionary<string, object>)[]
                    {
                        (
                            "toggleOverlayInteraction",
                            new Dictionary<string, object> { ["trigger_description"] = "Alt+O" }
                        ),
                    }
                    : Array.Empty<(string, IDictionary<string, object>)>(),
            };

        /// <summary>Responds synchronously after the request subscription was attached.</summary>
        private Task<ObjectPath> Respond(IDictionary<string, object> options, IDictionary<string, object> results)
        {
            var path = new ObjectPath("/org/freedesktop/portal/desktop/request/1_2/" + options["handle_token"]);
            if (Failure == "watcherror")
            {
                requests[path].SignalError();
            }
            else if (Failure != "pending")
            {
                requests[path]
                    .Respond(
                        Failure switch
                        {
                            "cancel" => 1u,
                            "deny" => 2u,
                            _ => 0u,
                        },
                        results
                    );
            }
            return Task.FromResult(Failure == "handle" ? new ObjectPath("/wrong") : path);
        }

        /// <summary>Attaches the activation signal observer.</summary>
        public Task<IDisposable> WatchActivatedAsync(
            Action<(ObjectPath Session, string Id, ulong Timestamp, IDictionary<string, object> Options)> handler,
            Action<Exception> onError
        )
        {
            pressed = handler;
            error = onError;
            return Task.FromResult<IDisposable>(new Subscription());
        }

        /// <summary>Attaches the release signal observer.</summary>
        public Task<IDisposable> WatchDeactivatedAsync(
            Action<(ObjectPath Session, string Id, ulong Timestamp, IDictionary<string, object> Options)> handler,
            Action<Exception> onError
        )
        {
            released = handler;
            return Task.FromResult<IDisposable>(new Subscription());
        }

        /// <summary>Captures desktop changes to granted shortcut actions.</summary>
        public Task<IDisposable> WatchShortcutsChangedAsync(
            Action<(ObjectPath Session, (string Id, IDictionary<string, object> Properties)[] Shortcuts)> handler,
            Action<Exception> onError
        )
        {
            changed = handler;
            return Task.FromResult<IDisposable>(new Subscription());
        }

        /// <summary>Changes the shortcuts registered for a selected session.</summary>
        public void ChangeBindings(ObjectPath session, params string[] ids) =>
            changed?.Invoke(
                (
                    session,
                    ids.Select(id => (id, (IDictionary<string, object>)new Dictionary<string, object>())).ToArray()
                )
            );

        /// <summary>Reports the desktop's readable key after it changes independently of the requested key.</summary>
        public void ChangeTrigger(string trigger) =>
            changed?.Invoke(
                (
                    SessionPath,
                    [("toggleOverlayInteraction", new Dictionary<string, object> { ["trigger_description"] = trigger })]
                )
            );

        /// <summary>Attaches the session-closure observer.</summary>
        public Task<IDisposable> WatchClosedAsync(
            Action<IDictionary<string, object>> handler,
            Action<Exception> onError
        )
        {
            closed = handler;
            return Task.FromResult<IDisposable>(new Subscription());
        }

        /// <summary>Emits one native portal activation.</summary>
        public void Press(ObjectPath session) =>
            pressed?.Invoke((session, "toggleOverlayInteraction", 1, new Dictionary<string, object>()));

        /// <summary>Emits one native portal release.</summary>
        public void Release(ObjectPath session) =>
            released?.Invoke((session, "toggleOverlayInteraction", 2, new Dictionary<string, object>()));

        /// <summary>Emits session closure.</summary>
        public void Close() => closed?.Invoke(new Dictionary<string, object>());

        /// <summary>Emits a bus error.</summary>
        public void SignalError(Exception exception) => error?.Invoke(exception);
    }

    /// <summary>Implements a subscribable request that can reply immediately or remain pending.</summary>
    private sealed class FakeRequest(ObjectPath path) : IRequest
    {
        private Action<(uint Response, IDictionary<string, object> Results)>? response;
        private Action<Exception>? error;
        public ObjectPath ObjectPath => path;
        public int CloseCalls { get; private set; }

        /// <summary>Records cancellation of a desktop request.</summary>
        public Task CloseAsync()
        {
            CloseCalls++;
            return Task.CompletedTask;
        }

        /// <summary>Attaches an observer before the request is sent.</summary>
        public Task<IDisposable> WatchResponseAsync(
            Action<(uint Response, IDictionary<string, object> Results)> handler,
            Action<Exception> onError
        )
        {
            response = handler;
            error = onError;
            return Task.FromResult<IDisposable>(new Subscription());
        }

        /// <summary>Reports a request subscription failure without creating an unobserved faulted task.</summary>
        public void SignalError() => error?.Invoke(new IOException("request signal failed"));

        /// <summary>Delivers the protocol response.</summary>
        public void Respond(uint code, IDictionary<string, object> results) => response?.Invoke((code, results));
    }

    /// <summary>Provides an inert test subscription.</summary>
    private sealed class Subscription : IDisposable
    {
        /// <summary>The fake proxy holds no native resources.</summary>
        public void Dispose() { }
    }
}
