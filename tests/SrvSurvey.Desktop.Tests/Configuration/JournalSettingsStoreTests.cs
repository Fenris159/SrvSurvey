using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class JournalSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-journal-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void MissingSettingsHaveNoOverride()
    {
        Assert.Equal(new JournalPreferences(null), CreateStore().Load());
    }

    [Fact]
    public void DirectoryRoundTripsWithoutRemovingUnknownSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Future\":{\"Keep\":42}}");
        var store = new JournalSettingsStore(path);

        store.Save(new JournalPreferences("  D:\\Elite Journals  "));

        Assert.Equal(
            new JournalPreferences("D:\\Elite Journals"),
            store.Load());
        Assert.Contains("\"Keep\": 42", File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private JournalSettingsStore CreateStore()
    {
        return new JournalSettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
