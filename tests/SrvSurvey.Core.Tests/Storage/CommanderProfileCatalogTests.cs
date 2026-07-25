using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class CommanderProfileCatalogTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-profile-catalog-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadsProfilesAcrossModesAndIsolatesMalformedFiles()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "F123-live.json"),
            "{\"fid\":\"F123\",\"commander\":\"Drew\",\"isOdyssey\":true}");
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "F123-legacy.json"),
            "{\"fid\":\"F123\",\"commander\":\"Old Drew\",\"isOdyssey\":false}");
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "F456-live.json"),
            "{\"commander\":\"Raven\"}");
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "F789-live.json"),
            "{malformed");

        var result = await new CommanderProfileCatalog(temporaryDirectory)
            .LoadAsync();

        Assert.Collection(
            result.Profiles,
            profile =>
            {
                Assert.Equal("F123", profile.FrontierId);
                Assert.Equal("Drew", profile.CommanderName);
                Assert.True(profile.HasLiveProfile);
                Assert.True(profile.HasLegacyProfile);
            },
            profile =>
            {
                Assert.Equal("F456", profile.FrontierId);
                Assert.Equal("Raven", profile.CommanderName);
                Assert.True(profile.HasLiveProfile);
                Assert.False(profile.HasLegacyProfile);
            });
        Assert.Single(result.Warnings);
        Assert.Contains("F789-live.json", result.Warnings[0]);
    }

    [Fact]
    public async Task EmptyDirectoryReturnsNoProfiles()
    {
        var result = await new CommanderProfileCatalog(temporaryDirectory)
            .LoadAsync();

        Assert.Empty(result.Profiles);
        Assert.Empty(result.Warnings);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
