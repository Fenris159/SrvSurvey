using Avalonia;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class CrossPlatformUiSettingsImporterTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-current-ui-import-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task ImportReplacesCurrentSettingsAndBacksUpThePreviousFile()
    {
        string source = Path.Combine(temporaryDirectory, "source", "cross-platform-ui.json");
        string destination = Path.Combine(temporaryDirectory, "config", "cross-platform-ui.json");
        string backup = Path.Combine(temporaryDirectory, "backup");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(source, "{\"Version\":1,\"Theme\":\"orange-dark\"}");
        await File.WriteAllTextAsync(destination, "{\"Version\":1,\"Theme\":\"blue-dark\"}");

        CrossPlatformUiSettingsImportResult result = await CrossPlatformUiSettingsImporter.ImportAsync(
            source,
            destination,
            backup
        );

        Assert.True(result.Imported);
        Assert.Equal(await File.ReadAllBytesAsync(source), await File.ReadAllBytesAsync(destination));
        Assert.Equal(
            "{\"Version\":1,\"Theme\":\"blue-dark\"}",
            await File.ReadAllTextAsync(Path.Combine(backup, LegacyUiSettingsMigrator.BackupFileName))
        );
        Assert.True(File.Exists(Path.Combine(backup, CrossPlatformUiSettingsImporter.CompletionSignalFileName)));
    }

    [Fact]
    public async Task InvalidSourcePreservesCurrentSettings()
    {
        string source = Path.Combine(temporaryDirectory, "source", "cross-platform-ui.json");
        string destination = Path.Combine(temporaryDirectory, "config", "cross-platform-ui.json");
        string backup = Path.Combine(temporaryDirectory, "backup");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(source, "{invalid");
        await File.WriteAllTextAsync(destination, "{\"Version\":1}");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CrossPlatformUiSettingsImporter.ImportAsync(source, destination, backup)
        );

        Assert.Equal("{\"Version\":1}", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task ImportRecordsTheSourceDisplaySizeForRelativeOverlayPlacement()
    {
        string source = Path.Combine(temporaryDirectory, "source", "cross-platform-ui.json");
        string destination = Path.Combine(temporaryDirectory, "config", "cross-platform-ui.json");
        string backup = Path.Combine(temporaryDirectory, "backup");
        string data = Path.Combine(temporaryDirectory, "data");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(data);
        await File.WriteAllTextAsync(
            source,
            """
            {
              "Version": 1,
              "DesktopBehavior": {
                "ApplicationWindowPosition": { "X": 32, "Y": 342, "Monitor": "bounds:0,0,3840,2160" }
              }
            }
            """
        );
        await File.WriteAllTextAsync(Path.Combine(data, "plotters.json"), """{"PlotBodyInfo":"left:960, top:540"}""");

        CrossPlatformUiSettingsImportResult result = await CrossPlatformUiSettingsImporter.ImportAsync(
            source,
            destination,
            backup,
            data
        );
        LegacyOverlayLayout layout = new LegacyOverlayLayoutStore(data).Load();

        Assert.Equal(1, result.ReferencedOverlayCount);
        Assert.Equal(
            new PixelPoint(640, 360),
            layout.GetPosition("PlotBodyInfo", new PixelRect(0, 0, 2560, 1440), new PixelSize(300, 120))
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
