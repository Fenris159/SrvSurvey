using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Search;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The session implements asynchronous disposal through IAsyncDisposable.")]
public sealed class BoxelSearchSession : IBoxelSearchSession
{
    private readonly IBoxelSearchProfileStore profileStore;
    private readonly IBoxelLocalSystemReader localSystemReader;
    private readonly IBoxelEmptyStore emptyBoxelStore;
    private readonly IBoxelSearchLibraryStore libraryStore;
    private readonly IBoxelSystemResolver systemResolver;
    private readonly IBoxelClipboard clipboard;
    private readonly IBoxelSearchDiagnosticSink diagnostics;
    private readonly TimeProvider timeProvider;
    private readonly BoxelSearchSessionOptions options;
    private readonly BoxelCompletionAuditor completionAuditor;
    private readonly BoxelSearchState state = new();
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "The gate may still be released by an operation that entered before asynchronous disposal began.")]
    private readonly SemaphoreSlim mutationGate = new(1, 1);
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "The gate may still be released by an in-flight refresh during asynchronous disposal.")]
    private readonly SemaphoreSlim refreshStartGate = new(1, 1);
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly object externalWorkSync = new();
    private readonly Dictionary<BoxelSearchHealthSubsystem, BoxelSearchHealthIssue>
        healthIssues = [];
    private BoxelSearchSessionSnapshot current = BoxelSearchSessionSnapshot.Empty;
    private BoxelSearchSessionSearchSnapshot searchSnapshot =
        BoxelSearchSessionSearchSnapshot.Empty;
    private BoxelSearchContextSnapshot contextSnapshot = BoxelSearchContextSnapshot.Empty;
    private BoxelSearchActivitySnapshot activitySnapshot = BoxelSearchActivitySnapshot.Empty;
    private BoxelSearchHealthSnapshot healthSnapshot = BoxelSearchHealthSnapshot.Empty;
    private BoxelSearchProfileIdentity? profile;
    private string? currentSystemName;
    private GalacticCoordinate? currentPosition;
    private long? currentSystemAddress;
    private NavRouteSnapshot? latestRoute;
    private EliteStatus? latestStatus;
    private string? musicTrack;
    private bool isGalaxyMapOpen;
    private string? lastCopiedSystemName;
    private string? automaticCopyEligibility;
    private long sessionVersion;
    private long profileGeneration;
    private long contextVersion;
    private long activityVersion;
    private long healthVersion;
    private long libraryRevision;
    private int projectedStateVersion = -1;
    private CancellationTokenSource? refreshCancellation;
    private CancellationTokenSource? auditCancellation;
    private Task<BoxelSearchOutcome>? refreshTask;
    private Task<BoxelSearchOutcome>? auditTask;
    private Task? retryTask;
    private PendingPersistence? pendingProfile;
    private PendingPersistence? pendingLibrary;
    private bool disposing;
    private bool disposed;

    public BoxelSearchSession(
        IBoxelSearchProfileStore profileStore,
        IBoxelLocalSystemReader localSystemReader,
        IBoxelEmptyStore emptyBoxelStore,
        IBoxelSearchLibraryStore libraryStore,
        IBoxelSystemResolver systemResolver,
        BoxelSearchSessionServices? services = null)
    {
        this.profileStore = profileStore
            ?? throw new ArgumentNullException(nameof(profileStore));
        this.localSystemReader = localSystemReader
            ?? throw new ArgumentNullException(nameof(localSystemReader));
        this.emptyBoxelStore = emptyBoxelStore
            ?? throw new ArgumentNullException(nameof(emptyBoxelStore));
        this.libraryStore = libraryStore
            ?? throw new ArgumentNullException(nameof(libraryStore));
        this.systemResolver = systemResolver
            ?? throw new ArgumentNullException(nameof(systemResolver));
        services ??= new BoxelSearchSessionServices();
        clipboard = services.Clipboard ?? UnavailableBoxelClipboard.Instance;
        diagnostics = services.Diagnostics ?? NullBoxelSearchDiagnosticSink.Instance;
        timeProvider = services.TimeProvider ?? TimeProvider.System;
        options = services.Options ?? new BoxelSearchSessionOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(
            this.options.InitialRetryDelay,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            this.options.MaximumRetryDelay,
            this.options.InitialRetryDelay);
        completionAuditor = new BoxelCompletionAuditor(localSystemReader, systemResolver);
    }

    public BoxelSearchSessionSnapshot Current => Volatile.Read(ref current);

    public event EventHandler<BoxelSearchSessionChangedEventArgs>? Changed;

    public async Task<BoxelSearchOutcome> SwitchProfileAsync(
        BoxelSearchProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.FrontierId);
        ArgumentNullException.ThrowIfNull(profile.Search);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();
        await CancelAndAwaitExternalWorkAsync().ConfigureAwait(false);

        BoxelSearchSessionChangedEventArgs? change;
        ActionResult result;
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            await RetryPendingOnceLockedAsync(CancellationToken.None).ConfigureAwait(false);
            pendingProfile = null;
            pendingLibrary = null;
            this.profile = new BoxelSearchProfileIdentity(
                ++profileGeneration,
                profile.FrontierId,
                profile.CommanderName,
                profile.IsOdyssey);
            state.Reset(profile.Search);
            lastCopiedSystemName = null;
            automaticCopyEligibility = null;
            TouchContextLocked();
            ClearActivityLocked();
            ClearHealthLocked();

            var warnings = new List<BoxelSearchWarning>();
            if (state.TopBoxel is not null)
            {
                try
                {
                    state.ApplyEmptyBoxels(await emptyBoxelStore.LoadGroupAsync(
                            state.TopBoxel,
                            lifetimeCancellation.Token)
                        .ConfigureAwait(false));
                }
                catch (InvalidDataException exception)
                {
                    AddHealthLocked(
                        BoxelSearchHealthSubsystem.LocalData,
                        BoxelSearchHealthSeverity.Warning,
                        BoxelSearchMessageCode.RefreshFailed,
                        exception);
                    warnings.Add(new BoxelSearchWarning(
                        BoxelSearchHealthSubsystem.LocalData,
                        BoxelSearchMessageCode.RefreshFailed));
                }
            }

            await ReconcileLinkedSearchLockedAsync(warnings).ConfigureAwait(false);
            var loadedCode = BoxelSearchMessageCode.SearchNotConfigured;
            if (state.TopBoxel is not null)
            {
                loadedCode = state.IsActive
                    ? BoxelSearchMessageCode.ProfileLoaded
                    : BoxelSearchMessageCode.SearchLoadedInactive;
            }

            result = new ActionResult(
                warnings.Count == 0
                    ? BoxelSearchOutcomeKind.Success
                    : BoxelSearchOutcomeKind.AppliedWithWarnings,
                loadedCode,
                Warnings: warnings);
            change = CaptureChangeLocked();
        }
        finally
        {
            mutationGate.Release();
        }

        RaiseChanged(change);
        var outcome = CreateOutcome(result);
        if (state.IsActive)
        {
            return await RunRefreshAsync(outcome, preserveActivationCode: true)
                .ConfigureAwait(false);
        }

        return outcome;
    }

    public async Task<BoxelSearchOutcome> ClearProfileAsync(
        BoxelSearchMessageCode reason = BoxelSearchMessageCode.ProfileUnavailable,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();
        await CancelAndAwaitExternalWorkAsync().ConfigureAwait(false);
        return await RunSerializedAsync(async token =>
        {
            await RetryPendingOnceLockedAsync(CancellationToken.None).ConfigureAwait(false);
            pendingProfile = null;
            pendingLibrary = null;
            profile = null;
            profileGeneration++;
            state.Reset();
            latestRoute = null;
            latestStatus = null;
            musicTrack = null;
            isGalaxyMapOpen = false;
            lastCopiedSystemName = null;
            automaticCopyEligibility = null;
            TouchContextLocked();
            ClearActivityLocked();
            ClearHealthLocked();
            return new ActionResult(BoxelSearchOutcomeKind.Success, reason);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<BoxelSearchOutcome> ApplyAsync(
        BoxelSearchUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(update.JournalEvents);
        return RunSerializedAsync(
            token => ApplyUpdateLockedAsync(update, token),
            cancellationToken);
    }

    public Task<BoxelSearchOutcome> ExecuteAsync(
        BoxelSearchAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action switch
        {
            RefreshCurrentBoxel => StartRefreshAsync(cancellationToken),
            AuditAllBoxels => StartAuditAsync(cancellationToken),
            CancelBoxelAudit => CancelAuditAsync(cancellationToken),
            ActivateBoxelSearch activate => ActivateAsync(activate, cancellationToken),
            NavigateToBoxel navigate => NavigateAsync(navigate, cancellationToken),
            _ => RunSerializedAsync(
                token => ExecuteLockedAsync(action, token),
                cancellationToken),
        };
    }

    public async Task<BoxelSearchLibrarySnapshot> GetLibraryAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();
        var activeProfile = Current.Context.Profile;
        if (activeProfile is null)
        {
            return new BoxelSearchLibrarySnapshot(Current.LibraryRevision, []);
        }

        var entries = await libraryStore.ListAsync(
                activeProfile.FrontierId,
                cancellationToken)
            .ConfigureAwait(false);
        return new BoxelSearchLibrarySnapshot(Current.LibraryRevision, entries);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed || disposing)
        {
            return;
        }

        disposing = true;
        await CancelAndAwaitExternalWorkAsync().ConfigureAwait(false);
        BoxelSearchSessionChangedEventArgs? change;
        await mutationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await RetryPendingOnceLockedAsync(CancellationToken.None).ConfigureAwait(false);
            change = CaptureChangeLocked();
        }
        finally
        {
            mutationGate.Release();
        }

        RaiseChanged(change);
        await lifetimeCancellation.CancelAsync().ConfigureAwait(false);
        var ownedRetry = retryTask;
        if (ownedRetry is not null)
        {
            try
            {
                await ownedRetry.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
            {
                // Cancellation is the expected retry-worker shutdown path.
            }
        }

        refreshCancellation?.Dispose();
        auditCancellation?.Dispose();
        lifetimeCancellation.Dispose();
        disposed = true;
        disposing = false;
    }

    private async Task<ActionResult> ApplyUpdateLockedAsync(
        BoxelSearchUpdate update,
        CancellationToken cancellationToken)
    {
        var stateVersionBefore = state.Version;
        var contextChanged = ApplyCurrentSystemContextLocked(update)
            | ApplyRouteContextLocked(update)
            | ApplyStatusContextLocked(update);
        ApplyJournalEventsLocked(update);

        if (contextChanged)
        {
            TouchContextLocked();
        }

        var warnings = new List<BoxelSearchWarning>();
        var stateChanged = state.Version != stateVersionBefore;
        if (stateChanged)
        {
            await PersistLockedAsync(warnings, cancellationToken).ConfigureAwait(false);
        }

        await ApplyAutomaticCopyLockedAsync(update, warnings, cancellationToken)
            .ConfigureAwait(false);

        if (!stateChanged && !contextChanged && warnings.Count == 0)
        {
            return new ActionResult(
                BoxelSearchOutcomeKind.NoChange,
                BoxelSearchMessageCode.None);
        }

        return new ActionResult(
            warnings.Count == 0
                ? BoxelSearchOutcomeKind.Success
                : BoxelSearchOutcomeKind.AppliedWithWarnings,
            BoxelSearchMessageCode.None,
            Warnings: warnings);
    }

    private bool ApplyCurrentSystemContextLocked(BoxelSearchUpdate update)
    {
        if (!update.HasCurrentSystem)
        {
            return false;
        }

        var normalizedName = string.IsNullOrWhiteSpace(update.CurrentSystemName)
            ? null
            : update.CurrentSystemName.Trim();
        var normalizedAddress = update.CurrentSystemAddress is > 0
            ? update.CurrentSystemAddress
            : null;
        var changed = !string.Equals(
                currentSystemName,
                normalizedName,
                StringComparison.OrdinalIgnoreCase)
            || currentPosition != update.CurrentPosition
            || currentSystemAddress != normalizedAddress;
        currentSystemName = normalizedName;
        currentPosition = update.CurrentPosition;
        currentSystemAddress = normalizedAddress;
        return changed;
    }

    private bool ApplyRouteContextLocked(BoxelSearchUpdate update)
    {
        if (!update.HasRoute)
        {
            return false;
        }

        var changed = !Equals(latestRoute, update.Route);
        latestRoute = update.Route;
        if (state.IsActive && update.Route is not null)
        {
            state.MergeRoute(update.Route.Route
                .Select(entry => entry.ToBoxelObservation())
                .OfType<BoxelSystemObservation>());
        }

        return changed;
    }

    private bool ApplyStatusContextLocked(BoxelSearchUpdate update)
    {
        if (!update.HasStatus)
        {
            return false;
        }

        var changed = !Equals(latestStatus, update.Status)
            || !string.Equals(musicTrack, update.MusicTrack, StringComparison.Ordinal)
            || isGalaxyMapOpen != update.IsGalaxyMapOpen;
        latestStatus = update.Status;
        musicTrack = update.MusicTrack;
        isGalaxyMapOpen = update.IsGalaxyMapOpen;
        if (!isGalaxyMapOpen)
        {
            lastCopiedSystemName = null;
            automaticCopyEligibility = null;
        }

        return changed;
    }

    private void ApplyJournalEventsLocked(BoxelSearchUpdate update)
    {
        if (!state.IsActive)
        {
            return;
        }

        foreach (var journalEvent in update.JournalEvents)
        {
            state.Apply(journalEvent);
        }
    }

    private async Task ApplyAutomaticCopyLockedAsync(
        BoxelSearchUpdate update,
        List<BoxelSearchWarning> warnings,
        CancellationToken cancellationToken)
    {
        if (!update.HasStatus
            || !isGalaxyMapOpen
            || !state.IsActive
            || !state.AutoCopy
            || !IsCurrentSystemInsideSearchLocked())
        {
            return;
        }

        if (!update.AllowAutoCopy)
        {
            if (state.NextSystem is not null)
            {
                automaticCopyEligibility = state.NextSystem + "|" + isGalaxyMapOpen;
            }

            return;
        }

        var copyResult = await CopyNextLockedAsync(
                automatic: true,
                cancellationToken)
            .ConfigureAwait(false);
        warnings.AddRange(copyResult.Warnings ?? []);
    }

    private async Task<ActionResult> ExecuteLockedAsync(
        BoxelSearchAction action,
        CancellationToken cancellationToken)
    {
        var versionBefore = state.Version;
        var result = action switch
        {
            StopBoxelSearch => StopLocked(),
            SetBoxelAutoCopy set => SetAutoCopyLocked(set.Enabled),
            SetBoxelSortDirection set => SetSortLocked(set.Descending),
            SetExpectedSystemCount set => SetExpectedSystemCountLocked(set.Count),
            MarkNextBoxelSystemEmpty => MarkNextEmptyLocked(),
            CompleteBoxelSystem complete => CompleteSystemLocked(complete.SystemName),
            ReopenBoxelSystem reopen => ReopenSystemLocked(reopen.SystemName),
            DeferBoxelSystem defer => DeferSystemLocked(defer.SystemName),
            StartBoxelSurveyAt start => StartAtSystemLocked(start.SystemName),
            CopyNextBoxelSystem copy => await CopyNextLockedAsync(
                    copy.Automatic,
                    cancellationToken)
                .ConfigureAwait(false),
            SaveBoxelSearchToLibrary save => await SaveToLibraryLockedAsync(
                    save,
                    cancellationToken)
                .ConfigureAwait(false),
            ResumeSavedBoxelSearch resume => await ResumeSavedLockedAsync(
                    resume,
                    cancellationToken)
                .ConfigureAwait(false),
            RenameSavedBoxelSearch rename => await RenameSavedLockedAsync(
                    rename,
                    cancellationToken)
                .ConfigureAwait(false),
            UpdateSavedBoxelSearchNotes notes => await UpdateNotesLockedAsync(
                    notes,
                    cancellationToken)
                .ConfigureAwait(false),
            SetSavedBoxelSearchFavorite favorite => await SetFavoriteLockedAsync(
                    favorite,
                    cancellationToken)
                .ConfigureAwait(false),
            DeleteSavedBoxelSearch delete => await DeleteSavedLockedAsync(
                    delete,
                    cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        if (state.Version != versionBefore
            && action is not SaveBoxelSearchToLibrary
            && action is not ResumeSavedBoxelSearch
            && action is not DeleteSavedBoxelSearch)
        {
            var warnings = result.Warnings?.ToList() ?? [];
            await PersistLockedAsync(warnings, cancellationToken).ConfigureAwait(false);
            result = result with
            {
                Kind = GetAppliedKind(result.Kind, warnings),
                Warnings = warnings,
            };
        }

        return result;
    }

    private async Task<BoxelSearchOutcome> ActivateAsync(
        ActivateBoxelSearch action,
        CancellationToken cancellationToken)
    {
        var activation = await RunSerializedAsync(token =>
        {
            if (profile is null)
            {
                return Task.FromResult(new ActionResult(
                    BoxelSearchOutcomeKind.Rejected,
                    BoxelSearchMessageCode.ProfileUnavailable));
            }

            if (!state.TryActivate(action.Request, out var error))
            {
                return Task.FromResult(new ActionResult(
                    BoxelSearchOutcomeKind.Rejected,
                    BoxelSearchMessageCode.SearchInvalid,
                    PrimaryValue: error));
            }

            return PersistResultLockedAsync(
                new ActionResult(
                    BoxelSearchOutcomeKind.Success,
                    BoxelSearchMessageCode.SearchActivated),
                token);
        }, cancellationToken).ConfigureAwait(false);
        if (activation.Kind is BoxelSearchOutcomeKind.Rejected
            or BoxelSearchOutcomeKind.Cancelled)
        {
            return activation;
        }

        return await RunRefreshAsync(activation, preserveActivationCode: true)
            .ConfigureAwait(false);
    }

    private async Task<BoxelSearchOutcome> NavigateAsync(
        NavigateToBoxel action,
        CancellationToken cancellationToken)
    {
        await CancelRefreshAsync().ConfigureAwait(false);
        var navigation = await RunSerializedAsync(token =>
        {
            if (state.Current is not null
                && string.Equals(
                    state.Current.Prefix,
                    action.Boxel.Prefix,
                    StringComparison.Ordinal))
            {
                return Task.FromResult(new ActionResult(
                    BoxelSearchOutcomeKind.NoChange,
                    BoxelSearchMessageCode.NavigationChanged));
            }

            if (!state.TrySetCurrent(action.Boxel, out var error))
            {
                return Task.FromResult(new ActionResult(
                    BoxelSearchOutcomeKind.Rejected,
                    BoxelSearchMessageCode.NavigationChanged,
                    PrimaryValue: error));
            }

            return PersistResultLockedAsync(
                new ActionResult(
                    BoxelSearchOutcomeKind.Success,
                    BoxelSearchMessageCode.NavigationChanged,
                    PrimaryValue: action.Boxel.Prefix),
                token);
        }, cancellationToken).ConfigureAwait(false);
        if (navigation.Kind is BoxelSearchOutcomeKind.Rejected
            or BoxelSearchOutcomeKind.Cancelled)
        {
            return navigation;
        }

        return await RunRefreshAsync(navigation, preserveActivationCode: false)
            .ConfigureAwait(false);
    }

    private async Task<BoxelSearchOutcome> StartRefreshAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();
        Task<BoxelSearchOutcome> task;
        await refreshStartGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            await CancelRefreshAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (externalWorkSync)
            {
                refreshCancellation?.Dispose();
                refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    lifetimeCancellation.Token);
                task = RunRefreshCoreAsync(refreshCancellation.Token);
                refreshTask = task;
            }
        }
        finally
        {
            refreshStartGate.Release();
        }

        return await AwaitAndClearRefreshAsync(task).ConfigureAwait(false);
    }

    private async Task<BoxelSearchOutcome> RunRefreshAsync(
        BoxelSearchOutcome precedingOutcome,
        bool preserveActivationCode)
    {
        var refreshed = await StartRefreshAsync(CancellationToken.None).ConfigureAwait(false);
        if (!preserveActivationCode
            || refreshed.Kind is BoxelSearchOutcomeKind.Rejected
                or BoxelSearchOutcomeKind.Cancelled)
        {
            return refreshed;
        }

        return refreshed with
        {
            Code = precedingOutcome.Code,
            PrimaryValue = refreshed.PrimaryValue,
        };
    }

    private async Task<BoxelSearchOutcome> RunRefreshCoreAsync(CancellationToken cancellationToken)
    {
        RefreshRequest? request = null;
        BoxelSearchSessionChangedEventArgs? startedChange;
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            if (profile is null || !state.IsActive || state.Current is null)
            {
                return CreateOutcome(new ActionResult(
                    BoxelSearchOutcomeKind.Rejected,
                    BoxelSearchMessageCode.RefreshFailed));
            }

            request = new RefreshRequest(
                profile.Generation,
                profile.FrontierId,
                state.TopBoxel?.Prefix,
                state.Current,
                latestRoute);
            SetActivityLocked(
                BoxelSearchActivityKind.Refreshing,
                0,
                1,
                state.Current.Prefix);
            startedChange = CaptureChangeLocked();
        }
        finally
        {
            mutationGate.Release();
        }

        RaiseChanged(startedChange);
        var warnings = new List<BoxelSearchWarning>();
        RefreshSources sources;
        try
        {
            sources = await LoadRefreshSourcesAsync(
                    request,
                    warnings,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await CompleteCancelledActivityAsync(BoxelSearchMessageCode.RefreshFailed)
                .ConfigureAwait(false);
        }

        return await RunSerializedAsync(async token =>
        {
            if (!IsRequestCurrentLocked(
                    request.Generation,
                    request.TopPrefix,
                    request.Current.Prefix))
            {
                ClearActivityLocked();
                return new ActionResult(
                    BoxelSearchOutcomeKind.Cancelled,
                    BoxelSearchMessageCode.Superseded);
            }

            state.ApplyEmptyBoxels(sources.Empty);
            if (!state.CurrentIsEmpty)
            {
                state.MergeLocalSystems(sources.Local.Systems);
                if (request.Route is not null)
                {
                    state.MergeRoute(request.Route.Route
                        .Select(entry => entry.ToBoxelObservation())
                        .OfType<BoxelSystemObservation>());
                }

                state.MergeSpanshSystems(sources.Remote);
            }

            UpdateRefreshHealthLocked(warnings);
            ClearActivityLocked();
            await PersistLockedAsync(warnings, token).ConfigureAwait(false);
            return new ActionResult(
                warnings.Count == 0
                    ? BoxelSearchOutcomeKind.Success
                    : BoxelSearchOutcomeKind.AppliedWithWarnings,
                BoxelSearchMessageCode.RefreshCompleted,
                PrimaryValue: state.Current?.Prefix,
                Count: state.Systems.Count,
                Warnings: warnings);
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<RefreshSources> LoadRefreshSourcesAsync(
        RefreshRequest request,
        List<BoxelSearchWarning> warnings,
        CancellationToken cancellationToken)
    {
        var empty = await LoadEmptyBoxelsAsync(request, warnings, cancellationToken)
            .ConfigureAwait(false);
        var local = await localSystemReader.ReadAsync(
                request.FrontierId,
                request.Current,
                cancellationToken)
            .ConfigureAwait(false);
        warnings.AddRange(local.Errors.Select(error => new BoxelSearchWarning(
            BoxelSearchHealthSubsystem.LocalData,
            BoxelSearchMessageCode.RefreshFailed,
            error)));
        var remote = empty.Contains(request.Current.Id)
            ? []
            : await LoadRemoteSystemsAsync(request, warnings, cancellationToken)
                .ConfigureAwait(false);
        return new RefreshSources(empty, local, remote);
    }

    private async Task<IReadOnlySet<string>> LoadEmptyBoxelsAsync(
        RefreshRequest request,
        List<BoxelSearchWarning> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await emptyBoxelStore.LoadGroupAsync(
                    request.Current,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            ReportDiagnostic(
                BoxelSearchHealthSubsystem.LocalData,
                BoxelSearchMessageCode.RefreshFailed,
                exception,
                request.Current.Prefix);
            warnings.Add(new BoxelSearchWarning(
                BoxelSearchHealthSubsystem.LocalData,
                BoxelSearchMessageCode.RefreshFailed));
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private async Task<IReadOnlyList<BoxelSystemObservation>> LoadRemoteSystemsAsync(
        RefreshRequest request,
        List<BoxelSearchWarning> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await systemResolver.SearchAsync(request.Current, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsResolverException(exception))
        {
            ReportDiagnostic(
                BoxelSearchHealthSubsystem.Resolver,
                BoxelSearchMessageCode.RefreshFailed,
                exception,
                request.Current.Prefix);
            warnings.Add(new BoxelSearchWarning(
                BoxelSearchHealthSubsystem.Resolver,
                BoxelSearchMessageCode.RefreshFailed));
            return [];
        }
    }

    private Task<BoxelSearchOutcome> StartAuditAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();
        Task<BoxelSearchOutcome> task;
        lock (externalWorkSync)
        {
            if (auditTask is { IsCompleted: false })
            {
                return Task.FromResult(CreateOutcome(new ActionResult(
                    BoxelSearchOutcomeKind.Rejected,
                    BoxelSearchMessageCode.AuditStarted)));
            }

            auditCancellation?.Dispose();
            auditCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeCancellation.Token);
            task = RunAuditCoreAsync(auditCancellation.Token);
            auditTask = task;
        }

        return AwaitAndClearAuditAsync(task);
    }

    private async Task<BoxelSearchOutcome> RunAuditCoreAsync(CancellationToken cancellationToken)
    {
        AuditRequest? request = null;
        BoxelSearchSessionChangedEventArgs? startedChange;
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            if (profile is null || !state.IsActive || state.TopBoxel is null)
            {
                return CreateOutcome(new ActionResult(
                    BoxelSearchOutcomeKind.Rejected,
                    BoxelSearchMessageCode.AuditFailed));
            }

            var persistent = state.CreateSnapshot();
            request = new AuditRequest(
                profile.Generation,
                state.TopBoxel.Prefix,
                new BoxelCompletionAuditRequest(
                    profile.FrontierId,
                    state.Boxels,
                    state.EmptyBoxelPrefixes,
                    state.Current?.Prefix,
                    persistent.StartedOn,
                    persistent.SkipAlreadyVisited,
                    persistent.SkipKnownToSpansh,
                    persistent.CompletionMode,
                    latestRoute?.Route
                        .Select(entry => entry.ToBoxelObservation())
                        .OfType<BoxelSystemObservation>()
                        .ToArray() ?? []));
            SetActivityLocked(
                BoxelSearchActivityKind.Auditing,
                0,
                Math.Max(1, state.TotalBoxelCount),
                state.TopBoxel.Prefix);
            startedChange = CaptureChangeLocked();
        }
        finally
        {
            mutationGate.Release();
        }

        RaiseChanged(startedChange);
        BoxelCompletionAuditResult auditResult;
        try
        {
            auditResult = await completionAuditor.AuditAsync(
                    request.Request,
                    PublishAuditProgressAsync,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsLocalDataException(exception))
        {
            ReportDiagnostic(
                BoxelSearchHealthSubsystem.LocalData,
                BoxelSearchMessageCode.AuditFailed,
                exception,
                request.TopPrefix);
            return await CompleteFailedAuditAsync(exception).ConfigureAwait(false);
        }

        return await RunSerializedAsync(async token =>
        {
            if (!IsRequestCurrentLocked(
                    request.Generation,
                    request.TopPrefix,
                    currentPrefix: null))
            {
                ClearActivityLocked();
                return new ActionResult(
                    BoxelSearchOutcomeKind.Cancelled,
                    BoxelSearchMessageCode.Superseded);
            }

            state.ApplyCompletionAudit(auditResult.Entries);
            ClearActivityLocked();
            var warnings = auditResult.Errors.Select(error => new BoxelSearchWarning(
                    BoxelSearchHealthSubsystem.Resolver,
                    BoxelSearchMessageCode.AuditFailed,
                    error))
                .ToList();
            await PersistLockedAsync(warnings, token).ConfigureAwait(false);
            return new ActionResult(
                GetAuditOutcomeKind(auditResult.WasCancelled, warnings),
                auditResult.WasCancelled
                    ? BoxelSearchMessageCode.AuditCancelled
                    : BoxelSearchMessageCode.AuditCompleted,
                Count: auditResult.Processed,
                Total: auditResult.Total,
                Warnings: warnings);
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private static BoxelSearchOutcomeKind GetAuditOutcomeKind(
        bool wasCancelled,
        List<BoxelSearchWarning> warnings)
    {
        if (wasCancelled)
        {
            return BoxelSearchOutcomeKind.Cancelled;
        }

        return warnings.Count == 0
            ? BoxelSearchOutcomeKind.Success
            : BoxelSearchOutcomeKind.AppliedWithWarnings;
    }

    private async Task<BoxelSearchOutcome> CancelAuditAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();
        Task<BoxelSearchOutcome>? running;
        lock (externalWorkSync)
        {
            running = auditTask;
            if (running is null || running.IsCompleted || auditCancellation is null)
            {
                return CreateOutcome(new ActionResult(
                    BoxelSearchOutcomeKind.NoChange,
                    BoxelSearchMessageCode.AuditCancelled));
            }

            auditCancellation.Cancel();
        }

        await SetCancellingAuditAsync().ConfigureAwait(false);
        return await running.ConfigureAwait(false);
    }

    private ActionResult StopLocked()
    {
        if (!state.IsActive)
        {
            return new ActionResult(
                BoxelSearchOutcomeKind.NoChange,
                BoxelSearchMessageCode.SearchStopped);
        }

        state.Disable();
        return new ActionResult(
            BoxelSearchOutcomeKind.Success,
            BoxelSearchMessageCode.SearchStopped);
    }

    private ActionResult SetAutoCopyLocked(bool enabled)
    {
        if (state.AutoCopy == enabled)
        {
            return new ActionResult(
                BoxelSearchOutcomeKind.NoChange,
                BoxelSearchMessageCode.AutoCopyChanged);
        }

        state.SetAutoCopy(enabled);
        if (!enabled)
        {
            automaticCopyEligibility = null;
        }

        return new ActionResult(
            BoxelSearchOutcomeKind.Success,
            BoxelSearchMessageCode.AutoCopyChanged,
            PrimaryValue: enabled.ToString());
    }

    private ActionResult SetSortLocked(bool descending)
    {
        if (state.SortDescending == descending)
        {
            return new ActionResult(
                BoxelSearchOutcomeKind.NoChange,
                BoxelSearchMessageCode.SortDirectionChanged);
        }

        state.SetSortDescending(descending);
        return new ActionResult(
            BoxelSearchOutcomeKind.Success,
            BoxelSearchMessageCode.SortDirectionChanged);
    }

    private ActionResult SetExpectedSystemCountLocked(int count)
    {
        if (!state.IsActive || state.Current is null)
        {
            return new ActionResult(
                BoxelSearchOutcomeKind.Rejected,
                BoxelSearchMessageCode.ExpectedSystemCountChanged);
        }

        if (count < state.CurrentMaximumSystemNumber + 1)
        {
            return new ActionResult(
                BoxelSearchOutcomeKind.Rejected,
                BoxelSearchMessageCode.ExpectedSystemCountChanged,
                Count: state.CurrentMaximumSystemNumber);
        }

        if (count == state.CurrentCount)
        {
            return new ActionResult(
                BoxelSearchOutcomeKind.NoChange,
                BoxelSearchMessageCode.ExpectedSystemCountChanged);
        }

        state.SetExpectedSystemCount(count);
        return new ActionResult(
            BoxelSearchOutcomeKind.Success,
            BoxelSearchMessageCode.ExpectedSystemCountChanged,
            Count: count - 1);
    }

    private ActionResult MarkNextEmptyLocked()
    {
        string? error = null;
        if (!state.IsActive
            || !state.TryMarkNextSystemEmpty(out var marked, out error))
        {
            return new ActionResult(
                BoxelSearchOutcomeKind.Rejected,
                BoxelSearchMessageCode.NextSystemMarkedEmpty,
                PrimaryValue: error);
        }

        return new ActionResult(
            BoxelSearchOutcomeKind.Success,
            BoxelSearchMessageCode.NextSystemMarkedEmpty,
            PrimaryValue: marked,
            SecondaryValue: state.NextSystem);
    }

    private ActionResult CompleteSystemLocked(string systemName)
    {
        return state.TrySetSystemComplete(systemName, true, out var error)
            ? new ActionResult(
                BoxelSearchOutcomeKind.Success,
                BoxelSearchMessageCode.SystemCompleted,
                PrimaryValue: systemName)
            : new ActionResult(
                BoxelSearchOutcomeKind.Rejected,
                BoxelSearchMessageCode.SystemCompleted,
                PrimaryValue: error);
    }

    private ActionResult ReopenSystemLocked(string systemName)
    {
        bool changed;
        string? error;
        if (state.IsSystemDeferred(systemName))
        {
            changed = state.TrySetSystemDeferred(systemName, false, out error);
        }
        else if (state.IsSystemEmpty(systemName))
        {
            changed = state.TrySetSystemEmpty(systemName, false, out error);
        }
        else
        {
            changed = state.TrySetSystemComplete(systemName, false, out error);
        }

        return changed
            ? new ActionResult(
                BoxelSearchOutcomeKind.Success,
                BoxelSearchMessageCode.SystemReopened,
                PrimaryValue: systemName)
            : new ActionResult(
                BoxelSearchOutcomeKind.Rejected,
                BoxelSearchMessageCode.SystemReopened,
                PrimaryValue: error);
    }

    private ActionResult DeferSystemLocked(string systemName)
    {
        return state.TrySetSystemDeferred(systemName, true, out var error)
            ? new ActionResult(
                BoxelSearchOutcomeKind.Success,
                BoxelSearchMessageCode.SystemDeferred,
                PrimaryValue: systemName)
            : new ActionResult(
                BoxelSearchOutcomeKind.Rejected,
                BoxelSearchMessageCode.SystemDeferred,
                PrimaryValue: error);
    }

    private ActionResult StartAtSystemLocked(string systemName)
    {
        return state.TryStartAtSystem(systemName, out var deferred, out var error)
            ? new ActionResult(
                BoxelSearchOutcomeKind.Success,
                BoxelSearchMessageCode.SurveyStartChanged,
                PrimaryValue: systemName,
                Count: deferred)
            : new ActionResult(
                BoxelSearchOutcomeKind.Rejected,
                BoxelSearchMessageCode.SurveyStartChanged,
                PrimaryValue: error);
    }

    private async Task<ActionResult> CopyNextLockedAsync(
        bool automatic,
        CancellationToken cancellationToken)
    {
        if (state.NextSystem is null)
        {
            return new ActionResult(
                BoxelSearchOutcomeKind.Rejected,
                BoxelSearchMessageCode.NextSystemCopied);
        }

        var eligibility = state.NextSystem + "|" + isGalaxyMapOpen;
        if (automatic
            && string.Equals(
                automaticCopyEligibility,
                eligibility,
                StringComparison.Ordinal))
        {
            return new ActionResult(
                BoxelSearchOutcomeKind.NoChange,
                BoxelSearchMessageCode.NextSystemCopied);
        }

        if (!clipboard.IsReady)
        {
            AddHealthLocked(
                BoxelSearchHealthSubsystem.Clipboard,
                BoxelSearchHealthSeverity.Warning,
                BoxelSearchMessageCode.ClipboardNotReady);
            return new ActionResult(
                BoxelSearchOutcomeKind.Rejected,
                BoxelSearchMessageCode.ClipboardNotReady);
        }

        if (automatic)
        {
            automaticCopyEligibility = eligibility;
        }

        try
        {
            await clipboard.WriteTextAsync(state.NextSystem, cancellationToken)
                .ConfigureAwait(false);
            lastCopiedSystemName = state.NextSystem;
            TouchContextLocked();
            RemoveHealthLocked(BoxelSearchHealthSubsystem.Clipboard);
            return new ActionResult(
                BoxelSearchOutcomeKind.Success,
                BoxelSearchMessageCode.NextSystemCopied,
                PrimaryValue: state.NextSystem);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AddHealthLocked(
                BoxelSearchHealthSubsystem.Clipboard,
                BoxelSearchHealthSeverity.Warning,
                BoxelSearchMessageCode.ClipboardFailed,
                exception);
            return new ActionResult(
                BoxelSearchOutcomeKind.AppliedWithWarnings,
                BoxelSearchMessageCode.ClipboardFailed,
                PrimaryValue: state.NextSystem,
                Warnings:
                [
                    new BoxelSearchWarning(
                        BoxelSearchHealthSubsystem.Clipboard,
                        BoxelSearchMessageCode.ClipboardFailed)
                ]);
        }
    }

    private async Task<ActionResult> SaveToLibraryLockedAsync(
        SaveBoxelSearchToLibrary action,
        CancellationToken cancellationToken)
    {
        if (profile is null || state.TopBoxel is null)
        {
            return new ActionResult(
                BoxelSearchOutcomeKind.Rejected,
                BoxelSearchMessageCode.SearchNotConfigured);
        }

        if (state.SavedSearchFileName is { } existingFile)
        {
            try
            {
                if (await libraryStore.ExistsAsync(
                        profile.FrontierId,
                        existingFile,
                        cancellationToken)
                    .ConfigureAwait(false))
                {
                    return new ActionResult(
                        BoxelSearchOutcomeKind.NoChange,
                        BoxelSearchMessageCode.SearchAlreadySavedToLibrary);
                }

                state.SetSavedSearchFileName(null);
                var missingWarnings = new List<BoxelSearchWarning>();
                await PersistLockedAsync(missingWarnings, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsLibraryException(exception))
            {
                AddHealthLocked(
                    BoxelSearchHealthSubsystem.LibraryPersistence,
                    BoxelSearchHealthSeverity.Warning,
                    BoxelSearchMessageCode.LibraryUnavailable,
                    exception);
                return new ActionResult(
                    BoxelSearchOutcomeKind.AppliedWithWarnings,
                    BoxelSearchMessageCode.LibraryUnavailable,
                    Warnings:
                    [
                        new BoxelSearchWarning(
                            BoxelSearchHealthSubsystem.LibraryPersistence,
                            BoxelSearchMessageCode.LibraryUnavailable)
                    ]);
            }
        }

        if (string.IsNullOrWhiteSpace(action.Name))
        {
            return new ActionResult(
                BoxelSearchOutcomeKind.Rejected,
                BoxelSearchMessageCode.LibraryDetailsRequired);
        }

        try
        {
            var document = await libraryStore.CreateAsync(
                    profile.FrontierId,
                    action.Name,
                    action.Notes,
                    state.CreateSnapshot(),
                    cancellationToken)
                .ConfigureAwait(false);
            state.SetSavedSearchFileName(document.FileName);
            var warnings = new List<BoxelSearchWarning>();
            await PersistLockedAsync(warnings, cancellationToken).ConfigureAwait(false);
            libraryRevision++;
            RemoveHealthLocked(BoxelSearchHealthSubsystem.LibraryPersistence);
            return new ActionResult(
                GetAppliedKind(BoxelSearchOutcomeKind.Success, warnings),
                BoxelSearchMessageCode.SearchSavedToLibrary,
                PrimaryValue: document.Name,
                SavedSearch: document,
                Warnings: warnings);
        }
        catch (Exception exception) when (IsLibraryException(exception))
        {
            AddHealthLocked(
                BoxelSearchHealthSubsystem.LibraryPersistence,
                BoxelSearchHealthSeverity.Warning,
                BoxelSearchMessageCode.LibraryUnavailable,
                exception);
            return new ActionResult(
                BoxelSearchOutcomeKind.AppliedWithWarnings,
                BoxelSearchMessageCode.LibraryUnavailable,
                Warnings:
                [
                    new BoxelSearchWarning(
                        BoxelSearchHealthSubsystem.LibraryPersistence,
                        BoxelSearchMessageCode.LibraryUnavailable)
                ]);
        }
    }

    private async Task<ActionResult> ResumeSavedLockedAsync(
        ResumeSavedBoxelSearch action,
        CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            return new ActionResult(
                BoxelSearchOutcomeKind.Rejected,
                BoxelSearchMessageCode.ProfileUnavailable);
        }

        try
        {
            var document = await libraryStore.LoadAsync(
                    profile.FrontierId,
                    action.FileName,
                    cancellationToken)
                .ConfigureAwait(false);
            state.Reset(document.Search with
            {
                Active = true,
                SavedSearchFileName = document.FileName,
            });
            state.ApplyEmptyBoxels(await emptyBoxelStore.LoadGroupAsync(
                    state.TopBoxel!,
                    cancellationToken)
                .ConfigureAwait(false));
            var warnings = new List<BoxelSearchWarning>();
            await PersistLockedAsync(warnings, cancellationToken).ConfigureAwait(false);
            return new ActionResult(
                GetAppliedKind(BoxelSearchOutcomeKind.Success, warnings),
                BoxelSearchMessageCode.SavedSearchResumed,
                PrimaryValue: document.Name,
                SavedSearch: document,
                Warnings: warnings);
        }
        catch (Exception exception) when (IsLibraryException(exception))
        {
            AddHealthLocked(
                BoxelSearchHealthSubsystem.LibraryPersistence,
                BoxelSearchHealthSeverity.Warning,
                BoxelSearchMessageCode.LibraryUnavailable,
                exception);
            return new ActionResult(
                BoxelSearchOutcomeKind.Rejected,
                BoxelSearchMessageCode.LibraryUnavailable);
        }
    }

    private async Task<ActionResult> RenameSavedLockedAsync(
        RenameSavedBoxelSearch action,
        CancellationToken cancellationToken)
    {
        var document = await RunLibraryMutationAsync(
                (frontierId, token) => libraryStore.RenameAsync(
                    frontierId,
                    action.FileName,
                    action.Name,
                    token),
                cancellationToken)
            .ConfigureAwait(false);
        return new ActionResult(
            document is null
                ? BoxelSearchOutcomeKind.Rejected
                : BoxelSearchOutcomeKind.Success,
            document is null
                ? BoxelSearchMessageCode.LibraryUnavailable
                : BoxelSearchMessageCode.SavedSearchRenamed,
            PrimaryValue: document?.Name,
            SavedSearch: document);
    }

    private async Task<ActionResult> UpdateNotesLockedAsync(
        UpdateSavedBoxelSearchNotes action,
        CancellationToken cancellationToken)
    {
        var document = await RunLibraryMutationAsync(
                (frontierId, token) => libraryStore.SaveNotesAsync(
                    frontierId,
                    action.FileName,
                    action.Notes,
                    token),
                cancellationToken)
            .ConfigureAwait(false);
        return new ActionResult(
            document is null
                ? BoxelSearchOutcomeKind.Rejected
                : BoxelSearchOutcomeKind.Success,
            document is null
                ? BoxelSearchMessageCode.LibraryUnavailable
                : BoxelSearchMessageCode.SavedSearchNotesUpdated,
            SavedSearch: document);
    }

    private async Task<ActionResult> SetFavoriteLockedAsync(
        SetSavedBoxelSearchFavorite action,
        CancellationToken cancellationToken)
    {
        var document = await RunLibraryMutationAsync(
                (frontierId, token) => libraryStore.SetFavoriteAsync(
                    frontierId,
                    action.FileName,
                    action.IsFavorite,
                    token),
                cancellationToken)
            .ConfigureAwait(false);
        return new ActionResult(
            document is null
                ? BoxelSearchOutcomeKind.Rejected
                : BoxelSearchOutcomeKind.Success,
            document is null
                ? BoxelSearchMessageCode.LibraryUnavailable
                : BoxelSearchMessageCode.SavedSearchFavoriteUpdated,
            PrimaryValue: action.IsFavorite.ToString(),
            SavedSearch: document);
    }

    private async Task<ActionResult> DeleteSavedLockedAsync(
        DeleteSavedBoxelSearch action,
        CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            return new ActionResult(
                BoxelSearchOutcomeKind.Rejected,
                BoxelSearchMessageCode.ProfileUnavailable);
        }

        try
        {
            await libraryStore.DeleteAsync(
                    profile.FrontierId,
                    action.FileName,
                    cancellationToken)
                .ConfigureAwait(false);
            libraryRevision++;
            if (string.Equals(
                    state.SavedSearchFileName,
                    action.FileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                state.SetSavedSearchFileName(null);
                var warnings = new List<BoxelSearchWarning>();
                await PersistLockedAsync(warnings, cancellationToken)
                    .ConfigureAwait(false);
            }

            RemoveHealthLocked(BoxelSearchHealthSubsystem.LibraryPersistence);
            return new ActionResult(
                BoxelSearchOutcomeKind.Success,
                BoxelSearchMessageCode.SavedSearchDeleted);
        }
        catch (Exception exception) when (IsLibraryException(exception))
        {
            AddHealthLocked(
                BoxelSearchHealthSubsystem.LibraryPersistence,
                BoxelSearchHealthSeverity.Warning,
                BoxelSearchMessageCode.LibraryUnavailable,
                exception);
            return new ActionResult(
                BoxelSearchOutcomeKind.Rejected,
                BoxelSearchMessageCode.LibraryUnavailable);
        }
    }

    private async Task<SavedBoxelSearchDocument?> RunLibraryMutationAsync(
        Func<string, CancellationToken, Task<SavedBoxelSearchDocument>> mutation,
        CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            return null;
        }

        try
        {
            var result = await mutation(profile.FrontierId, cancellationToken)
                .ConfigureAwait(false);
            libraryRevision++;
            RemoveHealthLocked(BoxelSearchHealthSubsystem.LibraryPersistence);
            return result;
        }
        catch (Exception exception) when (IsLibraryException(exception))
        {
            AddHealthLocked(
                BoxelSearchHealthSubsystem.LibraryPersistence,
                BoxelSearchHealthSeverity.Warning,
                BoxelSearchMessageCode.LibraryUnavailable,
                exception);
            return null;
        }
    }

    private async Task<ActionResult> PersistResultLockedAsync(
        ActionResult result,
        CancellationToken cancellationToken)
    {
        var warnings = result.Warnings?.ToList() ?? [];
        await PersistLockedAsync(warnings, cancellationToken).ConfigureAwait(false);
        return result with
        {
            Kind = GetAppliedKind(result.Kind, warnings),
            Warnings = warnings,
        };
    }

    private async Task PersistLockedAsync(
        List<BoxelSearchWarning> warnings,
        CancellationToken cancellationToken)
    {
        if (profile is null || state.TopBoxel is null)
        {
            return;
        }

        var snapshot = state.CreateSnapshot();
        var pending = new PendingPersistence(profile, snapshot, snapshot.SavedSearchFileName);
        try
        {
            await profileStore.SaveBoxelSearchAsync(
                    profile.FrontierId,
                    profile.CommanderName,
                    profile.IsOdyssey,
                    snapshot,
                    cancellationToken)
                .ConfigureAwait(false);
            pendingProfile = null;
            RemoveHealthLocked(BoxelSearchHealthSubsystem.ProfilePersistence);
        }
        catch (Exception exception) when (IsPersistenceException(exception))
        {
            pendingProfile = pending;
            AddHealthLocked(
                BoxelSearchHealthSubsystem.ProfilePersistence,
                BoxelSearchHealthSeverity.Error,
                BoxelSearchMessageCode.SynchronizationDegraded,
                exception);
            warnings.Add(new BoxelSearchWarning(
                BoxelSearchHealthSubsystem.ProfilePersistence,
                BoxelSearchMessageCode.SynchronizationDegraded));
            EnsureRetryWorkerLocked();
            return;
        }

        if (snapshot.SavedSearchFileName is not { } savedFileName)
        {
            pendingLibrary = null;
            return;
        }

        try
        {
            await libraryStore.SaveProgressAsync(
                    profile.FrontierId,
                    savedFileName,
                    snapshot,
                    cancellationToken)
                .ConfigureAwait(false);
            pendingLibrary = null;
            libraryRevision++;
            RemoveHealthLocked(BoxelSearchHealthSubsystem.LibraryPersistence);
        }
        catch (FileNotFoundException exception)
        {
            ReportDiagnostic(
                BoxelSearchHealthSubsystem.LibraryPersistence,
                BoxelSearchMessageCode.LibraryUnavailable,
                exception,
                savedFileName);
            state.SetSavedSearchFileName(null);
            await PersistProfileRepairLockedAsync(warnings, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            await RecoverCorruptLinkedSearchLockedAsync(
                    savedFileName,
                    exception,
                    warnings,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsPersistenceException(exception))
        {
            pendingLibrary = pending;
            AddHealthLocked(
                BoxelSearchHealthSubsystem.LibraryPersistence,
                BoxelSearchHealthSeverity.Warning,
                BoxelSearchMessageCode.SynchronizationDegraded,
                exception);
            warnings.Add(new BoxelSearchWarning(
                BoxelSearchHealthSubsystem.LibraryPersistence,
                BoxelSearchMessageCode.SynchronizationDegraded));
            EnsureRetryWorkerLocked();
        }
    }

    private async Task PersistProfileRepairLockedAsync(
        List<BoxelSearchWarning> warnings,
        CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            return;
        }

        var repaired = state.CreateSnapshot();
        try
        {
            await profileStore.SaveBoxelSearchAsync(
                    profile.FrontierId,
                    profile.CommanderName,
                    profile.IsOdyssey,
                    repaired,
                    cancellationToken)
                .ConfigureAwait(false);
            pendingProfile = null;
        }
        catch (Exception exception) when (IsPersistenceException(exception))
        {
            pendingProfile = new PendingPersistence(profile, repaired, null);
            AddHealthLocked(
                BoxelSearchHealthSubsystem.ProfilePersistence,
                BoxelSearchHealthSeverity.Error,
                BoxelSearchMessageCode.SynchronizationDegraded,
                exception);
            warnings.Add(new BoxelSearchWarning(
                BoxelSearchHealthSubsystem.ProfilePersistence,
                BoxelSearchMessageCode.SynchronizationDegraded));
            EnsureRetryWorkerLocked();
        }
    }

    private async Task ReconcileLinkedSearchLockedAsync(
        List<BoxelSearchWarning> warnings)
    {
        if (profile is null || state.SavedSearchFileName is not { } fileName)
        {
            return;
        }

        try
        {
            var saved = await libraryStore.LoadAsync(
                    profile.FrontierId,
                    fileName,
                    lifetimeCancellation.Token)
                .ConfigureAwait(false);
            var currentSnapshot = state.CreateSnapshot();
            if (!BoxelSearchSnapshotComparer.Equals(currentSnapshot, saved.Search))
            {
                await libraryStore.SaveProgressAsync(
                        profile.FrontierId,
                        fileName,
                        currentSnapshot,
                        lifetimeCancellation.Token)
                    .ConfigureAwait(false);
                libraryRevision++;
            }

            RemoveHealthLocked(BoxelSearchHealthSubsystem.LibraryPersistence);
        }
        catch (FileNotFoundException exception)
        {
            ReportDiagnostic(
                BoxelSearchHealthSubsystem.LibraryPersistence,
                BoxelSearchMessageCode.LibraryUnavailable,
                exception,
                fileName);
            state.SetSavedSearchFileName(null);
            await PersistProfileRepairLockedAsync(
                    warnings,
                    lifetimeCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            await RecoverCorruptLinkedSearchLockedAsync(
                    fileName,
                    exception,
                    warnings,
                    lifetimeCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsPersistenceException(exception))
        {
            pendingLibrary = new PendingPersistence(
                profile,
                state.CreateSnapshot(),
                fileName);
            AddHealthLocked(
                BoxelSearchHealthSubsystem.LibraryPersistence,
                BoxelSearchHealthSeverity.Warning,
                BoxelSearchMessageCode.SynchronizationDegraded,
                exception);
            warnings.Add(new BoxelSearchWarning(
                BoxelSearchHealthSubsystem.LibraryPersistence,
                BoxelSearchMessageCode.SynchronizationDegraded));
            EnsureRetryWorkerLocked();
        }
    }

    private async Task RecoverCorruptLinkedSearchLockedAsync(
        string fileName,
        InvalidDataException exception,
        List<BoxelSearchWarning> warnings,
        CancellationToken cancellationToken)
    {
        ReportDiagnostic(
            BoxelSearchHealthSubsystem.LibraryPersistence,
            BoxelSearchMessageCode.LibraryUnavailable,
            exception,
            fileName);
        try
        {
            await libraryStore.DeleteAsync(
                    profile!.FrontierId,
                    fileName,
                    cancellationToken)
                .ConfigureAwait(false);
            libraryRevision++;
            state.SetSavedSearchFileName(null);
            warnings.Add(new BoxelSearchWarning(
                BoxelSearchHealthSubsystem.LibraryPersistence,
                BoxelSearchMessageCode.LibraryUnavailable));
            await PersistProfileRepairLockedAsync(warnings, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception recoveryException) when (IsLibraryException(recoveryException))
        {
            pendingLibrary = new PendingPersistence(
                profile!,
                state.CreateSnapshot(),
                fileName);
            AddHealthLocked(
                BoxelSearchHealthSubsystem.LibraryPersistence,
                BoxelSearchHealthSeverity.Warning,
                BoxelSearchMessageCode.SynchronizationDegraded,
                recoveryException);
            warnings.Add(new BoxelSearchWarning(
                BoxelSearchHealthSubsystem.LibraryPersistence,
                BoxelSearchMessageCode.SynchronizationDegraded));
            EnsureRetryWorkerLocked();
        }
    }

    private void EnsureRetryWorkerLocked()
    {
        if (retryTask is { IsCompleted: false })
        {
            return;
        }

        retryTask = Task.Run(
            () => RetryLoopAsync(lifetimeCancellation.Token),
            CancellationToken.None);
    }

    private async Task RetryLoopAsync(CancellationToken cancellationToken)
    {
        var delay = options.InitialRetryDelay;
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
            BoxelSearchSessionChangedEventArgs? change;
            var hasPending = false;
            await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (disposing || disposed)
                {
                    return;
                }

                await RetryPendingOnceLockedAsync(cancellationToken).ConfigureAwait(false);
                hasPending = pendingProfile is not null || pendingLibrary is not null;
                change = CaptureChangeLocked();
            }
            finally
            {
                mutationGate.Release();
            }

            RaiseChanged(change);
            if (!hasPending)
            {
                return;
            }

            delay = TimeSpan.FromTicks(Math.Min(
                options.MaximumRetryDelay.Ticks,
                Math.Max(delay.Ticks + 1, delay.Ticks * 2)));
        }
    }

    private async Task RetryPendingOnceLockedAsync(CancellationToken cancellationToken)
    {
        await RetryPendingProfileOnceLockedAsync(cancellationToken).ConfigureAwait(false);
        if (pendingProfile is null)
        {
            await RetryPendingLibraryOnceLockedAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task RetryPendingProfileOnceLockedAsync(
        CancellationToken cancellationToken)
    {
        if (pendingProfile is not { } pending)
        {
            return;
        }

        if (!IsPendingCurrent(pending))
        {
            pendingProfile = null;
            return;
        }

        try
        {
            await profileStore.SaveBoxelSearchAsync(
                    pending.Profile.FrontierId,
                    pending.Profile.CommanderName,
                    pending.Profile.IsOdyssey,
                    pending.Snapshot,
                    cancellationToken)
                .ConfigureAwait(false);
            pendingProfile = null;
            RemoveHealthLocked(BoxelSearchHealthSubsystem.ProfilePersistence);
        }
        catch (Exception exception) when (IsPersistenceException(exception))
        {
            ReportDiagnostic(
                BoxelSearchHealthSubsystem.ProfilePersistence,
                BoxelSearchMessageCode.SynchronizationDegraded,
                exception,
                pending.Profile.FrontierId);
        }
    }

    private async Task RetryPendingLibraryOnceLockedAsync(
        CancellationToken cancellationToken)
    {
        if (pendingLibrary is not { } pending)
        {
            return;
        }

        if (!IsPendingCurrent(pending) || pending.FileName is null)
        {
            pendingLibrary = null;
            return;
        }

        try
        {
            await libraryStore.SaveProgressAsync(
                    pending.Profile.FrontierId,
                    pending.FileName,
                    pending.Snapshot,
                    cancellationToken)
                .ConfigureAwait(false);
            pendingLibrary = null;
            libraryRevision++;
            RemoveHealthLocked(BoxelSearchHealthSubsystem.LibraryPersistence);
        }
        catch (Exception exception) when (IsPersistenceException(exception))
        {
            ReportDiagnostic(
                BoxelSearchHealthSubsystem.LibraryPersistence,
                BoxelSearchMessageCode.SynchronizationDegraded,
                exception,
                pending.FileName);
        }
    }

    private bool IsPendingCurrent(PendingPersistence pending)
    {
        return profile is not null
            && pending.Profile.Generation == profile.Generation
            && string.Equals(
                pending.Profile.FrontierId,
                profile.FrontierId,
                StringComparison.Ordinal);
    }

    private async Task<BoxelSearchOutcome> RunSerializedAsync(
        Func<CancellationToken, Task<ActionResult>> action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();
        BoxelSearchSessionChangedEventArgs? change;
        ActionResult result;
        try
        {
            await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CreateOutcome(new ActionResult(
                BoxelSearchOutcomeKind.Cancelled,
                BoxelSearchMessageCode.None));
        }

        try
        {
            ThrowIfUnavailable();
            result = await action(lifetimeCancellation.Token).ConfigureAwait(false);
            change = CaptureChangeLocked();
        }
        finally
        {
            mutationGate.Release();
        }

        RaiseChanged(change);
        return CreateOutcome(result);
    }

    private BoxelSearchSessionChangedEventArgs? CaptureChangeLocked()
    {
        if (projectedStateVersion != state.Version)
        {
            searchSnapshot = CreateSearchSnapshotLocked();
            projectedStateVersion = state.Version;
        }

        if (contextSnapshot.Version != contextVersion)
        {
            contextSnapshot = new BoxelSearchContextSnapshot(
                contextVersion,
                profile,
                currentSystemName,
                currentPosition,
                currentSystemAddress,
                latestRoute,
                latestStatus,
                musicTrack,
                isGalaxyMapOpen,
                lastCopiedSystemName);
        }

        if (activitySnapshot.Version != activityVersion)
        {
            activitySnapshot = activitySnapshot with { Version = activityVersion };
        }

        if (healthSnapshot.Version != healthVersion)
        {
            healthSnapshot = new BoxelSearchHealthSnapshot(
                healthVersion,
                new Dictionary<BoxelSearchHealthSubsystem, BoxelSearchHealthIssue>(
                    healthIssues));
        }

        var previous = current;
        if (ReferenceEquals(previous.Search, searchSnapshot)
            && ReferenceEquals(previous.Context, contextSnapshot)
            && ReferenceEquals(previous.Activity, activitySnapshot)
            && ReferenceEquals(previous.Health, healthSnapshot)
            && previous.LibraryRevision == libraryRevision)
        {
            return null;
        }

        var next = new BoxelSearchSessionSnapshot(
            ++sessionVersion,
            searchSnapshot,
            contextSnapshot,
            activitySnapshot,
            healthSnapshot,
            libraryRevision);
        Volatile.Write(ref current, next);
        return new BoxelSearchSessionChangedEventArgs(previous, next);
    }

    private BoxelSearchSessionSearchSnapshot CreateSearchSnapshotLocked()
    {
        return new BoxelSearchSessionSearchSnapshot
        {
            Version = state.Version,
            Persistence = state.CreateSnapshot(),
            NextSystem = state.NextSystem,
            NextSystemAscending = state.GetNextSystem(descending: false),
            NextSystemDescending = state.GetNextSystem(descending: true),
            CurrentIsEmpty = state.CurrentIsEmpty,
            CurrentMinimumSystemNumber = state.CurrentMinimumSystemNumber,
            CurrentMaximumSystemNumber = state.CurrentMaximumSystemNumber,
            CompletedSystemCount = state.CompletedSystemCount,
            TotalCompletedSystemCount = state.TotalCompletedSystemCount,
            CompletedBoxelCount = state.CompletedBoxelCount,
            TotalBoxelCount = state.TotalBoxelCount,
            CurrentSystemsComplete = state.CurrentSystemsComplete,
            Systems = state.Systems,
            Boxels = state.Boxels,
            EmptyBoxelPrefixes = state.EmptyBoxelPrefixes,
        };
    }

    private BoxelSearchOutcome CreateOutcome(ActionResult result)
    {
        var snapshot = Current;
        var kind = result.Kind;
        if ((kind == BoxelSearchOutcomeKind.Success
                || kind == BoxelSearchOutcomeKind.AppliedWithWarnings)
            && result.Warnings is { Count: > 0 })
        {
            kind = result.Warnings.Any(warning =>
                warning.Subsystem == BoxelSearchHealthSubsystem.ProfilePersistence)
                    ? BoxelSearchOutcomeKind.AppliedNotPersisted
                    : BoxelSearchOutcomeKind.AppliedWithWarnings;
        }

        return new BoxelSearchOutcome(
            kind,
            result.Code,
            snapshot.Version,
            snapshot.Search.Version,
            snapshot.Context.Version,
            snapshot.Activity.Version,
            snapshot.Health.Version,
            snapshot.LibraryRevision,
            result.PrimaryValue,
            result.SecondaryValue,
            result.Count,
            result.Total,
            result.SavedSearch,
            result.Warnings ?? []);
    }

    private void RaiseChanged(BoxelSearchSessionChangedEventArgs? eventArgs)
    {
        if (eventArgs is null || disposed)
        {
            return;
        }

        var handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList()
                     .Cast<EventHandler<BoxelSearchSessionChangedEventArgs>>())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch (Exception exception)
            {
                ReportDiagnostic(
                    BoxelSearchHealthSubsystem.LocalData,
                    BoxelSearchMessageCode.None,
                    exception,
                    "Changed subscriber");
            }
        }
    }

    private void TouchContextLocked()
    {
        contextVersion++;
    }

    private void SetActivityLocked(
        BoxelSearchActivityKind kind,
        int processed,
        int total,
        string? prefix)
    {
        activityVersion++;
        activitySnapshot = new BoxelSearchActivitySnapshot(
            activityVersion,
            kind,
            processed,
            Math.Max(0, total),
            prefix);
    }

    private void ClearActivityLocked()
    {
        SetActivityLocked(BoxelSearchActivityKind.Idle, 0, 0, null);
    }

    private void AddHealthLocked(
        BoxelSearchHealthSubsystem subsystem,
        BoxelSearchHealthSeverity severity,
        BoxelSearchMessageCode code,
        Exception? exception = null)
    {
        healthIssues[subsystem] = new BoxelSearchHealthIssue(
            subsystem,
            severity,
            code,
            timeProvider.GetUtcNow());
        healthVersion++;
        if (exception is not null)
        {
            ReportDiagnostic(subsystem, code, exception);
        }
    }

    private void RemoveHealthLocked(BoxelSearchHealthSubsystem subsystem)
    {
        if (healthIssues.Remove(subsystem))
        {
            healthVersion++;
        }
    }

    private void ClearHealthLocked()
    {
        if (healthIssues.Count == 0)
        {
            return;
        }

        healthIssues.Clear();
        healthVersion++;
    }

    private void UpdateRefreshHealthLocked(IReadOnlyList<BoxelSearchWarning> warnings)
    {
        if (!warnings.Any(warning =>
                warning.Subsystem == BoxelSearchHealthSubsystem.Resolver))
        {
            RemoveHealthLocked(BoxelSearchHealthSubsystem.Resolver);
        }

        if (!warnings.Any(warning =>
                warning.Subsystem == BoxelSearchHealthSubsystem.LocalData))
        {
            RemoveHealthLocked(BoxelSearchHealthSubsystem.LocalData);
        }
    }

    private bool IsCurrentSystemInsideSearchLocked()
    {
        return state.TopBoxel is not null
            && BoxelAddress.TryParse(currentSystemName, out var currentSystem)
            && currentSystem is not null
            && state.TopBoxel.Contains(currentSystem);
    }

    private bool IsRequestCurrentLocked(
        long generation,
        string? topPrefix,
        string? currentPrefix)
    {
        return profile?.Generation == generation
            && string.Equals(
                state.TopBoxel?.Prefix,
                topPrefix,
                StringComparison.Ordinal)
            && (currentPrefix is null
                || string.Equals(
                    state.Current?.Prefix,
                    currentPrefix,
                    StringComparison.Ordinal));
    }

    private async Task PublishAuditProgressAsync(
        BoxelCompletionAuditProgress progress,
        CancellationToken cancellationToken)
    {
        BoxelSearchSessionChangedEventArgs? change;
        try
        {
            await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (activitySnapshot.Kind != BoxelSearchActivityKind.Auditing
                || progress.Processed <= activitySnapshot.Processed)
            {
                return;
            }

            SetActivityLocked(
                BoxelSearchActivityKind.Auditing,
                progress.Processed,
                progress.Total,
                progress.Prefix);
            change = CaptureChangeLocked();
        }
        finally
        {
            mutationGate.Release();
        }

        RaiseChanged(change);
    }

    private async Task<BoxelSearchOutcome> SetCancellingAuditAsync()
    {
        return await RunSerializedAsync(token =>
        {
            SetActivityLocked(
                BoxelSearchActivityKind.CancellingAudit,
                activitySnapshot.Processed,
                activitySnapshot.Total,
                activitySnapshot.Prefix);
            return Task.FromResult(new ActionResult(
                BoxelSearchOutcomeKind.Success,
                BoxelSearchMessageCode.AuditCancelled));
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<BoxelSearchOutcome> CompleteCancelledActivityAsync(
        BoxelSearchMessageCode code)
    {
        return await RunSerializedAsync(token =>
        {
            ClearActivityLocked();
            return Task.FromResult(new ActionResult(
                BoxelSearchOutcomeKind.Cancelled,
                code));
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<BoxelSearchOutcome> CompleteFailedAuditAsync(Exception exception)
    {
        return await RunSerializedAsync(token =>
        {
            AddHealthLocked(
                BoxelSearchHealthSubsystem.LocalData,
                BoxelSearchHealthSeverity.Warning,
                BoxelSearchMessageCode.AuditFailed,
                exception);
            ClearActivityLocked();
            return Task.FromResult(new ActionResult(
                BoxelSearchOutcomeKind.AppliedWithWarnings,
                BoxelSearchMessageCode.AuditFailed,
                Warnings:
                [
                    new BoxelSearchWarning(
                        BoxelSearchHealthSubsystem.LocalData,
                        BoxelSearchMessageCode.AuditFailed)
                ]));
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task CancelRefreshAsync()
    {
        Task<BoxelSearchOutcome>? running;
        lock (externalWorkSync)
        {
            refreshCancellation?.Cancel();
            running = refreshTask;
        }

        if (running is not null)
        {
            try
            {
                await running.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Replacing a refresh intentionally cancels the superseded operation.
            }
        }
    }

    private async Task CancelAndAwaitExternalWorkAsync()
    {
        Task<BoxelSearchOutcome>? runningRefresh;
        Task<BoxelSearchOutcome>? runningAudit;
        lock (externalWorkSync)
        {
            refreshCancellation?.Cancel();
            auditCancellation?.Cancel();
            runningRefresh = refreshTask;
            runningAudit = auditTask;
        }

        foreach (var task in new[] { runningRefresh, runningAudit })
        {
            if (task is null)
            {
                continue;
            }

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Profile changes and disposal intentionally cancel external work.
            }
        }
    }

    private async Task<BoxelSearchOutcome> AwaitAndClearRefreshAsync(
        Task<BoxelSearchOutcome> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            lock (externalWorkSync)
            {
                if (ReferenceEquals(refreshTask, task))
                {
                    refreshTask = null;
                }
            }
        }
    }

    private async Task<BoxelSearchOutcome> AwaitAndClearAuditAsync(
        Task<BoxelSearchOutcome> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            lock (externalWorkSync)
            {
                if (ReferenceEquals(auditTask, task))
                {
                    auditTask = null;
                }
            }
        }
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(disposed || disposing, this);
    }

    private void ReportDiagnostic(
        BoxelSearchHealthSubsystem subsystem,
        BoxelSearchMessageCode code,
        Exception exception,
        string? context = null)
    {
        diagnostics.Report(new BoxelSearchDiagnostic(
            subsystem,
            code,
            exception,
            timeProvider.GetUtcNow(),
            context));
    }

    private static BoxelSearchOutcomeKind GetAppliedKind(
        BoxelSearchOutcomeKind currentKind,
        List<BoxelSearchWarning> warnings)
    {
        if (warnings.Any(warning =>
                warning.Subsystem == BoxelSearchHealthSubsystem.ProfilePersistence))
        {
            return BoxelSearchOutcomeKind.AppliedNotPersisted;
        }

        return warnings.Count > 0
            ? BoxelSearchOutcomeKind.AppliedWithWarnings
            : currentKind;
    }

    private static bool IsResolverException(Exception exception)
    {
        return exception is HttpRequestException
            or TaskCanceledException
            or InvalidDataException
            or JsonException;
    }

    private static bool IsPersistenceException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or JsonException;
    }

    private static bool IsLibraryException(Exception exception)
    {
        return IsPersistenceException(exception)
            || exception is ArgumentException;
    }

    private static bool IsLocalDataException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException;
    }

    private sealed record ActionResult(
        BoxelSearchOutcomeKind Kind,
        BoxelSearchMessageCode Code,
        string? PrimaryValue = null,
        string? SecondaryValue = null,
        int Count = 0,
        int Total = 0,
        SavedBoxelSearchDocument? SavedSearch = null,
        IReadOnlyList<BoxelSearchWarning>? Warnings = null);

    private sealed record PendingPersistence(
        BoxelSearchProfileIdentity Profile,
        BoxelSearchSnapshot Snapshot,
        string? FileName);

    private sealed record RefreshRequest(
        long Generation,
        string FrontierId,
        string? TopPrefix,
        BoxelAddress Current,
        NavRouteSnapshot? Route);

    private sealed record RefreshSources(
        IReadOnlySet<string> Empty,
        LegacySystemDataReadResult Local,
        IReadOnlyList<BoxelSystemObservation> Remote);

    private sealed record AuditRequest(
        long Generation,
        string TopPrefix,
        BoxelCompletionAuditRequest Request);

    private sealed class UnavailableBoxelClipboard : IBoxelClipboard
    {
        public static UnavailableBoxelClipboard Instance { get; } = new();

        public bool IsReady => false;

        public Task WriteTextAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The clipboard is not ready.");
        }
    }

    private static class BoxelSearchSnapshotComparer
    {
        public static bool Equals(BoxelSearchSnapshot left, BoxelSearchSnapshot right)
        {
            return left.Active == right.Active
                && Equals(left.TopBoxel, right.TopBoxel)
                && left.StartedOn == right.StartedOn
                && Equals(left.Current, right.Current)
                && left.CurrentCount == right.CurrentCount
                && left.LowMassCode == right.LowMassCode
                && left.AutoCopy == right.AutoCopy
                && left.SortDescending == right.SortDescending
                && left.Collapsed == right.Collapsed
                && left.SkipAlreadyVisited == right.SkipAlreadyVisited
                && left.SkipKnownToSpansh == right.SkipKnownToSpansh
                && left.CompletionMode == right.CompletionMode
                && string.Equals(
                    left.SavedSearchFileName,
                    right.SavedSearchFileName,
                    StringComparison.OrdinalIgnoreCase)
                && left.CompletedPrefixes.SequenceEqual(right.CompletedPrefixes)
                && left.CompletedSystems.SequenceEqual(right.CompletedSystems)
                && left.EmptySystems.SequenceEqual(right.EmptySystems)
                && left.DeferredSystems.SequenceEqual(right.DeferredSystems)
                && left.DeferredRanges.SequenceEqual(right.DeferredRanges)
                && left.ProgressByPrefix.Count == right.ProgressByPrefix.Count
                && left.ProgressByPrefix.All(entry =>
                    right.ProgressByPrefix.TryGetValue(entry.Key, out var value)
                    && value == entry.Value);
        }
    }
}
