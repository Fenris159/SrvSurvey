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
        CancellationToken cancellationToken)
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
                    startFirstChapter: false,
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
                    runtime.Progress.Messages.ToArray(),
                    runtime.Progress.Tags.ToHashSet(StringComparer.Ordinal));
            })
            .OrderBy(snapshot => snapshot.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.Reference.Publisher, StringComparer.Ordinal)
            .ThenBy(snapshot => snapshot.Reference.Id, StringComparer.Ordinal)
            .ToArray();
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
    IReadOnlyList<RavenQuestMessage> Messages,
    IReadOnlySet<string> Tags);

public sealed record QuestRuntimeUpdateResult(
    IReadOnlyList<QuestRuntimeSnapshot> Quests,
    IReadOnlyList<string> Warnings,
    int ProcessedEventCount);
