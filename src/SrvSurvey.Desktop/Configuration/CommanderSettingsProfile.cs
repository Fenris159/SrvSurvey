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
    public static IReadOnlySet<string> PublishImportedOverlays(AppDataPaths paths, ProfileImportResult result)
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
        string previousDirectory = shared
            ? Path.Combine(result.BackupDirectory, "previous-destination")
            : paths.OverlaySettingsDirectory;
        IReadOnlySet<string> addedPanels = importedFiles.Contains("plotters.json")
            ? FindAddedPanels(
                Path.Combine(sourceDirectory, "plotters.json"),
                Path.Combine(previousDirectory, "plotters.json")
            )
            : new HashSet<string>(StringComparer.Ordinal);
        if (shared)
        {
            return addedPanels;
        }
        Directory.CreateDirectory(paths.OverlaySettingsDirectory);
        using OverlayLayoutFileAccess.Transaction transaction = OverlayLayoutFileAccess.BeginTransaction(
            paths.OverlaySettingsDirectory
        );
        foreach (string fileName in OverlayFiles)
        {
            if (!importedFiles.Contains(fileName))
            {
                continue;
            }

            string source = Path.Combine(sourceDirectory, fileName);
            string target = Path.Combine(paths.OverlaySettingsDirectory, fileName);
            string temporaryPath = target + $".{Guid.NewGuid():N}.tmp";
            try
            {
                StageImportedOverlay(source, target, temporaryPath, result.Manifest.Version >= 4, addedPanels);
                File.Move(temporaryPath, target, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        transaction.Commit();
        return addedPanels;
    }

    private static void StageImportedOverlay(
        string source,
        string target,
        string staged,
        bool merge,
        IReadOnlySet<string> addedPanels
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

    private static HashSet<string> FindAddedPanels(string source, string current)
    {
        JsonObject incoming = LegacyOverlayLayoutStore.ParseObject(source);
        JsonObject existing = File.Exists(current) ? LegacyOverlayLayoutStore.ParseObject(current) : [];
        return incoming
            .Select(entry => entry.Key)
            .Where(key => !existing.ContainsKey(key))
            .ToHashSet(StringComparer.Ordinal);
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
