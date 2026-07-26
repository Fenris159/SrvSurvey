using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class CodexImageSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-CodexImageSettings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadSavePreserveUnknownSettingsAndUsePortableDefault()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        var defaultCache = Path.Combine(temporaryDirectory, "default-cache");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "Version": 1,
              "FutureRoot": true,
              "CodexImages": {
                "FutureSetting": 42
              }
            }
            """);
        var store = new CodexImageSettingsStore(path, defaultCache);

        Assert.Equal(
            new CodexImagePreferences(
                Path.GetFullPath(defaultCache),
                null,
                false),
            store.Load());

        var preferences = new CodexImagePreferences(
            Path.Combine(temporaryDirectory, "cache"),
            Path.Combine(temporaryDirectory, "flora"),
            true);
        store.Save(preferences);

        Assert.Equal(preferences, store.Load());
        var saved = Assert.IsType<JsonObject>(
            JsonNode.Parse(await File.ReadAllTextAsync(path)));
        Assert.True(saved["FutureRoot"]?.GetValue<bool>());
        Assert.Equal(
            42,
            saved["CodexImages"]?["FutureSetting"]?.GetValue<int>());
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
