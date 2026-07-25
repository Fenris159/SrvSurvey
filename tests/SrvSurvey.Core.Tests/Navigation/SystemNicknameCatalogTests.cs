using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Tests.Navigation;

public sealed class SystemNicknameCatalogTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-nickname-tests-{Guid.NewGuid():N}");

    [Fact]
    public void LocalNamesOverrideRavenNamesCaseInsensitively()
    {
        Directory.CreateDirectory(Path.Combine(temporaryDirectory, "pub"));
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "system-nick-names.json"),
            """
            {
              "timestamp": "2026-01-01T00:00:00Z",
              "event": "SystemNickNames",
              "map": {
                "Sol": "Birthplace of Humanity",
                "Shinrarta Dezhra": "Founders World"
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "pub", "nicknames.json"),
            """
            {
              "sol": "Raven Sol",
              "Colonia": "The Colonia Nebula"
            }
            """);

        var catalog = SystemNicknameCatalog.Load(temporaryDirectory);

        Assert.Equal("Birthplace of Humanity", catalog.Resolve("SOL"));
        Assert.Equal("The Colonia Nebula", catalog.Resolve("colonia"));
        Assert.Equal("Achenar", catalog.Resolve("Achenar"));
        Assert.Equal("Sol", catalog.Resolve("Sol", enabled: false));
        Assert.Equal(2, catalog.LocalCount);
        Assert.Equal(2, catalog.RavenCount);
        Assert.Empty(catalog.Warnings);
    }

    [Fact]
    public void MalformedAndInvalidEntriesAreNonDestructiveWarnings()
    {
        Directory.CreateDirectory(Path.Combine(temporaryDirectory, "pub"));
        var localPath = Path.Combine(
            temporaryDirectory,
            "system-nick-names.json");
        const string malformed = "{\"map\":";
        File.WriteAllText(localPath, malformed);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "pub", "nicknames.json"),
            "{\"Valid\":\"Name\",\"Invalid\":42}");

        var catalog = SystemNicknameCatalog.Load(temporaryDirectory);

        Assert.Equal("Name", catalog.Resolve("valid"));
        Assert.Equal("Invalid", catalog.Resolve("Invalid"));
        Assert.Equal(2, catalog.Warnings.Count);
        Assert.Equal(malformed, File.ReadAllText(localPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
