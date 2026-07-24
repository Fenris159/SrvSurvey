using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class ColonizationSettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-colonization-settings-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultsOffAndPersistsExplicitConsent()
    {
        var path = Path.Combine(directory, "ui.json");
        var store = new ColonizationSettingsStore(path);

        Assert.False(store.LoadEnabled());

        store.SaveEnabled(true);

        Assert.True(store.LoadEnabled());
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(root["Colonization"]?["Enabled"]?.GetValue<bool>());
    }

    [Fact]
    public void PreservesOtherUiSettings()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ui.json");
        File.WriteAllText(path, "{\"Theme\":{\"Selected\":\"blue-dark\"}}");

        var store = new ColonizationSettingsStore(path);
        store.SaveEnabled(true);

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal("blue-dark",
            root["Theme"]?["Selected"]?.GetValue<string>());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
