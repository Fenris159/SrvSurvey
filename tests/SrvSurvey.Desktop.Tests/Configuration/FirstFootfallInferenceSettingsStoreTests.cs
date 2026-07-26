using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class FirstFootfallInferenceSettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-first-footfall-settings-{Guid.NewGuid():N}");

    [Fact]
    public void LoadUsesLegacyCompatibleDefaults()
    {
        var preferences = new FirstFootfallInferenceSettingsStore(
            Path.Combine(directory, "ui-settings.json")).Load();

        Assert.Equal(FirstFootfallInferencePreferences.Default, preferences);
    }

    [Fact]
    public void SaveNormalizesValuesAndPreservesUnknownProperties()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ui-settings.json");
        File.WriteAllText(
            path,
            """
            {
              "FutureRoot": true,
              "FirstFootfallInference": {
                "FutureSetting": "keep",
                "Color": { "FutureColor": 7 }
              }
            }
            """);
        var store = new FirstFootfallInferenceSettingsStore(path);

        store.Save(new FirstFootfallInferencePreferences(
            false,
            -1,
            500,
            64,
            500,
            double.NaN,
            0,
            100));

        var preferences = store.Load();
        Assert.False(preferences.Enabled);
        Assert.Equal(0, preferences.Red);
        Assert.Equal(255, preferences.Green);
        Assert.Equal(64, preferences.Blue);
        Assert.Equal(255, preferences.Tolerance);
        Assert.Equal(0.002, preferences.Threshold);
        Assert.Equal(1, preferences.DurationSeconds);
        Assert.Equal(60, preferences.SamplesPerSecond);
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(root["FutureRoot"]!.GetValue<bool>());
        Assert.Equal(
            "keep",
            root["FirstFootfallInference"]!["FutureSetting"]!
                .GetValue<string>());
        Assert.Equal(
            7,
            root["FirstFootfallInference"]!["Color"]!["FutureColor"]!
                .GetValue<int>());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
