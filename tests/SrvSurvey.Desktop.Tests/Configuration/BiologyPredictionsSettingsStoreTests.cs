using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class BiologyPredictionsSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-BiologyPredictionsSettings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingDocumentUsesLegacyCompatibleDefaults()
    {
        var preferences = CreateStore().Load();

        Assert.Equal(BiologyPredictionsPreferences.Default, preferences);
    }

    [Fact]
    public void PreferencesRoundTripWithoutRemovingOtherUiSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Theme\":\"Blue-dark\"}");
        var store = new BiologyPredictionsSettingsStore(path);
        var expected = new BiologyPredictionsPreferences(true, 3);

        store.Save(expected);

        Assert.Equal(expected, store.Load());
        Assert.Contains("Blue-dark", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(5, 3)]
    public void RowSizeIsClamped(int storedValue, int expected)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            "{\"BiologyPredictions\":{\"RowSize\":"
                + storedValue
                + "}}");

        var preferences = new BiologyPredictionsSettingsStore(path).Load();

        Assert.Equal(expected, preferences.RowSize);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private BiologyPredictionsSettingsStore CreateStore()
    {
        return new BiologyPredictionsSettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
