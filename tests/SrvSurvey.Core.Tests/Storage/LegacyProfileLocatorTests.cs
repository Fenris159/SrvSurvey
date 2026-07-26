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

    [Fact]
    public void DiscoverFindsOlderSiblingVersionProfiles()
    {
        var productRoot = Path.Combine(temporaryDirectory, "SrvSurvey");
        var olderProfile = Path.Combine(productRoot, "1.0.0.0");
        var newestProfile = Path.Combine(productRoot, "1.2.0.0");
        Directory.CreateDirectory(olderProfile);
        Directory.CreateDirectory(newestProfile);
        File.WriteAllText(Path.Combine(olderProfile, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(newestProfile, "settings.json"), "{}");

        var result = LegacyProfileLocator.Discover(
        [
            new LegacyProfileCandidate(
                LegacyProfileLocationKind.Desktop,
                Path.Combine(productRoot, "1.1.0.0")),
        ]);

        Assert.Equal(2, result.Count);
        Assert.Equal(
            Path.GetFullPath(newestProfile),
            result[0].Path);
        Assert.Equal(
            Path.GetFullPath(olderProfile),
            result[1].Path);
    }

    [Fact]
    public void DiscoverIgnoresEmptyVersionDirectories()
    {
        var productRoot = Path.Combine(temporaryDirectory, "SrvSurvey");
        var emptyProfile = Path.Combine(productRoot, "1.1.0.0");
        var populatedProfile = Path.Combine(productRoot, "1.0.0.0");
        Directory.CreateDirectory(Path.Combine(emptyProfile, "systems"));
        Directory.CreateDirectory(populatedProfile);
        File.WriteAllText(Path.Combine(populatedProfile, "settings.json"), "{}");

        var result = LegacyProfileLocator.Discover(
        [
            new LegacyProfileCandidate(
                LegacyProfileLocationKind.Desktop,
                emptyProfile),
        ]);

        Assert.Equal(
            Path.GetFullPath(populatedProfile),
            Assert.Single(result).Path);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
