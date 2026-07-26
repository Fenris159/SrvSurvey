using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class RavenServiceSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-raven-service-tests-{Guid.NewGuid():N}");

    [Fact]
    public void MissingOrInvalidOverrideUsesProductionDefault()
    {
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        var store = new RavenServiceSettingsStore(path);
        Assert.Null(store.LoadServiceUri());

        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            path,
            "{\"RavenService\":{\"ServiceUri\":\"file:///tmp/server\"}}");

        Assert.Null(store.LoadServiceUri());
    }

    [Theory]
    [InlineData("https://example.test", "https://example.test/")]
    [InlineData("http://localhost:7007/api", "http://localhost:7007/api/")]
    [InlineData(
        " https://example.test/dev?ignored=true#fragment ",
        "https://example.test/dev/")]
    public void ValidHttpOverrideIsNormalized(string value, string expected)
    {
        Assert.Equal(
            new Uri(expected),
            RavenServiceSettingsStore.NormalizeServiceUri(value));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
