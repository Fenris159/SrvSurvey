using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Quests;

public sealed class QuestRuntimeCoordinator : IAsyncDisposable
{
    private readonly LegacyQuestStateStore legacyStore;
    private readonly IRavenQuestClient ravenClient;
    private readonly Action<string>? log;
    private readonly QuestCommanderContextTracker contextTracker = new();
    private readonly SemaphoreSlim coordinatorLock = new(1, 1);
    private readonly Dictionary<QuestIdentity, RuntimeRegistration> runtimes = [];
    private QuestRuntimeConfiguration? configuration;
    private bool disposed;

    public QuestRuntimeCoordinator(
        LegacyQuestStateStore legacyStore,
        IRavenQuestClient ravenClient,
        Action<string>? log = null)
    {
        this.legacyStore = legacyStore
            ?? throw new ArgumentNullException(nameof(legacyStore));
        this.ravenClient = ravenClient
            ?? throw new ArgumentNullException(nameof(ravenClient));
        this.log = log;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<QuestRuntimeSnapshot> Snapshot { get; private set; } = [];

    public async Task<IReadOnlyList<RavenQuestDefinition>>
        GetPublishedQuestsAsync(CancellationToken cancellationToken = default)
    {
        await coordinatorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await ravenClient.GetPublishedQuestsAsync(
                    configuration?.RavenApiKey,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            coordinatorLock.Release();
        }
    }

    public async Task<IReadOnlyList<RavenCommanderQuestStatus>>
        GetCommanderQuestStatusesAsync(
            CancellationToken cancellationToken = default)
    {
        await coordinatorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var current = RequireRemoteConfiguration();
            return await ravenClient.GetCommanderQuestStatusesAsync(
                    current.RavenApiKey!,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            coordinatorLock.Release();
        }
    }

    public async Task<QuestRuntimeUpdateResult> ApplyUpdateAsync(
        QuestRuntimeConfiguration nextConfiguration,
        string journalDirectory,
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        bool isBootstrap,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nextConfiguration);
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        ArgumentNullException.ThrowIfNull(journalEvents);
        ValidateConfiguration(nextConfiguration);

        var warnings = new List<string>();
        var processedEvents = 0;
        await coordinatorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var identityChanged = !HasSameIdentity(
                configuration,
                nextConfiguration);
            if (identityChanged)
            {
                await ClearRuntimesAsync().ConfigureAwait(false);
                contextTracker.Reset();
            }

            configuration = nextConfiguration;
            contextTracker.Apply(journalEvents);
            var context = contextTracker.CreateContext(
                nextConfiguration.CommanderName,
                nextConfiguration.Status);

            if (!nextConfiguration.Enabled)
            {
                if (runtimes.Count > 0)
                {
                    await ClearRuntimesAsync().ConfigureAwait(false);
                }
            }
            else if (identityChanged)
            {
                await LoadRuntimesAsync(
                        nextConfiguration,
                        context,
                        warnings,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (var registration in runtimes.Values)
            {
                registration.Runtime.CommanderContext = context;
            }

            if (!isBootstrap && nextConfiguration.Enabled)
            {
                foreach (var journalEvent in journalEvents)
                {
                    var resolved = await QuestJournalPayloadResolver.ResolveAsync(
                            journalDirectory,
                            journalEvent,
                            cancellationToken)
                        .ConfigureAwait(false);
                    AddWarning(warnings, resolved.Warning);
                    await ProcessEventAsync(
                            resolved.Payload,
                            warnings,
                            cancellationToken)
                        .ConfigureAwait(false);
                    processedEvents++;
                }
            }

            Snapshot = CreateSnapshot();
        }
        finally
        {
            coordinatorLock.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new QuestRuntimeUpdateResult(
            Snapshot,
            warnings,
            processedEvents);
    }

    public async Task<QuestRuntimeUpdateResult> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        await coordinatorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var current = configuration
                ?? throw new InvalidOperationException(
                    "A commander journal session is required to refresh quests.");
            await ClearRuntimesAsync().ConfigureAwait(false);
            if (current.Enabled)
            {
                await LoadRuntimesAsync(
                        current,
                        contextTracker.CreateContext(
                            current.CommanderName,
                            current.Status),
                        warnings,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            Snapshot = CreateSnapshot();
        }
        finally
        {
            coordinatorLock.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new QuestRuntimeUpdateResult(Snapshot, warnings, 0);
    }

    public async Task<QuestRuntimeUpdateResult> ReplayEventAsync(
        string journalDirectory,
        JournalEventEnvelope journalEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        ArgumentNullException.ThrowIfNull(journalEvent);
        var warnings = new List<string>();
        await coordinatorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var current = configuration
                ?? throw new InvalidOperationException(
                    "A commander journal session is required to replay an event.");
            if (!current.Enabled)
            {
                throw new InvalidOperationException(
                    "Quests must be enabled before replaying an event.");
            }

            var resolved = await QuestJournalPayloadResolver.ResolveAsync(
                    journalDirectory,
                    journalEvent,
                    cancellationToken)
                .ConfigureAwait(false);
            AddWarning(warnings, resolved.Warning);
            await ProcessEventAsync(
                    resolved.Payload,
                    warnings,
                    cancellationToken)
                .ConfigureAwait(false);
            Snapshot = CreateSnapshot();
        }
        finally
        {
            coordinatorLock.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new QuestRuntimeUpdateResult(Snapshot, warnings, 1);
    }

    public async Task<QuestRuntimeUpdateResult> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        await coordinatorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var current = configuration
                ?? throw new InvalidOperationException(
                    "A commander journal session is required to change quest availability.");
            if (current.Enabled == enabled)
            {
                return new QuestRuntimeUpdateResult(Snapshot, warnings, 0);
            }

            current = current with { Enabled = enabled };
            configuration = current;
            await ClearRuntimesAsync().ConfigureAwait(false);
            if (enabled)
            {
                await LoadRuntimesAsync(
                        current,
                        contextTracker.CreateContext(
                            current.CommanderName,
                            current.Status),
                        warnings,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            Snapshot = CreateSnapshot();
        }
        finally
        {
            coordinatorLock.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new QuestRuntimeUpdateResult(Snapshot, warnings, 0);
    }

    public async Task ActivateQuestAsync(
        string publisher,
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisher);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await coordinatorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var current = RequireRemoteConfiguration();
            var definition = await ravenClient.ActivateQuestAsync(
                    publisher,
                    id,
                    current.RavenApiKey!,
                    cancellationToken)
                .ConfigureAwait(false);
            var progress = new RavenCommanderQuest
            {
                Publisher = definition.Publisher,
                Id = definition.Id,
                Version = definition.Version,
                Quest = definition,
                StartTime = DateTimeOffset.UtcNow,
                Chapters = definition.Chapters.Keys.Select(chapterId =>
                    new RavenQuestChapterState { Id = chapterId }).ToList(),
            };
            var warnings = new List<string>();
            await TryAddRuntimeAsync(
                    progress,
                    isDevelopment: false,
                    current,
                    contextTracker.CreateContext(
                        current.CommanderName,
                        current.Status),
                    warnings,
                    cancellationToken,
                    startFirstChapter: true)
                .ConfigureAwait(false);
            if (warnings.Count > 0)
            {
                throw new InvalidOperationException(string.Join(
                    Environment.NewLine,
                    warnings));
            }

            Snapshot = CreateSnapshot();
        }
        finally
        {
            coordinatorLock.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task PauseQuestAsync(
        RavenQuestReference reference,
        CancellationToken cancellationToken = default)
    {
        return RemoveOrPauseQuestAsync(
            reference,
            pause: true,
            cancellationToken);
    }

    public Task RemoveQuestAsync(
        RavenQuestReference reference,
        CancellationToken cancellationToken = default)
    {
        return RemoveOrPauseQuestAsync(
            reference,
            pause: false,
            cancellationToken);
    }

    public async Task ResumeQuestAsync(
        RavenQuestReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        await coordinatorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var current = RequireRemoteConfiguration();
            if (!await ravenClient.SetQuestStateAsync(
                    reference.Publisher,
                    reference.Id,
                    RavenQuestState.active,
                    current.RavenApiKey!,
                    cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"Raven quest '{reference}' was not found while resuming it.");
            }

            var warnings = new List<string>();
            await ClearRuntimesAsync().ConfigureAwait(false);
            await LoadRuntimesAsync(
                    current,
                    contextTracker.CreateContext(
                        current.CommanderName,
                        current.Status),
                    warnings,
                    cancellationToken)
                .ConfigureAwait(false);
            if (warnings.Count > 0)
            {
                log?.Invoke(string.Join(Environment.NewLine, warnings));
            }

            Snapshot = CreateSnapshot();
        }
        finally
        {
            coordinatorLock.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task MarkMessageReadAsync(
        RavenQuestReference reference,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        await InvokeRuntimeAsync(
                reference,
                (runtime, token) => runtime.MarkMessageReadAsync(
                    messageId,
                    token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ReplyToMessageAsync(
        RavenQuestReference reference,
        string messageId,
        string action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        await InvokeRuntimeAsync(
                reference,
                (runtime, token) => runtime.ReplyToMessageAsync(
                    messageId,
                    action,
                    token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await coordinatorLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            await ClearRuntimesAsync().ConfigureAwait(false);
            Snapshot = [];
        }
        finally
        {
            coordinatorLock.Release();
        }
    }

    private async Task LoadRuntimesAsync(
        QuestRuntimeConfiguration current,
        QuestCommanderContext context,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(current.RavenApiKey))
        {
            try
            {
                var remoteQuests = await ravenClient.LoadCommanderQuestsAsync(
                        RavenQuestState.active,
                        current.RavenApiKey,
                        cancellationToken)
                    .ConfigureAwait(false);
                foreach (var progress in remoteQuests)
                {
                    await TryAddRemoteRuntimeAsync(
                            progress,
                            current,
                            context,
                            warnings,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                AddWarning(
                    warnings,
                    "Active Raven quests could not be loaded: "
                        + exception.Message);
            }
        }

        var legacy = legacyStore.Load(current.FrontierId);
        foreach (var warning in legacy.Warnings)
        {
            AddWarning(warnings, warning);
        }

        AddWarning(
            warnings,
            legacy.Error is null
                ? null
                : "Legacy quest state could not be loaded: " + legacy.Error);
        if (legacy.Data?.DevelopmentQuest is { } developmentQuest)
        {
            var progress = QuestProgressMapper.FromLegacy(developmentQuest);
            if (progress.Quest is null)
            {
                AddWarning(
                    warnings,
                    $"Development quest '{progress.Reference}' has no usable definition.");
            }
            else
            {
                await TryAddRuntimeAsync(
                        progress,
                        isDevelopment: true,
                        current,
                        context,
                        warnings,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task TryAddRemoteRuntimeAsync(
        RavenCommanderQuest progress,
        QuestRuntimeConfiguration current,
        QuestCommanderContext context,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            if (progress.Quest is null)
            {
                var definition = await ravenClient.GetQuestAsync(
                        progress.Reference,
                        current.RavenApiKey,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (definition is null)
                {
                    AddWarning(
                        warnings,
                        $"Raven quest '{progress.Reference}' has no available definition.");
                    return;
                }

                progress = progress with { Quest = definition };
            }

            await TryAddRuntimeAsync(
                    progress,
                    isDevelopment: false,
                    current,
                    context,
                    warnings,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            AddWarning(
                warnings,
                $"Raven quest '{progress.Reference}' could not be initialized: "
                    + exception.Message);
        }
    }

    private async Task TryAddRuntimeAsync(
        RavenCommanderQuest progress,
        bool isDevelopment,
        QuestRuntimeConfiguration current,
        QuestCommanderContext context,
        ICollection<string> warnings,
        CancellationToken cancellationToken,
        bool startFirstChapter = false)
    {
        QuestScriptRuntime? runtime = null;
        try
        {
            runtime = new QuestScriptRuntime(
                progress,
                context,
                chapterSourceProvider: (reference, chapterId, token) =>
                    ravenClient.GetQuestChapterAsync(
                        reference,
                        chapterId,
                        current.RavenApiKey,
                        token),
                saveProgress: isDevelopment
                    ? (quest, token) => SaveDevelopmentAsync(
                        current,
                        quest,
                        token)
                    : (quest, token) => ravenClient.SaveCommanderQuestAsync(
                        quest,
                        current.RavenApiKey
                            ?? throw new InvalidOperationException(
                                "A Raven API key is required to save quest progress."),
                        token),
                transitionState: isDevelopment
                    ? (_, token) => ClearDevelopmentAsync(current, token)
                    : (state, token) => TransitionRemoteAsync(
                        current,
                        progress.Reference,
                        state,
                        token),
                log: log);
            await runtime.InitializeAsync(
                    startFirstChapter,
                    cancellationToken)
                .ConfigureAwait(false);

            var identity = QuestIdentity.From(progress.Reference);
            if (runtimes.Remove(identity, out var replaced))
            {
                await replaced.Runtime.DisposeAsync().ConfigureAwait(false);
            }

            runtimes.Add(
                identity,
                new RuntimeRegistration(runtime, isDevelopment));
            runtime = null;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            AddWarning(
                warnings,
                $"Quest '{progress.Reference}' could not be initialized: "
                    + exception.Message);
        }
        finally
        {
            if (runtime is not null)
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessEventAsync(
        System.Text.Json.JsonElement payload,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        foreach (var pair in runtimes.ToArray())
        {
            try
            {
                await pair.Value.Runtime.ProcessJournalEntryAsync(
                        payload,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (pair.Value.Runtime.TerminalState is not null)
                {
                    runtimes.Remove(pair.Key);
                    await pair.Value.Runtime.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                AddWarning(
                    warnings,
                    $"Quest '{pair.Value.Runtime.Progress.Reference}' failed to process a journal event: "
                        + exception.Message);
            }
        }
    }

    private async Task InvokeRuntimeAsync(
        RavenQuestReference reference,
        Func<QuestScriptRuntime, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        await coordinatorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var identity = QuestIdentity.From(reference);
            if (!runtimes.TryGetValue(identity, out var registration))
            {
                throw new KeyNotFoundException(
                    $"Quest '{reference}' is not active.");
            }

            await action(registration.Runtime, cancellationToken)
                .ConfigureAwait(false);
            if (registration.Runtime.TerminalState is not null)
            {
                runtimes.Remove(identity);
                await registration.Runtime.DisposeAsync().ConfigureAwait(false);
            }

            Snapshot = CreateSnapshot();
        }
        finally
        {
            coordinatorLock.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task RemoveOrPauseQuestAsync(
        RavenQuestReference reference,
        bool pause,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        await coordinatorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var identity = QuestIdentity.From(reference);
            if (!runtimes.TryGetValue(identity, out var registration))
            {
                throw new KeyNotFoundException(
                    $"Quest '{reference}' is not active.");
            }

            var current = configuration
                ?? throw new InvalidOperationException(
                    "A commander journal session is required to change quests.");
            if (registration.IsDevelopment)
            {
                if (pause)
                {
                    throw new InvalidOperationException(
                        "A development quest cannot be paused.");
                }

                await legacyStore.SaveDevelopmentQuestAsync(
                        current.FrontierId,
                        current.CommanderName,
                        null,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                var remote = RequireRemoteConfiguration();
                var changed = pause
                    ? await ravenClient.SetQuestStateAsync(
                            reference.Publisher,
                            reference.Id,
                            RavenQuestState.paused,
                            remote.RavenApiKey!,
                            cancellationToken)
                        .ConfigureAwait(false)
                    : await ravenClient.DeleteQuestAsync(
                            reference.Publisher,
                            reference.Id,
                            remote.RavenApiKey!,
                            cancellationToken)
                        .ConfigureAwait(false);
                if (!changed)
                {
                    throw new InvalidOperationException(
                        $"Raven quest '{reference}' was not found while "
                            + (pause ? "pausing it." : "removing it."));
                }
            }

            runtimes.Remove(identity);
            await registration.Runtime.DisposeAsync().ConfigureAwait(false);
            Snapshot = CreateSnapshot();
        }
        finally
        {
            coordinatorLock.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Task SaveDevelopmentAsync(
        QuestRuntimeConfiguration current,
        RavenCommanderQuest progress,
        CancellationToken cancellationToken)
    {
        return legacyStore.SaveDevelopmentQuestAsync(
            current.FrontierId,
            current.CommanderName,
            progress,
            cancellationToken);
    }

    private async Task ClearDevelopmentAsync(
        QuestRuntimeConfiguration current,
        CancellationToken cancellationToken)
    {
        await legacyStore.SaveDevelopmentQuestAsync(
                current.FrontierId,
                current.CommanderName,
                null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task TransitionRemoteAsync(
        QuestRuntimeConfiguration current,
        RavenQuestReference reference,
        RavenQuestState state,
        CancellationToken cancellationToken)
    {
        var apiKey = current.RavenApiKey
            ?? throw new InvalidOperationException(
                "A Raven API key is required to change quest state.");
        if (!await ravenClient.SetQuestStateAsync(
                reference.Publisher,
                reference.Id,
                state,
                apiKey,
                cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Raven quest '{reference}' was not found while changing its state.");
        }
    }

    private IReadOnlyList<QuestRuntimeSnapshot> CreateSnapshot()
    {
        return runtimes.Values
            .Select(registration =>
            {
                var runtime = registration.Runtime;
                return new QuestRuntimeSnapshot(
                    runtime.Progress.Reference,
                    runtime.Definition.Title,
                    runtime.Definition.Subtitle,
                    registration.IsDevelopment,
                    runtime.Progress.Paused,
                    runtime.TerminalState,
                    runtime.UnreadMessageCount,
                    runtime.Progress.Objectives.ToDictionary(
                        StringComparer.Ordinal),
                    runtime.Definition.Objectives.Keys.ToDictionary(
                        objectiveId => objectiveId,
                        objectiveId => runtime.Definition.Strings.GetValueOrDefault(
                            objectiveId)
                            ?? runtime.Definition.Objectives[objectiveId],
                        StringComparer.Ordinal),
                    runtime.Progress.Messages.Select(message =>
                        CreateMessageSnapshot(runtime, message)).ToArray(),
                    runtime.Progress.Tags.ToHashSet(StringComparer.Ordinal),
                    runtime.Progress.BodyLocations.ToDictionary(
                        StringComparer.Ordinal));
            })
            .OrderBy(snapshot => snapshot.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.Reference.Publisher, StringComparer.Ordinal)
            .ThenBy(snapshot => snapshot.Reference.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static QuestRuntimeMessageSnapshot CreateMessageSnapshot(
        QuestScriptRuntime runtime,
        RavenQuestMessage message)
    {
        var definition = runtime.Definition.Messages.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, message.Id, StringComparison.Ordinal));
        var actionIds = message.Actions
            ?? definition?.Actions?.Keys.ToArray()
            ?? [];
        var actions = actionIds.ToDictionary(
            action => action,
            action => definition?.Actions?.GetValueOrDefault(action) ?? action,
            StringComparer.Ordinal);
        return new QuestRuntimeMessageSnapshot(
            runtime.Progress.Reference,
            message.Id,
            message.Received,
            message.From ?? definition?.From ?? string.Empty,
            message.Subject ?? definition?.Subject,
            message.Body ?? definition?.Body ?? string.Empty,
            message.Chapter,
            actions,
            definition?.Tags?.ToHashSet(StringComparer.Ordinal)
                ?? new HashSet<string>(StringComparer.Ordinal),
            message.Read,
            message.Replied);
    }

    private async Task ClearRuntimesAsync()
    {
        foreach (var registration in runtimes.Values)
        {
            await registration.Runtime.DisposeAsync().ConfigureAwait(false);
        }

        runtimes.Clear();
        Snapshot = [];
    }

    private static void ValidateConfiguration(
        QuestRuntimeConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.FrontierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.CommanderName);
    }

    private static bool HasSameIdentity(
        QuestRuntimeConfiguration? prior,
        QuestRuntimeConfiguration current)
    {
        return prior is not null
            && prior.Enabled == current.Enabled
            && string.Equals(
                prior.FrontierId,
                current.FrontierId,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                prior.CommanderName,
                current.CommanderName,
                StringComparison.Ordinal)
            && string.Equals(
                prior.RavenApiKey,
                current.RavenApiKey,
                StringComparison.Ordinal);
    }

    private QuestRuntimeConfiguration RequireRemoteConfiguration()
    {
        var current = configuration
            ?? throw new InvalidOperationException(
                "A commander journal session is required to use Raven quests.");
        if (!current.Enabled)
        {
            throw new InvalidOperationException("Quests are disabled.");
        }

        if (string.IsNullOrWhiteSpace(current.RavenApiKey))
        {
            throw new InvalidOperationException(
                "A Raven Colonial API key is required for commander quests.");
        }

        return current;
    }

    private static bool IsRecoverable(Exception exception)
    {
        return exception is not OperationCanceledException;
    }

    private void AddWarning(ICollection<string> warnings, string? warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
        {
            return;
        }

        warnings.Add(warning);
        log?.Invoke(warning);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private sealed record RuntimeRegistration(
        QuestScriptRuntime Runtime,
        bool IsDevelopment);

    private readonly record struct QuestIdentity(string Publisher, string Id)
    {
        public static QuestIdentity From(RavenQuestReference reference)
        {
            return new QuestIdentity(reference.Publisher, reference.Id);
        }
    }
}

public sealed record QuestRuntimeConfiguration(
    bool Enabled,
    string FrontierId,
    string CommanderName,
    string? RavenApiKey,
    EliteStatus? Status);

public sealed record QuestRuntimeSnapshot(
    RavenQuestReference Reference,
    string Title,
    string? Subtitle,
    bool IsDevelopment,
    bool IsPaused,
    RavenQuestState? TerminalState,
    int UnreadMessageCount,
    IReadOnlyDictionary<string, string> Objectives,
    IReadOnlyDictionary<string, string> ObjectiveLabels,
    IReadOnlyList<QuestRuntimeMessageSnapshot> Messages,
    IReadOnlySet<string> Tags,
    IReadOnlyDictionary<string, string> BodyLocations);

public sealed record QuestRuntimeMessageSnapshot(
    RavenQuestReference Quest,
    string Id,
    DateTimeOffset Received,
    string From,
    string? Subject,
    string Body,
    string? Chapter,
    IReadOnlyDictionary<string, string> Actions,
    IReadOnlySet<string> Tags,
    bool Read,
    string? Replied);

public sealed record QuestRuntimeUpdateResult(
    IReadOnlyList<QuestRuntimeSnapshot> Quests,
    IReadOnlyList<string> Warnings,
    int ProcessedEventCount);
