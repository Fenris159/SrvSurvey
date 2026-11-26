using System.Text.Json;

namespace SrvSurvey.Core.Journal;

public sealed partial class JournalDirectoryMonitor
{
    private bool currentJournalIsContinuation;

    private sealed record ResolvedJournalIdentity(string FrontierId, IReadOnlyList<FileInfo> Parts);

    private async Task<ResolvedJournalIdentity?> ResolveJournalIdentityAsync(
        FileInfo file,
        CancellationToken cancellationToken
    )
    {
        var parts = new List<FileInfo>();
        bool requirePart = false;
        while (file.Exists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JournalIdentityCacheEntry? identity = await ReadJournalIdentityAsync(file, cancellationToken)
                .ConfigureAwait(false);
            if (identity is null || (requirePart && identity.Part is null))
            {
                return null;
            }
            parts.Add(file);
            if (!string.IsNullOrWhiteSpace(identity.FrontierId))
            {
                parts.Reverse();
                return new(identity.FrontierId, parts);
            }
            if (identity.Part?.PreviousPath is not { } previous)
            {
                return null;
            }
            file = new FileInfo(previous);
            requirePart = true;
        }
        return null;
    }

    private async Task<string?> ReadFrontierIdAsync(FileInfo file, CancellationToken cancellationToken) =>
        (await ResolveJournalIdentityAsync(file, cancellationToken).ConfigureAwait(false))?.FrontierId;

    private async Task ReadSelectedPartsAsync(
        FileInfo latest,
        List<JournalEventEnvelope> events,
        List<string> errors,
        CancellationToken cancellationToken
    )
    {
        ResolvedJournalIdentity? identity = await ResolveJournalIdentityAsync(latest, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<FileInfo> parts = identity?.Parts ?? [latest];
        int start = FindCurrentPartStartIndex(parts);
        int endExclusive = parts.Count;
        long? checkpointByteOffset = null;
        bool evaluatedSwitchResumeBoundary = ApplySessionBoundaryIfNeeded(
            parts,
            start,
            ref endExclusive,
            ref checkpointByteOffset
        );
        bool requiresBootstrapBoundary =
            evaluatedSwitchResumeBoundary
            && (pollForcesBootstrapRead || (!hasCompletedFirstPoll && firstPollRequiresBootstrap));
        for (int index = start; index < endExclusive; index++)
        {
            FileInfo part = parts[index];
            SwitchToSessionPartIfNeeded(part, index > 0, events, errors);
            long? maxOffset = index == endExclusive - 1 ? checkpointByteOffset : null;
            try
            {
                await ReadJournalAppendAsync(events, errors, cancellationToken, maxOffset).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (requiresBootstrapBoundary)
                {
                    RecordPendingBootstrapBoundaries(parts, index, endExclusive);
                }

                throw;
            }

            pendingBoundaryCheckCatchUpPaths.Remove(part.FullName);
            startupUnverifiedJournalLengths.Remove(part.FullName);
        }
    }

    private int FindCurrentPartStartIndex(IReadOnlyList<FileInfo> parts)
    {
        for (int index = 0; index < parts.Count; index++)
        {
            if (PathsEqual(parts[index].FullName, currentJournalPath))
            {
                return index;
            }
        }

        return 0;
    }

    private void SwitchToSessionPartIfNeeded(
        FileInfo part,
        bool isContinuation,
        List<JournalEventEnvelope> events,
        List<string> errors
    )
    {
        if (PathsEqual(part.FullName, currentJournalPath))
        {
            return;
        }

        FlushPendingLine(events, errors);
        SetActiveJournalDirectory(part.DirectoryName);
        currentJournalPath = part.FullName;
        currentJournalOffset = 0;
        currentJournalContentOccurrences.Clear();
        previousJournalContentIdentity = null;
        pendingJournalBytes = [];
        // Only verified predecessors share a running session. A continuation
        // header must not reset commander, vehicle or workspace reducers.
        currentJournalIsContinuation = isContinuation;
    }

    private void RecordPendingBootstrapBoundaries(IReadOnlyList<FileInfo> parts, int startIndex, int endExclusive)
    {
        for (int index = startIndex; index < endExclusive; index++)
        {
            pendingBoundaryCheckCatchUpPaths.Add(parts[index].FullName);
        }
    }

    private bool HasPendingSessionBoundary(FileInfo sessionLatest) =>
        pendingBoundaryCheckCatchUpPaths.Contains(sessionLatest.FullName)
        || startupUnverifiedJournalLengths.ContainsKey(sessionLatest.FullName);

    private bool ApplySessionBoundaryIfNeeded(
        IReadOnlyList<FileInfo> parts,
        int start,
        ref int endExclusive,
        ref long? checkpointByteOffset
    )
    {
        if (!hasCompletedFirstPoll && switchResume is not null)
        {
            ApplySwitchResumeBoundary(parts, start, isInitialPoll: true, ref endExclusive, ref checkpointByteOffset);
            return true;
        }

        if (!hasCompletedFirstPoll)
        {
            return false;
        }

        int boundarySearchStart = ResolveDeferredBoundarySearchStart(parts, start);
        if (boundarySearchStart >= parts.Count)
        {
            return false;
        }

        if (switchResume is not null)
        {
            if (HasPendingCatchUpBoundary(parts, boundarySearchStart))
            {
                ApplySwitchResumeBoundary(
                    parts,
                    boundarySearchStart,
                    isInitialPoll: false,
                    ref endExclusive,
                    ref checkpointByteOffset
                );
                return true;
            }

            return false;
        }

        if (
            TryResolveStartupUnverifiedBoundary(
                parts,
                boundarySearchStart,
                out int startupBoundaryIndex,
                out long startupBoundaryOffset
            )
        )
        {
            pollForcesBootstrapRead = true;
            endExclusive = startupBoundaryIndex + 1;
            checkpointByteOffset = startupBoundaryOffset;
            if (startupBoundaryIndex < parts.Count - 1)
            {
                lastPollHasPendingLiveTail = true;
            }
        }

        return false;
    }

    private int ResolveDeferredBoundarySearchStart(IReadOnlyList<FileInfo> parts, int start)
    {
        if (PathsEqual(parts[start].FullName, currentJournalPath) && !HasPendingSessionBoundary(parts[start]))
        {
            return start + 1;
        }

        return start;
    }

    private bool HasPendingCatchUpBoundary(IReadOnlyList<FileInfo> parts, int start) =>
        parts.Skip(start).Any(part => pendingBoundaryCheckCatchUpPaths.Contains(part.FullName));

    private void ApplySwitchResumeBoundary(
        IReadOnlyList<FileInfo> parts,
        int start,
        bool isInitialPoll,
        ref int endExclusive,
        ref long? checkpointByteOffset
    )
    {
        (int? authoritativeIndex, long? authoritativeOffset) = FindAuthoritativeBoundary(parts, start: 0);
        if (authoritativeIndex is { } authIndex)
        {
            if (authIndex < start)
            {
                // The session's confirmed checkpoint was already crossed in an earlier part,
                // so remaining continuation parts stay live instead of falling back to startup baselines.
                if (isInitialPoll)
                {
                    firstPollRequiresBootstrap = false;
                }

                return;
            }

            ApplyBootstrapBoundary(
                parts,
                authIndex,
                authoritativeOffset,
                isInitialPoll,
                ref endExclusive,
                ref checkpointByteOffset
            );
            return;
        }

        (int? baselineIndex, long? baselineOffset) = FindBaselineBoundary(parts, start);
        if (baselineIndex is { } baseIndex)
        {
            ApplyBootstrapBoundary(
                parts,
                baseIndex,
                baselineOffset,
                isInitialPoll,
                ref endExclusive,
                ref checkpointByteOffset
            );
            return;
        }

        bool provenNewSinceBaseline = IsProvenNewSinceBaseline(parts, start);
        if (isInitialPoll)
        {
            firstPollRequiresBootstrap = !provenNewSinceBaseline;
        }
        else if (!provenNewSinceBaseline)
        {
            pollForcesBootstrapRead = true;
        }
    }

    private void ApplyBootstrapBoundary(
        IReadOnlyList<FileInfo> parts,
        int boundaryIndex,
        long? boundaryByteOffset,
        bool isInitialPoll,
        ref int endExclusive,
        ref long? checkpointByteOffset
    )
    {
        if (isInitialPoll)
        {
            firstPollRequiresBootstrap = true;
        }
        else
        {
            pollForcesBootstrapRead = true;
        }

        endExclusive = boundaryIndex + 1;
        checkpointByteOffset = boundaryByteOffset;
        if (boundaryIndex < parts.Count - 1)
        {
            lastPollHasPendingLiveTail = true;
        }
    }

    private static readonly TimeSpan BaselineClockSkewTolerance = TimeSpan.FromSeconds(5);

    private bool IsProvenNewSinceBaseline(IReadOnlyList<FileInfo> parts, int start)
    {
        if (switchResume?.Baseline is not { } baseline || start >= parts.Count)
        {
            return false;
        }

        DateTimeOffset minimumStartUtc = baseline.CapturedAtUtc - BaselineClockSkewTolerance;
        for (int index = start; index < parts.Count; index++)
        {
            FileInfo part = parts[index];
            if (
                !baseline.IsDirectoryScanned(part.DirectoryName)
                || !journalIdentityCache.TryGetValue(part.FullName, out JournalIdentityCacheEntry? identity)
                || identity.EarliestTimestampUtc is not { } openedAtUtc
                || openedAtUtc < minimumStartUtc
            )
            {
                return false;
            }
        }

        return true;
    }

    private bool TryResolveStartupUnverifiedBoundary(
        IReadOnlyList<FileInfo> parts,
        int start,
        out int boundaryIndex,
        out long boundaryOffset
    )
    {
        for (int index = parts.Count - 1; index >= start; index--)
        {
            if (
                startupUnverifiedJournalLengths.TryGetValue(parts[index].FullName, out long recordedLength)
                && recordedLength > 0
            )
            {
                boundaryIndex = index;
                boundaryOffset = recordedLength;
                return true;
            }
        }

        boundaryIndex = -1;
        boundaryOffset = 0;
        return false;
    }

    private void RecordStartupUnverifiedJournalIfNeeded(FileInfo journal)
    {
        if (hasCompletedFirstPoll || switchResume is not null)
        {
            return;
        }

        long length;
        try
        {
            journal.Refresh();
            length = journal.Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            length = long.MaxValue;
        }

        if (length > 0)
        {
            startupUnverifiedJournalLengths.TryAdd(journal.FullName, length);
        }
    }

    private async Task<FileInfo?> SelectSwitchCatchUpSessionAsync(
        FileInfo[] journals,
        CancellationToken cancellationToken
    )
    {
        var scan = new SwitchCatchUpScanState();
        for (int index = 0; index < journals.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                await EvaluateSwitchCatchUpCandidateAsync(journals[index], index, scan, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                break;
            }
        }

        return FinalizeSwitchCatchUpSelection(journals, scan);
    }

    private async Task<bool> EvaluateSwitchCatchUpCandidateAsync(
        FileInfo journal,
        int index,
        SwitchCatchUpScanState scan,
        CancellationToken cancellationToken
    )
    {
        if (index > 0 && scan.CoveredPaths.Contains(journal.FullName))
        {
            return false;
        }

        int identityErrorsBefore = journalIdentityReadErrors.Count;
        ResolvedJournalIdentity? identity = await ResolveJournalIdentityAsync(journal, cancellationToken)
            .ConfigureAwait(false);
        string? frontierId = identity?.FrontierId;
        if (index == 0)
        {
            scan.NewestFrontierId = frontierId;
            await RecordNewestIdentifiedJournalAsync(journal, frontierId, cancellationToken).ConfigureAwait(false);
        }

        if (identity is null)
        {
            if (journalIdentityReadErrors.Count > identityErrorsBefore)
            {
                RecordUnresolvedCatchUpFailure(
                    journal,
                    scan.FirstMatchIndex,
                    scan.UnresolvedNewerPaths,
                    ref scan.HadUnresolvedOlderSession
                );
            }

            return false;
        }

        if (!MatchesTargetFrontierId(frontierId))
        {
            return false;
        }

        if (scan.FirstMatchIndex < 0)
        {
            scan.FirstMatchIndex = index;
        }

        foreach (FileInfo part in identity.Parts)
        {
            scan.CoveredPaths.Add(part.FullName);
        }

        return TryAddSwitchCatchUpSession(journal, identity.Parts, scan.CatchUpSessions);
    }

    private FileInfo? FinalizeSwitchCatchUpSelection(FileInfo[] journals, SwitchCatchUpScanState scan)
    {
        if (scan.CatchUpSessions.Count == 0 || scan.HadUnresolvedOlderSession)
        {
            isAwaitingCommanderIdentity = journals.Length > 0 && scan.NewestFrontierId is null;
            isCompanionDirectorySuperseded = IsDirectorySupersededByNewerJournal(journals, selectedJournal: null);
            return null;
        }

        hasSelectedSwitchCatchUpSessions = true;
        foreach (string path in scan.UnresolvedNewerPaths)
        {
            pendingUnresolvedCatchUpPaths.Add(path);
            pendingBoundaryCheckCatchUpPaths.Add(path);
        }

        isAutomaticIdentityScanIncomplete = false;
        isAwaitingCommanderIdentity = scan.FirstMatchIndex > 0 && scan.NewestFrontierId is null;
        scan.CatchUpSessions.Reverse();
        isCompanionDirectorySuperseded = IsDirectorySupersededByNewerJournal(journals, scan.CatchUpSessions[^1]);
        foreach (FileInfo session in scan.CatchUpSessions)
        {
            pendingSwitchCatchUpSessions.Enqueue(session);
        }

        return scan.CatchUpSessions[0];
    }

    private sealed class SwitchCatchUpScanState
    {
        public HashSet<string> CoveredPaths { get; } =
            new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        public List<FileInfo> CatchUpSessions { get; } = [];
        public List<string> UnresolvedNewerPaths { get; } = [];
        public string? NewestFrontierId { get; set; }
        public int FirstMatchIndex { get; set; } = -1;
        public bool HadUnresolvedOlderSession;
    }

    private void RecordUnresolvedCatchUpFailure(
        FileInfo journal,
        int firstMatchIndex,
        List<string> unresolvedNewerPaths,
        ref bool hadUnresolvedOlderSession
    )
    {
        if (firstMatchIndex >= 0)
        {
            if (!IsUnchangedPreBaselineHistoricalFile(journal))
            {
                hadUnresolvedOlderSession = true;
            }

            return;
        }

        unresolvedNewerPaths.Add(journal.FullName);
    }

    private bool IsUnchangedPreBaselineHistoricalFile(FileInfo journal)
    {
        if (
            switchResume?.Baseline is not { } baseline
            || !baseline.TryGetLength(journal.FullName, out long baselineLength)
        )
        {
            return false;
        }

        long currentLength;
        try
        {
            journal.Refresh();
            currentLength = journal.Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (currentLength > baselineLength)
        {
            return false;
        }

        if (
            switchResume.PersistedCheckpoint is { ByteOffset: > 0 } checkpoint
            && PathsEqual(journal.FullName, checkpoint.JournalPath)
            && currentLength > checkpoint.ByteOffset
        )
        {
            return false;
        }

        if (
            switchResume.ConfirmedOffsets is { Count: > 0 } confirmedOffsets
            && TryGetConfirmedOffset(confirmedOffsets, journal.FullName, out long confirmedOffset)
            && confirmedOffset > 0
            && currentLength > confirmedOffset
        )
        {
            return false;
        }

        return true;
    }

    private async Task<FileInfo?> SelectUnresolvedCatchUpSessionAsync(
        FileInfo[] journals,
        CancellationToken cancellationToken
    )
    {
        StringComparer pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var scan = new UnresolvedCatchUpScanState(pathComparer, pendingUnresolvedCatchUpPaths);

        for (int index = 0; index < journals.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                await ProcessUnresolvedCatchUpEntryAsync(journals[index], index, scan, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                break;
            }
        }

        return FinalizeUnresolvedCatchUpSelection(journals, scan);
    }

    private async Task<bool> ProcessUnresolvedCatchUpEntryAsync(
        FileInfo journal,
        int index,
        UnresolvedCatchUpScanState scan,
        CancellationToken cancellationToken
    )
    {
        if (PathsEqual(journal.FullName, currentJournalPath))
        {
            return await HandleCurrentJournalInUnresolvedScanAsync(journal, index, scan, cancellationToken)
                .ConfigureAwait(false);
        }

        bool isTrackedUnresolved = scan.RemainingUnresolved.Remove(journal.FullName);
        if (ShouldSkipUnresolvedScanEntry(journal, index, isTrackedUnresolved, scan))
        {
            return scan.IsScanComplete;
        }

        int identityErrorsBefore = journalIdentityReadErrors.Count;
        ResolvedJournalIdentity? identity = await ResolveJournalIdentityAsync(journal, cancellationToken)
            .ConfigureAwait(false);
        string? frontierId = identity?.FrontierId;
        if (index == 0)
        {
            scan.NewestFrontierId = frontierId;
            await RecordNewestIdentifiedJournalAsync(journal, frontierId, cancellationToken).ConfigureAwait(false);
        }

        if (identity is null)
        {
            RecordUnresolvedScanMiss(journal.FullName, index, identityErrorsBefore, scan);
            return scan.IsScanComplete;
        }

        if (!MatchesTargetFrontierId(frontierId))
        {
            pendingBoundaryCheckCatchUpPaths.Remove(journal.FullName);
            return scan.IsScanComplete;
        }

        RecordUnresolvedScanMatch(journal, index, identity, scan);
        return scan.IsScanComplete;
    }

    private async Task<bool> HandleCurrentJournalInUnresolvedScanAsync(
        FileInfo journal,
        int index,
        UnresolvedCatchUpScanState scan,
        CancellationToken cancellationToken
    )
    {
        scan.CurrentJournalFile = journal;
        scan.ReachedCurrentJournal = true;
        if (index == 0)
        {
            string? currentFrontierId = await ReadFrontierIdAsync(journal, cancellationToken).ConfigureAwait(false);
            scan.NewestFrontierId = currentFrontierId;
            await RecordNewestIdentifiedJournalAsync(journal, currentFrontierId, cancellationToken)
                .ConfigureAwait(false);
        }

        return scan.RemainingUnresolved.Count == 0;
    }

    private static bool ShouldSkipUnresolvedScanEntry(
        FileInfo journal,
        int index,
        bool isTrackedUnresolved,
        UnresolvedCatchUpScanState scan
    ) =>
        (scan.ReachedCurrentJournal && !isTrackedUnresolved)
        || (index > 0 && scan.CoveredPaths.Contains(journal.FullName));

    private void RecordUnresolvedScanMatch(
        FileInfo journal,
        int index,
        ResolvedJournalIdentity identity,
        UnresolvedCatchUpScanState scan
    )
    {
        foreach (FileInfo part in identity.Parts)
        {
            scan.CoveredPaths.Add(part.FullName);
        }

        scan.MatchingSessions.Add((index, journal));
        if (identity.Parts.Any(part => PathsEqual(part.FullName, currentJournalPath)))
        {
            scan.ReachedCurrentJournal = true;
        }
    }

    private void RecordUnresolvedScanMiss(
        string journalPath,
        int index,
        int identityErrorsBefore,
        UnresolvedCatchUpScanState scan
    )
    {
        if (journalIdentityReadErrors.Count > identityErrorsBefore)
        {
            scan.StillUnresolvedPaths.Add(journalPath);
            scan.MaxStillUnresolvedIndex = index;
            return;
        }

        pendingBoundaryCheckCatchUpPaths.Remove(journalPath);
    }

    private FileInfo? FinalizeUnresolvedCatchUpSelection(FileInfo[] journals, UnresolvedCatchUpScanState scan)
    {
        foreach (string missingPath in scan.RemainingUnresolved)
        {
            pendingBoundaryCheckCatchUpPaths.Remove(missingPath);
        }

        pendingUnresolvedCatchUpPaths.Clear();
        foreach (string path in scan.StillUnresolvedPaths)
        {
            pendingUnresolvedCatchUpPaths.Add(path);
        }

        List<FileInfo> readySessions =
            scan.MaxStillUnresolvedIndex < 0
                ? scan.MatchingSessions.Select(item => item.Journal).ToList()
                : scan
                    .MatchingSessions.Where(item => item.Index > scan.MaxStillUnresolvedIndex)
                    .Select(item => item.Journal)
                    .ToList();

        if (readySessions.Count == 0)
        {
            isAwaitingCommanderIdentity = journals.Length > 0 && scan.NewestFrontierId is null;
            isCompanionDirectorySuperseded = IsDirectorySupersededByNewerJournal(journals, scan.CurrentJournalFile);
            return scan.CurrentJournalFile;
        }

        isAutomaticIdentityScanIncomplete = false;
        isAwaitingCommanderIdentity = scan.MatchingSessions[0].Index > 0 && scan.NewestFrontierId is null;
        readySessions.Reverse();
        isCompanionDirectorySuperseded = IsDirectorySupersededByNewerJournal(journals, readySessions[^1]);
        foreach (FileInfo session in readySessions)
        {
            pendingSwitchCatchUpSessions.Enqueue(session);
        }

        return readySessions[0];
    }

    private sealed class UnresolvedCatchUpScanState(StringComparer pathComparer, HashSet<string> pendingUnresolvedPaths)
    {
        public HashSet<string> CoveredPaths { get; } = new(pathComparer);

        public List<(int Index, FileInfo Journal)> MatchingSessions { get; } = [];

        public HashSet<string> StillUnresolvedPaths { get; } = new(pathComparer);

        public HashSet<string> RemainingUnresolved { get; } = new(pendingUnresolvedPaths, pathComparer);

        public int MaxStillUnresolvedIndex { get; set; } = -1;

        public string? NewestFrontierId { get; set; }

        public FileInfo? CurrentJournalFile { get; set; }

        public bool ReachedCurrentJournal { get; set; }

        public bool IsScanComplete => ReachedCurrentJournal && RemainingUnresolved.Count == 0;
    }

    private bool TryAddSwitchCatchUpSession(
        FileInfo journal,
        IReadOnlyList<FileInfo> parts,
        List<FileInfo> catchUpSessions
    )
    {
        (int? boundaryPartIndex, long? boundaryByteOffset) = ResolveFirstPollBoundary(parts, start: 0);
        if (boundaryPartIndex is { } boundaryIndex)
        {
            FileInfo boundaryPart = parts[boundaryIndex];
            boundaryPart.Refresh();
            bool hasUnreadAfterBoundary =
                boundaryIndex < parts.Count - 1 || boundaryPart.Length > boundaryByteOffset!.Value;
            if (hasUnreadAfterBoundary || catchUpSessions.Count == 0)
            {
                catchUpSessions.Add(journal);
            }

            return true;
        }

        catchUpSessions.Add(journal);
        return !IsProvenNewSinceBaseline(parts, start: 0);
    }

    private (int? PartIndex, long? ByteOffset) ResolveFirstPollBoundary(IReadOnlyList<FileInfo> parts, int start)
    {
        (int? partIndex, long? byteOffset) = FindAuthoritativeBoundary(parts, start);
        return partIndex is not null ? (partIndex, byteOffset) : FindBaselineBoundary(parts, start);
    }

    private (int? PartIndex, long? ByteOffset) FindAuthoritativeBoundary(IReadOnlyList<FileInfo> parts, int start)
    {
        (int? partIndex, long? byteOffset) = FindPersistedCheckpointBoundary(parts, start);
        (int? confirmedIndex, long? confirmedOffset) = FindConfirmedOffsetBoundary(parts, start);
        return IsNewerBoundary(confirmedIndex, confirmedOffset, partIndex, byteOffset)
            ? (confirmedIndex, confirmedOffset)
            : (partIndex, byteOffset);
    }

    private (int? PartIndex, long? ByteOffset) FindPersistedCheckpointBoundary(IReadOnlyList<FileInfo> parts, int start)
    {
        if (switchResume?.PersistedCheckpoint is not { ByteOffset: > 0 } checkpoint)
        {
            return (null, null);
        }

        for (int index = start; index < parts.Count; index++)
        {
            if (PathsEqual(parts[index].FullName, checkpoint.JournalPath))
            {
                return (index, checkpoint.ByteOffset);
            }
        }

        return (null, null);
    }

    private (int? PartIndex, long? ByteOffset) FindConfirmedOffsetBoundary(IReadOnlyList<FileInfo> parts, int start)
    {
        if (switchResume?.ConfirmedOffsets is not { Count: > 0 } confirmedOffsets)
        {
            return (null, null);
        }

        for (int index = parts.Count - 1; index >= start; index--)
        {
            if (
                TryGetConfirmedOffset(confirmedOffsets, parts[index].FullName, out long confirmedOffset)
                && confirmedOffset > 0
            )
            {
                return (index, confirmedOffset);
            }
        }

        return (null, null);
    }

    private (int? PartIndex, long? ByteOffset) FindBaselineBoundary(IReadOnlyList<FileInfo> parts, int start)
    {
        if (switchResume?.Baseline is not { } baseline)
        {
            return (null, null);
        }

        for (int index = parts.Count - 1; index >= start; index--)
        {
            if (baseline.TryGetLength(parts[index].FullName, out long baseLength) && baseLength > 0)
            {
                return (index, baseLength);
            }
        }

        return (null, null);
    }

    private static bool IsNewerBoundary(
        int? candidateIndex,
        long? candidateOffset,
        int? currentIndex,
        long? currentOffset
    ) =>
        candidateIndex is not null
        && (
            currentIndex is null
            || candidateIndex.Value > currentIndex.Value
            || (candidateIndex.Value == currentIndex.Value && candidateOffset!.Value > currentOffset!.Value)
        );

    private static bool TryGetConfirmedOffset(
        IReadOnlyDictionary<string, long> confirmedOffsets,
        string journalPath,
        out long offset
    )
    {
        if (confirmedOffsets.TryGetValue(journalPath, out offset))
        {
            return true;
        }

        string resolved = JournalBaselineSnapshot.ResolveFilePath(journalPath);
        if (confirmedOffsets.TryGetValue(resolved, out offset))
        {
            return true;
        }

        long matchedOffset = confirmedOffsets
            .Where(entry => PathsEqual(JournalBaselineSnapshot.ResolveFilePath(entry.Key), resolved))
            .Select(entry => entry.Value)
            .DefaultIfEmpty(0)
            .Max();
        if (matchedOffset > 0)
        {
            offset = matchedOffset;
            return true;
        }

        offset = 0;
        return false;
    }

    // A temporarily missing continuation must not make its already-consumed
    // predecessor look like a new journal. Keep the cursor until it returns.
    private async Task<bool> IsEarlierSessionPartAsync(FileInfo candidate, CancellationToken cancellationToken)
    {
        if (
            currentJournalPath is null
            || !PathsEqual(Path.GetDirectoryName(currentJournalPath)!, candidate.DirectoryName)
            || !journalIdentityCache.TryGetValue(currentJournalPath, out JournalIdentityCacheEntry? current)
            || current.Part is not { } currentPart
        )
        {
            return false;
        }
        JournalIdentityCacheEntry? identity = await ReadJournalIdentityAsync(candidate, cancellationToken)
            .ConfigureAwait(false);
        return identity?.Part is { } candidatePart
            && string.Equals(currentPart.Series, candidatePart.Series, StringComparison.Ordinal)
            && candidatePart.Number < currentPart.Number;
    }

    private static string? ReadDeclaredFrontierId(JournalEventEnvelope entry) =>
        entry.EventName is "Commander" or "LoadGame"
        && entry.Payload.TryGetProperty("FID", out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
