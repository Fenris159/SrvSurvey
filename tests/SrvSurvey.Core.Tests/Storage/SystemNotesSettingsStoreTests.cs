using System.Text.Json.Nodes;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class SystemNotesSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-system-notes-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void MissingSettingsReturnDefaultsWithoutCreatingAFile()
    {
        var store = new SystemNotesSettingsStore(temporaryDirectory);

        var result = store.Load();

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Exists);
        Assert.Equal(SystemNotesSettingsSnapshot.Default, result.Snapshot);
        Assert.False(File.Exists(store.Path));
    }

    [Fact]
    public async Task LoadAndSaveUseLegacyFieldsAndPreserveUnknownSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "settings.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "systemNotesTopMost": false,
              "screenshotTargetFolder": "C:\\Elite Screenshots",
              "futureSetting": { "enabled": true }
            }
            """);
        var store = new SystemNotesSettingsStore(temporaryDirectory);

        var result = store.Load();

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Snapshot!.AlwaysOnTop);
        Assert.Equal("C:\\Elite Screenshots", result.Snapshot.ScreenshotTargetFolder);

        await store.SaveAlwaysOnTopAsync(true);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.True(root["systemNotesTopMost"]!.GetValue<bool>());
        Assert.Equal(
            "C:\\Elite Screenshots",
            root["screenshotTargetFolder"]!.GetValue<string>());
        Assert.True(root["futureSetting"]!["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task SaveRefusesToOverwriteMalformedSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "settings.json");
        const string malformed = "{\"systemNotesTopMost\":";
        await File.WriteAllTextAsync(path, malformed);
        var store = new SystemNotesSettingsStore(temporaryDirectory);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveAlwaysOnTopAsync(true));

        Assert.Equal(malformed, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ResolvesLegacyImagesDirectoryWithSafeSystemName()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "settings.json"),
            "{\"screenshotTargetFolder\":\"C:\\\\Elite Screenshots\"}");
        var store = new SystemNotesSettingsStore(temporaryDirectory);

        var result = store.GetImagesDirectory("Test: System/One");

        Assert.Equal(
            Path.Combine("C:\\Elite Screenshots", "Test- System-One"),
            result);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
