using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class VrOverlaySettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-vr-settings-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void MissingSettingsUseSafeDisabledDefaults()
    {
        VrOverlaySettingsStore store = CreateStore();

        Assert.Equal(new VrOverlayPreferences(false, "steamvr", "vrserver"), store.Load());
    }

    [Fact]
    public void SavedPreferencesRoundTripWithoutRemovingFutureSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Future\":{\"Keep\":42}}");
        var store = new VrOverlaySettingsStore(path);

        store.Save(new VrOverlayPreferences(true, "meta-via-steamvr", "vrcompositor"));

        Assert.Equal(new VrOverlayPreferences(true, "meta-via-steamvr", "vrcompositor"), store.Load());
        Assert.Contains("\"Keep\": 42", File.ReadAllText(path));
    }

    [Fact]
    public void ExistingCustomProcessMigratesToCustomProfile()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"VirtualReality\":{\"Enabled\":true,\"RuntimeProcessName\":\"my-compositor\"}}");

        var store = new VrOverlaySettingsStore(path);

        Assert.Equal(new VrOverlayPreferences(true, "custom-openvr", "my-compositor"), store.Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private VrOverlaySettingsStore CreateStore()
    {
        return new VrOverlaySettingsStore(Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
