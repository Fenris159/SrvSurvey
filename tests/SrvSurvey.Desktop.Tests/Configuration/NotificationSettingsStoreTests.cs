using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class NotificationSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-notification-settings-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void SaveRoundTripsAndPreservesUnknownSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Future\":42,\"Notifications\":{\"FutureOption\":true}}");
        var store = new NotificationSettingsStore(path);
        var preferences = new NotificationPreferences(false, false, true, false, true, false)
        {
            VisibleMessageLimit = 7,
        };

        store.Save(preferences);

        Assert.Equal(preferences, store.Load());
        JsonObject root = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(path)));
        Assert.Equal(42, root["Future"]?.GetValue<int>());
        Assert.True(root["Notifications"]?["FutureOption"]?.GetValue<bool>());
    }

    /// <summary>Older and malformed settings use the default, while numeric limits stay within the supported range.</summary>
    [Theory]
    [InlineData("{}", 3)]
    [InlineData("{\"VisibleMessageLimit\":null}", 3)]
    [InlineData("{\"VisibleMessageLimit\":\"many\"}", 3)]
    [InlineData("{\"VisibleMessageLimit\":{}}", 3)]
    [InlineData("{\"VisibleMessageLimit\":1.5}", 3)]
    [InlineData("{\"VisibleMessageLimit\":-1}", 1)]
    [InlineData("{\"VisibleMessageLimit\":100}", 20)]
    [InlineData("{\"VisibleMessageLimit\":5}", 5)]
    public void VisibleMessageLimitLoadsSafely(string settings, int expected)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Notifications\":" + settings + "}");
        Assert.Equal(expected, new NotificationSettingsStore(path).Load().VisibleMessageLimit);
    }

    /// <summary>Invalid directly supplied preferences cannot persist an unusable row limit.</summary>
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(100, 20)]
    public void SaveBoundsVisibleMessageLimit(int value, int expected)
    {
        var store = new NotificationSettingsStore(Path.Combine(temporaryDirectory, "ui-settings.json"));
        store.Save(NotificationPreferences.Default with { VisibleMessageLimit = value });
        Assert.Equal(expected, store.Load().VisibleMessageLimit);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
