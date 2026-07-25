using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class SystemNicknameSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-nickname-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void PreferenceRoundTripsWithoutRemovingOtherSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui.json");
        File.WriteAllText(path, "{\"Theme\":\"green-dark\"}");
        var store = new SystemNicknameSettingsStore(path);

        store.SaveEnabled(true);

        Assert.True(store.LoadEnabled());
        Assert.Contains("green-dark", File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
