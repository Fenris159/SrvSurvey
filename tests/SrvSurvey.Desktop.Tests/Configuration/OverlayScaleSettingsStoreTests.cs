using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class OverlayScaleSettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-scale-tests-{Guid.NewGuid():N}");

    [Fact]
    public void CatalogMatchesEveryLegacyScaleIndex()
    {
        var expected = new double?[]
        {
            null,
            1,
            1.1,
            1.2,
            1.25,
            1.3,
            1.4,
            1.5,
            1.6,
            1.7,
            1.75,
            1.8,
            1.9,
            2,
            2.1,
            2.2,
            2.25,
            2.3,
            2.4,
            2.5,
            0.9,
            0.8,
            0.75,
            0.7,
            0.6,
            0.5,
        };

        Assert.Equal(expected.Length, OverlayScaleCatalog.Options.Count);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(index, OverlayScaleCatalog.Options[index].Index);
            Assert.Equal(
                expected[index],
                OverlayScaleCatalog.Options[index].AbsoluteScale);
        }
    }

    [Fact]
    public void ForcedScaleCompensatesForDesktopRenderScaling()
    {
        Assert.Equal(1, OverlayScaleCatalog.GetRelativeScale(0, 1.5));
        Assert.Equal(1.5, OverlayScaleCatalog.GetRelativeScale(16, 1.5));
        Assert.Equal(0.4, OverlayScaleCatalog.GetRelativeScale(25, 1.25), 10);
    }

    [Fact]
    public async Task LoadSavePreservesUnknownFieldsAndAcceptsLegacyFloatIndex()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ui-settings.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "Version": 1,
              "FutureRoot": true,
              "OverlayScale": {
                "Index": 22.0,
                "FutureScale": "keep"
              }
            }
            """);
        var store = new OverlayScaleSettingsStore(path);

        Assert.Equal(new OverlayScalePreferences(22), store.Load());

        store.Save(new OverlayScalePreferences(7));

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(root["FutureRoot"]!.GetValue<bool>());
        Assert.Equal(
            "keep",
            root["OverlayScale"]!["FutureScale"]!.GetValue<string>());
        Assert.Equal(7, root["OverlayScale"]!["Index"]!.GetValue<int>());
    }

    [Fact]
    public async Task UnsupportedOrMalformedIndexFallsBackWithoutRewriting()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ui-settings.json");
        const string content =
            "{\"Version\":1,\"OverlayScale\":{\"Index\":26.5}}";
        await File.WriteAllTextAsync(path, content);

        var loaded = new OverlayScaleSettingsStore(path).Load();

        Assert.Equal(OverlayScalePreferences.Default, loaded);
        Assert.Equal(content, await File.ReadAllTextAsync(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
