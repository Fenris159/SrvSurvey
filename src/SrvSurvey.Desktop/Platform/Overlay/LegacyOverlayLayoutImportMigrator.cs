using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal static class LegacyOverlayLayoutImportMigrator
{
    internal const string CompletionMarkerFileName =
        ".srv-survey-overlay-layout-import-v1";

    public static LegacyOverlayLayoutImportMigrationResult MigrateIfNeeded(
        AppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var manifestPath = Path.Combine(
            paths.DataDirectory,
            LegacyProfileImporter.ManifestFileName);
        var plottersPath = Path.Combine(paths.DataDirectory, "plotters.json");
        var completionMarkerPath = Path.Combine(
            paths.DataDirectory,
            CompletionMarkerFileName);
        if (File.Exists(completionMarkerPath)
            || !File.Exists(manifestPath)
            || !File.Exists(plottersPath))
        {
            return LegacyOverlayLayoutImportMigrationResult.NotRequired;
        }

        var store = new LegacyOverlayLayoutStore(paths.DataDirectory);
        var layout = store.Load();
        if (layout.Error is not null)
        {
            return new LegacyOverlayLayoutImportMigrationResult(
                false,
                0,
                null,
                layout.Error);
        }

        var normalized = GetNormalizedPlacements(layout);
        return SaveMigration(store, normalized, completionMarkerPath);
    }

    private static Dictionary<string, LegacyOverlayPlacement>
        GetNormalizedPlacements(LegacyOverlayLayout layout)
    {
        var normalized = new Dictionary<string, LegacyOverlayPlacement>(
            StringComparer.Ordinal);
        foreach (var definition in OverlayLayoutCatalog.Supported)
        {
            if (!layout.Placements.TryGetValue(
                    definition.Name,
                    out var placement)
                || !RequiresNormalization(placement))
            {
                continue;
            }

            normalized[definition.Name] = NormalizePlacement(
                placement,
                definition.DefaultPlacement);
        }

        return normalized;
    }

    private static bool RequiresNormalization(LegacyOverlayPlacement placement) =>
        placement.Horizontal is LegacyHorizontalAnchor.Screen
        || placement.Vertical is LegacyVerticalAnchor.Screen;

    private static LegacyOverlayPlacement NormalizePlacement(
        LegacyOverlayPlacement placement,
        LegacyOverlayPlacement defaults) =>
        placement with
        {
            Horizontal = placement.Horizontal is LegacyHorizontalAnchor.Screen
                ? defaults.Horizontal
                : placement.Horizontal,
            HorizontalOffset = placement.Horizontal
                is LegacyHorizontalAnchor.Screen
                    ? defaults.HorizontalOffset
                    : placement.HorizontalOffset,
            Vertical = placement.Vertical is LegacyVerticalAnchor.Screen
                ? defaults.Vertical
                : placement.Vertical,
            VerticalOffset = placement.Vertical
                is LegacyVerticalAnchor.Screen
                    ? defaults.VerticalOffset
                    : placement.VerticalOffset,
        };

    private static LegacyOverlayLayoutImportMigrationResult SaveMigration(
        LegacyOverlayLayoutStore store,
        Dictionary<string, LegacyOverlayPlacement> normalized,
        string completionMarkerPath)
    {
        try
        {
            if (normalized.Count == 0)
            {
                WriteCompletionMarker(completionMarkerPath);
                return LegacyOverlayLayoutImportMigrationResult.NotRequired;
            }

            var result = store.Save(normalized);
            WriteCompletionMarker(completionMarkerPath);
            return new LegacyOverlayLayoutImportMigrationResult(
                true,
                result.UpdatedPlacementCount,
                result.BackupPath,
                null);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or FormatException
                or OverflowException)
        {
            return new LegacyOverlayLayoutImportMigrationResult(
                false,
                0,
                null,
                exception.Message);
        }
    }

    private static void WriteCompletionMarker(string completionMarkerPath)
    {
        var temporaryPath = $"{completionMarkerPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, "1");
            File.Move(temporaryPath, completionMarkerPath, true);
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

internal sealed record LegacyOverlayLayoutImportMigrationResult(
    bool Migrated,
    int NormalizedPlacementCount,
    string? BackupPath,
    string? Error)
{
    public static LegacyOverlayLayoutImportMigrationResult NotRequired { get; } =
        new(false, 0, null, null);
}
