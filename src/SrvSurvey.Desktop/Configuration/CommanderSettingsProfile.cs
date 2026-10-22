using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Configuration;

internal static class CommanderSettingsProfile
{
    private static readonly string[] OverlayFiles =
    [
        "plotters.json",
        "settings.json",
        "overlay-scale-overrides.json",
        "overlay-typography-overrides.json",
        "overlay-size-overrides.json",
        "overlay-position-references.json",
        "overlay-growth-overrides.json",
        "theme.json",
        "overlay-theme-states.json",
    ];

    public static string? FindLatestJournalFrontierId(string sharedUiSettingsPath, string? configuredJournalDirectory)
    {
        IReadOnlyList<string> configuredPaths = new JournalSettingsStore(sharedUiSettingsPath).Load().Directories;
        JournalFolderResolution folders = JournalFolderLocator.ResolveCurrentWithSettings(
            configuredPaths,
            configuredJournalDirectory
        );
        foreach (string directory in folders.AvailablePaths.OrderByDescending(LatestJournalWriteTime))
        {
            try
            {
                JournalSnapshot snapshot = JournalSnapshotReader.ReadLatestAsync(directory).GetAwaiter().GetResult();
                if (NormalizeFrontierId(snapshot.FrontierId) is { } frontierId)
                {
                    return frontierId;
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // Another journal directory can still identify the active commander.
            }
        }

        return null;
    }

    /// <summary>Initializes commander-specific layout files while excluding concurrent imports and saves.</summary>
    public static void Prepare(AppDataPaths paths)
    {
        if (paths.SettingsFrontierId is null)
        {
            return;
        }

        if (NormalizeFrontierId(paths.SettingsFrontierId) is null)
        {
            throw new ArgumentException("The settings commander ID is invalid.", nameof(paths));
        }

        string marker = Path.Combine(paths.OverlaySettingsDirectory, ".profile-initialized");
        string vrMarker = Path.Combine(paths.OverlaySettingsDirectory, ".vr-profile-initialized");
        if (File.Exists(marker) && File.Exists(vrMarker))
        {
            return;
        }

        using IDisposable layoutLeases = OverlayLayoutFileAccess.AcquireForFiles(
            OverlayFiles.SelectMany(file =>
                new[] { Path.Combine(paths.DataDirectory, file), Path.Combine(paths.OverlaySettingsDirectory, file) }
            )
        );
        if (File.Exists(marker) && File.Exists(vrMarker))
        {
            return;
        }
        if (!File.Exists(marker))
        {
            CopyIfMissing(paths.SharedUiSettingsPath, paths.UiSettingsPath);
            foreach (string fileName in OverlayFiles)
            {
                CopyIfMissing(
                    Path.Combine(paths.DataDirectory, fileName),
                    Path.Combine(paths.OverlaySettingsDirectory, fileName)
                );
            }
        }

        Directory.CreateDirectory(paths.OverlaySettingsDirectory);
        if (!File.Exists(vrMarker))
        {
            string legacyVrDirectory = Path.Combine(paths.DataDirectory, "vr");
            if (Directory.Exists(legacyVrDirectory))
            {
                foreach (string source in Directory.EnumerateFiles(legacyVrDirectory, "*.json"))
                {
                    CopyIfMissing(source, Path.Combine(paths.OverlaySettingsDirectory, "vr", Path.GetFileName(source)));
                }
            }
            WriteInitializationMarker(vrMarker);
        }
        WriteInitializationMarker(marker);
    }

    /// <summary>Records completed migration while accepting a marker already written by another instance.</summary>
    private static void WriteInitializationMarker(string marker)
    {
        if (File.Exists(marker))
        {
            return;
        }
        try
        {
            using var initialized = new FileStream(marker, FileMode.CreateNew, FileAccess.Write);
        }
        catch (IOException) when (File.Exists(marker))
        {
            // Another instance finished the same profile migration first.
        }
    }

    /// <summary>Publishes imported layout files as a recoverable generation under source and destination leases.</summary>
    public static IReadOnlySet<string> PublishImportedOverlays(
        AppDataPaths paths,
        ProfileImportResult result,
        ICollection<ProfileImportWarning>? warnings = null
    )
    {
        using IDisposable layoutLeases = OverlayLayoutFileAccess.AcquireForFiles(
            OverlayFiles.SelectMany(file =>
                new[] { Path.Combine(paths.DataDirectory, file), Path.Combine(paths.OverlaySettingsDirectory, file) }
            )
        );
        bool capturedSource =
            result.Manifest.Version >= 4 && result.Manifest.SourceKind == ProfileImportSourceKind.CrossPlatform;
        string sourceDirectory = capturedSource ? Path.Combine(result.BackupDirectory, "profile") : paths.DataDirectory;
        var importedFiles = (capturedSource ? result.Manifest.Entries : result.Manifest.GetImportedEntries())
            .Select(entry => entry.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool shared = Path.GetFullPath(paths.DataDirectory) == Path.GetFullPath(paths.OverlaySettingsDirectory);
        string relativeDirectory = Path.GetRelativePath(paths.DataDirectory, paths.OverlaySettingsDirectory);
        string previousDirectory = Path.Combine(result.BackupDirectory, "previous-destination", relativeDirectory);
        JsonObject incoming = ReadOverlayObject(Path.Combine(sourceDirectory, "plotters.json"), warnings);
        if (capturedSource && !shared)
        {
            JsonObject nested = ReadOverlayObject(
                Path.Combine(sourceDirectory, relativeDirectory, "plotters.json"),
                warnings
            );
            foreach ((string name, JsonNode? position) in nested)
            {
                incoming[name] = position?.DeepClone();
            }
        }
        JsonObject previous = ReadOverlayObject(Path.Combine(previousDirectory, "plotters.json"), warnings);
        var addedPanels = incoming
            .Select(entry => entry.Key)
            .Where(name => !previous.ContainsKey(name))
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlySet<string> eligible = addedPanels;
        if (capturedSource)
        {
            JsonObject current = ReadOverlayObject(
                Path.Combine(paths.OverlaySettingsDirectory, "plotters.json"),
                warnings
            );
            eligible = new ImportedOverlayReferenceRecovery(paths).Remember(
                result,
                incoming,
                current,
                addedPanels,
                warnings
            );
        }
        if (shared)
        {
            return eligible;
        }
        Directory.CreateDirectory(paths.OverlaySettingsDirectory);
        using OverlayLayoutFileAccess.Transaction transaction = OverlayLayoutFileAccess.BeginTransaction(
            paths.OverlaySettingsDirectory
        );
        PublishOverlayFiles(
            sourceDirectory,
            paths.OverlaySettingsDirectory,
            importedFiles,
            result.Manifest.Version >= 4,
            addedPanels,
            warnings
        );
        transaction.Commit();
        return eligible;
    }

    private static void PublishOverlayFiles(
        string sourceDirectory,
        string destinationDirectory,
        HashSet<string> importedFiles,
        bool merge,
        HashSet<string> addedPanels,
        ICollection<ProfileImportWarning>? warnings
    )
    {
        foreach (string fileName in OverlayFiles)
        {
            if (!importedFiles.Contains(fileName))
            {
                continue;
            }

            string source = Path.Combine(sourceDirectory, fileName);
            string target = Path.Combine(destinationDirectory, fileName);
            string temporaryPath = target + $".{Guid.NewGuid():N}.tmp";
            try
            {
                StageImportedOverlay(source, target, temporaryPath, merge, addedPanels);
                File.Move(temporaryPath, target, overwrite: true);
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or UnauthorizedAccessException
                            or InvalidDataException
                            or JsonException
                            or ArgumentException
                )
            {
                warnings?.Add(
                    new ProfileImportWarning(
                        fileName,
                        "Skipped incompatible overlay settings; other recovered panels were kept. " + exception.Message
                    )
                );
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    private static void StageImportedOverlay(
        string source,
        string target,
        string staged,
        bool merge,
        HashSet<string> addedPanels
    )
    {
        if (!merge)
        {
            File.Copy(source, staged);
            return;
        }
        JsonObject current = File.Exists(target) ? LegacyOverlayLayoutStore.ParseObject(target) : [];
        JsonObject incoming = LegacyOverlayLayoutStore.ParseObject(source);
        if (Path.GetFileName(target) == "overlay-position-references.json")
        {
            foreach (
                string key in incoming.Select(entry => entry.Key).Where(key => !addedPanels.Contains(key)).ToArray()
            )
            {
                incoming.Remove(key);
            }
        }
        File.WriteAllText(staged, ProfileJsonMerge.MergeMissing(current, incoming).ToJsonString());
    }

    private static JsonObject ReadOverlayObject(string path, ICollection<ProfileImportWarning>? warnings)
    {
        try
        {
            return File.Exists(path) ? LegacyOverlayLayoutStore.ParseObject(path) : [];
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or JsonException
                        or ArgumentException
            )
        {
            warnings?.Add(new ProfileImportWarning(Path.GetFileName(path), exception.Message));
            return [];
        }
    }

    private static DateTime LatestJournalWriteTime(string directory)
    {
        try
        {
            return Directory
                .EnumerateFiles(directory, "Journal.*.log", SearchOption.TopDirectoryOnly)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    public static string? NormalizeFrontierId(string? frontierId)
    {
        string? candidate = frontierId?.Trim();
        return candidate is { Length: > 1 } && candidate[0] is 'F' or 'f' && candidate[1..].All(char.IsAsciiDigit)
            ? candidate.ToUpperInvariant()
            : null;
    }

    private static void CopyIfMissing(string source, string target)
    {
        if (!File.Exists(source) || File.Exists(target))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporaryPath = target + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(source, temporaryPath, overwrite: false);
            File.Move(temporaryPath, target, overwrite: false);
        }
        catch (IOException) when (File.Exists(target))
        {
            // Two instances of the same commander can initialize together.
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
