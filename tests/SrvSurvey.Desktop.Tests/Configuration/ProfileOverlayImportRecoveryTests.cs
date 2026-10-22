using Avalonia;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Tests.ProfileSync;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class ProfileOverlayImportRecoveryTests
{
    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"PlotBodyInfo\":null}")]
    [InlineData("{\"PlotBodyInfo\":{}}")]
    public async Task DamagedReferenceRecoveryStateDoesNotBlockNewPanels(string damaged)
    {
        using var temp = new ProfileSyncTestRoot();
        AppDataPaths paths = PrepareCurrent(temp);
        string source = PrepareSource(temp, shared: true, nested: false);
        const string stateFile = "pending-overlay-import-references.json";
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(paths.UiSettingsPath)!, stateFile), damaged);
        ProfileImportResult result = await ImportAsync(paths, source);
        var warnings = new List<ProfileImportWarning>();
        CommanderSettingsProfile.PublishImportedOverlays(paths, result, warnings);
        Assert.Contains(
            "PlotBodyInfo",
            new LegacyOverlayLayoutStore(paths.OverlaySettingsDirectory).Load().Placements.Keys
        );
        Assert.Contains(warnings, warning => warning.RelativePath == stateFile);
        Assert.Equal(damaged, await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, stateFile)));
    }

    [Fact]
    public async Task RejectedOptionalOverridesDoNotBlockRecoveredPanels()
    {
        using var temp = new ProfileSyncTestRoot();
        AppDataPaths paths = PrepareCurrent(temp);
        string source = PrepareSource(temp, shared: true, nested: false);
        await File.WriteAllTextAsync(Path.Combine(source, "overlay-scale-overrides.json"), "{broken");
        await File.WriteAllTextAsync(Path.Combine(paths.DataDirectory, "overlay-scale-overrides.json"), "{}");
        await File.WriteAllTextAsync(
            Path.Combine(paths.OverlaySettingsDirectory, "overlay-scale-overrides.json"),
            "{}"
        );
        ProfileImportResult result = await ImportAsync(paths, source);

        CommanderSettingsProfile.PublishImportedOverlays(paths, result);

        Assert.Contains(
            "PlotBodyInfo",
            new LegacyOverlayLayoutStore(paths.OverlaySettingsDirectory).Load().Placements.Keys
        );
        Assert.Equal(
            "{}",
            await File.ReadAllTextAsync(Path.Combine(paths.OverlaySettingsDirectory, "overlay-scale-overrides.json"))
        );
        Assert.Contains(result.Warnings, warning => warning.RelativePath == "overlay-scale-overrides.json");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedCommanderAdditionsReceiveTheirSourceDisplayDimensions(bool shared)
    {
        using var temp = new ProfileSyncTestRoot();
        AppDataPaths paths = PrepareCurrent(temp);
        string source = PrepareSource(temp, shared, nested: true);
        string ui = Path.Combine(Path.GetDirectoryName(source)!, "cross-platform-ui.json");
        WriteSourceUi(ui);
        ProfileImportResult result = await ImportAsync(paths, source);

        IReadOnlySet<string> added = CommanderSettingsProfile.PublishImportedOverlays(paths, result);
        await CrossPlatformUiSettingsImporter.ImportAsync(
            ui,
            paths.UiSettingsPath,
            result.BackupDirectory,
            paths.OverlaySettingsDirectory,
            added
        );
        new ImportedOverlayReferenceRecovery(paths).Complete(result);
        ProfileImportResult repeat = await ImportAsync(paths, source);
        Assert.Empty(CommanderSettingsProfile.PublishImportedOverlays(paths, repeat));

        LegacyOverlayLayout layout = new LegacyOverlayLayoutStore(paths.OverlaySettingsDirectory).Load();
        Assert.Equal(
            new PixelPoint(640, 360),
            layout.GetPosition("PlotBodyInfo", new PixelRect(0, 0, 2560, 1440), new PixelSize(300, 120))
        );
        Assert.Null(layout.Placements["PlotJumpInfo"].PositionReference);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedImportRepairsPendingReferencesWithoutChangingManualMoves(bool moved)
    {
        using var temp = new ProfileSyncTestRoot();
        AppDataPaths paths = PrepareCurrent(temp);
        string source = PrepareSource(temp, shared: true, nested: false);
        string ui = Path.Combine(Path.GetDirectoryName(source)!, "cross-platform-ui.json");
        await File.WriteAllTextAsync(ui, "{broken");
        ProfileImportResult first = await ImportAsync(paths, source);
        IReadOnlySet<string> firstAdded = CommanderSettingsProfile.PublishImportedOverlays(paths, first);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CrossPlatformUiSettingsImporter.ImportAsync(
                ui,
                paths.UiSettingsPath,
                first.BackupDirectory,
                paths.OverlaySettingsDirectory,
                firstAdded
            )
        );
        if (moved)
        {
            await File.WriteAllTextAsync(
                Path.Combine(paths.OverlaySettingsDirectory, "plotters.json"),
                """{"PlotJumpInfo":"left:100, top:200","PlotBodyInfo":"left:10, top:20"}"""
            );
        }
        WriteSourceUi(ui);
        ProfileImportResult retry = await ImportAsync(paths, source);
        IReadOnlySet<string> added = CommanderSettingsProfile.PublishImportedOverlays(paths, retry);
        await CrossPlatformUiSettingsImporter.ImportAsync(
            ui,
            paths.UiSettingsPath,
            retry.BackupDirectory,
            paths.OverlaySettingsDirectory,
            added
        );
        new ImportedOverlayReferenceRecovery(paths).Complete(retry);

        LegacyOverlayLayout layout = new LegacyOverlayLayoutStore(paths.OverlaySettingsDirectory).Load();
        Assert.Equal(
            moved ? new PixelPoint(10, 20) : new PixelPoint(640, 360),
            layout.GetPosition("PlotBodyInfo", new PixelRect(0, 0, 2560, 1440), new PixelSize(300, 120))
        );
        Assert.Equal(!moved, layout.Placements["PlotBodyInfo"].PositionReference is not null);
    }

    private static AppDataPaths PrepareCurrent(ProfileSyncTestRoot temp)
    {
        AppDataPaths paths = temp.Paths with { SettingsFrontierId = "F123" };
        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.OverlaySettingsDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.UiSettingsPath)!);
        File.WriteAllText(
            Path.Combine(paths.OverlaySettingsDirectory, "plotters.json"),
            """{"PlotJumpInfo":"left:100, top:200"}"""
        );
        File.WriteAllText(
            paths.UiSettingsPath,
            """{"DesktopBehavior":{"ApplicationWindowPosition":{"Monitor":"bounds:0,0,2560,1440"}}}"""
        );
        return paths;
    }

    private static string PrepareSource(ProfileSyncTestRoot temp, bool shared, bool nested)
    {
        string source = Path.Combine(temp.Paths.CacheDirectory, "source", "cross-platform");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "F123-live.json"), "{}");
        if (shared)
        {
            File.WriteAllText(Path.Combine(source, "plotters.json"), """{"PlotBodyInfo":"left:960, top:540"}""");
        }
        if (nested)
        {
            string directory = Path.Combine(source, "commanders", "F123", "overlays");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "plotters.json"), """{"PlotBodyInfo":"left:960, top:540"}""");
        }
        return source;
    }

    private static void WriteSourceUi(string path) =>
        File.WriteAllText(
            path,
            """{"DesktopBehavior":{"ApplicationWindowPosition":{"Monitor":"bounds:0,0,3840,2160"}}}"""
        );

    private static Task<ProfileImportResult> ImportAsync(AppDataPaths paths, string source) =>
        new LegacyProfileImporter().ImportAsync(
            new ProfileImportSource(source, ProfileImportSourceKind.CrossPlatform, null),
            paths.DataDirectory,
            Path.Combine(paths.CacheDirectory, "backups")
        );
}
