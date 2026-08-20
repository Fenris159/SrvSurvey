using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class LegacyOverlayLayoutImportMigratorTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-import-migration-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ImportedAbsoluteDesktopAnchorsAreConvertedToRelativeDefaults()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(
            Path.Combine(
                paths.DataDirectory,
                LegacyProfileImporter.ManifestFileName),
            "{}");
        File.WriteAllText(
            Path.Combine(paths.DataDirectory, "plotters.json"),
            """
            {
              "PlotBodyInfo": "screen:3140, os:220, 0.65 { s: 10, p: <1, 2, 3>, r: <4, 5, 6>}",
              "PlotSysStatus": "right:18, bottom:44",
              "PlotAdjustVR": "screen:-100, os:25"
            }
            """);

        var result = LegacyOverlayLayoutImportMigrator.MigrateIfNeeded(paths);

        Assert.True(result.Migrated);
        Assert.Equal(1, result.NormalizedPlacementCount);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        var layout = new LegacyOverlayLayoutStore(paths.DataDirectory).Load();
        Assert.Equal(
            new LegacyOverlayPlacement(
                LegacyHorizontalAnchor.Left,
                8,
                LegacyVerticalAnchor.Top,
                8,
                0.65),
            layout.Placements["PlotBodyInfo"]);
        Assert.Equal(
            new LegacyOverlayPlacement(
                LegacyHorizontalAnchor.Right,
                18,
                LegacyVerticalAnchor.Bottom,
                44,
                null),
            layout.Placements["PlotSysStatus"]);
        Assert.Equal(
            new LegacyOverlayPlacement(
                LegacyHorizontalAnchor.Screen,
                -100,
                LegacyVerticalAnchor.Screen,
                25,
                null),
            layout.Placements["PlotAdjustVR"]);
        var saved = File.ReadAllText(
            Path.Combine(paths.DataDirectory, "plotters.json"));
        Assert.Contains("left:8, top:8, 0.65", saved);
        Assert.Contains(
            "{ s: 10, p: <1, 2, 3>, r: <4, 5, 6>}",
            saved);
        Assert.Contains("screen:-100, os:25", saved);

        Assert.True(File.Exists(Path.Combine(
            paths.DataDirectory,
            LegacyOverlayLayoutImportMigrator.CompletionMarkerFileName)));

        const string remapped =
            "{\"PlotBodyInfo\":\"screen:400, os:250\"}";
        File.WriteAllText(
            Path.Combine(paths.DataDirectory, "plotters.json"),
            remapped);
        var repeated = LegacyOverlayLayoutImportMigrator.MigrateIfNeeded(paths);

        Assert.False(repeated.Migrated);
        Assert.Null(repeated.Error);
        Assert.Equal(
            remapped,
            File.ReadAllText(Path.Combine(paths.DataDirectory, "plotters.json")));
    }

    [Fact]
    public void LayoutWithoutImportManifestIsNotChanged()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.DataDirectory);
        var plottersPath = Path.Combine(paths.DataDirectory, "plotters.json");
        const string original = "{\"PlotBodyInfo\":\"screen:3140, os:220\"}";
        File.WriteAllText(plottersPath, original);

        var result = LegacyOverlayLayoutImportMigrator.MigrateIfNeeded(paths);

        Assert.False(result.Migrated);
        Assert.Equal(original, File.ReadAllText(plottersPath));
    }

    [Fact]
    public void CompletedNoOpImportDoesNotReprocessLaterAbsoluteAnchor()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(
            Path.Combine(
                paths.DataDirectory,
                LegacyProfileImporter.ManifestFileName),
            "{}");
        var plottersPath = Path.Combine(paths.DataDirectory, "plotters.json");
        File.WriteAllText(
            plottersPath,
            "{\"PlotBodyInfo\":\"left:8, top:8\"}");

        var result = LegacyOverlayLayoutImportMigrator.MigrateIfNeeded(paths);

        Assert.False(result.Migrated);
        Assert.True(File.Exists(Path.Combine(
            paths.DataDirectory,
            LegacyOverlayLayoutImportMigrator.CompletionMarkerFileName)));

        const string laterAbsolute =
            "{\"PlotBodyInfo\":\"screen:900, os:300\"}";
        File.WriteAllText(plottersPath, laterAbsolute);

        var repeated = LegacyOverlayLayoutImportMigrator.MigrateIfNeeded(paths);

        Assert.False(repeated.Migrated);
        Assert.Equal(laterAbsolute, File.ReadAllText(plottersPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private AppDataPaths CreatePaths() => new(
        Path.Combine(temporaryDirectory, "config"),
        Path.Combine(temporaryDirectory, "data"),
        Path.Combine(temporaryDirectory, "cache"),
        []);
}
