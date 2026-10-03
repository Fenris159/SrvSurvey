using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class OverlayBehaviorSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-behavior-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void MissingSettingsUseLegacyDefaults()
    {
        Assert.Equal(new OverlayBehaviorPreferences(false, false, false), CreateStore().Load());
    }

    /// <summary>Checks that the bypass choice is durable without removing unrelated settings.</summary>
    [Fact]
    public void PreferencesRoundTripWithoutRemovingUnknownSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Future\":{\"Keep\":42}}");
        var store = new OverlayBehaviorSettingsStore(path);
        var expected = new OverlayBehaviorPreferences(true, true, true, true, "DP-1", true, true);

        store.Save(expected);

        Assert.Equal(expected, store.Load());
        Assert.Contains("\"Keep\": 42", File.ReadAllText(path));
    }

    /// <summary>Rejects malformed bypass values rather than enabling experimental placement implicitly.</summary>
    [Theory]
    [InlineData("null")]
    [InlineData("\"true\"")]
    [InlineData("1")]
    public void InvalidBypassPreferencesRemainDisabled(string jsonValue)
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "ui-settings.json"),
            "{\"OverlayBehavior\":{\"BypassWindowManagement\":" + jsonValue + "}}"
        );

        Assert.False(CreateStore().Load().BypassWindowManagement);
    }

    /// <summary>Checks that commanders sharing a process environment retain independent overlay choices.</summary>
    [Fact]
    public void BypassPreferenceIsIsolatedBySettingsProfile()
    {
        OverlayBehaviorSettingsStore first = CreateStore();
        var second = new OverlayBehaviorSettingsStore(Path.Combine(temporaryDirectory, "second", "ui-settings.json"));
        first.Save(first.Load() with { BypassWindowManagement = true });
        Assert.False(second.Load().BypassWindowManagement);
        second.Save(second.Load() with { LockToMonitor = true });
        first.Save(first.Load() with { BypassWindowManagement = false });
        Assert.False(first.Load().BypassWindowManagement);
        Assert.True(second.Load().LockToMonitor);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("\"  \"")]
    public void InvalidMonitorPreferencesUseAutomatic(string jsonValue)
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "ui-settings.json"),
            "{\"OverlayBehavior\":{\"PreferredMonitor\":" + jsonValue + "}}"
        );

        Assert.Null(CreateStore().Load().PreferredMonitorId);
    }

    [Fact]
    public void AutomaticRemovesOnlyTheOverlayMonitorPreference()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"DesktopBehavior\":{\"PreferredMonitor\":\"HDMI-A-1\"}}");
        OverlayBehaviorSettingsStore store = CreateStore();
        store.Save(new OverlayBehaviorPreferences(false, false, false, PreferredMonitorId: " DP-1 "));
        Assert.Equal("DP-1", store.Load().PreferredMonitorId);

        store.Save(store.Load() with { PreferredMonitorId = null });

        Assert.Null(store.Load().PreferredMonitorId);
        Assert.Equal("HDMI-A-1", new DesktopBehaviorSettingsStore(path).Load().PreferredMonitorId);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private OverlayBehaviorSettingsStore CreateStore()
    {
        return new OverlayBehaviorSettingsStore(Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
