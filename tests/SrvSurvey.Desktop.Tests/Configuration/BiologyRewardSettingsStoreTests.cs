using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class BiologyRewardSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-biology-reward-tests-{Guid.NewGuid():N}");

    [Fact]
    public void MissingSettingsUseLegacyThresholds()
    {
        Assert.Equal(BiologyRewardThresholds.Default, CreateStore().Load());
    }

    [Fact]
    public void ThresholdsRoundTripWithoutRemovingUnknownSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Future\":{\"Keep\":42}}");
        var store = new BiologyRewardSettingsStore(path);
        var expected = new BiologyRewardThresholds(2.5, 6.5, 11.5);

        store.Save(expected);

        Assert.Equal(expected, store.Load());
        Assert.Contains("\"Keep\": 42", File.ReadAllText(path));
    }

    [Fact]
    public void InvalidOrUnorderedValuesAreNormalizedSafely()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            "{\"BiologyRewards\":{" +
                "\"BucketOneMillions\":8," +
                "\"BucketTwoMillions\":2," +
                "\"BucketThreeMillions\":99}}");

        Assert.Equal(
            new BiologyRewardThresholds(8, 8, 20),
            new BiologyRewardSettingsStore(path).Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private BiologyRewardSettingsStore CreateStore()
    {
        return new BiologyRewardSettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
