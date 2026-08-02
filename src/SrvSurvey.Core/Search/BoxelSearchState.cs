using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Search;

public sealed class BoxelSearchState
{
    private readonly Dictionary<string, int> progress = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> progressIds = new(
        StringComparer.Ordinal);
    private readonly Dictionary<string, BoxelSystemState> systems = new(
        StringComparer.Ordinal);
    private readonly HashSet<string> completed = new(StringComparer.Ordinal);
    private int projectionVersion = -1;
    private IReadOnlyList<BoxelSystemState> systemProjection = [];
    private IReadOnlyList<BoxelAddress> boxelProjection = [];
    private IReadOnlySet<string> emptyBoxelProjection = new HashSet<string>(
        StringComparer.Ordinal);

    public BoxelSearchState(BoxelSearchSnapshot? seed = null)
    {
        Reset(seed);
    }

    public bool IsActive { get; private set; }

    public BoxelAddress? TopBoxel { get; private set; }

    public DateTimeOffset StartedOn { get; private set; }

    public BoxelAddress? Current { get; private set; }

    public string? NextSystem { get; private set; }

    public int CurrentCount { get; private set; }

    public bool AutoCopy { get; private set; }

    public bool Collapsed { get; private set; }

    public bool SkipAlreadyVisited { get; private set; }

    public bool SkipKnownToSpansh { get; private set; }

    public BoxelCompletionMode CompletionMode { get; private set; }

    public char LowMassCode { get; private set; }

    public bool CurrentIsEmpty { get; private set; }

    public int Version { get; private set; }

    public int CurrentMinimumSystemNumber => systems.Count == 0
        ? -1
        : systems.Values.Min(system => system.Boxel.N2);

    public int CurrentMaximumSystemNumber => systems.Count == 0
        ? 0
        : systems.Values.Max(system => system.Boxel.N2);

    public int CompletedSystemCount => systems.Values.Count(system => system.IsComplete);

    public int CompletedBoxelCount => completed.Count;

    public int TotalBoxelCount => progress.Count;

    public bool CurrentSystemsComplete => systems.Count > 0
        && systems.Values.All(system => system.IsComplete);

    public IReadOnlyList<BoxelSystemState> Systems
    {
        get
        {
            RefreshProjections();
            return systemProjection;
        }
    }

    public IReadOnlyList<BoxelAddress> Boxels
    {
        get
        {
            RefreshProjections();
            return boxelProjection;
        }
    }

    public IReadOnlySet<string> EmptyBoxelPrefixes
    {
        get
        {
            RefreshProjections();
            return emptyBoxelProjection;
        }
    }

    private void RefreshProjections()
    {
        if (projectionVersion == Version)
        {
            return;
        }

        systemProjection = systems.Values
            .OrderBy(system => system.Boxel.N2)
            .ToArray();
        boxelProjection = progress.Keys
            .Select(prefix => BoxelAddress.Parse(prefix + "0"))
            .ToArray();
        emptyBoxelProjection = progress
            .Where(entry => entry.Value == -1)
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
        projectionVersion = Version;
    }

    public void Reset(BoxelSearchSnapshot? seed = null)
    {
        seed ??= BoxelSearchSnapshot.Empty;
        TopBoxel = seed.TopBoxel;
        Current = seed.Current;
        StartedOn = seed.Active
            && seed.TopBoxel is not null
            && seed.StartedOn == DateTimeOffset.MinValue
                ? new DateTimeOffset(DateTime.Today)
                : seed.StartedOn;
        CurrentCount = Math.Max(0, seed.CurrentCount);
        AutoCopy = seed.AutoCopy;
        Collapsed = seed.Collapsed;
        SkipAlreadyVisited = seed.SkipAlreadyVisited;
        SkipKnownToSpansh = seed.SkipKnownToSpansh;
        CompletionMode = seed.CompletionMode;
        LowMassCode = BoxelAddress.IsValidMassCode(seed.LowMassCode)
            ? seed.LowMassCode
            : 'c';
        completed.Clear();
        completed.UnionWith(seed.CompletedPrefixes);
        systems.Clear();
        CurrentIsEmpty = false;

        var configurationIsValid = TopBoxel is not null
            && TopBoxel.MassCode != BoxelAddress.MaximumMassCode
            && LowMassCode <= TopBoxel.MassCode;
        IsActive = seed.Active && configurationIsValid;
        if (TopBoxel is not null)
        {
            if (Current is null
                || !TopBoxel.Contains(Current)
                || Current.MassCode < LowMassCode)
            {
                Current = TopBoxel.WithSystemNumber(0);
            }

            InitializeProgress();
        }
        else
        {
            progress.Clear();
            progressIds.Clear();
            Current = null;
        }

        SetNextSystem();
        Version++;
    }

    public bool TryActivate(
        BoxelAddress? topBoxel,
        char lowMassCode,
        DateTimeOffset startedOn,
        bool skipAlreadyVisited,
        bool skipKnownToSpansh,
        BoxelCompletionMode completionMode,
        bool autoCopy,
        out string? error)
    {
        if (topBoxel is null)
        {
            error = "Enter a valid generated system or boxel name.";
            return false;
        }

        if (topBoxel.MassCode == BoxelAddress.MaximumMassCode)
        {
            error = "Mass-code h boxel searches are not supported because empty-boxel tracking is unavailable at that scale.";
            return false;
        }

        if (!BoxelAddress.IsValidMassCode(lowMassCode)
            || lowMassCode > topBoxel.MassCode)
        {
            error = $"Choose a lower mass code from a through {topBoxel.MassCode}.";
            return false;
        }

        TopBoxel = topBoxel;
        Current = topBoxel.WithSystemNumber(0);
        LowMassCode = lowMassCode;
        StartedOn = startedOn;
        SkipAlreadyVisited = skipAlreadyVisited;
        SkipKnownToSpansh = skipKnownToSpansh;
        CompletionMode = completionMode;
        AutoCopy = autoCopy;
        CurrentCount = 1;
        CurrentIsEmpty = false;
        systems.Clear();
        IsActive = true;
        InitializeProgress();
        SetNextSystem();
        Version++;
        error = null;
        return true;
    }

    public void Disable()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Version++;
    }

    public void SetAutoCopy(bool enabled)
    {
        if (AutoCopy == enabled)
        {
            return;
        }

        AutoCopy = enabled;
        Version++;
    }

    public void ApplyEmptyBoxels(IEnumerable<string> emptyBoxelIds)
    {
        ArgumentNullException.ThrowIfNull(emptyBoxelIds);
        var emptyIds = emptyBoxelIds.ToHashSet(StringComparer.Ordinal);
        var changed = false;
        foreach (var entry in progressIds)
        {
            var shouldBeEmpty = emptyIds.Contains(entry.Key);
            var isEmpty = progress.GetValueOrDefault(entry.Value) == -1;
            if (shouldBeEmpty == isEmpty)
            {
                continue;
            }

            progress[entry.Value] = shouldBeEmpty ? -1 : 0;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        CurrentIsEmpty = Current is not null
            && progress.GetValueOrDefault(Current.Prefix) == -1;
        SetNextSystem();
        Version++;
    }

    public bool TrySetCurrent(BoxelAddress? boxel, out string? error)
    {
        if (TopBoxel is null || boxel is null || !TopBoxel.Contains(boxel))
        {
            error = "The selected boxel is outside the configured search area.";
            return false;
        }

        if (boxel.MassCode < LowMassCode)
        {
            error = $"The selected boxel is below the configured mass-code {LowMassCode} limit.";
            return false;
        }

        Current = boxel.WithSystemNumber(0);
        CurrentCount = Math.Max(1, progress.GetValueOrDefault(Current.Prefix));
        CurrentIsEmpty = progress.GetValueOrDefault(Current.Prefix) == -1;
        systems.Clear();
        SetNextSystem();
        Version++;
        error = null;
        return true;
    }

    public void SetExpectedSystemCount(int count)
    {
        if (Current is null || count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        CurrentCount = Math.Max(count, CurrentMaximumSystemNumber + 1);
        SetProgress(Current, CurrentCount);
        SetNextSystem();
        Version++;
    }

    public bool TrySetSystemComplete(
        string systemName,
        bool isComplete,
        out string? error)
    {
        var entry = systems.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Value.Boxel.Name,
                systemName,
                StringComparison.Ordinal)
            || string.Equals(
                candidate.Value.Boxel.GeneratedName,
                systemName,
                StringComparison.Ordinal));
        if (entry.Value is null)
        {
            error = "Systems must be discovered or visited before completion can be changed.";
            return false;
        }

        systems[entry.Key] = entry.Value with { IsComplete = isComplete };
        UpdateCurrentCompletion();
        SetNextSystem();
        Version++;
        error = null;
        return true;
    }

    public void SetCurrentEmpty(bool isEmpty)
    {
        if (Current is null)
        {
            return;
        }

        CurrentIsEmpty = isEmpty;
        if (isEmpty)
        {
            progress[Current.Prefix] = -1;
            systems.Clear();
        }
        else
        {
            CurrentCount = Math.Max(1, CurrentCount);
            progress[Current.Prefix] = CurrentCount;
        }

        SetNextSystem();
        Version++;
    }

    public bool ApplyCompletionAudit(
        IEnumerable<BoxelCompletionAuditEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var changed = false;
        foreach (var entry in entries)
        {
            if (!progress.TryGetValue(entry.Boxel.Prefix, out var currentProgress))
            {
                continue;
            }

            var auditedProgress = entry.IsEmpty ? -1 : Math.Max(0, entry.SystemCount);
            if (currentProgress != auditedProgress)
            {
                progress[entry.Boxel.Prefix] = auditedProgress;
                changed = true;
            }

            changed |= entry.IsComplete
                ? completed.Add(entry.Boxel.Prefix)
                : completed.Remove(entry.Boxel.Prefix);
        }

        if (changed)
        {
            SetNextSystem();
            Version++;
        }

        return changed;
    }

    public bool Apply(JournalEventEnvelope journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        if (!IsActive || Current is null)
        {
            return false;
        }

        return journalEvent.EventName switch
        {
            "FSDJump" => ApplyVisitedSystem(journalEvent, false),
            "FSSAllBodiesFound" => ApplyVisitedSystem(journalEvent, true),
            _ => false,
        };
    }

    public bool MergeRoute(IEnumerable<BoxelSystemObservation> route)
    {
        return MergeObservations(route, BoxelObservationSource.Route);
    }

    public bool MergeLocalSystems(IEnumerable<BoxelSystemObservation> localSystems)
    {
        return MergeObservations(localSystems, BoxelObservationSource.LocalProfile);
    }

    public bool MergeSpanshSystems(IEnumerable<BoxelSystemObservation> spanshSystems)
    {
        return MergeObservations(spanshSystems, BoxelObservationSource.Spansh);
    }

    public BoxelSearchSnapshot CreateSnapshot()
    {
        return new BoxelSearchSnapshot(
            IsActive,
            TopBoxel,
            StartedOn,
            Current,
            CurrentCount,
            LowMassCode,
            completed.Order(StringComparer.Ordinal).ToArray(),
            AutoCopy,
            Collapsed,
            SkipAlreadyVisited,
            SkipKnownToSpansh,
            CompletionMode);
    }

    private bool ApplyVisitedSystem(
        JournalEventEnvelope journalEvent,
        bool allBodiesFound)
    {
        var root = journalEvent.Payload;
        var nameProperty = allBodiesFound ? "SystemName" : "StarSystem";
        var systemName = GetString(root, nameProperty);
        var systemAddress = GetInt64(root, "SystemAddress") ?? 0;
        var resolved = systemAddress > 0
            ? BoxelAddress.TryFromSystemAddress(
                systemAddress,
                systemName,
                out var boxel)
            : BoxelAddress.TryParse(systemName, out boxel);
        if (!resolved
            || boxel is null
            || Current is null
            || !string.Equals(boxel.Prefix, Current.Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var observation = new BoxelSystemObservation(
            boxel,
            GetGalacticCoordinate(root, "StarPos"),
            journalEvent.Timestamp,
            null,
            allBodiesFound);
        MergeObservation(observation, BoxelObservationSource.Journal);
        if ((CompletionMode == BoxelCompletionMode.EnterSystem && !allBodiesFound)
            || (CompletionMode == BoxelCompletionMode.FssAllBodies && allBodiesFound))
        {
            var system = systems[boxel.GeneratedName];
            systems[boxel.GeneratedName] = system with { IsComplete = true };
        }

        UpdateCurrentCompletion();
        SetNextSystem();
        Version++;
        return true;
    }

    private bool MergeObservations(
        IEnumerable<BoxelSystemObservation> observations,
        BoxelObservationSource source)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var changed = false;
        foreach (var observation in observations)
        {
            changed |= MergeObservation(observation, source);
        }

        if (changed)
        {
            UpdateCurrentCompletion();
            SetNextSystem();
            Version++;
        }

        return changed;
    }

    private bool MergeObservation(
        BoxelSystemObservation observation,
        BoxelObservationSource source)
    {
        if (Current is null
            || !string.Equals(
                observation.Boxel.Prefix,
                Current.Prefix,
                StringComparison.Ordinal))
        {
            return false;
        }

        systems.TryGetValue(observation.Boxel.GeneratedName, out var existing);
        var isComplete = existing?.IsComplete ?? false;
        if (source == BoxelObservationSource.LocalProfile)
        {
            isComplete |= CompletionMode == BoxelCompletionMode.FssAllBodies
                ? observation.FssAllBodies
                    && observation.VisitedAt > StartedOn
                : observation.VisitedAt > StartedOn || SkipAlreadyVisited;
        }
        else if (source == BoxelObservationSource.Spansh
            && CompletionMode == BoxelCompletionMode.EnterSystem
            && observation.HasKnownBodies
            && SkipKnownToSpansh)
        {
            isComplete |= observation.SpanshUpdatedAt < StartedOn;
        }

        var observedBoxel = observation.Boxel.PublicName is not null
            || existing is null
                ? observation.Boxel
                : existing.Boxel with
                {
                    SystemAddress = observation.Boxel.SystemAddress > 0
                        ? observation.Boxel.SystemAddress
                        : existing.Boxel.SystemAddress,
                };
        systems[observation.Boxel.GeneratedName] = new BoxelSystemState(
            observedBoxel,
            isComplete,
            observation.Position ?? existing?.Position,
            Max(existing?.VisitedAt, observation.VisitedAt),
            Max(existing?.SpanshUpdatedAt, observation.SpanshUpdatedAt),
            observation.HasKnownBodies || existing?.HasKnownBodies == true);
        CurrentCount = Math.Max(CurrentCount, observation.Boxel.N2 + 1);
        SetProgress(Current, observation.Boxel.N2 + 1);
        return true;
    }

    private void InitializeProgress()
    {
        progress.Clear();
        progressIds.Clear();
        if (TopBoxel is null)
        {
            return;
        }

        InitializeProgress(TopBoxel);
    }

    private void InitializeProgress(BoxelAddress boxel)
    {
        progress[boxel.Prefix] = 0;
        progressIds[boxel.Id] = boxel.Prefix;
        if (boxel.MassCode > LowMassCode)
        {
            foreach (var child in boxel.Children)
            {
                InitializeProgress(child);
            }
        }
    }

    private void SetProgress(BoxelAddress boxel, int count)
    {
        if (count <= 0
            || !progress.TryGetValue(boxel.Prefix, out var existing)
            || existing < count)
        {
            progress[boxel.Prefix] = count;
        }
    }

    private void UpdateCurrentCompletion()
    {
        if (Current is null)
        {
            return;
        }

        if (CurrentSystemsComplete)
        {
            completed.Add(Current.Prefix);
        }
        else
        {
            completed.Remove(Current.Prefix);
        }
    }

    private void SetNextSystem()
    {
        if (Current is null)
        {
            NextSystem = null;
            return;
        }

        string? next = null;
        if (!CurrentIsEmpty
            && systems.Count > 0
            && systems.Values.All(system => !system.IsComplete))
        {
            next = Current.Prefix;
        }

        if (!CurrentIsEmpty && next is null)
        {
            var maximum = Math.Max(CurrentMaximumSystemNumber, CurrentCount);
            for (var number = maximum - 1; number >= 0; number--)
            {
                var system = systems.Values.FirstOrDefault(
                    candidate => candidate.Boxel.N2 == number);
                if (system?.IsComplete == true)
                {
                    continue;
                }

                next = system?.Boxel.Name ?? Current.WithSystemNumber(number).Name;
                break;
            }
        }

        next ??= progress.FirstOrDefault(entry =>
                entry.Value != -1 && !completed.Contains(entry.Key))
            .Key;
        NextSystem = next ?? Current.Prefix;
    }

    private static DateTimeOffset? Max(
        DateTimeOffset? first,
        DateTimeOffset? second)
    {
        if (first is null)
        {
            return second;
        }

        return second is not null && second > first ? second : first;
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
                ? number
                : null;
    }

    private static GalacticCoordinate? GetGalacticCoordinate(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() < 3)
        {
            return null;
        }

        var coordinates = value.EnumerateArray().Take(3).ToArray();
        return coordinates.All(coordinate =>
                coordinate.ValueKind == JsonValueKind.Number
                && coordinate.TryGetDouble(out var number)
                && double.IsFinite(number))
            ? new GalacticCoordinate(
                coordinates[0].GetDouble(),
                coordinates[1].GetDouble(),
                coordinates[2].GetDouble())
            : null;
    }

    private enum BoxelObservationSource
    {
        Journal,
        Route,
        LocalProfile,
        Spansh,
    }
}

public enum BoxelCompletionMode
{
    EnterSystem,
    FssAllBodies,
}

public sealed record BoxelSearchSnapshot(
    bool Active,
    BoxelAddress? TopBoxel,
    DateTimeOffset StartedOn,
    BoxelAddress? Current,
    int CurrentCount,
    char LowMassCode,
    IReadOnlyList<string> CompletedPrefixes,
    bool AutoCopy,
    bool Collapsed,
    bool SkipAlreadyVisited,
    bool SkipKnownToSpansh,
    BoxelCompletionMode CompletionMode)
{
    public static BoxelSearchSnapshot Empty { get; } = new(
        false,
        null,
        DateTimeOffset.MinValue,
        null,
        0,
        'c',
        [],
        false,
        false,
        false,
        false,
        BoxelCompletionMode.EnterSystem);
}

public sealed record BoxelSystemObservation(
    BoxelAddress Boxel,
    GalacticCoordinate? Position,
    DateTimeOffset? VisitedAt,
    DateTimeOffset? SpanshUpdatedAt,
    bool HasKnownBodies,
    bool FssAllBodies = false);

public sealed record BoxelSystemState(
    BoxelAddress Boxel,
    bool IsComplete,
    GalacticCoordinate? Position,
    DateTimeOffset? VisitedAt,
    DateTimeOffset? SpanshUpdatedAt,
    bool HasKnownBodies);
