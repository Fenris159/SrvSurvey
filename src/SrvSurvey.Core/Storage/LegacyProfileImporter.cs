using System.Text.Json;

namespace SrvSurvey.Core.Storage;

public sealed class LegacyProfileImporter(TimeProvider? timeProvider = null)
{
    public const string ManifestFileName = ".srv-survey-import.json";

    private const int ManifestVersion = 1;
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ProfileImportResult> ImportAsync(
        string sourceDirectory,
        string destinationDirectory,
        string backupDirectory,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourceDirectory);
        var destination = Path.GetFullPath(destinationDirectory);
        var backupParent = Path.GetFullPath(backupDirectory);
        ValidateLocations(source, destination, backupParent);

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException(
                $"The import destination already exists and will not be overwritten: {destination}");
        }

        var inventory = await ProfileInventory.CreateAsync(source, cancellationToken)
            .ConfigureAwait(false);
        var operationId = Guid.NewGuid().ToString("N");
        var timestamp = timeProvider.GetUtcNow();
        var backupName = $"legacy-profile-{timestamp:yyyyMMddTHHmmssZ}-{operationId}";
        var finalBackup = Path.Combine(backupParent, backupName);
        var backupStage = $"{finalBackup}.importing";
        var destinationStage = $"{destination}.importing-{operationId}";
        var backupProfileStage = Path.Combine(backupStage, "profile");

        Directory.CreateDirectory(backupParent);

        try
        {
            Directory.CreateDirectory(backupProfileStage);
            await CopyAndVerifyAsync(
                    source,
                    backupProfileStage,
                    inventory,
                    cancellationToken)
                .ConfigureAwait(false);

            var manifest = new ProfileImportManifest(
                ManifestVersion,
                timestamp,
                source,
                destination,
                finalBackup,
                inventory.RelativeDirectories,
                inventory.Entries);
            await WriteManifestAsync(
                    Path.Combine(backupStage, ManifestFileName),
                    manifest,
                    cancellationToken)
                .ConfigureAwait(false);
            Directory.Move(backupStage, finalBackup);

            Directory.CreateDirectory(destinationStage);
            await CopyAndVerifyAsync(
                    Path.Combine(finalBackup, "profile"),
                    destinationStage,
                    inventory,
                    cancellationToken)
                .ConfigureAwait(false);
            await WriteManifestAsync(
                    Path.Combine(destinationStage, ManifestFileName),
                    manifest,
                    cancellationToken)
                .ConfigureAwait(false);
            Directory.Move(destinationStage, destination);

            return new ProfileImportResult(destination, finalBackup, manifest);
        }
        finally
        {
            DeleteStagingDirectory(backupStage);
            DeleteStagingDirectory(destinationStage);
        }
    }

    private static async Task CopyAndVerifyAsync(
        string sourceRoot,
        string destinationRoot,
        ProfileInventory inventory,
        CancellationToken cancellationToken)
    {
        foreach (var relativeDirectory in inventory.RelativeDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(
                ProfileInventory.ResolveEntryPath(destinationRoot, relativeDirectory));
        }

        foreach (var entry in inventory.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = ProfileInventory.ResolveEntryPath(
                sourceRoot,
                entry.RelativePath);
            var destinationPath = ProfileInventory.ResolveEntryPath(
                destinationRoot,
                entry.RelativePath);
            var destinationParent = Path.GetDirectoryName(destinationPath)
                ?? throw new InvalidDataException(
                    $"The profile entry has no parent directory: {entry.RelativePath}");
            Directory.CreateDirectory(destinationParent);

            await using (var input = new FileStream(
                             sourcePath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.ReadWrite | FileShare.Delete,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                             destinationPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var copiedHash = await ProfileInventory.ComputeSha256Async(
                    destinationPath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(copiedHash, entry.Sha256, StringComparison.Ordinal))
            {
                throw new IOException(
                    $"The profile changed while it was being imported: {entry.RelativePath}");
            }

            File.SetLastWriteTimeUtc(destinationPath, entry.LastWriteTimeUtc);
        }
    }

    private static async Task WriteManifestAsync(
        string path,
        ProfileImportManifest manifest,
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
                manifest,
                new JsonSerializerOptions { WriteIndented = true },
                cancellationToken)
            .ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateLocations(
        string source,
        string destination,
        string backupParent)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(
                $"The legacy profile directory does not exist: {source}");
        }

        if (PathsOverlap(source, destination) || PathsOverlap(source, backupParent))
        {
            throw new InvalidOperationException(
                "The import destination and backup directory must be outside the legacy profile.");
        }

        if (PathsOverlap(destination, backupParent))
        {
            throw new InvalidOperationException(
                "The import destination and backup directory must not contain one another.");
        }
    }

    private static bool PathsOverlap(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var firstWithSeparator = Path.EndsInDirectorySeparator(first)
            ? first
            : first + Path.DirectorySeparatorChar;
        var secondWithSeparator = Path.EndsInDirectorySeparator(second)
            ? second
            : second + Path.DirectorySeparatorChar;

        return string.Equals(first, second, comparison)
            || firstWithSeparator.StartsWith(secondWithSeparator, comparison)
            || secondWithSeparator.StartsWith(firstWithSeparator, comparison);
    }

    private static void DeleteStagingDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}

public sealed record ProfileImportManifest(
    int Version,
    DateTimeOffset ImportedAtUtc,
    string SourceDirectory,
    string DestinationDirectory,
    string BackupDirectory,
    IReadOnlyList<string> RelativeDirectories,
    IReadOnlyList<ProfileInventoryEntry> Entries);

public sealed record ProfileImportResult(
    string DestinationDirectory,
    string BackupDirectory,
    ProfileImportManifest Manifest);
