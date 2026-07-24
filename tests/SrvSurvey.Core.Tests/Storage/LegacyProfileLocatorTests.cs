using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class LegacyProfileLocatorTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-profile-locator-tests-{Guid.NewGuid():N}");

    [Fact]
    public void DiscoverReturnsOnlyExistingProfilesWithoutChangingThem()
    {
        var desktopPath = Path.Combine(temporaryDirectory, "desktop");
        Directory.CreateDirectory(Path.Combine(desktopPath, "systems"));
        File.WriteAllText(Path.Combine(desktopPath, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(desktopPath, "systems", "one.json"), "{}");

        var result = LegacyProfileLocator.Discover(
        [
            new LegacyProfileCandidate(LegacyProfileLocationKind.Desktop, desktopPath),
            new LegacyProfileCandidate(
                LegacyProfileLocationKind.MicrosoftStore,
                Path.Combine(temporaryDirectory, "missing")),
        ]);

        var profile = Assert.Single(result);
        Assert.Equal(LegacyProfileLocationKind.Desktop, profile.Kind);
        Assert.Equal(Path.GetFullPath(desktopPath), profile.Path);
        Assert.Equal(2, profile.FileCount);
        Assert.True(File.Exists(Path.Combine(desktopPath, "settings.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
