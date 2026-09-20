using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class OverlayScaleSettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-scale-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void CatalogUsesOperatingSystemScaleAsSignedPercentageBaseline()
    {
        Assert.Equal(61, OverlayScaleCatalog.Options.Count);
        Assert.Equal(-100, OverlayScaleCatalog.Options[0].Percent);
        Assert.Equal(0, OverlayScaleCatalog.Options[20].Percent);
        Assert.Equal(200, OverlayScaleCatalog.Options[^1].Percent);
        Assert.All(
            OverlayScaleCatalog.Options.Zip(OverlayScaleCatalog.Options.Skip(1)),
            pair => Assert.Equal(5, pair.Second.Percent - pair.First.Percent)
        );
        Assert.Equal(0d, OverlayScaleCatalog.GetRelativeScale(OverlayScaleCatalog.GetIndex(-100), 1.5));
        Assert.Equal(1d, OverlayScaleCatalog.GetRelativeScale(OverlayScaleCatalog.GetIndex(0), 1.5));
        Assert.Equal(3d, OverlayScaleCatalog.GetRelativeScale(OverlayScaleCatalog.GetIndex(200), 1.5));
    }

    [Fact]
    public void LegacyAbsoluteScaleIndexesConvertRelativeToTheDisplayBaseline()
    {
        Assert.Equal(0, OverlayScaleCatalog.GetPercent(0, 1.5));
        Assert.Equal(0, OverlayScaleCatalog.GetPercent(7, 1.5));
        Assert.Equal(25, OverlayScaleCatalog.GetPercent(12, 1.5));
        Assert.Equal(35, OverlayScaleCatalog.GetPercent(13, 1.5));
        Assert.Equal(45, OverlayScaleCatalog.GetPercent(15, 1.5));
        Assert.Equal(50, OverlayScaleCatalog.GetPercent(16, 1.5));
        Assert.Equal(-65, OverlayScaleCatalog.GetPercent(25, 1.5));
    }

    [Fact]
    public void ForcedScaleCompensatesForDesktopRenderScaling()
    {
        Assert.Equal(1, OverlayScaleCatalog.GetRelativeScale(0, 1.5));
        Assert.Equal(1.5, OverlayScaleCatalog.GetRelativeScale(16, 1.5));
        Assert.Equal(0.4, OverlayScaleCatalog.GetRelativeScale(25, 1.25), 10);
        Assert.Equal(2.25, OverlayScaleCatalog.GetRelativeScale(OverlayScaleCatalog.GetIndex(125), 1.5));
    }

    [Fact]
    public async Task LoadSavePreservesUnknownFieldsAndAcceptsLegacyFloatIndex()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ui-settings.json");
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
            """
        );
        var store = new OverlayScaleSettingsStore(path);

        Assert.Equal(new OverlayScalePreferences(22), store.Load());

        int updatedIndex = OverlayScaleCatalog.GetIndex(50);
        store.Save(new OverlayScalePreferences(updatedIndex));

        JsonObject root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.True(root["FutureRoot"]!.GetValue<bool>());
        Assert.Equal("keep", root["OverlayScale"]!["FutureScale"]!.GetValue<string>());
        Assert.Equal(updatedIndex, root["OverlayScale"]!["Index"]!.GetValue<int>());
    }

    [Fact]
    public async Task UnsupportedOrMalformedIndexFallsBackWithoutRewriting()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ui-settings.json");
        const string content = "{\"Version\":1,\"OverlayScale\":{\"Index\":26.5}}";
        await File.WriteAllTextAsync(path, content);

        OverlayScalePreferences loaded = new OverlayScaleSettingsStore(path).Load();

        Assert.Equal(OverlayScalePreferences.Default, loaded);
        Assert.Equal(content, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task LegacyScaleMigrationIsBackedUpPersistedAndIdempotent()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ui-settings.json");
        const string original = "{\"Version\":1,\"FutureRoot\":true,\"OverlayScale\":{\"Index\":15}}";
        await File.WriteAllTextAsync(path, original);
        var store = new OverlayScaleSettingsStore(path);

        OverlayScaleMigrationResult first = store.MigrateLegacyScale(1.5);
        OverlayScaleMigrationResult second = store.MigrateLegacyScale(1.5);

        Assert.True(first.Migrated);
        Assert.Equal(15, first.PreviousIndex);
        Assert.Equal(OverlayScaleCatalog.GetIndex(45), first.MigratedIndex);
        Assert.NotNull(first.BackupPath);
        Assert.Equal(original, await File.ReadAllTextAsync(first.BackupPath));
        Assert.Equal(new OverlayScalePreferences(OverlayScaleCatalog.GetIndex(45)), store.Load());
        Assert.True(store.Load().Index >= 1000);
        Assert.False(second.Migrated);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
