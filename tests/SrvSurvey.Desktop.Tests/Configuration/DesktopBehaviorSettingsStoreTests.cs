using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class DesktopBehaviorSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-desktop-behavior-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void MissingSettingsUseLegacyDefaults()
    {
        Assert.Equal(
            new DesktopBehaviorPreferences(
                true,
                true,
                false,
                false,
                PreferredMonitorId: null,
                ApplicationWindowScalePercent: 100,
                LastApplicationWindowPosition: null,
                ReduceMotion: false
            ),
            CreateStore().Load()
        );
    }

    [Fact]
    public void PreferencesRoundTripWithoutRemovingUnknownSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Future\":{\"Keep\":42}}");
        var store = new DesktopBehaviorSettingsStore(path);
        var expected = new DesktopBehaviorPreferences(
            false,
            false,
            true,
            true,
            "\\\\.\\DISPLAY2",
            125,
            new ApplicationWindowPosition(2140, 86, "\\\\.\\DISPLAY2"),
            ReduceMotion: true,
            LastApplicationWindowGeometry: new ApplicationWindowGeometry(1370, 870, true, 125, 1, 2560, 1440)
        );

        store.Save(expected);

        Assert.Equal(expected, store.Load());
        Assert.Contains("\"Keep\": 42", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(75)]
    [InlineData(200)]
    public void UnsupportedWindowScalesUseDefault(int scalePercent)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            $$"""
            {
              "DesktopBehavior": {
                "ApplicationWindowScalePercent": {{scalePercent}}
              }
            }
            """
        );

        Assert.Equal(100, new DesktopBehaviorSettingsStore(path).Load().ApplicationWindowScalePercent);
    }

    [Theory]
    [InlineData("{\"Width\":\"broken\"}")]
    [InlineData("42")]
    public void CorruptGeometryLeavesOtherPreferencesReadable(string geometry)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            "{\"DesktopBehavior\":{\"FocusGameOnStart\":false,\"ApplicationWindowGeometry\":" + geometry + "}}"
        );
        DesktopBehaviorPreferences saved = new DesktopBehaviorSettingsStore(path).Load();
        Assert.False(saved.FocusGameOnStart);
        Assert.Null(saved.LastApplicationWindowGeometry);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private DesktopBehaviorSettingsStore CreateStore()
    {
        return new DesktopBehaviorSettingsStore(Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
