using System.Text.Json;

namespace SrvSurvey.Core.Storage;

public sealed class LegacyProfileImporter
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public const string ManifestFileName = ".srv-survey-import.json";
    public const string RecoveryReportFileName = ".srv-survey-recovery.json";
    public const string RecoveryDirectoryDataKey = "SrvSurvey.ProfileImport.RecoveryDirectory";

    private const int ManifestVersion = 4;
    private readonly TimeProvider timeProvider;
    private readonly Action<ProfileImportCheckpoint>? checkpoint;

    public LegacyProfileImporter(TimeProvider? timeProvider = null)
        : this(timeProvider, null) { }

    internal LegacyProfileImporter(TimeProvider? timeProvider, Action<ProfileImportCheckpoint>? checkpoint)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.checkpoint = checkpoint;
    }

    public Task<ProfileImportResult> ImportAsync(
        string sourceDirectory,
        string destinationDirectory,
        string backupDirectory,
        CancellationToken cancellationToken = default
    ) => ImportAsync(sourceDirectory, destinationDirectory, backupDirectory, progress: null, cancellationToken);

    public Task<ProfileImportResult> ImportAsync(
        string sourceDirectory,
        string destinationDirectory,
        string backupDirectory,
        IProgress<ProfileImportProgress>? progress,
        CancellationToken cancellationToken = default
    ) =>
        ImportAsync(
            new ProfileImportSource(sourceDirectory, ProfileImportSourceKind.Legacy, null),
            destinationDirectory,
            backupDirectory,
            progress,
            cancellationToken
        );

    public async Task<ProfileImportResult> ImportAsync(
        ProfileImportSource importSource,
        string destinationDirectory,
        string backupDirectory,
        IProgress<ProfileImportProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(importSource);
        string source = Path.GetFullPath(importSource.DataDirectory);
        string destination = Path.GetFullPath(destinationDirectory);
        string backupParent = Path.GetFullPath(backupDirectory);
        ValidateLocations(source, destination, backupParent);

        if (File.Exists(destination))
        {
            throw new IOException($"The import destination is a file and cannot contain a profile: {destination}");
        }

        progress?.Report(new ProfileImportProgress(ProfileImportStage.ScanningLegacyProfile));
        var warnings = new List<ProfileImportWarning>();
        ProfileInventory sourceInventory = await ProfileInventory
            .CreateForImportAsync(source, warnings, cancellationToken)
            .ConfigureAwait(false);
        if (sourceInventory.Entries.Count == 0)
        {
            string details = string.Join(
                " | ",
                warnings.Select(warning => $"{warning.RelativePath}: {warning.Message}")
            );
            throw new InvalidDataException(
                warnings.Count == 0
                    ? $"The selected profile does not contain any files: {source}"
                    : $"No source files could be read from {source}. {details}"
            );
        }
        checkpoint?.Invoke(ProfileImportCheckpoint.AfterSourceScan);

        bool destinationExisted = Directory.Exists(destination);
        progress?.Report(new ProfileImportProgress(ProfileImportStage.ScanningCurrentProfile));
        ProfileInventory destinationInventory = destinationExisted
            ? await ProfileInventory.CreateAsync(destination, cancellationToken).ConfigureAwait(false)
            : EmptyInventory(destination);
        ProfileInventory mergeInventory = destinationInventory with
        {
            Entries = destinationInventory
                .Entries.Where(entry => !string.Equals(entry.RelativePath, ManifestFileName, PathComparison))
                .ToArray(),
        };
        string operationId = Guid.NewGuid().ToString("N");
        DateTimeOffset timestamp = timeProvider.GetUtcNow();
        string backupName = $"profile-import-{timestamp:yyyyMMddTHHmmssZ}-{operationId}";
        string finalBackup = Path.Combine(backupParent, backupName);
        string backupStage = $"{finalBackup}.importing";
        string destinationStage = $"{destination}.importing-{operationId}";
        string rollbackDirectory = $"{destination}.rollback-{operationId}";
        string failedActivationDirectory = $"{destination}.failed-import-{operationId}";
        string backupProfileStage = Path.Combine(backupStage, "profile");
        string previousDestinationStage = Path.Combine(backupStage, "previous-destination");
        bool profileActivated = false;

        Directory.CreateDirectory(backupParent);

        try
        {
            progress?.Report(new ProfileImportProgress(ProfileImportStage.BackingUpLegacyProfile));
            Directory.CreateDirectory(backupProfileStage);
            sourceInventory = await CopyAndVerifyAsync(
                    source,
                    backupProfileStage,
                    sourceInventory,
                    overwrite: false,
                    new ProfileCopyProgress(progress, ProfileImportStage.BackingUpLegacyProfile),
                    cancellationToken,
                    warnings
                )
                .ConfigureAwait(false);
            await VerifyExactProfileAsync(backupProfileStage, sourceInventory, cancellationToken).ConfigureAwait(false);
            ProfileInventory importedInventory = await ProfileImportCompatibility
                .SelectReadableDataAsync(
                    SelectImportedFiles(sourceInventory),
                    backupProfileStage,
                    warnings,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(
                    Path.Combine(backupStage, RecoveryReportFileName),
                    JsonSerializer.Serialize(
                        new
                        {
                            SourceDirectory = source,
                            sourceInventory.Entries,
                            Warnings = warnings,
                        },
                        IndentedJson
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (!HasCompatiblePayload(importedInventory, importSource))
            {
                throw new InvalidDataException("No compatible files could be recovered from the selected profile.");
            }
            ProfileImportConflict[] conflicts = FindConflicts(importedInventory, destinationInventory);

            if (destinationInventory.Entries.Count > 0 || destinationInventory.RelativeDirectories.Count > 0)
            {
                progress?.Report(new ProfileImportProgress(ProfileImportStage.BackingUpCurrentProfile));
                Directory.CreateDirectory(previousDestinationStage);
                await CopyAndVerifyAsync(
                        destination,
                        previousDestinationStage,
                        destinationInventory,
                        overwrite: false,
                        new ProfileCopyProgress(progress, ProfileImportStage.BackingUpCurrentProfile),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                await VerifyExactProfileAsync(previousDestinationStage, destinationInventory, cancellationToken)
                    .ConfigureAwait(false);
            }

            progress?.Report(new ProfileImportProgress(ProfileImportStage.BuildingMergedProfile));
            importedInventory = await ProfileImportMerger
                .BuildPayloadAsync(
                    importedInventory with
                    {
                        RootPath = backupProfileStage,
                    },
                    mergeInventory with
                    {
                        RootPath = previousDestinationStage,
                    },
                    Path.Combine(backupStage, "import-payload"),
                    warnings,
                    cancellationToken
                )
                .ConfigureAwait(false);

            var activatedPaths = importedInventory
                .Entries.Select(entry => entry.RelativePath)
                .ToHashSet(StringComparer.Ordinal);
            var manifest = new ProfileImportManifest(
                ManifestVersion,
                timestamp,
                source,
                destination,
                finalBackup,
                sourceInventory.RelativeDirectories,
                sourceInventory.Entries,
                destinationInventory.RelativeDirectories,
                destinationInventory.Entries,
                conflicts,
                sourceInventory
                    .Entries.Where(entry => !activatedPaths.Contains(entry.RelativePath))
                    .Select(entry => entry.RelativePath)
                    .ToArray(),
                warnings.ToArray(),
                importedInventory.Entries,
                SourceKind: importSource.Kind
            );
            await File.WriteAllTextAsync(
                    Path.Combine(backupStage, RecoveryReportFileName),
                    JsonSerializer.Serialize(
                        new
                        {
                            SourceDirectory = source,
                            sourceInventory.Entries,
                            Warnings = warnings,
                        },
                        IndentedJson
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);
            await WriteManifestAsync(Path.Combine(backupStage, ManifestFileName), manifest, cancellationToken)
                .ConfigureAwait(false);
            Directory.Move(backupStage, finalBackup);

            progress?.Report(new ProfileImportProgress(ProfileImportStage.BuildingMergedProfile));
            Directory.CreateDirectory(destinationStage);
            int mergedFileCount = mergeInventory.Entries.Count + importedInventory.Entries.Count;
            long mergedByteCount = GetTotalBytes(mergeInventory) + GetTotalBytes(importedInventory);
            if (destinationInventory.Entries.Count > 0 || destinationInventory.RelativeDirectories.Count > 0)
            {
                await CopyAndVerifyAsync(
                        Path.Combine(finalBackup, "previous-destination"),
                        destinationStage,
                        mergeInventory,
                        overwrite: false,
                        new ProfileCopyProgress(
                            progress,
                            ProfileImportStage.BuildingMergedProfile,
                            TotalFileCount: mergedFileCount,
                            TotalByteCount: mergedByteCount
                        ),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            await CopyAndVerifyAsync(
                    Path.Combine(finalBackup, "import-payload"),
                    destinationStage,
                    importedInventory,
                    overwrite: true,
                    new ProfileCopyProgress(
                        progress,
                        ProfileImportStage.BuildingMergedProfile,
                        CompletedFileOffset: mergeInventory.Entries.Count,
                        TotalFileCount: mergedFileCount,
                        CompletedByteOffset: GetTotalBytes(mergeInventory),
                        TotalByteCount: mergedByteCount
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);
            await VerifyMergedProfileExactAsync(
                    destinationStage,
                    importedInventory,
                    mergeInventory,
                    hasImportManifest: false,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await WriteManifestAsync(Path.Combine(destinationStage, ManifestFileName), manifest, cancellationToken)
                .ConfigureAwait(false);

            checkpoint?.Invoke(ProfileImportCheckpoint.BeforeActivationValidation);
            progress?.Report(new ProfileImportProgress(ProfileImportStage.VerifyingMergedProfile));
            await VerifyMergedProfileExactAsync(
                    destinationStage,
                    importedInventory,
                    mergeInventory,
                    hasImportManifest: true,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await VerifyManifestCopyAsync(
                    Path.Combine(finalBackup, ManifestFileName),
                    Path.Combine(destinationStage, ManifestFileName),
                    cancellationToken
                )
                .ConfigureAwait(false);
            await VerifyInventoryUnchangedAsync(
                    destination,
                    destinationInventory,
                    destinationExisted,
                    "The current cross-platform profile changed while the import was staged",
                    cancellationToken
                )
                .ConfigureAwait(false);

            progress?.Report(new ProfileImportProgress(ProfileImportStage.ActivatingProfile));
            ActivateStagedProfile(destination, destinationStage, rollbackDirectory);
            profileActivated = true;
            checkpoint?.Invoke(ProfileImportCheckpoint.AfterProfileActivation);
            await VerifyInventoryUnchangedAsync(
                    rollbackDirectory,
                    destinationInventory,
                    destinationExisted,
                    "The current cross-platform profile changed during import activation",
                    cancellationToken
                )
                .ConfigureAwait(false);
            await VerifyMergedProfileExactAsync(
                    destination,
                    importedInventory,
                    mergeInventory,
                    hasImportManifest: true,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await VerifyManifestCopyAsync(
                    Path.Combine(finalBackup, ManifestFileName),
                    Path.Combine(destination, ManifestFileName),
                    cancellationToken
                )
                .ConfigureAwait(false);
            // The activated profile is now independently verified. A failure
            // while pruning the redundant rollback must never replace it with
            // a partially deleted rollback directory.
            profileActivated = false;
            TryDeleteVerifiedRollback(rollbackDirectory);
            return new ProfileImportResult(destination, finalBackup, manifest);
        }
        catch (Exception exception)
        {
            if (profileActivated)
            {
                RollBackActivatedProfile(destination, rollbackDirectory, failedActivationDirectory);
            }
            string? retained = PreserveRecoveryBackup(backupStage, finalBackup);
            if (retained is not null)
            {
                exception.Data[RecoveryDirectoryDataKey] = retained;
            }
            throw;
        }
        finally
        {
            DeleteStagingDirectory(destinationStage);
            RestoreRollbackIfRequired(destination, rollbackDirectory);
            TryDeleteVerifiedRollback(failedActivationDirectory);
        }
    }

    public static bool CanRetryRecovery(string destinationDirectory) =>
        GetRecoveryReportPath(destinationDirectory) is not null;

    public static string? GetRecoveryReportPath(string destinationDirectory)
    {
        try
        {
            ProfileImportManifest? manifest = JsonSerializer.Deserialize<ProfileImportManifest>(
                File.ReadAllText(Path.Combine(destinationDirectory, ManifestFileName))
            );
            return
                manifest?.Warnings?.Count > 0
                && !string.IsNullOrWhiteSpace(manifest.BackupDirectory)
                && Path.IsPathFullyQualified(manifest.BackupDirectory)
                ? Path.Combine(Path.GetFullPath(manifest.BackupDirectory), RecoveryReportFileName)
                : null;
        }
        catch (Exception exception)
            when (ProfileInventory.IsRecoverable(exception) || exception is JsonException or ArgumentException)
        {
            return null;
        }
    }

    public static async Task SaveRecoveryWarningsAsync(
        ProfileImportResult result,
        IReadOnlyList<ProfileImportWarning> warnings
    )
    {
        ProfileImportManifest manifest = result.Manifest with { Warnings = warnings };
        foreach (string directory in new[] { result.DestinationDirectory, result.BackupDirectory })
        {
            string target = Path.Combine(directory, ManifestFileName);
            string temporary = target + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await WriteManifestAsync(temporary, manifest, CancellationToken.None).ConfigureAwait(false);
                File.Move(temporary, target, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
    }

    private static string? PreserveRecoveryBackup(string stage, string final)
    {
        if (Directory.Exists(stage))
        {
            try
            {
                Directory.Move(stage, final);
            }
            catch (Exception exception) when (ProfileInventory.IsRecoverable(exception))
            {
                return stage;
            }
        }
        return Directory.Exists(final) ? final : null;
    }

    private static bool HasCompatiblePayload(ProfileInventory inventory, ProfileImportSource source) =>
        inventory.Entries.Count > 0
        || source.Kind == ProfileImportSourceKind.CrossPlatform && source.UiSettingsPath is not null;

    private static ProfileInventory SelectImportedFiles(ProfileInventory source)
    {
        bool ImportPath(string path) =>
            !string.Equals(path, ManifestFileName, StringComparison.OrdinalIgnoreCase) && !IsOverlayLayoutPath(path);
        return source with
        {
            RelativeDirectories = source.RelativeDirectories.Where(ImportPath).ToArray(),
            Entries = source.Entries.Where(entry => ImportPath(entry.RelativePath)).ToArray(),
        };
    }

    private static bool IsOverlayLayoutPath(string path)
    {
        const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        if (string.Equals(path, "vr", comparison) || path.StartsWith("vr/", comparison))
        {
            return true;
        }
        if (
            path.StartsWith("commanders/", comparison)
            && (path.EndsWith("/overlays", comparison) || path.Contains("/overlays/", comparison))
        )
        {
            return true;
        }
        return !path.Contains('/')
            && (
                string.Equals(path, "plotters.json", comparison)
                || string.Equals(path, "theme.json", comparison)
                || path.StartsWith("overlay-", comparison)
            );
    }

    private static async Task<ProfileInventory> CopyAndVerifyAsync(
        string sourceRoot,
        string destinationRoot,
        ProfileInventory inventory,
        bool overwrite,
        ProfileCopyProgress progress,
        CancellationToken cancellationToken,
        List<ProfileImportWarning>? warnings = null
    )
    {
        foreach (string relativeDirectory in inventory.RelativeDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.CreateDirectory(ProfileInventory.ResolveEntryPath(destinationRoot, relativeDirectory));
            }
            catch (Exception exception) when (warnings is not null && ProfileInventory.IsRecoverable(exception))
            {
                warnings.Add(new ProfileImportWarning(relativeDirectory, exception.Message));
            }
        }

        int completedFiles = progress.CompletedFileOffset;
        long completedBytes = progress.CompletedByteOffset;
        int expectedFileCount = progress.TotalFileCount ?? progress.CompletedFileOffset + inventory.Entries.Count;
        long expectedByteCount = progress.TotalByteCount ?? progress.CompletedByteOffset + GetTotalBytes(inventory);
        var copied = new List<ProfileInventoryEntry>();
        foreach (ProfileInventoryEntry entry in inventory.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !await CopyEntryAsync(sourceRoot, destinationRoot, entry, overwrite, warnings, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                continue;
            }
            copied.Add(entry);
            completedFiles++;
            completedBytes += entry.Length;
            progress.Reporter?.Report(
                new ProfileImportProgress(
                    progress.Stage,
                    completedFiles,
                    expectedFileCount,
                    completedBytes,
                    expectedByteCount
                )
            );
        }
        if (warnings is null)
        {
            return inventory;
        }
        ProfileInventory actual = await ProfileInventory
            .CreateAsync(destinationRoot, cancellationToken)
            .ConfigureAwait(false);
        return inventory with { Entries = copied, RelativeDirectories = actual.RelativeDirectories };
    }

    internal static async Task<bool> CopyEntryAsync(
        string sourceRoot,
        string destinationRoot,
        ProfileInventoryEntry entry,
        bool overwrite,
        List<ProfileImportWarning>? warnings,
        CancellationToken cancellationToken
    )
    {
        string destinationPath = ProfileInventory.ResolveEntryPath(destinationRoot, entry.RelativePath);
        try
        {
            string sourcePath = ProfileInventory.ResolveEntryPath(sourceRoot, entry.RelativePath);
            string destinationParent =
                Path.GetDirectoryName(destinationPath)
                ?? throw new InvalidDataException($"The profile entry has no parent directory: {entry.RelativePath}");
            Directory.CreateDirectory(destinationParent);

            await using (
                var input = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan
                )
            )
            await using (
                var output = new FileStream(
                    destinationPath,
                    overwrite ? FileMode.Create : FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan
                )
            )
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            await VerifyEntryAsync(destinationPath, entry, cancellationToken).ConfigureAwait(false);
            File.SetLastWriteTimeUtc(destinationPath, entry.LastWriteTimeUtc);
            return true;
        }
        catch (Exception exception) when (warnings is not null && ProfileInventory.IsRecoverable(exception))
        {
            warnings.Add(new ProfileImportWarning(entry.RelativePath, exception.Message));
            try
            {
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }
            }
            catch (Exception cleanupException) when (ProfileInventory.IsRecoverable(cleanupException))
            {
                warnings.Add(
                    new ProfileImportWarning(
                        entry.RelativePath,
                        "Partial-file cleanup failed: " + cleanupException.Message
                    )
                );
            }
            return false;
        }
    }

    private static long GetTotalBytes(ProfileInventory inventory) => inventory.Entries.Sum(entry => entry.Length);

    private sealed record ProfileCopyProgress(
        IProgress<ProfileImportProgress>? Reporter,
        ProfileImportStage Stage,
        int CompletedFileOffset = 0,
        int? TotalFileCount = null,
        long CompletedByteOffset = 0,
        long? TotalByteCount = null
    );

    /// <summary>Verifies the merged directory identities and exact file contents before and after activation.</summary>
    private static async Task VerifyMergedProfileExactAsync(
        string destinationRoot,
        ProfileInventory sourceInventory,
        ProfileInventory previousInventory,
        bool hasImportManifest,
        CancellationToken cancellationToken
    )
    {
        var mergedEntries = previousInventory.Entries.ToDictionary(entry => entry.RelativePath, PathComparer);
        foreach (ProfileInventoryEntry entry in sourceInventory.Entries)
        {
            mergedEntries[entry.RelativePath] = entry;
        }

        ProfileInventory actual = await ProfileInventory
            .CreateAsync(destinationRoot, cancellationToken)
            .ConfigureAwait(false);
        ProfileInventoryEntry[] actualEntries = actual
            .Entries.Where(entry =>
                !hasImportManifest || !string.Equals(entry.RelativePath, ManifestFileName, PathComparison)
            )
            .ToArray();
        string[] expectedDirectories = sourceInventory
            .RelativeDirectories.Concat(previousInventory.RelativeDirectories)
            .Distinct(PathComparer)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (
            !ProfileInventory.DirectoryPathsMatch(expectedDirectories, actual.RelativeDirectories, PathComparer)
            || !EntriesMatchContent(mergedEntries.Values.ToArray(), actualEntries)
        )
        {
            throw new IOException("The staged profile contains unexpected, missing, or changed files.");
        }
    }

    /// <summary>Verifies a complete backup without relying on directory enumeration order.</summary>
    private static async Task VerifyExactProfileAsync(
        string destinationRoot,
        ProfileInventory expected,
        CancellationToken cancellationToken
    )
    {
        ProfileInventory actual = await ProfileInventory
            .CreateAsync(destinationRoot, cancellationToken)
            .ConfigureAwait(false);
        if (
            !ProfileInventory.DirectoryPathsMatch(
                expected.RelativeDirectories,
                actual.RelativeDirectories,
                PathComparer
            ) || !EntriesMatchContent(expected.Entries, actual.Entries)
        )
        {
            throw new IOException("A verified profile backup contains unexpected, missing, or changed files.");
        }
    }

    private static async Task VerifyEntryAsync(
        string path,
        ProfileInventoryEntry entry,
        CancellationToken cancellationToken
    )
    {
        string copiedHash = await ProfileInventory.ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(copiedHash, entry.Sha256, StringComparison.Ordinal))
        {
            throw new IOException($"A profile file changed while it was being imported: {entry.RelativePath}");
        }
    }

    private static async Task VerifyManifestCopyAsync(
        string expectedPath,
        string activatedPath,
        CancellationToken cancellationToken
    )
    {
        string expectedHash = await ProfileInventory
            .ComputeSha256Async(expectedPath, cancellationToken)
            .ConfigureAwait(false);
        string activatedHash = await ProfileInventory
            .ComputeSha256Async(activatedPath, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(expectedHash, activatedHash, StringComparison.Ordinal))
        {
            throw new IOException("The activated profile import manifest did not match its verified backup.");
        }
    }

    /// <summary>Rejects concurrent profile writes while comparing directory identities using platform path rules.</summary>
    private static async Task VerifyInventoryUnchangedAsync(
        string root,
        ProfileInventory expected,
        bool expectedToExist,
        string errorMessage,
        CancellationToken cancellationToken
    )
    {
        if (Directory.Exists(root) != expectedToExist)
        {
            throw new IOException($"{errorMessage}: the profile directory changed.");
        }

        if (!expectedToExist)
        {
            return;
        }

        ProfileInventory current;
        try
        {
            current = await ProfileInventory.CreateAsync(root, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new IOException($"{errorMessage}: {exception.Message}", exception);
        }

        if (
            !ProfileInventory.DirectoryPathsMatch(
                expected.RelativeDirectories,
                current.RelativeDirectories,
                PathComparer
            ) || !EntriesMatch(expected.Entries, current.Entries)
        )
        {
            throw new IOException($"{errorMessage}; retry after closing the other instance.");
        }
    }

    private static bool EntriesMatch(
        IReadOnlyList<ProfileInventoryEntry> expected,
        IReadOnlyList<ProfileInventoryEntry> current
    )
    {
        if (expected.Count != current.Count)
        {
            return false;
        }

        var currentByPath = current.ToDictionary(entry => entry.RelativePath, PathComparer);
        foreach (ProfileInventoryEntry entry in expected)
        {
            if (
                !currentByPath.TryGetValue(entry.RelativePath, out ProfileInventoryEntry? currentEntry)
                || entry.Length != currentEntry.Length
                || entry.LastWriteTimeUtc != currentEntry.LastWriteTimeUtc
                || !string.Equals(entry.Sha256, currentEntry.Sha256, StringComparison.Ordinal)
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool EntriesMatchContent(
        IReadOnlyCollection<ProfileInventoryEntry> expected,
        IReadOnlyCollection<ProfileInventoryEntry> current
    )
    {
        if (expected.Count != current.Count)
        {
            return false;
        }

        var currentByPath = current.ToDictionary(entry => entry.RelativePath, PathComparer);
        return expected.All(entry =>
            currentByPath.TryGetValue(entry.RelativePath, out ProfileInventoryEntry? currentEntry)
            && entry.Length == currentEntry.Length
            && string.Equals(entry.Sha256, currentEntry.Sha256, StringComparison.Ordinal)
        );
    }

    private static ProfileImportConflict[] FindConflicts(ProfileInventory source, ProfileInventory destination)
    {
        var destinationEntries = destination.Entries.ToDictionary(entry => entry.RelativePath, PathComparer);
        return source
            .Entries.Where(entry => destinationEntries.ContainsKey(entry.RelativePath))
            .Select(entry =>
            {
                ProfileInventoryEntry previous = destinationEntries[entry.RelativePath];
                return new ProfileImportConflict(
                    entry.RelativePath,
                    previous.Sha256,
                    entry.Sha256,
                    string.Equals(previous.Sha256, entry.Sha256, StringComparison.Ordinal)
                );
            })
            .ToArray();
    }

    private static ProfileInventory EmptyInventory(string root)
    {
        return new ProfileInventory(root, [], []);
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static void ActivateStagedProfile(string destination, string destinationStage, string rollbackDirectory)
    {
        if (Directory.Exists(destination))
        {
            Directory.Move(destination, rollbackDirectory);
        }

        try
        {
            Directory.Move(destinationStage, destination);
        }
        catch
        {
            RestoreRollbackIfRequired(destination, rollbackDirectory);
            throw;
        }
    }

    private static void RollBackActivatedProfile(
        string destination,
        string rollbackDirectory,
        string failedActivationDirectory
    )
    {
        if (Directory.Exists(destination))
        {
            Directory.Move(destination, failedActivationDirectory);
        }

        try
        {
            if (Directory.Exists(rollbackDirectory))
            {
                Directory.Move(rollbackDirectory, destination);
            }
        }
        catch
        {
            if (!Directory.Exists(destination) && Directory.Exists(failedActivationDirectory))
            {
                Directory.Move(failedActivationDirectory, destination);
            }

            throw;
        }

        TryDeleteVerifiedRollback(failedActivationDirectory);
    }

    private static async Task WriteManifestAsync(
        string path,
        ProfileImportManifest manifest,
        CancellationToken cancellationToken
    )
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous
        );
        await JsonSerializer.SerializeAsync(stream, manifest, IndentedJson, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateLocations(string source, string destination, string backupParent)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"The selected profile directory does not exist: {source}");
        }

        if (LegacyProfileLocator.IsWinePrefixRoot(source))
        {
            throw new InvalidDataException(
                "No SrvSurvey profile was found in the selected Wine/Proton prefix. Choose the folder containing the player's F*-live.json or F*-codex.json files."
            );
        }

        if (PathsOverlap(source, destination) || PathsOverlap(source, backupParent))
        {
            throw new InvalidOperationException(
                "The import destination and backup directory must be outside the selected profile."
            );
        }

        if (PathsOverlap(destination, backupParent))
        {
            throw new InvalidOperationException(
                "The import destination and backup directory must not contain one another."
            );
        }
    }

    private static bool PathsOverlap(string first, string second)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string firstWithSeparator = Path.EndsInDirectorySeparator(first) ? first : first + Path.DirectorySeparatorChar;
        string secondWithSeparator = Path.EndsInDirectorySeparator(second)
            ? second
            : second + Path.DirectorySeparatorChar;

        return string.Equals(first, second, comparison)
            || firstWithSeparator.StartsWith(secondWithSeparator, comparison)
            || secondWithSeparator.StartsWith(firstWithSeparator, comparison);
    }

    private static void RestoreRollbackIfRequired(string destination, string rollbackDirectory)
    {
        if (!Directory.Exists(destination) && Directory.Exists(rollbackDirectory))
        {
            Directory.Move(rollbackDirectory, destination);
        }
    }

    private static void DeleteStagingDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private static void TryDeleteVerifiedRollback(string path)
    {
        try
        {
            DeleteStagingDirectory(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The new destination and permanent backup are already verified.
            // Retaining this redundant rollback is safer than reporting a
            // failed import after activation succeeded.
        }
    }
}

public enum ProfileImportStage
{
    ScanningLegacyProfile,
    ScanningCurrentProfile,
    BackingUpLegacyProfile,
    BackingUpCurrentProfile,
    BuildingMergedProfile,
    VerifyingMergedProfile,
    ActivatingProfile,
}

public readonly record struct ProfileImportProgress(
    ProfileImportStage Stage,
    int CompletedFiles = 0,
    int TotalFiles = 0,
    long CompletedBytes = 0,
    long TotalBytes = 0
);

internal enum ProfileImportCheckpoint
{
    AfterSourceScan,
    BeforeActivationValidation,
    AfterProfileActivation,
}

public sealed record ProfileImportManifest(
    int Version,
    DateTimeOffset ImportedAtUtc,
    string SourceDirectory,
    string DestinationDirectory,
    string BackupDirectory,
    IReadOnlyList<string> RelativeDirectories,
    IReadOnlyList<ProfileInventoryEntry> Entries,
    IReadOnlyList<string> PreviousDestinationDirectories,
    IReadOnlyList<ProfileInventoryEntry> PreviousDestinationEntries,
    IReadOnlyList<ProfileImportConflict> Conflicts,
    IReadOnlyList<string>? SkippedRelativePaths = null,
    IReadOnlyList<ProfileImportWarning>? Warnings = null,
    IReadOnlyList<ProfileInventoryEntry>? ActivatedEntries = null,
    ProfileImportSourceKind SourceKind = ProfileImportSourceKind.Legacy
)
{
    public IReadOnlyList<ProfileInventoryEntry> GetImportedEntries() =>
        ActivatedEntries
        ?? (
            SkippedRelativePaths is null
                ? Entries
                : Entries
                    .Where(entry => !SkippedRelativePaths.Contains(entry.RelativePath, StringComparer.Ordinal))
                    .ToArray()
        );
}

public sealed record ProfileImportConflict(
    string RelativePath,
    string PreviousSha256,
    string ImportedSha256,
    bool IsIdentical
);

public sealed record ProfileImportResult(
    string DestinationDirectory,
    string BackupDirectory,
    ProfileImportManifest Manifest
)
{
    public IReadOnlyList<ProfileImportWarning> Warnings => Manifest.Warnings ?? Array.Empty<ProfileImportWarning>();
}

public sealed record ProfileImportWarning(string RelativePath, string Message);
