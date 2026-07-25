using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class NotificationSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-notification-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void SaveRoundTripsAndPreservesUnknownSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            "{\"Future\":42,\"Notifications\":{\"FutureOption\":true}}");
        var store = new NotificationSettingsStore(path);
        var preferences = new NotificationPreferences(
            false,
            false,
            true,
            false,
            true,
            false);

        store.Save(preferences);

        Assert.Equal(preferences, store.Load());
        var root = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(path)));
        Assert.Equal(42, root["Future"]?.GetValue<int>());
        Assert.True(root["Notifications"]?["FutureOption"]?.GetValue<bool>());
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
