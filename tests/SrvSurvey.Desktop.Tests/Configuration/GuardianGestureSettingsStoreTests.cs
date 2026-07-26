using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class GuardianGestureSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-guardian-gesture-settings-{Guid.NewGuid():N}");

    [Fact]
    public void DefaultsMatchLegacyGesture()
    {
        var store = new GuardianGestureSettingsStore(Path.Combine(
            temporaryDirectory,
            "ui.json"));

        Assert.Equal(GuardianGesturePreferences.Default, store.Load());
    }

    [Fact]
    public void RoundTripsValidGestureAndNormalizesUnsafeValues()
    {
        var store = new GuardianGestureSettingsStore(Path.Combine(
            temporaryDirectory,
            "ui.json"));
        store.Save(new GuardianGesturePreferences(StatusFlags.LightsOn, 2_500));
        Assert.Equal(
            new GuardianGesturePreferences(StatusFlags.LightsOn, 2_500),
            store.Load());

        store.Save(new GuardianGesturePreferences(
            StatusFlags.LightsOn | StatusFlags.ShieldsUp,
            -1));

        Assert.Equal(GuardianGesturePreferences.Default, store.Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
