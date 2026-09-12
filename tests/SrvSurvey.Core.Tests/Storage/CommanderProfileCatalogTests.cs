using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class CommanderProfileCatalogTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-profile-catalog-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task LoadsProfilesAcrossModesAndIsolatesMalformedFiles()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "F123-live.json"),
            "{\"fid\":\"F123\",\"commander\":\"Drew\",\"isOdyssey\":true}"
        );
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "F123-legacy.json"),
            "{\"fid\":\"F123\",\"commander\":\"Old Drew\",\"isOdyssey\":false}"
        );
        await File.WriteAllTextAsync(Path.Combine(temporaryDirectory, "F456-live.json"), "{\"commander\":\"Raven\"}");
        await File.WriteAllTextAsync(Path.Combine(temporaryDirectory, "F789-live.json"), "{malformed");

        var result = await new CommanderProfileCatalog(temporaryDirectory).LoadAsync();

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
            }
        );
        Assert.Single(result.Warnings);
        Assert.Contains("F789-live.json", result.Warnings[0]);
    }

    [Fact]
    public async Task EmptyDirectoryReturnsNoProfiles()
    {
        var result = await new CommanderProfileCatalog(temporaryDirectory).LoadAsync();

        Assert.Empty(result.Profiles);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task DiscoversProfilesAndSourceDirectoriesAcrossJournalRoots()
    {
        var profileDirectory = Path.Combine(temporaryDirectory, "profiles");
        var steam = Path.Combine(temporaryDirectory, "steam");
        var epic = Path.Combine(temporaryDirectory, "epic");
        Directory.CreateDirectory(profileDirectory);
        Directory.CreateDirectory(steam);
        Directory.CreateDirectory(epic);
        await File.WriteAllTextAsync(
            Path.Combine(steam, "Journal.2026-09-01T100000.01.log"),
            "{\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                + "{\"event\":\"Commander\",\"Name\":\"Steam Cmdr\",\"FID\":\"F123\"}\n"
        );
        await File.WriteAllTextAsync(
            Path.Combine(epic, "Journal.2026-09-01T110000.01.log"),
            "{\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                + "{\"event\":\"LoadGame\",\"Commander\":\"Epic Cmdr\",\"FID\":\"F456\"}\n"
        );

        var result = await new CommanderProfileCatalog(profileDirectory, [steam, epic]).LoadAsync();

        Assert.Collection(
            result.Profiles,
            profile =>
            {
                Assert.Equal("F456", profile.FrontierId);
                Assert.Equal("Epic Cmdr", profile.CommanderName);
                Assert.Equal(epic, profile.JournalDirectory);
            },
            profile =>
            {
                Assert.Equal("F123", profile.FrontierId);
                Assert.Equal("Steam Cmdr", profile.CommanderName);
                Assert.Equal(steam, profile.JournalDirectory);
            }
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
