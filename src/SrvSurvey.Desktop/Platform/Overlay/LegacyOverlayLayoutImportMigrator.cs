using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal sealed class LegacyOverlayLayoutImportMigrator
{
    public LegacyOverlayLayoutImportMigrationResult MigrateIfNeeded(
        AppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var manifestPath = Path.Combine(
            paths.DataDirectory,
            LegacyProfileImporter.ManifestFileName);
        var plottersPath = Path.Combine(paths.DataDirectory, "plotters.json");
        if (!File.Exists(manifestPath) || !File.Exists(plottersPath))
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

        var normalized = new Dictionary<string, LegacyOverlayPlacement>(
            StringComparer.Ordinal);
        foreach (var definition in OverlayLayoutCatalog.Supported)
        {
            if (!layout.Placements.TryGetValue(
                    definition.Name,
                    out var placement)
                || placement.Horizontal is not LegacyHorizontalAnchor.Screen
                    && placement.Vertical is not LegacyVerticalAnchor.Screen)
            {
                continue;
            }

            var defaults = definition.DefaultPlacement;
            normalized[definition.Name] = placement with
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
        }

        if (normalized.Count == 0)
        {
            return LegacyOverlayLayoutImportMigrationResult.NotRequired;
        }

        try
        {
            var result = store.Save(normalized);
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
