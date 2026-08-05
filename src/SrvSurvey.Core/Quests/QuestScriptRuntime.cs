using System.Globalization;
using System.Text.Json;
using Lua;
using Lua.Runtime;
using Lua.Standard;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Quests;

public sealed class QuestScriptRuntime : IAsyncDisposable
{
    private readonly Func<RavenQuestReference, string, CancellationToken,
        Task<string?>>? chapterSourceProvider;
    private readonly Func<RavenCommanderQuest, CancellationToken, Task>?
        saveProgress;
    private readonly Func<RavenQuestState, CancellationToken, Task>?
        transitionState;
    private readonly Action<string>? log;
    private readonly SemaphoreSlim runtimeLock = new(1, 1);
    private readonly Dictionary<string, QuestChapterRuntime> loadedChapters =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> chaptersToStart = new(StringComparer.Ordinal);
    private readonly HashSet<string> chaptersToStop = new(StringComparer.Ordinal);
    private RavenQuestState? pendingTerminalState;
    private bool terminalTransitionSent;
    private bool dirty;
    private bool disposed;

    public QuestScriptRuntime(
        RavenCommanderQuest progress,
        QuestCommanderContext? commanderContext = null,
        Func<RavenQuestReference, string, CancellationToken, Task<string?>>?
            chapterSourceProvider = null,
        Func<RavenCommanderQuest, CancellationToken, Task>? saveProgress = null,
        Func<RavenQuestState, CancellationToken, Task>? transitionState = null,
        Action<string>? log = null)
    {
        Progress = progress ?? throw new ArgumentNullException(nameof(progress));
        Definition = progress.Quest
            ?? throw new ArgumentException(
                "Quest progress must include a hydrated definition.",
                nameof(progress));
        if (!string.Equals(
                progress.Publisher,
                Definition.Publisher,
                StringComparison.Ordinal)
            || !string.Equals(progress.Id, Definition.Id, StringComparison.Ordinal)
            || progress.Version.CompareTo(Definition.Version) != 0)
        {
            throw new ArgumentException(
                "Quest progress and definition identities do not match.",
                nameof(progress));
        }

        CommanderContext = commanderContext ?? QuestCommanderContext.Empty;
        this.chapterSourceProvider = chapterSourceProvider;
        this.saveProgress = saveProgress;
        this.transitionState = transitionState;
        this.log = log;
    }

    public RavenCommanderQuest Progress { get; private set; }

    public RavenQuestDefinition Definition { get; }

    public QuestCommanderContext CommanderContext { get; set; }

    public RavenQuestState? TerminalState { get; private set; }

    public string? InvokingChapterId { get; private set; }

    public bool IsDirty => dirty;

    public int UnreadMessageCount => Progress.Messages.Count(message => !message.Read);

    public async Task InitializeAsync(
        bool startFirstChapter = false,
        CancellationToken cancellationToken = default)
    {
        await runtimeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            SeedPriorJournalEvents();
            EnsureChapterStates();
            foreach (var chapter in Progress.Chapters.Where(IsActive).ToArray())
            {
                await LoadChapterAsync(chapter.Id, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (startFirstChapter
                && !Progress.Chapters.Any(IsActive)
                && !string.IsNullOrWhiteSpace(Definition.FirstChapter))
            {
                Progress = Progress with
                {
                    StartTime = Progress.StartTime ?? DateTimeOffset.UtcNow,
                    EndTime = null,
                };
                RequestStartChapter(Definition.FirstChapter);
                dirty = true;
                await ApplyPendingChapterChangesAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            await SaveIfDirtyAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            runtimeLock.Release();
        }
    }

    public async Task<bool> ProcessJournalEntryAsync(
        JsonElement entry,
        CancellationToken cancellationToken = default)
    {
        if (entry.ValueKind != JsonValueKind.Object
            || !entry.TryGetProperty("event", out var eventNode)
            || eventNode.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(eventNode.GetString()))
        {
            throw new ArgumentException(
                "A quest journal entry must be an object with an event name.",
                nameof(entry));
        }

        await runtimeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var eventName = eventNode.GetString()!;
            var eventTable = QuestLuaConverter.ToLua(entry).Read<LuaTable>();
            var shouldSave = false;
            var active = Progress.Chapters
                .Where(IsActive)
                .Select(chapter => chapter.Id)
                .ToArray();
            foreach (var chapterId in active)
            {
                var chapter = await LoadChapterAsync(
                        chapterId,
                        cancellationToken)
                    .ConfigureAwait(false);
                shouldSave |= await chapter.ProcessJournalEntryAsync(
                        eventName,
                        eventTable,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (eventName == "ReceiveText"
                    && TryParseHumanoidEmote(
                        entry,
                        out var actor,
                        out var action,
                        out var target))
                {
                    shouldSave |= await chapter.InvokeIfPresentAsync(
                            "onEmote",
                            [actor, action, target],
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            await ApplyPendingChapterChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            if (Progress.KeptJournalEvents.ContainsKey(eventName)
                || eventName is "Docked" or "FSDJump")
            {
                Progress.KeptJournalEvents[eventName] = entry.Clone();
                dirty = true;
            }

            dirty |= shouldSave;
            await SaveIfDirtyAsync(cancellationToken).ConfigureAwait(false);
            return shouldSave;
        }
        finally
        {
            runtimeLock.Release();
        }
    }

    public async Task MarkMessageReadAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        await runtimeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var index = Progress.Messages.FindIndex(message =>
                string.Equals(message.Id, messageId, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new KeyNotFoundException(
                    $"Quest message '{messageId}' was not found.");
            }

            var message = Progress.Messages[index];
            if (message.Read)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(message.Chapter))
            {
                var chapter = await RequireActiveChapterAsync(
                        message.Chapter,
                        cancellationToken)
                    .ConfigureAwait(false);
                dirty |= await chapter.InvokeIfPresentAsync(
                        "onMsgRead",
                        [new LuaValue(messageId)],
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            Progress.Messages[index] = message with { Read = true };
            dirty = true;
            await ApplyPendingChapterChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            await SaveIfDirtyAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            runtimeLock.Release();
        }
    }

    public async Task ReplyToMessageAsync(
        string messageId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        await runtimeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var index = Progress.Messages.FindIndex(message =>
                string.Equals(message.Id, messageId, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new KeyNotFoundException(
                    $"Quest message '{messageId}' was not found.");
            }

            var message = Progress.Messages[index];
            if (string.IsNullOrWhiteSpace(message.Chapter))
            {
                throw new InvalidOperationException(
                    $"Quest message '{messageId}' has no chapter action context.");
            }

            var chapter = await RequireActiveChapterAsync(
                    message.Chapter,
                    cancellationToken)
                .ConfigureAwait(false);
            dirty |= await chapter.InvokeRequiredAsync(
                    "onMsgAction",
                    [new LuaValue(actionId), new LuaValue(messageId)],
                    cancellationToken)
                .ConfigureAwait(false);
            Progress.Messages[index] = message with { Replied = actionId };
            dirty = true;
            await ApplyPendingChapterChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            await SaveIfDirtyAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            runtimeLock.Release();
        }
    }

    public async Task<JsonElement> RunDebugAsync(
        string chapterId,
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chapterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        await runtimeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var chapter = await RequireActiveChapterAsync(
                    chapterId,
                    cancellationToken)
                .ConfigureAwait(false);
            var result = await chapter.RunDebugAsync(code, cancellationToken)
                .ConfigureAwait(false);
            await ApplyPendingChapterChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            await SaveIfDirtyAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            runtimeLock.Release();
        }
    }

    public async Task SetChapterActiveAsync(
        string chapterId,
        bool active,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chapterId);
        await runtimeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var chapter = RequireChapterState(chapterId);
            if (IsActive(chapter) == active)
            {
                return;
            }

            if (active)
            {
                RequestStartChapter(chapterId);
            }
            else
            {
                RequestStopChapter(chapterId);
            }

            await ApplyPendingChapterChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            await SaveIfDirtyAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            runtimeLock.Release();
        }
    }

    public async Task PrepareDevelopmentChaptersAsync(
        CancellationToken cancellationToken = default)
    {
        await runtimeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            foreach (var chapterId in Definition.Chapters.Keys)
            {
                var chapter = await LoadChapterAsync(chapterId, cancellationToken)
                    .ConfigureAwait(false);
                chapter.PullVariables();
            }
        }
        finally
        {
            runtimeLock.Release();
        }
    }

    public async Task<QuestDevelopmentStateSnapshot> GetDevelopmentStateAsync(
        CancellationToken cancellationToken = default)
    {
        await runtimeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            foreach (var chapterId in Definition.Chapters.Keys)
            {
                var chapter = await LoadChapterAsync(chapterId, cancellationToken)
                    .ConfigureAwait(false);
                chapter.PullVariables();
            }

            return new QuestDevelopmentStateSnapshot(
                Progress.Reference,
                Definition.Title,
                Progress.Objectives.ToDictionary(StringComparer.Ordinal),
                Progress.Chapters.Select(chapter =>
                    new QuestDevelopmentChapterSnapshot(
                        chapter.Id,
                        IsActive(chapter),
                        CloneJsonMap(chapter.Variables)))
                    .ToArray(),
                Progress.Messages.Select(CloneMessage).ToArray());
        }
        finally
        {
            runtimeLock.Release();
        }
    }

    public async Task UpdateDevelopmentObjectivesAsync(
        IReadOnlyDictionary<string, string> objectives,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objectives);
        foreach (var pair in objectives)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            _ = ParseObjective(pair.Value);
        }

        await runtimeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            Progress.Objectives.Clear();
            foreach (var pair in objectives)
            {
                Progress.Objectives[pair.Key] = pair.Value;
            }

            dirty = true;
            await SaveIfDirtyAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            runtimeLock.Release();
        }
    }

    public async Task UpdateDevelopmentChapterVariablesAsync(
        string chapterId,
        IReadOnlyDictionary<string, JsonElement> variables,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chapterId);
        ArgumentNullException.ThrowIfNull(variables);
        await runtimeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var runtime = await LoadChapterAsync(chapterId, cancellationToken)
                .ConfigureAwait(false);
            runtime.PullVariables();
            var state = RequireChapterState(chapterId);
            var unknown = variables.Keys.FirstOrDefault(key =>
                !state.Variables.ContainsKey(key));
            if (unknown is not null)
            {
                throw new InvalidDataException(
                    $"Cannot add new chapter variable '{unknown}'.");
            }

            state.Variables.Clear();
            foreach (var pair in variables)
            {
                state.Variables[pair.Key] = pair.Value.Clone();
            }

            runtime.PushVariables();
            dirty = true;
            await SaveIfDirtyAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            runtimeLock.Release();
        }
    }

    public async Task UpdateDevelopmentMessagesAsync(
        IReadOnlyList<RavenQuestMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var duplicate = messages
            .Where(message => !string.IsNullOrWhiteSpace(message.Id))
            .GroupBy(message => message.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (messages.Any(message => string.IsNullOrWhiteSpace(message.Id)))
        {
            throw new InvalidDataException(
                "Every delivered quest message must have an ID.");
        }

        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"Delivered quest message ID '{duplicate.Key}' is duplicated.");
        }

        await runtimeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            Progress.Messages.Clear();
            Progress.Messages.AddRange(messages.Select(CloneMessage));
            dirty = true;
            await SaveIfDirtyAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            runtimeLock.Release();
        }
    }

    public bool IsTagged(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return Progress.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await runtimeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }

            foreach (var chapter in loadedChapters.Values)
            {
                chapter.Dispose();
            }

            loadedChapters.Clear();
            disposed = true;
        }
        finally
        {
            runtimeLock.Release();
            runtimeLock.Dispose();
        }
    }

    internal void RequestTerminalState(RavenQuestState state)
    {
        if (state is not RavenQuestState.complete and not RavenQuestState.failed)
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        pendingTerminalState = state;
        dirty = true;
    }

    internal void RequestStartChapter(string id)
    {
        RequireDefinedChapter(id);
        chaptersToStart.Add(id);
        dirty = true;
    }

    internal void RequestNextChapter(string id)
    {
        if (InvokingChapterId is null)
        {
            throw new InvalidOperationException(
                "A quest can only advance chapters while invoking a chapter script.");
        }

        RequestStartChapter(id);
        chaptersToStop.Add(InvokingChapterId);
        dirty = true;
    }

    internal void RequestStopChapter(string id)
    {
        RequireChapterState(id);
        chaptersToStop.Add(id);
        dirty = true;
    }

    internal void SetQuestVariable(string name, LuaValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (value.Type == LuaValueType.Nil)
        {
            dirty |= Progress.Variables.Remove(name);
            return;
        }

        var converted = QuestLuaConverter.ToJson(value);
        if (!Progress.Variables.TryGetValue(name, out var prior)
            || !JsonElement.DeepEquals(prior, converted))
        {
            Progress.Variables[name] = converted;
            dirty = true;
        }
    }

    internal LuaValue GetQuestVariable(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Progress.Variables.TryGetValue(name, out var value)
            ? QuestLuaConverter.ToLua(value)
            : LuaValue.Nil;
    }

    internal void SendMessage(
        string? id,
        string? from,
        string? subject,
        string? body)
    {
        var declared = string.IsNullOrWhiteSpace(id)
            ? null
            : Definition.Messages.FirstOrDefault(message =>
                string.Equals(message.Id, id, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(id) && declared is null && body is null)
        {
            throw new KeyNotFoundException(
                $"Quest message definition '{id}' was not found.");
        }

        if (declared?.Actions?.Count > 0 && InvokingChapterId is null)
        {
            throw new InvalidOperationException(
                $"Quest message '{declared.Id}' requires a chapter action context.");
        }

        var resolvedFrom = from ?? declared?.From;
        var resolvedSubject = subject ?? declared?.Subject;
        var resolvedBody = body ?? declared?.Body;
        var message = new RavenQuestMessage
        {
            Id = declared?.Id
                ?? id
                ?? DateTimeOffset.UtcNow.ToString(
                    "yyyyMMddhhmmss",
                    CultureInfo.InvariantCulture),
            Received = DateTimeOffset.UtcNow,
            From = resolvedFrom == declared?.From ? null : resolvedFrom,
            Subject = resolvedSubject == declared?.Subject
                ? null
                : resolvedSubject,
            Body = resolvedBody == declared?.Body ? null : resolvedBody,
            Chapter = InvokingChapterId,
            Actions = declared?.Actions?.Keys.ToArray(),
        };
        Progress.Messages.RemoveAll(existing =>
            string.Equals(existing.Id, message.Id, StringComparison.Ordinal));
        Progress.Messages.Add(message);
        if (declared?.Tags is not null)
        {
            foreach (var tag in declared.Tags)
            {
                dirty |= Progress.Tags.Add(tag);
            }
        }

        dirty = true;
    }

    internal bool DeleteMessage(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var removed = Progress.Messages.RemoveAll(message =>
            string.Equals(message.Id, id, StringComparison.Ordinal)) > 0;
        dirty |= removed;
        return removed;
    }

    internal void AddTags(LuaValue value)
    {
        foreach (var tag in ReadStringValues(value).Where(tag =>
            !string.IsNullOrWhiteSpace(tag)))
        {
            dirty |= Progress.Tags.Add(tag);
        }
    }

    internal void RemoveTags(LuaValue value)
    {
        foreach (var tag in ReadStringValues(value))
        {
            dirty |= Progress.Tags.Remove(tag);
        }
    }

    internal void SetTags(LuaValue value)
    {
        var replacement = ReadStringValues(value)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .ToHashSet(StringComparer.Ordinal);
        if (!Progress.Tags.SetEquals(replacement))
        {
            Progress.Tags.Clear();
            Progress.Tags.UnionWith(replacement);
            dirty = true;
        }
    }

    internal void ClearTags()
    {
        if (Progress.Tags.Count > 0)
        {
            Progress.Tags.Clear();
            dirty = true;
        }
    }

    internal void TrackLocation(
        string name,
        double latitude,
        double longitude,
        float size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _ = new SurfaceCoordinate(latitude, longitude);
        if (!float.IsFinite(size) || size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        var value = string.Join(
            ',',
            latitude.ToString("R", CultureInfo.InvariantCulture),
            longitude.ToString("R", CultureInfo.InvariantCulture),
            size.ToString("R", CultureInfo.InvariantCulture));
        if (!Progress.BodyLocations.TryGetValue(name, out var prior)
            || !string.Equals(prior, value, StringComparison.Ordinal))
        {
            Progress.BodyLocations[name] = value;
            dirty = true;
        }
    }

    internal void ClearLocation(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        dirty |= Progress.BodyLocations.Remove(name);
    }

    internal void ClearAllLocations()
    {
        if (Progress.BodyLocations.Count > 0)
        {
            Progress.BodyLocations.Clear();
            dirty = true;
        }
    }

    internal void KeepLast(LuaValue value)
    {
        var names = ReadStringValues(value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);
        names.Add("Docked");
        names.Add("FSDJump");
        var existingNames = Progress.KeptJournalEvents.Keys.ToHashSet(
            StringComparer.Ordinal);
        if (existingNames.SetEquals(names))
        {
            return;
        }

        var replacement = new Dictionary<string, JsonElement>(
            StringComparer.Ordinal);
        foreach (var name in names)
        {
            replacement[name] = Progress.KeptJournalEvents.TryGetValue(
                name,
                out var prior)
                ? prior.Clone()
                : JsonSerializer.SerializeToElement<object?>(null);
        }

        Progress.KeptJournalEvents.Clear();
        foreach (var pair in replacement)
        {
            Progress.KeptJournalEvents[pair.Key] = pair.Value;
        }

        dirty = true;
    }

    internal LuaValue GetLast(string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        return Progress.KeptJournalEvents.TryGetValue(eventName, out var value)
            ? QuestLuaConverter.ToLua(value)
            : LuaValue.Nil;
    }

    internal void SetRoute(string id, double width, LuaTable coordinates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!double.IsFinite(width) || width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        var waypoints = coordinates
            .Select(pair => pair.Value.ToString()
                .Split(',', StringSplitOptions.TrimEntries)
                .Select(value => double.Parse(
                    value,
                    CultureInfo.InvariantCulture))
                .ToArray())
            .ToList();
        Progress.Routes.RemoveAll(route =>
            string.Equals(route.Id, id, StringComparison.Ordinal));
        Progress.Routes.Add(new RavenQuestRoute
        {
            Id = id,
            Width = width,
            Waypoints = waypoints,
        });
        dirty = true;
    }

    internal void ClearRoute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        dirty |= Progress.Routes.RemoveAll(route =>
            string.Equals(route.Id, id, StringComparison.Ordinal)) > 0;
    }

    internal void SetObjectiveState(
        LuaValue value,
        LegacyQuestObjectiveState? state,
        int current = -1,
        int total = -1)
    {
        foreach (var id in ReadStringValues(value))
        {
            if (!Definition.Objectives.ContainsKey(id))
            {
                throw new KeyNotFoundException(
                    $"Unknown objective ID '{id}'.");
            }

            var objective = ParseObjective(
                Progress.Objectives.GetValueOrDefault(id));
            var updated = new LegacyQuestObjective(
                state ?? objective.State,
                current >= 0 ? current : objective.Current,
                total >= 0 ? total : objective.Total);
            var formatted = FormatObjective(updated);
            if (!Progress.Objectives.TryGetValue(id, out var prior)
                || !string.Equals(prior, formatted, StringComparison.Ordinal))
            {
                Progress.Objectives[id] = formatted;
                dirty = true;
            }
        }
    }

    internal void RemoveObjectives(LuaValue value)
    {
        foreach (var id in ReadStringValues(value))
        {
            dirty |= Progress.Objectives.Remove(id);
        }
    }

    internal bool CheckObjectives(LuaValue value, string state)
    {
        if (!Enum.TryParse<LegacyQuestObjectiveState>(
                state,
                ignoreCase: false,
                out var expected))
        {
            throw new ArgumentException(
                $"Unknown objective state '{state}'.",
                nameof(state));
        }

        return ReadStringValues(value).All(id =>
        {
            if (!Definition.Objectives.ContainsKey(id))
            {
                throw new KeyNotFoundException(
                    $"Unknown objective ID '{id}'.");
            }

            return ParseObjective(
                Progress.Objectives.GetValueOrDefault(id)).State == expected;
        });
    }

    internal int GetObjectiveCurrent(string id)
    {
        RequireObjective(id);
        return ParseObjective(Progress.Objectives.GetValueOrDefault(id)).Current;
    }

    internal int GetObjectiveTotal(string id)
    {
        RequireObjective(id);
        return ParseObjective(Progress.Objectives.GetValueOrDefault(id)).Total;
    }

    internal bool IsObjectiveActive(string id)
    {
        RequireObjective(id);
        return ParseObjective(Progress.Objectives.GetValueOrDefault(id)).State
            == LegacyQuestObjectiveState.visible;
    }

    internal void SetInvokingChapter(string? chapterId)
    {
        InvokingChapterId = chapterId;
    }

    internal void MarkDirty()
    {
        dirty = true;
    }

    internal void WriteLog(string message)
    {
        log?.Invoke($"[{Progress.Id}/{InvokingChapterId}] {message}");
    }

    private async Task<QuestChapterRuntime> LoadChapterAsync(
        string chapterId,
        CancellationToken cancellationToken)
    {
        if (loadedChapters.TryGetValue(chapterId, out var loaded))
        {
            return loaded;
        }

        _ = RequireChapterState(chapterId);
        string? source = null;
        if (Definition.Chapters.TryGetValue(chapterId, out var embedded)
            && !string.IsNullOrWhiteSpace(embedded))
        {
            source = embedded;
        }
        else if (chapterSourceProvider is not null)
        {
            source = await chapterSourceProvider(
                    Progress.Reference,
                    chapterId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidDataException(
                $"Quest chapter '{chapterId}' has no script source.");
        }

        loaded = new QuestChapterRuntime(this, chapterId, source);
        await loaded.LoadAsync(cancellationToken).ConfigureAwait(false);
        loadedChapters[chapterId] = loaded;
        return loaded;
    }

    private async Task<QuestChapterRuntime> RequireActiveChapterAsync(
        string chapterId,
        CancellationToken cancellationToken)
    {
        var state = RequireChapterState(chapterId);
        if (!IsActive(state))
        {
            throw new InvalidOperationException(
                $"Quest chapter '{chapterId}' is not active.");
        }

        return await LoadChapterAsync(chapterId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ApplyPendingChapterChangesAsync(
        CancellationToken cancellationToken)
    {
        var iterations = 0;
        while (chaptersToStop.Count > 0 || chaptersToStart.Count > 0)
        {
            if (++iterations > 100)
            {
                throw new InvalidOperationException(
                    "Quest chapter transitions exceeded the safety limit.");
            }

            var stopping = chaptersToStop.ToArray();
            chaptersToStop.Clear();
            foreach (var chapterId in stopping)
            {
                StopChapter(chapterId);
            }

            var starting = chaptersToStart.ToArray();
            chaptersToStart.Clear();
            foreach (var chapterId in starting)
            {
                await StartChapterAsync(chapterId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (pendingTerminalState is { } terminal)
        {
            TerminalState = terminal;
            Progress = Progress with { EndTime = DateTimeOffset.UtcNow };
            pendingTerminalState = null;
            dirty = true;
        }
    }

    private async Task StartChapterAsync(
        string chapterId,
        CancellationToken cancellationToken)
    {
        var state = RequireChapterState(chapterId);
        if (IsActive(state))
        {
            return;
        }

        ReplaceChapterState(state with
        {
            StartTime = DateTimeOffset.UtcNow,
            EndTime = null,
        });
        var runtime = await LoadChapterAsync(chapterId, cancellationToken)
            .ConfigureAwait(false);
        dirty |= await runtime.InvokeIfPresentAsync(
                "onStart",
                [],
                cancellationToken)
            .ConfigureAwait(false);
        dirty = true;
    }

    private void StopChapter(string chapterId)
    {
        var state = RequireChapterState(chapterId);
        if (!IsActive(state))
        {
            return;
        }

        if (loadedChapters.Remove(chapterId, out var runtime))
        {
            runtime.PullVariables();
            runtime.Dispose();
        }

        ReplaceChapterState(state with { EndTime = DateTimeOffset.UtcNow });
        dirty = true;
    }

    private async Task SaveIfDirtyAsync(CancellationToken cancellationToken)
    {
        var transitionPending = TerminalState is not null
            && !terminalTransitionSent
            && transitionState is not null;
        if (!dirty && !transitionPending)
        {
            return;
        }

        if (dirty)
        {
            foreach (var runtime in loadedChapters.Values)
            {
                runtime.PullVariables();
            }

            if (saveProgress is not null)
            {
                await saveProgress(Progress, cancellationToken).ConfigureAwait(false);
            }

            dirty = false;
        }

        if (TerminalState is { } terminal
            && !terminalTransitionSent
            && transitionState is not null)
        {
            await transitionState(terminal, cancellationToken)
                .ConfigureAwait(false);
            terminalTransitionSent = true;
        }
    }

    private void EnsureChapterStates()
    {
        var ids = Definition.Chapters.Keys.ToHashSet(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(Definition.FirstChapter))
        {
            ids.Add(Definition.FirstChapter);
        }

        foreach (var id in ids.Where(id =>
            Progress.Chapters.All(chapter =>
                !string.Equals(chapter.Id, id, StringComparison.Ordinal))))
        {
            Progress.Chapters.Add(new RavenQuestChapterState { Id = id });
        }
    }

    private void SeedPriorJournalEvents()
    {
        if (CommanderContext.PriorJournalEvents is null)
        {
            return;
        }

        foreach (var eventName in new[] { "Docked", "FSDJump" })
        {
            if (!Progress.KeptJournalEvents.ContainsKey(eventName)
                && CommanderContext.PriorJournalEvents.TryGetValue(
                    eventName,
                    out var entry))
            {
                Progress.KeptJournalEvents[eventName] = entry.Clone();
                dirty = true;
            }
        }
    }

    private void RequireDefinedChapter(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!Definition.Chapters.ContainsKey(id)
            && Progress.Chapters.All(chapter =>
                !string.Equals(chapter.Id, id, StringComparison.Ordinal)))
        {
            throw new KeyNotFoundException(
                $"Quest chapter '{id}' is not defined.");
        }
    }

    private RavenQuestChapterState RequireChapterState(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Progress.Chapters.FirstOrDefault(chapter =>
            string.Equals(chapter.Id, id, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException(
                $"Quest chapter '{id}' was not found.");
    }

    private void ReplaceChapterState(RavenQuestChapterState replacement)
    {
        var index = Progress.Chapters.FindIndex(chapter =>
            string.Equals(chapter.Id, replacement.Id, StringComparison.Ordinal));
        if (index < 0)
        {
            Progress.Chapters.Add(replacement);
        }
        else
        {
            Progress.Chapters[index] = replacement;
        }
    }

    private void RequireObjective(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!Definition.Objectives.ContainsKey(id))
        {
            throw new KeyNotFoundException($"Unknown objective ID '{id}'.");
        }
    }

    private static bool IsActive(RavenQuestChapterState chapter)
    {
        return chapter.StartTime is not null && chapter.EndTime is null;
    }

    private static RavenQuestMessage CloneMessage(RavenQuestMessage message)
    {
        return message with
        {
            Actions = message.Actions?.ToArray(),
            ExtensionData = CloneJsonMap(message.ExtensionData),
        };
    }

    private static Dictionary<string, JsonElement> CloneJsonMap(
        IReadOnlyDictionary<string, JsonElement> source)
    {
        return source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.Ordinal);
    }

    private static bool TryParseHumanoidEmote(
        JsonElement entry,
        out string actor,
        out string action,
        out string target)
    {
        actor = string.Empty;
        action = string.Empty;
        target = string.Empty;
        if (!entry.TryGetProperty("Message", out var messageNode)
            || messageNode.ValueKind != JsonValueKind.String
            || messageNode.GetString() is not { } message
            || !message.StartsWith("$HumanoidEmote_", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = message.Split(
            [':', ';'],
            StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4
            || !TryReadAssignment(parts[2], out actor)
            || !TryReadAssignment(parts[3], out var rawAction))
        {
            return false;
        }

        var actionStart = rawAction.IndexOf('_');
        var actionEnd = actionStart < 0
            ? -1
            : rawAction.IndexOf('_', actionStart + 1);
        if (actionStart < 0 || actionEnd <= actionStart + 1)
        {
            return false;
        }

        action = rawAction[(actionStart + 1)..actionEnd];
        if (parts.Length >= 5
            && !TryReadAssignment(parts[^1], out target))
        {
            return false;
        }

        return true;
    }

    private static bool TryReadAssignment(string value, out string result)
    {
        var separator = value.IndexOf('=');
        if (separator < 0 || separator == value.Length - 1)
        {
            result = string.Empty;
            return false;
        }

        result = value[(separator + 1)..];
        return true;
    }

    private static IReadOnlyList<string> ReadStringValues(LuaValue value)
    {
        return value.Type == LuaValueType.Table
            ? value.Read<LuaTable>()
                .Select(pair => pair.Value.ToString())
                .ToArray()
            : [value.ToString()];
    }

    private static LegacyQuestObjective ParseObjective(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new LegacyQuestObjective(
                LegacyQuestObjectiveState.hidden,
                0,
                0);
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is not 1 and not 3
            || !Enum.TryParse<LegacyQuestObjectiveState>(
                parts[0],
                ignoreCase: false,
                out var state))
        {
            throw new InvalidDataException(
                $"Quest objective state '{value}' is invalid.");
        }

        if (parts.Length == 1)
        {
            return new LegacyQuestObjective(state, 0, 0);
        }

        if (!int.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var current)
            || !int.TryParse(
                parts[2],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var total))
        {
            throw new InvalidDataException(
                $"Quest objective state '{value}' is invalid.");
        }

        return new LegacyQuestObjective(state, current, total);
    }

    private static string FormatObjective(LegacyQuestObjective objective)
    {
        return objective.Current == 0 && objective.Total == 0
            ? objective.State.ToString()
            : string.Join(
                ',',
                objective.State,
                objective.Current.ToString(CultureInfo.InvariantCulture),
                objective.Total.ToString(CultureInfo.InvariantCulture));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private sealed class QuestChapterRuntime : IDisposable
    {
        private readonly QuestScriptRuntime owner;
        private readonly string chapterId;
        private readonly string source;
        private LuaState? state;
        private HashSet<string> variableNames = new(StringComparer.Ordinal);

        public QuestChapterRuntime(
            QuestScriptRuntime owner,
            string chapterId,
            string source)
        {
            this.owner = owner;
            this.chapterId = chapterId;
            this.source = source;
        }

        public async Task LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state = LuaState.Create();
            state.Environment["chapterId"] = chapterId;
            state.OpenStandardLibraries();
            state.Environment["print"] = new LuaFunction(
                "quest_print",
                (context, _) =>
                {
                    owner.WriteLog(string.Join(
                        ", ",
                        context.Arguments.ToArray().Select(value => value.ToString())));
                    return new(0);
                });
            await state.DoStringAsync(
                """
                function arrlen(tt)
                    local count = 0
                    for _ in pairs(tt) do
                        count = count + 1
                    end
                    return count
                end
                """,
                cancellationToken: cancellationToken);
            state.Environment["quest"] = new QuestScriptQuestApi(owner);
            state.Environment["objective"] = new QuestScriptObjectiveApi(owner);
            state.Environment["chapter"] = new QuestScriptChapterApi(owner, chapterId);
            state.Environment["cmdr"] = new QuestScriptCommanderApi(owner);

            var priorNames = state.Environment
                .Where(pair => pair.Key.Type == LuaValueType.String)
                .Select(pair => pair.Key.Read<string>())
                .ToHashSet(StringComparer.Ordinal);
            try
            {
                var closure = state.Load(source, $"@{chapterId}.lua");
                await state.RunAsync(closure, cancellationToken);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException)
            {
                throw CreateScriptException("load", exception);
            }

            variableNames = state.GetCurrentEnvironment()
                .Where(pair => pair.Key.Type == LuaValueType.String
                    && pair.Value.Type != LuaValueType.Function)
                .Select(pair => pair.Key.Read<string>())
                .Where(name => !priorNames.Contains(name))
                .ToHashSet(StringComparer.Ordinal);
            PushVariables();
            PullVariables();
        }

        public Task<bool> ProcessJournalEntryAsync(
            string eventName,
            LuaTable entry,
            CancellationToken cancellationToken)
        {
            return InvokeIfPresentAsync(
                "on_" + eventName,
                [entry],
                cancellationToken);
        }

        public Task<bool> InvokeIfPresentAsync(
            string functionName,
            LuaValue[] arguments,
            CancellationToken cancellationToken)
        {
            var current = RequireState();
            return current.Environment[functionName].Type == LuaValueType.Function
                ? InvokeRequiredAsync(functionName, arguments, cancellationToken)
                : Task.FromResult(false);
        }

        public async Task<bool> InvokeRequiredAsync(
            string functionName,
            LuaValue[] arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = RequireState();
            if (!current.Environment[functionName].TryRead<LuaFunction>(out var function))
            {
                throw new InvalidOperationException(
                    $"Quest chapter '{chapterId}' has no function '{functionName}'.");
            }

            owner.SetInvokingChapter(chapterId);
            try
            {
                var result = await current.CallAsync(
                    function,
                    arguments,
                    cancellationToken);
                return result.Length > 0
                    && result[0] != LuaValue.Nil
                    && !string.Equals(
                        result[0].ToString(),
                        "false",
                        StringComparison.Ordinal);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException)
            {
                throw CreateScriptException(functionName, exception);
            }
            finally
            {
                owner.SetInvokingChapter(null);
            }
        }

        public async Task<JsonElement> RunDebugAsync(
            string code,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await RequireState().DoStringAsync(
                    code,
                    cancellationToken: cancellationToken);
                return result.Length == 0
                    ? JsonSerializer.SerializeToElement<object?>(null)
                    : QuestLuaConverter.ToJson(result[0]);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException)
            {
                throw CreateScriptException("debug", exception);
            }
        }

        public void PullVariables()
        {
            var current = RequireState().GetCurrentEnvironment();
            var chapter = owner.RequireChapterState(chapterId);
            foreach (var name in variableNames)
            {
                chapter.Variables[name] = QuestLuaConverter.ToJson(current[name]);
            }
        }

        public void Dispose()
        {
            state?.Dispose();
            state = null;
        }

        public void PushVariables()
        {
            var current = RequireState();
            var chapter = owner.RequireChapterState(chapterId);
            foreach (var pair in chapter.Variables)
            {
                current.Environment[pair.Key] = QuestLuaConverter.ToLua(pair.Value);
            }
        }

        private LuaState RequireState()
        {
            return state
                ?? throw new ObjectDisposedException(
                    $"Quest chapter '{chapterId}'");
        }

        private QuestScriptException CreateScriptException(
            string functionName,
            Exception exception)
        {
            return new QuestScriptException(
                owner.Progress.Reference,
                chapterId,
                functionName,
                exception);
        }
    }
}

public sealed record QuestDevelopmentStateSnapshot(
    RavenQuestReference Reference,
    string Title,
    IReadOnlyDictionary<string, string> Objectives,
    IReadOnlyList<QuestDevelopmentChapterSnapshot> Chapters,
    IReadOnlyList<RavenQuestMessage> Messages);

public sealed record QuestDevelopmentChapterSnapshot(
    string Id,
    bool IsActive,
    IReadOnlyDictionary<string, JsonElement> Variables);

public sealed record QuestCommanderContext(
    string CommanderName,
    JsonElement? Status,
    QuestSurfaceContext? Surface,
    IReadOnlyDictionary<string, QuestFactionSnapshot> Factions,
    IReadOnlyDictionary<string, JsonElement>? PriorJournalEvents = null)
{
    public static QuestCommanderContext Empty { get; } = new(
        string.Empty,
        null,
        null,
        new Dictionary<string, QuestFactionSnapshot>(StringComparer.Ordinal));
}

public sealed record QuestSurfaceContext(
    double Latitude,
    double Longitude,
    double PlanetRadius,
    int Heading);

public sealed record QuestFactionSnapshot(
    double Reputation,
    double Influence,
    IReadOnlyList<string> ActiveStates,
    IReadOnlyList<string> PendingStates,
    IReadOnlyList<string> RecoveringStates);

public sealed class QuestScriptException : Exception
{
    public QuestScriptException(
        RavenQuestReference quest,
        string chapterId,
        string functionName,
        Exception innerException)
        : base(
            $"Quest '{quest}' chapter '{chapterId}' failed while running '{functionName}': "
                + innerException.Message,
            innerException)
    {
        Quest = quest;
        ChapterId = chapterId;
        FunctionName = functionName;
    }

    public RavenQuestReference Quest { get; }

    public string ChapterId { get; }

    public string FunctionName { get; }
}

[LuaObject]
public partial class QuestScriptQuestApi
{
    private readonly QuestScriptRuntime runtime;

    public QuestScriptQuestApi(QuestScriptRuntime runtime)
    {
        this.runtime = runtime;
    }

    [LuaMember("complete")]
    public void Complete() => runtime.RequestTerminalState(RavenQuestState.complete);

    [LuaMember("fail")]
    public void Fail() => runtime.RequestTerminalState(RavenQuestState.failed);

    [LuaMember("startChapter")]
    public void StartChapter(string id) => runtime.RequestStartChapter(id);

    [LuaMember("nextChapter")]
    public void NextChapter(string id) => runtime.RequestNextChapter(id);

    [LuaMember("stopChapter")]
    public void StopChapter(string id) => runtime.RequestStopChapter(id);

    [LuaMember("set")]
    public void Set(string name, LuaValue value) =>
        runtime.SetQuestVariable(name, value);

    [LuaMember("get")]
    public LuaValue Get(string name) => runtime.GetQuestVariable(name);

    [LuaMember("sendMsg")]
    public void SendMessage(
        string? id = null,
        string? from = null,
        string? subject = null,
        string? body = null) => runtime.SendMessage(id, from, subject, body);

    [LuaMember("deleteMsg")]
    public bool DeleteMessage(string id) => runtime.DeleteMessage(id);

    [LuaMember("tag")]
    public void Tag(LuaValue value) => runtime.AddTags(value);

    [LuaMember("untag")]
    public void Untag(LuaValue value) => runtime.RemoveTags(value);

    [LuaMember("setTags")]
    public void SetTags(LuaValue value) => runtime.SetTags(value);

    [LuaMember("clearTags")]
    public void ClearTags() => runtime.ClearTags();

    [LuaMember("trackLocation")]
    public void TrackLocation(
        string name,
        double latitude,
        double longitude,
        float size) => runtime.TrackLocation(name, latitude, longitude, size);

    [LuaMember("clearLocation")]
    public void ClearLocation(string name) => runtime.ClearLocation(name);

    [LuaMember("clearAllLocations")]
    public void ClearAllLocations() => runtime.ClearAllLocations();

    [LuaMember("keepLast")]
    public void KeepLast(LuaValue value) => runtime.KeepLast(value);

    [LuaMember("setRoute")]
    public void SetRoute(string id, double width, LuaTable coordinates) =>
        runtime.SetRoute(id, width, coordinates);

    [LuaMember("clearRoute")]
    public void ClearRoute(string id) => runtime.ClearRoute(id);
}

[LuaObject]
public partial class QuestScriptObjectiveApi
{
    private readonly QuestScriptRuntime runtime;

    public QuestScriptObjectiveApi(QuestScriptRuntime runtime)
    {
        this.runtime = runtime;
    }

    [LuaMember("complete")]
    public void Complete(LuaValue value) => runtime.SetObjectiveState(
        value,
        LegacyQuestObjectiveState.complete);

    [LuaMember("failed")]
    public void Failed(LuaValue value) => runtime.SetObjectiveState(
        value,
        LegacyQuestObjectiveState.failed);

    [LuaMember("hide")]
    public void Hide(LuaValue value) => runtime.SetObjectiveState(
        value,
        LegacyQuestObjectiveState.hidden);

    [LuaMember("show")]
    public void Show(LuaValue value, int current = -1, int total = -1) =>
        runtime.SetObjectiveState(
            value,
            LegacyQuestObjectiveState.visible,
            current,
            total);

    [LuaMember("progress")]
    public void Progress(LuaValue value, int current, int total) =>
        runtime.SetObjectiveState(value, null, current, total);

    [LuaMember("remove")]
    public void Remove(LuaValue value) => runtime.RemoveObjectives(value);

    [LuaMember("isActive")]
    public bool IsActive(string id) => runtime.IsObjectiveActive(id);

    [LuaMember("check")]
    public bool Check(LuaValue value, string state) =>
        runtime.CheckObjectives(value, state);

    [LuaMember("getCurrent")]
    public int GetCurrent(string id) => runtime.GetObjectiveCurrent(id);

    [LuaMember("getTotal")]
    public int GetTotal(string id) => runtime.GetObjectiveTotal(id);
}

[LuaObject]
public partial class QuestScriptChapterApi
{
    private readonly QuestScriptRuntime runtime;
    private readonly string chapterId;

    public QuestScriptChapterApi(
        QuestScriptRuntime runtime,
        string chapterId)
    {
        this.runtime = runtime;
        this.chapterId = chapterId;
    }

    [LuaMember("stop")]
    public void Stop() => runtime.RequestStopChapter(chapterId);
}

[LuaObject]
public partial class QuestScriptCommanderApi
{
    private readonly QuestScriptRuntime runtime;

    public QuestScriptCommanderApi(QuestScriptRuntime runtime)
    {
        this.runtime = runtime;
    }

    [LuaMember("name")]
    public LuaValue Name => runtime.CommanderContext.CommanderName;

    [LuaMember("last")]
    public LuaValue Last(string eventName) => runtime.GetLast(eventName);

    [LuaMember("lastDocked")]
    public LuaValue LastDocked => runtime.GetLast("Docked");

    [LuaMember("lastFSDJump")]
    public LuaValue LastFsdJump => runtime.GetLast("FSDJump");

    [LuaMember("getFactionRep")]
    public LuaValue GetFactionReputation(string factionName)
    {
        return FindFaction(factionName)?.Reputation ?? double.NaN;
    }

    [LuaMember("getFactionInf")]
    public LuaValue GetFactionInfluence(string factionName)
    {
        return FindFaction(factionName)?.Influence ?? double.NaN;
    }

    [LuaMember("getFactionStates")]
    public LuaTable GetFactionStates(
        string factionName,
        string tense = "active")
    {
        var faction = FindFaction(factionName);
        var values = tense switch
        {
            "active" => faction?.ActiveStates ?? [],
            "pending" => faction?.PendingStates ?? [],
            "recovering" => faction?.RecoveringStates ?? [],
            _ => throw new ArgumentException(
                "Faction state tense must be active, pending, or recovering.",
                nameof(tense)),
        };
        var result = new LuaTable(values.Count, 0);
        for (var index = 0; index < values.Count; index++)
        {
            result[index + 1] = values[index];
        }

        return result;
    }

    [LuaMember("status")]
    public LuaValue Status => runtime.CommanderContext.Status is { } status
        ? QuestLuaConverter.ToLua(status)
        : LuaValue.Nil;

    [LuaMember("distanceFrom")]
    public double DistanceFrom(double latitude, double longitude)
    {
        var surface = runtime.CommanderContext.Surface;
        if (surface is null)
        {
            return -1;
        }

        return SurfaceNavigation.GetDistance(
            new SurfaceCoordinate(latitude, longitude),
            new SurfaceCoordinate(surface.Latitude, surface.Longitude),
            surface.PlanetRadius);
    }

    [LuaMember("isWithin")]
    public bool IsWithin(
        double latitude,
        double longitude,
        double targetDistance)
    {
        var distance = DistanceFrom(latitude, longitude);
        return distance >= 0 && distance < targetDistance;
    }

    [LuaMember("headingBetween")]
    public bool HeadingBetween(int heading, int tolerance)
    {
        var surface = runtime.CommanderContext.Surface;
        if (surface is null)
        {
            return false;
        }

        var left = heading - tolerance;
        if (left < 0)
        {
            left += 360;
        }

        var right = heading + tolerance;
        if (right >= 360)
        {
            right -= 360;
        }

        return left > right
            ? surface.Heading >= left || surface.Heading <= right
            : surface.Heading >= left && surface.Heading <= right;
    }

    private QuestFactionSnapshot? FindFaction(string name)
    {
        return runtime.CommanderContext.Factions.FirstOrDefault(pair =>
            string.Equals(pair.Key, name, StringComparison.Ordinal)).Value;
    }
}
