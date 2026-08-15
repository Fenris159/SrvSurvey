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
    private readonly HashSet<string> retainedCompleted = new(StringComparer.Ordinal);
    private readonly HashSet<string> completedSystems = new(StringComparer.Ordinal);
    private readonly HashSet<string> emptySystems = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> completedSystemCounts = new(
        StringComparer.Ordinal);
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

    public string? SavedSearchFileName { get; private set; }

    public char LowMassCode { get; private set; }

    public bool CurrentIsEmpty { get; private set; }

    public int Version { get; private set; }

    public int CurrentMinimumSystemNumber => systems.Count == 0
        ? -1
        : systems.Values.Min(system => system.Boxel.N2);

    public int CurrentMaximumSystemNumber => systems.Count == 0
        ? 0
        : systems.Values.Max(system => system.Boxel.N2);

    public int CompletedSystemCount => CountHandledSystems(Current?.Prefix);

    public int TotalKnownSystemCount => progress.Values.Where(count => count > 0).Sum();

    public int TotalCompletedSystemCount
    {
        get
        {
            var completedByPrefix = completed.Sum(prefix =>
                Math.Max(0, progress.GetValueOrDefault(prefix)));
            var partial = completedSystemCounts
                .Where(entry => !completed.Contains(entry.Key))
                .Sum(entry => entry.Value);
            var empty = emptySystems.Count(systemName =>
                BoxelAddress.TryParse(systemName, out var boxel)
                && boxel is not null
                && !completed.Contains(boxel.Prefix)
                && !completedSystems.Contains(systemName));
            return completedByPrefix + partial + empty;
        }
    }

    public int CompletedBoxelCount => completed.Count;

    public int TotalBoxelCount => progress.Count;

    public BoxelProgress GetProgress(BoxelAddress boxel)
    {
        ArgumentNullException.ThrowIfNull(boxel);
        if (!progress.TryGetValue(boxel.Prefix, out var expectedSystemCount))
        {
            return BoxelProgress.Unknown;
        }

        var isEmpty = expectedSystemCount < 0;
        var normalizedExpectedCount = Math.Max(0, expectedSystemCount);
        var completedSystemCount = completed.Contains(boxel.Prefix)
            ? normalizedExpectedCount
            : completedSystemCounts.GetValueOrDefault(boxel.Prefix)
                + CountEmptySystems(boxel.Prefix);
        return new BoxelProgress(
            normalizedExpectedCount,
            Math.Min(completedSystemCount, normalizedExpectedCount),
            completed.Contains(boxel.Prefix),
            isEmpty);
    }

    public bool CurrentSystemsComplete => Current is not null
        && CurrentCount > 0
        && CountHandledSystems(Current.Prefix) >= CurrentCount;

    public IReadOnlySet<string> EmptySystems => emptySystems;

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
        StartedOn = NormalizeStartedOn(seed);
        CurrentCount = Math.Max(0, seed.CurrentCount);
        AutoCopy = seed.AutoCopy;
        Collapsed = seed.Collapsed;
        SkipAlreadyVisited = seed.SkipAlreadyVisited;
        SkipKnownToSpansh = seed.SkipKnownToSpansh;
        CompletionMode = seed.CompletionMode;
        SavedSearchFileName = NormalizeSavedSearchFileName(seed.SavedSearchFileName);
        LowMassCode = BoxelAddress.IsValidMassCode(seed.LowMassCode)
            ? seed.LowMassCode
            : 'c';
        completed.Clear();
        completed.UnionWith(seed.CompletedPrefixes);
        retainedCompleted.Clear();
        retainedCompleted.UnionWith(seed.CompletedPrefixes);
        completedSystems.Clear();
        completedSystemCounts.Clear();
        foreach (var systemName in seed.CompletedSystems)
        {
            AddCompletedSystem(systemName);
        }
        emptySystems.Clear();
        foreach (var systemName in seed.EmptySystems)
        {
            AddEmptySystem(systemName);
        }
        systems.Clear();
        CurrentIsEmpty = false;

        var configurationIsValid = TopBoxel is not null
            && TopBoxel.MassCode != BoxelAddress.MaximumMassCode
            && LowMassCode <= TopBoxel.MassCode;
        IsActive = seed.Active && configurationIsValid;
        RestoreProgress(seed);

        SetNextSystem();
        Version++;
    }

    private static DateTimeOffset NormalizeStartedOn(BoxelSearchSnapshot seed)
    {
        return seed.Active
            && seed.TopBoxel is not null
            && seed.StartedOn == DateTimeOffset.MinValue
                ? new DateTimeOffset(DateTime.Today)
                : seed.StartedOn;
    }

    private void RestoreProgress(BoxelSearchSnapshot seed)
    {
        if (TopBoxel is not null)
        {
            if (Current is null
                || !TopBoxel.Contains(Current)
                || Current.MassCode < LowMassCode)
            {
                Current = TopBoxel.WithSystemNumber(0);
            }

            InitializeProgress();
            foreach (var entry in seed.ProgressByPrefix.Where(entry =>
                         progress.ContainsKey(entry.Key)))
            {
                progress[entry.Key] = Math.Max(-1, entry.Value);
            }

            if (Current is not null)
            {
                CurrentCount = Math.Max(
                    CurrentCount,
                    Math.Max(0, progress.GetValueOrDefault(Current.Prefix)));
                CurrentIsEmpty = progress.GetValueOrDefault(Current.Prefix) == -1;
            }
        }
        else
        {
            progress.Clear();
            progressIds.Clear();
            Current = null;
        }
    }

    public bool TryActivate(
        BoxelSearchActivationRequest request,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(request);
        var topBoxel = request.TopBoxel;
        var lowMassCode = request.LowMassCode;
        var startedOn = request.StartedOn;
        var skipAlreadyVisited = request.SkipAlreadyVisited;
        var skipKnownToSpansh = request.SkipKnownToSpansh;
        var completionMode = request.CompletionMode;
        var autoCopy = request.AutoCopy;

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
        SavedSearchFileName = null;
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

    public void SetSavedSearchFileName(string? fileName)
    {
        SavedSearchFileName = NormalizeSavedSearchFileName(fileName);
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
            if (shouldBeEmpty)
            {
                completed.Remove(entry.Value);
                retainedCompleted.Remove(entry.Value);
                RemoveCompletedSystems(entry.Value);
                RemoveEmptySystems(entry.Value);
            }
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
        if (isComplete)
        {
            emptySystems.Remove(entry.Key);
            AddCompletedSystem(entry.Key);
        }
        else
        {
            RemoveCompletedSystem(entry.Key);
            retainedCompleted.Remove(entry.Value.Boxel.Prefix);
        }

        UpdateCurrentCompletion();
        SetNextSystem();
        Version++;
        error = null;
        return true;
    }

    public bool TrySetSystemEmpty(
        string systemName,
        bool isEmpty,
        out string? error)
    {
        if (!TryResolveCurrentSystem(systemName, out var boxel))
        {
            error = "Choose a numbered system in the current boxel.";
            return false;
        }

        var generatedName = boxel!.GeneratedName;
        var changed = isEmpty
            ? emptySystems.Add(generatedName)
            : emptySystems.Remove(generatedName);
        if (!changed)
        {
            error = isEmpty
                ? $"{boxel.Name} is already marked empty."
                : $"{boxel.Name} is not marked empty.";
            return false;
        }

        UpdateCurrentCompletion();
        SetNextSystem();
        Version++;
        error = null;
        return true;
    }

    public bool IsSystemEmpty(string systemName)
    {
        return TryResolveCurrentSystem(systemName, out var boxel)
            && emptySystems.Contains(boxel!.GeneratedName);
    }

    public bool TryMarkNextSystemEmpty(out string? systemName, out string? error)
    {
        systemName = NextSystem;
        if (string.IsNullOrWhiteSpace(systemName))
        {
            error = "There is no next incomplete system to mark empty.";
            return false;
        }

        if (TrySetSystemEmpty(systemName, true, out error))
        {
            return true;
        }

        systemName = null;
        return false;
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
            completed.Remove(Current.Prefix);
            retainedCompleted.Remove(Current.Prefix);
            RemoveCompletedSystems(Current.Prefix);
            RemoveEmptySystems(Current.Prefix);
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

            if (entry.IsComplete)
            {
                changed |= completed.Add(entry.Boxel.Prefix);
                retainedCompleted.Add(entry.Boxel.Prefix);
            }
            else
            {
                changed |= completed.Remove(entry.Boxel.Prefix);
                retainedCompleted.Remove(entry.Boxel.Prefix);
            }
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
        return new BoxelSearchSnapshot
        {
            Active = IsActive,
            TopBoxel = TopBoxel,
            StartedOn = StartedOn,
            Current = Current,
            CurrentCount = CurrentCount,
            LowMassCode = LowMassCode,
            CompletedPrefixes = completed.Order(StringComparer.Ordinal).ToArray(),
            CompletedSystems = completedSystems.Order(StringComparer.Ordinal).ToArray(),
            EmptySystems = emptySystems.Order(StringComparer.Ordinal).ToArray(),
            ProgressByPrefix = progress.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal),
            AutoCopy = AutoCopy,
            Collapsed = Collapsed,
            SkipAlreadyVisited = SkipAlreadyVisited,
            SkipKnownToSpansh = SkipKnownToSpansh,
            CompletionMode = CompletionMode,
            SavedSearchFileName = SavedSearchFileName
        };
    }

    private static string? NormalizeSavedSearchFileName(string? fileName)
    {
        return string.IsNullOrWhiteSpace(fileName)
            ? null
            : Path.GetFileName(fileName.Trim());
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
            AddCompletedSystem(boxel.GeneratedName);
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
        emptySystems.Remove(observation.Boxel.GeneratedName);
        var isComplete = IsObservationComplete(observation, source, existing);
        var observedBoxel = MergeObservedBoxel(observation.Boxel, existing);
        systems[observation.Boxel.GeneratedName] = new BoxelSystemState(
            observedBoxel,
            isComplete,
            observation.Position ?? existing?.Position,
            Max(existing?.VisitedAt, observation.VisitedAt),
            Max(existing?.SpanshUpdatedAt, observation.SpanshUpdatedAt),
            observation.HasKnownBodies || existing?.HasKnownBodies == true);
        if (isComplete)
        {
            AddCompletedSystem(observation.Boxel.GeneratedName);
        }
        CurrentCount = Math.Max(CurrentCount, observation.Boxel.N2 + 1);
        SetProgress(Current, observation.Boxel.N2 + 1);
        return true;
    }

    private bool IsObservationComplete(
        BoxelSystemObservation observation,
        BoxelObservationSource source,
        BoxelSystemState? existing)
    {
        var isComplete = existing?.IsComplete == true
            || completedSystems.Contains(observation.Boxel.GeneratedName)
            || retainedCompleted.Contains(observation.Boxel.Prefix);
        if (source == BoxelObservationSource.LocalProfile)
        {
            return isComplete || (CompletionMode == BoxelCompletionMode.FssAllBodies
                ? observation.FssAllBodies && observation.VisitedAt > StartedOn
                : observation.VisitedAt > StartedOn || SkipAlreadyVisited);
        }

        return isComplete || (source == BoxelObservationSource.Spansh
            && CompletionMode == BoxelCompletionMode.EnterSystem
            && observation.HasKnownBodies
            && SkipKnownToSpansh
            && observation.SpanshUpdatedAt < StartedOn);
    }

    private static BoxelAddress MergeObservedBoxel(
        BoxelAddress observed,
        BoxelSystemState? existing)
    {
        if (observed.PublicName is not null || existing is null)
        {
            return observed;
        }

        return existing.Boxel with
        {
            SystemAddress = observed.SystemAddress > 0
                ? observed.SystemAddress
                : existing.Boxel.SystemAddress,
        };
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

    private void RemoveCompletedSystems(string prefix)
    {
        var matching = completedSystems.Where(systemName =>
                BoxelAddress.TryParse(systemName, out var boxel)
                && boxel is not null
                && string.Equals(boxel.Prefix, prefix, StringComparison.Ordinal))
            .ToArray();
        foreach (var systemName in matching)
        {
            RemoveCompletedSystem(systemName);
        }
    }

    private void RemoveEmptySystems(string prefix)
    {
        emptySystems.RemoveWhere(systemName =>
            BoxelAddress.TryParse(systemName, out var boxel)
            && boxel is not null
            && string.Equals(boxel.Prefix, prefix, StringComparison.Ordinal));
    }

    private void AddEmptySystem(string systemName)
    {
        if (BoxelAddress.TryParse(systemName, out var boxel) && boxel is not null)
        {
            emptySystems.Add(boxel.GeneratedName);
            retainedCompleted.Remove(boxel.Prefix);
        }
    }

    private bool TryResolveCurrentSystem(
        string systemName,
        out BoxelAddress? boxel)
    {
        boxel = systems.Values
            .Select(system => system.Boxel)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, systemName, StringComparison.Ordinal)
                || string.Equals(
                    candidate.GeneratedName,
                    systemName,
                    StringComparison.Ordinal));
        if (boxel is null)
        {
            if (!BoxelAddress.TryParse(systemName, out var parsed) || parsed is null)
            {
                return false;
            }

            boxel = parsed;
        }

        return Current is not null
            && string.Equals(boxel.Prefix, Current.Prefix, StringComparison.Ordinal);
    }

    private int CountHandledSystems(string? prefix)
    {
        if (Current is null
            || string.IsNullOrWhiteSpace(prefix)
            || !string.Equals(Current.Prefix, prefix, StringComparison.Ordinal))
        {
            return 0;
        }

        var count = 0;
        for (var number = 0; number < CurrentCount; number++)
        {
            var generatedName = Current.WithSystemNumber(number).GeneratedName;
            systems.TryGetValue(generatedName, out var system);
            if (system?.IsComplete == true
                || completedSystems.Contains(generatedName)
                || emptySystems.Contains(generatedName))
            {
                count++;
            }
        }

        return count;
    }

    private int CountEmptySystems(string prefix)
    {
        return emptySystems.Count(systemName =>
            !completedSystems.Contains(systemName)
            && BoxelAddress.TryParse(systemName, out var boxel)
            && boxel is not null
            && string.Equals(boxel.Prefix, prefix, StringComparison.Ordinal));
    }

    private void AddCompletedSystem(string systemName)
    {
        if (!completedSystems.Add(systemName)
            || !BoxelAddress.TryParse(systemName, out var boxel)
            || boxel is null)
        {
            return;
        }

        completedSystemCounts[boxel.Prefix] =
            completedSystemCounts.GetValueOrDefault(boxel.Prefix) + 1;
    }

    private void RemoveCompletedSystem(string systemName)
    {
        if (!completedSystems.Remove(systemName)
            || !BoxelAddress.TryParse(systemName, out var boxel)
            || boxel is null
            || !completedSystemCounts.TryGetValue(boxel.Prefix, out var count))
        {
            return;
        }

        if (count <= 1)
        {
            completedSystemCounts.Remove(boxel.Prefix);
        }
        else
        {
            completedSystemCounts[boxel.Prefix] = count - 1;
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
        if (!CurrentIsEmpty)
        {
            var maximum = Math.Max(CurrentMaximumSystemNumber, CurrentCount);
            for (var number = 0; number < maximum; number++)
            {
                var generated = Current.WithSystemNumber(number);
                systems.TryGetValue(generated.GeneratedName, out var system);
                if (system?.IsComplete == true
                    || completedSystems.Contains(generated.GeneratedName)
                    || emptySystems.Contains(generated.GeneratedName))
                {
                    continue;
                }

                next = system?.Boxel.Name
                    ?? generated.Name;
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

public sealed class BoxelSearchActivationRequest
{
    public BoxelAddress? TopBoxel { get; init; }

    public char LowMassCode { get; init; }

    public DateTimeOffset StartedOn { get; init; }

    public bool SkipAlreadyVisited { get; init; }

    public bool SkipKnownToSpansh { get; init; }

    public BoxelCompletionMode CompletionMode { get; init; }

    public bool AutoCopy { get; init; }
}

public enum BoxelCompletionMode
{
    EnterSystem,
    FssAllBodies,
}

public sealed record BoxelSearchSnapshot
{
    public bool Active { get; init; }

    public BoxelAddress? TopBoxel { get; init; }

    public DateTimeOffset StartedOn { get; init; }

    public BoxelAddress? Current { get; init; }

    public int CurrentCount { get; init; }

    public char LowMassCode { get; init; } = 'c';

    public IReadOnlyList<string> CompletedPrefixes { get; init; } = [];

    public IReadOnlyList<string> CompletedSystems { get; init; } = [];

    public IReadOnlyList<string> EmptySystems { get; init; } = [];

    public IReadOnlyDictionary<string, int> ProgressByPrefix { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    public bool AutoCopy { get; init; }

    public bool Collapsed { get; init; }

    public bool SkipAlreadyVisited { get; init; }

    public bool SkipKnownToSpansh { get; init; }

    public BoxelCompletionMode CompletionMode { get; init; } =
        BoxelCompletionMode.EnterSystem;

    public string? SavedSearchFileName { get; init; }

    public static BoxelSearchSnapshot Empty { get; } = new();
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

public readonly record struct BoxelProgress(
    int ExpectedSystemCount,
    int CompletedSystemCount,
    bool IsComplete,
    bool IsEmpty)
{
    public static BoxelProgress Unknown { get; } = new(0, 0, false, false);
}
