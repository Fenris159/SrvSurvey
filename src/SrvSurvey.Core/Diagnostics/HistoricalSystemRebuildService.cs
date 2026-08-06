using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Diagnostics;

public sealed class HistoricalSystemRebuildService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string dataDirectory;
    private readonly string journalDirectory;
    private readonly string backupDirectory;
    private readonly Func<DateTimeOffset> currentTime;
    private readonly Func<string, Exception?>? activationFailure;

    public HistoricalSystemRebuildService(
        string dataDirectory,
        string journalDirectory,
        string backupDirectory,
        Func<DateTimeOffset>? currentTime = null)
        : this(
            dataDirectory,
            journalDirectory,
            backupDirectory,
            currentTime,
            null)
    {
    }

    internal HistoricalSystemRebuildService(
        string dataDirectory,
        string journalDirectory,
        string backupDirectory,
        Func<DateTimeOffset>? currentTime,
        Func<string, Exception?>? activationFailure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        this.dataDirectory = Path.GetFullPath(dataDirectory);
        this.journalDirectory = Path.GetFullPath(journalDirectory);
        this.backupDirectory = Path.GetFullPath(backupDirectory);
        this.currentTime = currentTime ?? (() => DateTimeOffset.Now);
        this.activationFailure = activationFailure;
        ValidateBackupLocation(backupDirectory);
    }

    public async Task<HistoricalSystemRebuildResult> RebuildAsync(
        string frontierId,
        string? commanderName,
        DateTimeOffset startTime,
        IProgress<HistoricalSystemRebuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFrontierId(frontierId);
        if (!Directory.Exists(journalDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The journal folder does not exist: {journalDirectory}");
        }

        var reconstruction = await ReconstructAsync(
                frontierId,
                startTime,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (reconstruction.Systems.Count == 0)
        {
            return reconstruction.CreateEmptyResult();
        }

        var store = new LegacySystemDataFileStore(dataDirectory);
        return await store.ExecuteProfileWriteAsync(
                frontierId,
                token => ActivateAsync(
                    frontierId,
                    commanderName,
                    reconstruction,
                    progress,
                    token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ReconstructionResult> ReconstructAsync(
        string frontierId,
        DateTimeOffset startTime,
        IProgress<HistoricalSystemRebuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var files = CollectJournalCandidates(startTime, warnings);
        var systems = new Dictionary<long, ReconstructedSystem>();
        var stats = new ReconstructionStats();
        await ProcessJournalCandidatesAsync(
                files,
                frontierId,
                systems,
                stats,
                warnings,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        return new ReconstructionResult(
            files.Length,
            stats.ProcessedFiles,
            stats.SkippedCommanderFiles,
            stats.SkippedRecentFiles,
            stats.SkippedLegacyFiles,
            stats.MalformedLines,
            stats.AppliedEvents,
            systems.Values
                .Where(IsValidReconstructedSystem)
                .ToArray(),
            warnings);
    }

    private JournalCandidate[] CollectJournalCandidates(
        DateTimeOffset startTime,
        List<string> warnings)
    {
        return new DirectoryInfo(journalDirectory)
            .EnumerateFiles("Journal.*.log", SearchOption.TopDirectoryOnly)
            .Select(file => new JournalCandidate(
                file,
                JournalHistoryAnalyzer.TryGetJournalTimestamp(
                    file.Name,
                    out var openedAt)
                        ? openedAt
                        : null))
            .Where(candidate => IsEligibleJournalCandidate(candidate, startTime, warnings))
            .OrderBy(candidate => candidate.OpenedAt)
            .ThenBy(candidate => candidate.File.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsEligibleJournalCandidate(
        JournalCandidate candidate,
        DateTimeOffset startTime,
        List<string> warnings)
    {
        if (candidate.OpenedAt is null)
        {
            warnings.Add(
                $"Ignored {candidate.File.Name} because its journal timestamp is invalid.");
            return false;
        }

        return candidate.OpenedAt > startTime
            && candidate.File.LastWriteTimeUtc >= startTime.UtcDateTime;
    }

    private async Task ProcessJournalCandidatesAsync(
        JournalCandidate[] files,
        string frontierId,
        Dictionary<long, ReconstructedSystem> systems,
        ReconstructionStats stats,
        List<string> warnings,
        IProgress<HistoricalSystemRebuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = files[index];
            Report(
                progress,
                "Reading journals",
                index,
                files.Length,
                candidate.File.Name);
            await TryProcessJournalFileAsync(
                    candidate,
                    frontierId,
                    systems,
                    stats,
                    warnings,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        Report(
            progress,
            "Reading journals",
            files.Length,
            files.Length,
            string.Empty);
    }

    private async Task<bool> TryProcessJournalFileAsync(
        JournalCandidate candidate,
        string frontierId,
        Dictionary<long, ReconstructedSystem> systems,
        ReconstructionStats stats,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        JournalReadResult read;
        try
        {
            read = await ReadJournalAsync(
                    candidate.File,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"{candidate.File.Name}: {exception.Message}");
            return false;
        }

        stats.MalformedLines += read.MalformedLineCount;
        if (!string.Equals(
                read.FrontierId,
                frontierId,
                StringComparison.OrdinalIgnoreCase))
        {
            stats.SkippedCommanderFiles++;
            return false;
        }

        if (!read.IsShutdown
            && candidate.OpenedAt > currentTime().AddDays(-2))
        {
            stats.SkippedRecentFiles++;
            return false;
        }

        if (!read.IsOdyssey)
        {
            stats.SkippedLegacyFiles++;
            return false;
        }

        stats.ProcessedFiles++;
        ApplyJournalEvents(read, candidate, systems, stats, cancellationToken);
        return true;
    }

    private static void ApplyJournalEvents(
        JournalReadResult read,
        JournalCandidate candidate,
        Dictionary<long, ReconstructedSystem> systems,
        ReconstructionStats stats,
        CancellationToken cancellationToken)
    {
        long? currentAddress = null;
        foreach (var journalEvent in read.Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            currentAddress = UpdateCurrentAddress(journalEvent, currentAddress);
            if (currentAddress is not { } address)
            {
                continue;
            }

            ApplyJournalEventToSystem(
                journalEvent,
                candidate,
                address,
                systems,
                stats);
        }
    }

    private static long? UpdateCurrentAddress(
        JournalEventEnvelope journalEvent,
        long? currentAddress)
    {
        if (journalEvent.EventName is not ("Location" or "FSDJump" or "CarrierJump"))
        {
            return currentAddress;
        }

        var address = GetInt64(journalEvent.Payload, "SystemAddress");
        return address is > 0 ? address : null;
    }

    private static void ApplyJournalEventToSystem(
        JournalEventEnvelope journalEvent,
        JournalCandidate candidate,
        long address,
        Dictionary<long, ReconstructedSystem> systems,
        ReconstructionStats stats)
    {
        if (!systems.TryGetValue(address, out var system))
        {
            system = new ReconstructedSystem(new SystemScanState());
            systems.Add(address, system);
        }

        if (!system.State.Apply(journalEvent))
        {
            return;
        }

        stats.AppliedEvents++;
        var timestamp = journalEvent.Timestamp ?? candidate.OpenedAt!.Value;
        system.FirstVisited = MinTimestamp(system.FirstVisited, timestamp);
        system.LastVisited = MaxTimestamp(system.LastVisited, timestamp);
    }

    private static DateTimeOffset MinTimestamp(
        DateTimeOffset? current,
        DateTimeOffset candidate) =>
        current is null || candidate < current ? candidate : current.Value;

    private static DateTimeOffset MaxTimestamp(
        DateTimeOffset? current,
        DateTimeOffset candidate) =>
        current is null || candidate > current ? candidate : current.Value;

    private static bool IsValidReconstructedSystem(ReconstructedSystem system)
    {
        if (system.FirstVisited is null || system.LastVisited is null)
        {
            return false;
        }

        var snapshot = system.State.CreateSnapshot();
        return snapshot.SystemAddress is > 0
            && !string.IsNullOrWhiteSpace(snapshot.SystemName);
    }

    private sealed class ReconstructionStats
    {
        public int ProcessedFiles { get; set; }
        public int SkippedCommanderFiles { get; set; }
        public int SkippedRecentFiles { get; set; }
        public int SkippedLegacyFiles { get; set; }
        public int MalformedLines { get; set; }
        public int AppliedEvents { get; set; }
    }

    private async Task<HistoricalSystemRebuildResult> ActivateAsync(
        string frontierId,
        string? commanderName,
        ReconstructionResult reconstruction,
        IProgress<HistoricalSystemRebuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        var operationId = Guid.NewGuid().ToString("N");
        var operationName = $"historical-system-rebuild-"
            + $"{currentTime().UtcDateTime:yyyyMMddTHHmmssZ}-{operationId}";
        var finalBackup = Path.Combine(backupDirectory, operationName);
        var stagingBackup = finalBackup + ".preparing";
        var candidateDirectory = Path.Combine(stagingBackup, "candidates");
        var originalDirectory = Path.Combine(stagingBackup, "originals");
        var systemDirectory = Path.Combine(dataDirectory, "systems", frontierId);
        var entries = new List<ActivationEntry>();
        var warnings = reconstruction.Warnings.ToList();
        try
        {
            Directory.CreateDirectory(candidateDirectory);
            Directory.CreateDirectory(originalDirectory);
            var existingPaths = Directory.Exists(systemDirectory)
                ? Directory.EnumerateFiles(
                        systemDirectory,
                        "*.json",
                        SearchOption.TopDirectoryOnly)
                    .Order(StringComparer.Ordinal)
                    .ToArray()
                : [];
            for (var index = 0; index < reconstruction.Systems.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reconstructed = reconstruction.Systems[index];
                var snapshot = reconstructed.State.CreateSnapshot();
                var target = FindTargetPath(
                    systemDirectory,
                    existingPaths,
                    snapshot.SystemName!,
                    snapshot.SystemAddress!.Value);
                Report(
                    progress,
                    "Preparing verified backup",
                    index,
                    reconstruction.Systems.Count,
                    Path.GetFileName(target));
                var prepared = await TryPrepareActivationEntryAsync(
                        new ActivationPrepareRequest
                        {
                            Reconstructed = reconstructed,
                            Snapshot = snapshot,
                            Target = target,
                            OriginalDirectory = originalDirectory,
                            CandidateDirectory = candidateDirectory,
                            CommanderName = commanderName,
                            Warnings = warnings,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                if (prepared is null)
                {
                    continue;
                }

                entries.Add(prepared);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (entries.Count == 0)
            {
                Directory.Delete(stagingBackup, true);
                return reconstruction.CreateResult(
                    0,
                    0,
                    null,
                    warnings);
            }

            await WriteManifestAsync(
                    stagingBackup,
                    frontierId,
                    entries,
                    cancellationToken)
                .ConfigureAwait(false);
            Directory.CreateDirectory(backupDirectory);
            Directory.Move(stagingBackup, finalBackup);
            var relocatedEntries = entries
                .Select(entry => entry with
                {
                    CandidatePath = Path.Combine(
                        finalBackup,
                        "candidates",
                        Path.GetFileName(entry.CandidatePath)),
                })
                .ToArray();
            await ActivateEntriesAsync(
                    relocatedEntries,
                    finalBackup,
                    progress)
                .ConfigureAwait(false);
            return reconstruction.CreateResult(
                relocatedEntries.Count(entry => entry.Existed),
                relocatedEntries.Count(entry => !entry.Existed),
                finalBackup,
                warnings);
        }
        catch
        {
            if (Directory.Exists(stagingBackup))
            {
                Directory.Delete(stagingBackup, true);
            }

            throw;
        }
    }

    private sealed class ActivationPrepareRequest
    {
        public required ReconstructedSystem Reconstructed { get; init; }
        public required SystemScanSnapshot Snapshot { get; init; }
        public required string Target { get; init; }
        public required string OriginalDirectory { get; init; }
        public required string CandidateDirectory { get; init; }
        public string? CommanderName { get; init; }
        public required List<string> Warnings { get; init; }
    }

    private static async Task<ActivationEntry?> TryPrepareActivationEntryAsync(
        ActivationPrepareRequest request,
        CancellationToken cancellationToken)
    {
        JsonObject? existing = null;
        string? originalHash = null;
        if (File.Exists(request.Target))
        {
            try
            {
                existing = await ReadObjectAsync(request.Target, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or JsonException
                    or InvalidDataException)
            {
                request.Warnings.Add(
                    $"{Path.GetFileName(request.Target)} was malformed and was not overwritten: "
                        + exception.Message);
                return null;
            }

            var originalPath = Path.Combine(
                request.OriginalDirectory,
                Path.GetFileName(request.Target));
            File.Copy(request.Target, originalPath, false);
            originalHash = await ComputeHashAsync(request.Target, cancellationToken)
                .ConfigureAwait(false);
            var backupHash = await ComputeHashAsync(
                    originalPath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(
                    originalHash,
                    backupHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The backup for {Path.GetFileName(request.Target)} did not match its source.");
            }
        }

        JsonObject merged;
        try
        {
            merged = LegacySystemSnapshotMerger.Merge(
                existing,
                request.Snapshot,
                request.CommanderName,
                request.Reconstructed.FirstVisited!.Value,
                request.Reconstructed.LastVisited!.Value);
        }
        catch (InvalidDataException exception)
        {
            request.Warnings.Add(
                $"{Path.GetFileName(request.Target)} was not rebuilt: {exception.Message}");
            return null;
        }

        var candidatePath = Path.Combine(
            request.CandidateDirectory,
            $"{request.Snapshot.SystemAddress!.Value}.json");
        await WriteObjectAsync(candidatePath, merged, cancellationToken)
            .ConfigureAwait(false);
        _ = await ReadObjectAsync(candidatePath, cancellationToken)
            .ConfigureAwait(false);
        var candidateHash = await ComputeHashAsync(
                candidatePath,
                cancellationToken)
            .ConfigureAwait(false);
        return new ActivationEntry(
            request.Target,
            candidatePath,
            File.Exists(request.Target),
            originalHash,
            candidateHash);
    }

    private async Task ActivateEntriesAsync(
        IReadOnlyList<ActivationEntry> entries,
        string finalBackup,
        IProgress<HistoricalSystemRebuildProgress>? progress)
    {
        var activated = new List<ActivationEntry>();
        try
        {
            await ActivateAllEntriesAsync(entries, activated, progress)
                .ConfigureAwait(false);
            Report(
                progress,
                "Activating reconstructed systems",
                entries.Count,
                entries.Count,
                string.Empty);
        }
        catch (Exception activationException)
        {
            await FailActivationAsync(activated, finalBackup, activationException)
                .ConfigureAwait(false);
        }
    }

    private async Task ActivateAllEntriesAsync(
        IReadOnlyList<ActivationEntry> entries,
        List<ActivationEntry> activated,
        IProgress<HistoricalSystemRebuildProgress>? progress)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            Report(
                progress,
                "Activating reconstructed systems",
                index,
                entries.Count,
                Path.GetFileName(entry.TargetPath));
            await ActivateSingleEntryAsync(entry).ConfigureAwait(false);
            activated.Add(entry);
        }
    }

    private async Task ActivateSingleEntryAsync(ActivationEntry entry)
    {
        if (activationFailure?.Invoke(entry.TargetPath) is { } failure)
        {
            throw failure;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(entry.TargetPath)!);
        var temporaryPath = $"{entry.TargetPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(entry.CandidatePath, temporaryPath, false);
            await VerifyHashAsync(
                    temporaryPath,
                    entry.CandidateHash,
                    $"The staged candidate for {Path.GetFileName(entry.TargetPath)} failed verification.")
                .ConfigureAwait(false);
            File.Move(temporaryPath, entry.TargetPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        await VerifyHashAsync(
                entry.TargetPath,
                entry.CandidateHash,
                $"The activated candidate for {Path.GetFileName(entry.TargetPath)} failed verification.")
            .ConfigureAwait(false);
    }

    private static async Task VerifyHashAsync(
        string path,
        string expectedHash,
        string failureMessage)
    {
        var actualHash = await ComputeHashAsync(path, CancellationToken.None)
            .ConfigureAwait(false);
        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(failureMessage);
        }
    }

    private static async Task FailActivationAsync(
        List<ActivationEntry> activated,
        string finalBackup,
        Exception activationException)
    {
        var rollbackErrors = await RollBackAsync(activated, finalBackup)
            .ConfigureAwait(false);
        if (rollbackErrors.Count > 0)
        {
            throw new AggregateException(
                $"Historical system activation failed and rollback was incomplete. Verified backup: {finalBackup}",
                rollbackErrors.Prepend(activationException));
        }

        throw new InvalidOperationException(
            $"Historical system activation failed and was rolled back. Verified backup: {finalBackup}",
            activationException);
    }

    private static async Task<IReadOnlyList<Exception>> RollBackAsync(
        IReadOnlyList<ActivationEntry> activated,
        string finalBackup)
    {
        var errors = new List<Exception>();
        foreach (var entry in activated.Reverse())
        {
            try
            {
                if (!entry.Existed)
                {
                    if (File.Exists(entry.TargetPath))
                    {
                        File.Delete(entry.TargetPath);
                    }

                    continue;
                }

                var originalPath = Path.Combine(
                    finalBackup,
                    "originals",
                    Path.GetFileName(entry.TargetPath));
                var temporaryPath = $"{entry.TargetPath}.{Guid.NewGuid():N}.rollback";
                try
                {
                    File.Copy(originalPath, temporaryPath, false);
                    File.Move(temporaryPath, entry.TargetPath, true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }

                var restoredHash = await ComputeHashAsync(
                        entry.TargetPath,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (!string.Equals(
                        restoredHash,
                        entry.OriginalHash,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Rollback verification failed for {entry.TargetPath}.");
                }
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException)
            {
                errors.Add(exception);
            }
        }

        return errors;
    }

    private static async Task<JournalReadResult> ReadJournalAsync(
        FileInfo file,
        CancellationToken cancellationToken)
    {
        var events = new List<JournalEventEnvelope>();
        var malformed = 0;
        string? frontierId = null;
        var isShutdown = false;
        var isOdyssey = true;
        await using var stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
               is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!JournalEventEnvelope.TryParse(
                    line,
                    out var journalEvent,
                    out _)
                || journalEvent is null)
            {
                malformed++;
                continue;
            }

            events.Add(journalEvent);
            if (journalEvent.EventName is "Commander" or "LoadGame")
            {
                frontierId = GetString(journalEvent.Payload, "FID")
                    ?? frontierId;
            }
            else if (journalEvent.EventName == "Fileheader")
            {
                isOdyssey = GetBoolean(journalEvent.Payload, "Odyssey")
                    ?? true;
            }
            else if (journalEvent.EventName == "Shutdown")
            {
                isShutdown = true;
            }
        }

        return new JournalReadResult(
            frontierId,
            isShutdown,
            isOdyssey,
            events,
            malformed);
    }

    private static async Task<JsonObject> ReadObjectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var node = await JsonNode.ParseAsync(
                stream,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return node as JsonObject
            ?? throw new InvalidDataException(
                $"{path} does not contain a JSON object.");
    }

    private static async Task WriteObjectAsync(
        string path,
        JsonObject value,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(
                stream,
                value,
                SerializerOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteManifestAsync(
        string stagingBackup,
        string frontierId,
        IReadOnlyList<ActivationEntry> entries,
        CancellationToken cancellationToken)
    {
        var manifest = new HistoricalSystemRebuildManifest(
            frontierId,
            entries.Select(entry => new HistoricalSystemRebuildManifestEntry(
                Path.GetFileName(entry.TargetPath),
                entry.Existed,
                entry.OriginalHash,
                entry.CandidateHash)).ToArray());
        var path = Path.Combine(stagingBackup, "manifest.json");
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(
                stream,
                manifest,
                SerializerOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ComputeHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static string FindTargetPath(
        string systemDirectory,
        IReadOnlyList<string> existingPaths,
        string systemName,
        long systemAddress)
    {
        var addressSuffix = $"_{systemAddress}.json";
        var addressMatch = existingPaths.FirstOrDefault(path =>
            Path.GetFileName(path).EndsWith(
                addressSuffix,
                StringComparison.OrdinalIgnoreCase));
        if (addressMatch is not null)
        {
            return addressMatch;
        }

        var namePrefix = LegacySystemDataFileStore.MakeSafeFileName(systemName)
            + "_";
        return existingPaths.FirstOrDefault(path =>
                Path.GetFileName(path).StartsWith(
                    namePrefix,
                    StringComparison.OrdinalIgnoreCase))
            ?? Path.Combine(
                systemDirectory,
                LegacySystemDataFileStore.MakeSafeFileName(
                    $"{systemName}_{systemAddress}.json"));
    }

    private void ValidateBackupLocation(string candidateBackupDirectory)
    {
        var systemsDirectory = Path.GetFullPath(Path.Combine(
            dataDirectory,
            "systems"));
        var normalizedBackupDirectory = Path.GetFullPath(
            candidateBackupDirectory);
        if (PathsOverlap(systemsDirectory, normalizedBackupDirectory))
        {
            throw new ArgumentException(
                "Historical rebuild backups must be outside the active systems directory.",
                nameof(candidateBackupDirectory));
        }
    }

    private static bool PathsOverlap(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var firstWithSeparator = Path.TrimEndingDirectorySeparator(first)
            + Path.DirectorySeparatorChar;
        var secondWithSeparator = Path.TrimEndingDirectorySeparator(second)
            + Path.DirectorySeparatorChar;
        return firstWithSeparator.StartsWith(secondWithSeparator, comparison)
            || secondWithSeparator.StartsWith(firstWithSeparator, comparison);
    }

    private static void ValidateFrontierId(string frontierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        if (frontierId is "." or ".."
            || !string.Equals(
                Path.GetFileName(frontierId),
                frontierId,
                StringComparison.Ordinal)
            || frontierId.IndexOfAny(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new ArgumentException(
                "The Frontier ID must be a folder name, not a path.",
                nameof(frontierId));
        }
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static bool? GetBoolean(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out number)
                    ? number
                    : null;
    }

    private static void Report(
        IProgress<HistoricalSystemRebuildProgress>? progress,
        string stage,
        int processed,
        int total,
        string currentFile)
    {
        progress?.Report(new HistoricalSystemRebuildProgress(
            stage,
            processed,
            total,
            currentFile));
    }

    private sealed record JournalCandidate(
        FileInfo File,
        DateTimeOffset? OpenedAt);

    private sealed record JournalReadResult(
        string? FrontierId,
        bool IsShutdown,
        bool IsOdyssey,
        IReadOnlyList<JournalEventEnvelope> Events,
        int MalformedLineCount);

    private sealed class ReconstructedSystem(SystemScanState state)
    {
        public SystemScanState State { get; } = state;

        public DateTimeOffset? FirstVisited { get; set; }

        public DateTimeOffset? LastVisited { get; set; }
    }

    private sealed record ReconstructionResult(
        int CandidateJournalFileCount,
        int ProcessedJournalFileCount,
        int SkippedCommanderFileCount,
        int SkippedRecentFileCount,
        int SkippedLegacyFileCount,
        int MalformedLineCount,
        int AppliedExplorationEventCount,
        IReadOnlyList<ReconstructedSystem> Systems,
        IReadOnlyList<string> Warnings)
    {
        public HistoricalSystemRebuildResult CreateEmptyResult() =>
            CreateResult(0, 0, null, Warnings);

        public HistoricalSystemRebuildResult CreateResult(
            int updatedFileCount,
            int createdFileCount,
            string? backupPath,
            IReadOnlyList<string> warnings) =>
            new(
                CandidateJournalFileCount,
                ProcessedJournalFileCount,
                SkippedCommanderFileCount,
                SkippedRecentFileCount,
                SkippedLegacyFileCount,
                MalformedLineCount,
                AppliedExplorationEventCount,
                Systems.Count,
                updatedFileCount,
                createdFileCount,
                backupPath,
                warnings);
    }

    private sealed record ActivationEntry(
        string TargetPath,
        string CandidatePath,
        bool Existed,
        string? OriginalHash,
        string CandidateHash);

    private sealed record HistoricalSystemRebuildManifest(
        string FrontierId,
        IReadOnlyList<HistoricalSystemRebuildManifestEntry> Files);

    private sealed record HistoricalSystemRebuildManifestEntry(
        string FileName,
        bool Existed,
        string? OriginalSha256,
        string CandidateSha256);
}

public sealed record HistoricalSystemRebuildProgress(
    string Stage,
    int ProcessedCount,
    int TotalCount,
    string CurrentFile);

public sealed record HistoricalSystemRebuildResult(
    int CandidateJournalFileCount,
    int ProcessedJournalFileCount,
    int SkippedCommanderFileCount,
    int SkippedRecentFileCount,
    int SkippedLegacyFileCount,
    int MalformedLineCount,
    int AppliedExplorationEventCount,
    int ReconstructedSystemCount,
    int UpdatedSystemFileCount,
    int CreatedSystemFileCount,
    string? BackupDirectory,
    IReadOnlyList<string> Warnings);
