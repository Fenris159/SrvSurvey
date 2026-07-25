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
        ValidateBackupLocation();
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
        var files = new DirectoryInfo(journalDirectory)
            .EnumerateFiles("Journal.*.log", SearchOption.TopDirectoryOnly)
            .Select(file => new JournalCandidate(
                file,
                JournalHistoryAnalyzer.TryGetJournalTimestamp(
                    file.Name,
                    out var openedAt)
                        ? openedAt
                        : null))
            .Where(candidate =>
            {
                if (candidate.OpenedAt is null)
                {
                    warnings.Add(
                        $"Ignored {candidate.File.Name} because its journal timestamp is invalid.");
                    return false;
                }

                return candidate.OpenedAt > startTime
                    && candidate.File.LastWriteTimeUtc >= startTime.UtcDateTime;
            })
            .OrderBy(candidate => candidate.OpenedAt)
            .ThenBy(candidate => candidate.File.Name, StringComparer.Ordinal)
            .ToArray();
        var systems = new Dictionary<long, ReconstructedSystem>();
        var processedFiles = 0;
        var skippedCommanderFiles = 0;
        var skippedRecentFiles = 0;
        var skippedLegacyFiles = 0;
        var malformedLines = 0;
        var appliedEvents = 0;
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
                continue;
            }

            malformedLines += read.MalformedLineCount;
            if (!string.Equals(
                    read.FrontierId,
                    frontierId,
                    StringComparison.OrdinalIgnoreCase))
            {
                skippedCommanderFiles++;
                continue;
            }

            if (!read.IsShutdown
                && candidate.OpenedAt > currentTime().AddDays(-2))
            {
                skippedRecentFiles++;
                continue;
            }

            if (!read.IsOdyssey)
            {
                skippedLegacyFiles++;
                continue;
            }

            processedFiles++;
            long? currentAddress = null;
            foreach (var journalEvent in read.Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (journalEvent.EventName is "Location" or "FSDJump" or "CarrierJump")
                {
                    currentAddress = GetInt64(
                        journalEvent.Payload,
                        "SystemAddress");
                    if (currentAddress is not > 0)
                    {
                        currentAddress = null;
                        continue;
                    }
                }

                if (currentAddress is not { } address)
                {
                    continue;
                }

                if (!systems.TryGetValue(address, out var system))
                {
                    system = new ReconstructedSystem(new SystemScanState());
                    systems.Add(address, system);
                }

                if (system.State.Apply(journalEvent))
                {
                    appliedEvents++;
                    var timestamp = journalEvent.Timestamp
                        ?? candidate.OpenedAt!.Value;
                    system.FirstVisited = system.FirstVisited is null
                        || timestamp < system.FirstVisited
                            ? timestamp
                            : system.FirstVisited;
                    system.LastVisited = system.LastVisited is null
                        || timestamp > system.LastVisited
                            ? timestamp
                            : system.LastVisited;
                }
            }
        }

        Report(
            progress,
            "Reading journals",
            files.Length,
            files.Length,
            string.Empty);
        return new ReconstructionResult(
            files.Length,
            processedFiles,
            skippedCommanderFiles,
            skippedRecentFiles,
            skippedLegacyFiles,
            malformedLines,
            appliedEvents,
            systems.Values
                .Where(system =>
                    system.FirstVisited is not null
                    && system.LastVisited is not null
                    && system.State.CreateSnapshot().SystemAddress is > 0
                    && !string.IsNullOrWhiteSpace(
                        system.State.CreateSnapshot().SystemName))
                .ToArray(),
            warnings);
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
                JsonObject? existing = null;
                string? originalHash = null;
                if (File.Exists(target))
                {
                    try
                    {
                        existing = await ReadObjectAsync(target, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception) when (
                        exception is IOException
                            or UnauthorizedAccessException
                            or JsonException
                            or InvalidDataException)
                    {
                        warnings.Add(
                            $"{Path.GetFileName(target)} was malformed and was not overwritten: "
                                + exception.Message);
                        continue;
                    }

                    var originalPath = Path.Combine(
                        originalDirectory,
                        Path.GetFileName(target));
                    File.Copy(target, originalPath, false);
                    originalHash = await ComputeHashAsync(
                            target,
                            cancellationToken)
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
                            $"The backup for {Path.GetFileName(target)} did not match its source.");
                    }
                }

                JsonObject merged;
                try
                {
                    merged = LegacySystemSnapshotMerger.Merge(
                        existing,
                        snapshot,
                        commanderName,
                        reconstructed.FirstVisited!.Value,
                        reconstructed.LastVisited!.Value);
                }
                catch (InvalidDataException exception)
                {
                    warnings.Add(
                        $"{Path.GetFileName(target)} was not rebuilt: {exception.Message}");
                    continue;
                }

                var candidatePath = Path.Combine(
                    candidateDirectory,
                    $"{snapshot.SystemAddress.Value}.json");
                await WriteObjectAsync(
                        candidatePath,
                        merged,
                        cancellationToken)
                    .ConfigureAwait(false);
                _ = await ReadObjectAsync(candidatePath, cancellationToken)
                    .ConfigureAwait(false);
                var candidateHash = await ComputeHashAsync(
                        candidatePath,
                        cancellationToken)
                    .ConfigureAwait(false);
                entries.Add(new ActivationEntry(
                    target,
                    candidatePath,
                    File.Exists(target),
                    originalHash,
                    candidateHash));
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

    private async Task ActivateEntriesAsync(
        IReadOnlyList<ActivationEntry> entries,
        string finalBackup,
        IProgress<HistoricalSystemRebuildProgress>? progress)
    {
        var activated = new List<ActivationEntry>();
        try
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
                if (activationFailure?.Invoke(entry.TargetPath) is { } failure)
                {
                    throw failure;
                }

                var directory = Path.GetDirectoryName(entry.TargetPath)!;
                Directory.CreateDirectory(directory);
                var temporaryPath = $"{entry.TargetPath}.{Guid.NewGuid():N}.tmp";
                try
                {
                    File.Copy(entry.CandidatePath, temporaryPath, false);
                    var stagedHash = await ComputeHashAsync(
                            temporaryPath,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    if (!string.Equals(
                            stagedHash,
                            entry.CandidateHash,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"The staged candidate for {Path.GetFileName(entry.TargetPath)} failed verification.");
                    }

                    File.Move(temporaryPath, entry.TargetPath, true);
                    activated.Add(entry);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }

                var targetHash = await ComputeHashAsync(
                        entry.TargetPath,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (!string.Equals(
                        targetHash,
                        entry.CandidateHash,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"The activated candidate for {Path.GetFileName(entry.TargetPath)} failed verification.");
                }

            }

            Report(
                progress,
                "Activating reconstructed systems",
                entries.Count,
                entries.Count,
                string.Empty);
        }
        catch (Exception activationException)
        {
            var rollbackErrors = await RollBackAsync(activated, finalBackup)
                .ConfigureAwait(false);
            if (rollbackErrors.Count > 0)
            {
                throw new AggregateException(
                    $"Historical system activation failed and rollback was incomplete. Verified backup: {finalBackup}",
                    [activationException, .. rollbackErrors]);
            }

            throw new InvalidOperationException(
                $"Historical system activation failed and was rolled back. Verified backup: {finalBackup}",
                activationException);
        }
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

    private void ValidateBackupLocation()
    {
        var systemsDirectory = Path.GetFullPath(Path.Combine(
            dataDirectory,
            "systems"));
        if (PathsOverlap(systemsDirectory, backupDirectory))
        {
            throw new ArgumentException(
                "Historical rebuild backups must be outside the active systems directory.",
                nameof(backupDirectory));
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
