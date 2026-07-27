using Avalonia;
using Avalonia.Media;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.Tests.Theming;

public sealed class ProfileThemeImportTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-theme-import-parity-{Guid.NewGuid():N}");

    [Fact]
    public async Task ImportPreservesOverlayThemeAndLayoutIndependentlyOfAppTheme()
    {
        var source = Path.Combine(temporaryDirectory, "legacy");
        var data = Path.Combine(temporaryDirectory, "data");
        var config = Path.Combine(temporaryDirectory, "config");
        var backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(config);
        var themeBytes = """
            {
              "orange": [128, 10, 20, 30],
              "future": "orange"
            }
            """u8.ToArray();
        var layoutBytes = """
            {
              "PlotBodyInfo": "left:8, top:12, 0.75 { s: 10, p: <1, 2, 3>, r: <4, 5, 6>}",
              "FutureOverlay": "right:18, bottom:44"
            }
            """u8.ToArray();
        await File.WriteAllBytesAsync(
            Path.Combine(source, "theme.json"),
            themeBytes);
        await File.WriteAllBytesAsync(
            Path.Combine(source, "plotters.json"),
            layoutBytes);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "{\"darkTheme\":false,\"plotterOpacity\":55}");
        var paths = new AppDataPaths(
            config,
            data,
            Path.Combine(temporaryDirectory, "cache"),
            []);
        var import = await new LegacyProfileImporter().ImportAsync(
            source,
            data,
            backups);

        var migration = new LegacyUiSettingsMigrator().MigrateIfNeeded(paths);
        var importedOverlay = new LegacyOverlayThemeStore(
            Path.Combine(data, "theme.json")).Load();
        var importedLayout = new LegacyOverlayLayoutStore(data).Load();
        var application = new Application();
        var themePreferences = new ThemePreferenceStore(paths.UiSettingsPath);
        Assert.True(migration.Migrated);
        Assert.Equal("blue-light", themePreferences.LoadThemeKey());
        var service = new RavenThemeService(
            application,
            themePreferences,
            importedOverlay);
        service.ApplyCurrent();
        var overlayAccentBefore = Assert.IsType<SolidColorBrush>(
            application.Resources["RavenOverlayAccentBrush"]).Color;

        service.Select("orange-dark");

        Assert.Equal("orange-dark", themePreferences.LoadThemeKey());
        Assert.True(importedOverlay.IsCustom);
        Assert.Null(importedOverlay.Error);
        Assert.Equal(
            Color.FromArgb(128, 10, 20, 30),
            importedOverlay.GetColor("orange"));
        Assert.Equal(
            importedOverlay.GetColor("orange"),
            importedOverlay.GetColor("future"));
        Assert.Equal(overlayAccentBefore, Assert.IsType<SolidColorBrush>(
            application.Resources["RavenOverlayAccentBrush"]).Color);
        Assert.Equal(importedOverlay, service.CurrentOverlayTheme);
        Assert.Null(importedLayout.Error);
        Assert.Equal(2, importedLayout.Placements.Count);
        Assert.Equal(0.75, importedLayout.GetOpacity("PlotBodyInfo"));
        Assert.Equal(0.55, importedLayout.GetOpacity("FutureOverlay"));
        Assert.Equal(
            themeBytes,
            await File.ReadAllBytesAsync(Path.Combine(source, "theme.json")));
        Assert.Equal(
            themeBytes,
            await File.ReadAllBytesAsync(Path.Combine(
                import.BackupDirectory,
                "profile",
                "theme.json")));
        Assert.Equal(
            themeBytes,
            await File.ReadAllBytesAsync(Path.Combine(data, "theme.json")));
        Assert.Equal(
            layoutBytes,
            await File.ReadAllBytesAsync(Path.Combine(source, "plotters.json")));
        Assert.Equal(
            layoutBytes,
            await File.ReadAllBytesAsync(Path.Combine(
                import.BackupDirectory,
                "profile",
                "plotters.json")));
        Assert.Equal(
            layoutBytes,
            await File.ReadAllBytesAsync(Path.Combine(data, "plotters.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
