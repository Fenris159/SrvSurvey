using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

/// <summary>Checks default cockpit visibility and durable, isolated UI preferences.</summary>
public sealed class FiregroupsOverlaySettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    /// <summary>Older settings and invalid entries keep the main cockpit as the only enabled view.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"FiregroupsOverlay\":[]}")]
    [InlineData("{\"FiregroupsOverlay\":{\"ShowInLeftView\":\"yes\",\"ShowInMainView\":null,\"ShowInRightView\":1}}")]
    public void MissingOrInvalidViewValuesUseDefaults(string json)
    {
        FiregroupsOverlaySettingsStore store = CreateStore();
        Assert.Equal(FiregroupsOverlayPreferences.Default, store.Load());
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "ui-settings.json"), json);
        Assert.Equal(new FiregroupsOverlayPreferences(false, true, false), store.Load());
    }

    /// <summary>Valid partial preferences retain their value while unspecified views use their defaults.</summary>
    [Fact]
    public void PartialPreferencesPreserveExplicitChoices()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "ui-settings.json"),
            """{"FiregroupsOverlay":{"ShowInLeftView":true,"ShowInMainView":false}}"""
        );
        Assert.Equal(new FiregroupsOverlayPreferences(true, false, false), CreateStore().Load());
    }

    /// <summary>Saving first and subsequent choices preserves unrelated settings and unknown section fields.</summary>
    [Fact]
    public void PreferencesRoundTripWithoutReplacingOtherSettings()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ui-settings.json");
        File.WriteAllText(path, """{"Theme":"Blue-dark"}""");
        FiregroupsOverlaySettingsStore store = CreateStore();
        store.Save(new(true, false, true));
        Assert.Equal(new FiregroupsOverlayPreferences(true, false, true), CreateStore().Load());
        new UiSettingsDocumentStore(path).Update(root => root["FiregroupsOverlay"]!["FutureSetting"] = "retained");
        store.Save(FiregroupsOverlayPreferences.Default);
        Assert.Equal(FiregroupsOverlayPreferences.Default, CreateStore().Load());
        JsonNode root = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("Blue-dark", root["Theme"]!.GetValue<string>());
        Assert.Equal("retained", root["FiregroupsOverlay"]!["FutureSetting"]!.GetValue<string>());
        Assert.Throws<ArgumentNullException>(() => store.Save(null!));
    }

    /// <summary>Creates an independent document for each test.</summary>
    private FiregroupsOverlaySettingsStore CreateStore() => new(Path.Combine(directory, "ui-settings.json"));

    /// <summary>Removes only the temporary preferences created by these tests.</summary>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
