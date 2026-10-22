using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class LegacyProfileLocatorTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-profile-locator-tests-{Guid.NewGuid():N}"
    );

    [Theory]
    [InlineData("", "drive_c")]
    [InlineData("pfx", "pfx/drive_c")]
    [InlineData("", "")]
    public void ManualSelectionFindsOriginalProfilesInsideWineOrProtonPrefixes(
        string prefixSegment,
        string selectedSegment
    )
    {
        string prefix = Path.Combine(temporaryDirectory, "prefix");
        string drive = Path.Combine(prefix, prefixSegment, "drive_c");
        string profile = Path.Combine(
            drive,
            "users",
            "steamuser",
            "AppData",
            "Roaming",
            "SrvSurvey",
            "SrvSurvey",
            "1.1.0.0"
        );
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "F123-live.json"), "{}");

        ProfileImportSource result = LegacyProfileLocator.ResolveManualSelectionSource(
            Path.Combine(prefix, selectedSegment)
        );

        Assert.Equal(profile, result.DataDirectory);
        Assert.Equal(ProfileImportSourceKind.Legacy, result.Kind);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void XpLayoutsWithoutPlayerFilesRemainCrossPlatformSources(bool selectParent, bool nested)
    {
        string root = Path.Combine(temporaryDirectory, "xp-layout-only");
        string data = Path.Combine(root, "cross-platform");
        string layout = nested ? Path.Combine(data, "commanders", "F123", "overlays") : data;
        Directory.CreateDirectory(layout);
        File.WriteAllText(Path.Combine(layout, "plotters.json"), "{}");
        string ui = Path.Combine(root, "cross-platform-ui.json");
        File.WriteAllText(ui, "{}");
        ProfileImportSource source = LegacyProfileLocator.ResolveManualSelectionSource(selectParent ? root : data);
        Assert.Equal(ProfileImportSourceKind.CrossPlatform, source.Kind);
        Assert.Equal(data, source.DataDirectory);
        Assert.Equal(ui, source.UiSettingsPath);
    }

    [Fact]
    public void DiscoverReturnsOnlyExistingProfilesWithoutChangingThem()
    {
        string desktopPath = Path.Combine(temporaryDirectory, "desktop");
        Directory.CreateDirectory(Path.Combine(desktopPath, "systems"));
        File.WriteAllText(Path.Combine(desktopPath, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(desktopPath, "systems", "one.json"), "{}");

        IReadOnlyList<LegacyProfileDiscovery> result = LegacyProfileLocator.Discover([
            new LegacyProfileCandidate(LegacyProfileLocationKind.Desktop, desktopPath),
            new LegacyProfileCandidate(
                LegacyProfileLocationKind.MicrosoftStore,
                Path.Combine(temporaryDirectory, "missing")
            ),
        ]);

        LegacyProfileDiscovery profile = Assert.Single(result);
        Assert.Equal(LegacyProfileLocationKind.Desktop, profile.Kind);
        Assert.Equal(Path.GetFullPath(desktopPath), profile.Path);
        Assert.Equal(2, profile.FileCount);
        Assert.True(File.Exists(Path.Combine(desktopPath, "settings.json")));
    }

    [Fact]
    public void DiscoverFindsOlderSiblingVersionProfiles()
    {
        string productRoot = Path.Combine(temporaryDirectory, "SrvSurvey");
        string olderProfile = Path.Combine(productRoot, "1.0.0.0");
        string newestProfile = Path.Combine(productRoot, "1.2.0.0");
        Directory.CreateDirectory(olderProfile);
        Directory.CreateDirectory(newestProfile);
        File.WriteAllText(Path.Combine(olderProfile, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(newestProfile, "settings.json"), "{}");

        IReadOnlyList<LegacyProfileDiscovery> result = LegacyProfileLocator.Discover([
            new LegacyProfileCandidate(LegacyProfileLocationKind.Desktop, Path.Combine(productRoot, "1.1.0.0")),
        ]);

        Assert.Equal(2, result.Count);
        Assert.Equal(Path.GetFullPath(newestProfile), result[0].Path);
        Assert.Equal(Path.GetFullPath(olderProfile), result[1].Path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiscoverIgnoresEmptyAndLogOnlyVersionDirectories(bool hasLogs)
    {
        string productRoot = Path.Combine(temporaryDirectory, "SrvSurvey");
        string emptyProfile = Path.Combine(productRoot, "1.1.0.0");
        string populatedProfile = Path.Combine(productRoot, "1.0.0.0");
        Directory.CreateDirectory(Path.Combine(emptyProfile, "systems"));
        if (hasLogs)
        {
            File.WriteAllText(Path.Combine(emptyProfile, "startup.log"), "startup");
        }
        Directory.CreateDirectory(populatedProfile);
        File.WriteAllText(Path.Combine(populatedProfile, "settings.json"), "{}");

        IReadOnlyList<LegacyProfileDiscovery> result = LegacyProfileLocator.Discover([
            new LegacyProfileCandidate(LegacyProfileLocationKind.Desktop, emptyProfile),
        ]);

        Assert.Equal(Path.GetFullPath(populatedProfile), Assert.Single(result).Path);
    }

    [Fact]
    public void ManualSelectionResolvesTheLegacyProfileInsideAWindowsApplicationDataRoot()
    {
        string selectedRoot = Path.Combine(temporaryDirectory, "SrvSurvey");
        string legacyProfile = Path.Combine(selectedRoot, "SrvSurvey", "1.1.0.0");
        Directory.CreateDirectory(legacyProfile);
        File.WriteAllText(Path.Combine(legacyProfile, "settings.json"), "{}");
        Directory.CreateDirectory(Path.Combine(selectedRoot, "cross-platform"));
        Directory.CreateDirectory(Path.Combine(selectedRoot, "legacy-backups"));

        string result = LegacyProfileLocator.ResolveManualSelection(selectedRoot);

        Assert.Equal(Path.GetFullPath(legacyProfile), result);
    }

    [Fact]
    public void ManualSelectionPrefersAPopulatedCrossPlatformProfileAndFindsItsUiSettings()
    {
        string selectedRoot = Path.Combine(temporaryDirectory, "SrvSurvey");
        string currentProfile = Path.Combine(selectedRoot, "cross-platform");
        string legacyProfile = Path.Combine(selectedRoot, "SrvSurvey", "1.1.0.0");
        Directory.CreateDirectory(currentProfile);
        Directory.CreateDirectory(legacyProfile);
        File.WriteAllText(Path.Combine(currentProfile, "F123-live.json"), "{}");
        File.WriteAllText(Path.Combine(legacyProfile, "settings.json"), "{}");
        string uiSettings = Path.Combine(selectedRoot, "cross-platform-ui.json");
        File.WriteAllText(uiSettings, "{}");

        ProfileImportSource result = LegacyProfileLocator.ResolveManualSelectionSource(selectedRoot);

        Assert.Equal(Path.GetFullPath(currentProfile), result.DataDirectory);
        Assert.Equal(ProfileImportSourceKind.CrossPlatform, result.Kind);
        Assert.Equal(Path.GetFullPath(uiSettings), result.UiSettingsPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualSelectionRecognizesTheCrossPlatformDirectoryItself(bool trailingSeparator)
    {
        string selectedRoot = Path.Combine(temporaryDirectory, "SrvSurvey");
        string currentProfile = Path.Combine(selectedRoot, "cross-platform");
        Directory.CreateDirectory(currentProfile);
        File.WriteAllText(Path.Combine(currentProfile, "F123-live.json"), "{}");

        ProfileImportSource result = LegacyProfileLocator.ResolveManualSelectionSource(
            currentProfile + (trailingSeparator ? Path.DirectorySeparatorChar.ToString() : string.Empty)
        );

        Assert.Equal(Path.GetFullPath(currentProfile), result.DataDirectory);
        Assert.Equal(ProfileImportSourceKind.CrossPlatform, result.Kind);
        Assert.Null(result.UiSettingsPath);
    }

    [Fact]
    public void ManualSelectionDoesNotMistakeTheActiveXpProfileForTheOriginalProfile()
    {
        string selectedRoot = Path.Combine(temporaryDirectory, "SrvSurvey");
        string currentProfile = Path.Combine(selectedRoot, "cross-platform");
        string legacyProfile = Path.Combine(selectedRoot, "SrvSurvey", "1.1.0.0");
        Directory.CreateDirectory(currentProfile);
        Directory.CreateDirectory(legacyProfile);
        File.WriteAllText(Path.Combine(currentProfile, "startup.log"), "XP is running");
        File.WriteAllText(Path.Combine(legacyProfile, "F123-live.json"), "{}");

        ProfileImportSource result = LegacyProfileLocator.ResolveManualSelectionSource(selectedRoot);

        Assert.Equal(Path.GetFullPath(legacyProfile), result.DataDirectory);
        Assert.Equal(ProfileImportSourceKind.Legacy, result.Kind);
    }

    [Fact]
    public void ManualSelectionFindsTheMicrosoftStoreMigrationBackup()
    {
        string selectedRoot = Path.Combine(temporaryDirectory, "LocalCache", "Roaming", "SrvSurvey");
        string legacyProfile = Path.Combine(selectedRoot, "SrvSurvey-", "1.1.0.0");
        Directory.CreateDirectory(legacyProfile);
        File.WriteAllText(Path.Combine(legacyProfile, "F123-codex.json"), "{}");

        ProfileImportSource result = LegacyProfileLocator.ResolveManualSelectionSource(selectedRoot);

        Assert.Equal(Path.GetFullPath(legacyProfile), result.DataDirectory);
        Assert.Equal(ProfileImportSourceKind.Legacy, result.Kind);
    }

    [Fact]
    public void ManualSelectionPrefersPlayerDataOverANewerDirectoryContainingOnlyLogs()
    {
        string selectedRoot = Path.Combine(temporaryDirectory, "SrvSurvey");
        string legacyProfile = Path.Combine(selectedRoot, "1.1.0.0");
        string logDirectory = Path.Combine(selectedRoot, "2.0.0.0", "logs");
        Directory.CreateDirectory(legacyProfile);
        Directory.CreateDirectory(logDirectory);
        File.WriteAllText(Path.Combine(legacyProfile, "F123-live.json"), "{}");
        File.WriteAllText(Path.Combine(logDirectory, "startup.log"), "startup");

        ProfileImportSource result = LegacyProfileLocator.ResolveManualSelectionSource(selectedRoot);

        Assert.Equal(Path.GetFullPath(legacyProfile), result.DataDirectory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualSelectionSkipsTheActiveDestinationEvenWhenItContainsPlayerData(bool trailingSeparator)
    {
        string selectedRoot = Path.Combine(temporaryDirectory, "SrvSurvey");
        string currentProfile = Path.Combine(selectedRoot, "cross-platform");
        string legacyProfile = Path.Combine(selectedRoot, "SrvSurvey", "1.1.0.0");
        Directory.CreateDirectory(currentProfile);
        Directory.CreateDirectory(legacyProfile);
        File.WriteAllText(Path.Combine(currentProfile, "F123-live.json"), "{}");
        File.WriteAllText(Path.Combine(legacyProfile, "F123-live.json"), "{}");

        ProfileImportSource result = LegacyProfileLocator.ResolveManualSelectionSource(
            selectedRoot,
            currentProfile + (trailingSeparator ? Path.DirectorySeparatorChar.ToString() : string.Empty)
        );

        Assert.Equal(Path.GetFullPath(legacyProfile), result.DataDirectory);
        Assert.Equal(ProfileImportSourceKind.Legacy, result.Kind);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
