using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class HumanSiteSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"SrvSurvey-human-site-settings-{Guid.NewGuid():N}");

    [Fact]
    public void MissingSettingsUseLegacyDefaults()
    {
        var settings = new HumanSiteSettingsStore(SettingsPath()).Load();

        Assert.Equal(HumanSitePreferences.Default, settings);
        Assert.True(settings.AutoShow);
        Assert.Equal(500, settings.Width);
        Assert.Equal(600, settings.Height);
        Assert.Equal(1.5, settings.SrvZoom);
        Assert.Equal(6, settings.ToolZoom);
    }

    [Fact]
    public void RoundTripsPreferencesWithoutReplacingOtherUiSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(SettingsPath(), """{"Theme":{"Mode":"Dark"}}""");
        var store = new HumanSiteSettingsStore(SettingsPath());
        var expected = HumanSitePreferences.Default with
        {
            AutoShow = false,
            Width = 720,
            Height = 800,
            ShipZoom = 1.2,
            ShowMedkits = false,
            SuppressForActiveBuildProjects = false,
        };

        store.Save(expected);
        var actual = store.Load();

        Assert.Equal(expected, actual);
        var root = JsonNode.Parse(File.ReadAllText(SettingsPath()))!.AsObject();
        Assert.Equal("Dark", root["Theme"]!["Mode"]!.GetValue<string>());
    }

    [Fact]
    public void InvalidSizesAndZoomsAreClampedOrDefaulted()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            SettingsPath(),
            """
            {"HumanSite":{"Width":1,"Height":9000,"ShipZoom":-2,"SrvZoom":"bad"}}
            """);

        var settings = new HumanSiteSettingsStore(SettingsPath()).Load();

        Assert.Equal(320, settings.Width);
        Assert.Equal(1400, settings.Height);
        Assert.Equal(0.2, settings.ShipZoom);
        Assert.Equal(HumanSitePreferences.Default.SrvZoom, settings.SrvZoom);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private string SettingsPath()
    {
        return System.IO.Path.Combine(temporaryDirectory, "ui-settings.json");
    }
}
