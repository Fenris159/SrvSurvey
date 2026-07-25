using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class SystemSurveySettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-SystemSurveySettings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingDocumentUsesLegacyCompatibleDefaults()
    {
        var preferences = CreateStore().Load();

        Assert.Equal(SystemSurveyPreferences.Default, preferences);
    }

    [Fact]
    public void PreferencesRoundTripWithoutRemovingOtherUiSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Theme\":\"Blue-dark\"}");
        var store = new SystemSurveySettingsStore(path);
        var expected = new SystemSurveyPreferences(
            false,
            false,
            false,
            true,
            false,
            150,
            true,
            2.5,
            false,
            true,
            false,
            true,
            false,
            true,
            true,
            false,
            false,
            true,
            true,
            false,
            true,
            25_000,
            false,
            750_000,
            true,
            50_000,
            false,
            false,
            true);

        store.Save(expected);

        Assert.Equal(expected, store.Load());
        Assert.Contains("Blue-dark", File.ReadAllText(path));
    }

    [Fact]
    public void NegativeNumericValuesAreClamped()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            "{\"SystemSurvey\":{\"FssBodyValueFloor\":-1,"
                + "\"DssValueFloor\":-2,\"DssDistanceLimitLs\":-3,"
                + "\"BodyInfoBubbleSizeLy\":-4,"
                + "\"HighGravityWarningLevel\":75}}");

        var preferences = new SystemSurveySettingsStore(path).Load();

        Assert.Equal(0, preferences.FssBodyValueFloor);
        Assert.Equal(0, preferences.DssValueFloor);
        Assert.Equal(0, preferences.DssDistanceLimitLs);
        Assert.Equal(0, preferences.BodyInfoBubbleSizeLy);
        Assert.Equal(50, preferences.HighGravityWarningLevel);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private SystemSurveySettingsStore CreateStore()
    {
        return new SystemSurveySettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
