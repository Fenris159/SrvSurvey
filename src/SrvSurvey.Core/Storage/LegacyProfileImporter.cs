using System.Text.Json;

namespace SrvSurvey.Core.Storage;

public sealed class LegacyProfileImporter
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public const string ManifestFileName = ".srv-survey-import.json";

    private const int ManifestVersion = 2;
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

    public async Task<ProfileImportResult> ImportAsync(
        string sourceDirectory,
        string destinationDirectory,
        string backupDirectory,
        IProgress<ProfileImportProgress>? progress,
        CancellationToken cancellationToken = default
    )
    {
        string source = Path.GetFullPath(sourceDirectory);
        string destination = Path.GetFullPath(destinationDirectory);
        string backupParent = Path.GetFullPath(backupDirectory);
        ValidateLocations(source, destination, backupParent);

        if (File.Exists(destination))
        {
            throw new IOException($"The import destination is a file and cannot contain a profile: {destination}");
        }

        if (File.Exists(Path.Combine(destination, ManifestFileName)))
        {
            throw new InvalidOperationException(
                $"The cross-platform profile has already imported profile data: {destination}"
            );
        }

        progress?.Report(new ProfileImportProgress(ProfileImportStage.ScanningLegacyProfile));
        ProfileInventory sourceInventory = await ProfileInventory
            .CreateAsync(source, cancellationToken)
            .ConfigureAwait(false);
        if (sourceInventory.Entries.Count == 0)
        {
            throw new InvalidDataException($"The selected profile does not contain any files: {source}");
        }

        bool destinationExisted = Directory.Exists(destination);
        progress?.Report(new ProfileImportProgress(ProfileImportStage.ScanningCurrentProfile));
        ProfileInventory destinationInventory = destinationExisted
            ? await ProfileInventory.CreateAsync(destination, cancellationToken).ConfigureAwait(false)
            : EmptyInventory(destination);
        ProfileImportConflict[] conflicts = FindConflicts(sourceInventory, destinationInventory);
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
            await CopyAndVerifyAsync(
                    source,
                    backupProfileStage,
                    sourceInventory,
                    overwrite: false,
                    new ProfileCopyProgress(progress, ProfileImportStage.BackingUpLegacyProfile),
                    cancellationToken
                )
                .ConfigureAwait(false);
            await VerifyExactProfileAsync(backupProfileStage, sourceInventory, cancellationToken).ConfigureAwait(false);

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
                conflicts
            );
            await WriteManifestAsync(Path.Combine(backupStage, ManifestFileName), manifest, cancellationToken)
                .ConfigureAwait(false);
            Directory.Move(backupStage, finalBackup);

            progress?.Report(new ProfileImportProgress(ProfileImportStage.BuildingMergedProfile));
            Directory.CreateDirectory(destinationStage);
            int mergedFileCount = destinationInventory.Entries.Count + sourceInventory.Entries.Count;
            long mergedByteCount = GetTotalBytes(destinationInventory) + GetTotalBytes(sourceInventory);
            if (destinationInventory.Entries.Count > 0 || destinationInventory.RelativeDirectories.Count > 0)
            {
                await CopyAndVerifyAsync(
                        Path.Combine(finalBackup, "previous-destination"),
                        destinationStage,
                        destinationInventory,
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
                    Path.Combine(finalBackup, "profile"),
                    destinationStage,
                    sourceInventory,
                    overwrite: true,
                    new ProfileCopyProgress(
                        progress,
                        ProfileImportStage.BuildingMergedProfile,
                        CompletedFileOffset: destinationInventory.Entries.Count,
                        TotalFileCount: mergedFileCount,
                        CompletedByteOffset: GetTotalBytes(destinationInventory),
                        TotalByteCount: mergedByteCount
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);
            await VerifyMergedProfileExactAsync(
                    destinationStage,
                    sourceInventory,
                    destinationInventory,
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
                    sourceInventory,
                    destinationInventory,
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
                    source,
                    sourceInventory,
                    expectedToExist: true,
                    "The selected profile changed while it was being imported",
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
                    sourceInventory,
                    destinationInventory,
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
        catch
        {
            if (profileActivated)
            {
                RollBackActivatedProfile(destination, rollbackDirectory, failedActivationDirectory);
            }

            throw;
        }
        finally
        {
            DeleteStagingDirectory(backupStage);
            DeleteStagingDirectory(destinationStage);
            RestoreRollbackIfRequired(destination, rollbackDirectory);
            TryDeleteVerifiedRollback(failedActivationDirectory);
        }
    }

    private static async Task CopyAndVerifyAsync(
        string sourceRoot,
        string destinationRoot,
        ProfileInventory inventory,
        bool overwrite,
        ProfileCopyProgress progress,
        CancellationToken cancellationToken
    )
    {
        foreach (string relativeDirectory in inventory.RelativeDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(ProfileInventory.ResolveEntryPath(destinationRoot, relativeDirectory));
        }

        int completedFiles = progress.CompletedFileOffset;
        long completedBytes = progress.CompletedByteOffset;
        int expectedFileCount = progress.TotalFileCount ?? progress.CompletedFileOffset + inventory.Entries.Count;
        long expectedByteCount = progress.TotalByteCount ?? progress.CompletedByteOffset + GetTotalBytes(inventory);
        foreach (ProfileInventoryEntry entry in inventory.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sourcePath = ProfileInventory.ResolveEntryPath(sourceRoot, entry.RelativePath);
            string destinationPath = ProfileInventory.ResolveEntryPath(destinationRoot, entry.RelativePath);
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
            !expectedDirectories.SequenceEqual(actual.RelativeDirectories, PathComparer)
            || !EntriesMatchContent(mergedEntries.Values.ToArray(), actualEntries)
        )
        {
            throw new IOException("The staged profile contains unexpected, missing, or changed files.");
        }
    }

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
            !expected.RelativeDirectories.SequenceEqual(actual.RelativeDirectories, PathComparer)
            || !EntriesMatchContent(expected.Entries, actual.Entries)
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
            !expected.RelativeDirectories.SequenceEqual(current.RelativeDirectories, PathComparer)
            || !EntriesMatch(expected.Entries, current.Entries)
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
    IReadOnlyList<ProfileImportConflict> Conflicts
);

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
);
