using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class AppDataPathsTests
{
    [Theory]
    [InlineData(".wine/drive_c/users/cmdr")]
    [InlineData(".local/share/Steam/steamapps/compatdata/359320/pfx/drive_c/users/steamuser")]
    [InlineData(".steam/steam/steamapps/compatdata/123456/pfx/drive_c/users/steamuser")]
    [InlineData(".var/app/com.valvesoftware.Steam/data/Steam/steamapps/compatdata/359320/pfx/drive_c/users/steamuser")]
    [InlineData(".local/share/bottles/bottles/SrvSurvey/drive_c/users/cmdr")]
    [InlineData(".var/app/com.usebottles.bottles/data/bottles/bottles/SrvSurvey/drive_c/users/cmdr")]
    [InlineData("Games/srvsurvey/drive_c/users/cmdr")]
    public void LinuxDetectsOriginalProfilesInWineAndProtonPrefixes(string userDirectory)
    {
        string home = Path.Combine(Path.GetTempPath(), $"SrvSurvey-prefix-tests-{Guid.NewGuid():N}");
        string profile = Path.GetFullPath(
            Path.Combine(home, userDirectory, "AppData", "Roaming", "SrvSurvey", "SrvSurvey", "1.1.0.0")
        );
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "F123-live.json"), "{}");
        try
        {
            var paths = AppDataPaths.Resolve(DesktopPlatform.Linux, home, string.Empty, string.Empty);
            Assert.Contains(
                LegacyProfileLocator.Discover(paths.LegacyProfileCandidates),
                candidate => candidate.Path == profile
            );
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    [Theory]
    [InlineData("WINEPREFIX", "drive_c/users/cmdr")]
    [InlineData("STEAM_COMPAT_DATA_PATH", "pfx/drive_c/users/steamuser")]
    [InlineData("external-library", "steamapps/compatdata/12345/pfx/drive_c/users/steamuser")]
    public void LinuxDetectsProfilesInConfiguredExternalPrefixes(string configuration, string userDirectory)
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-external-prefix-tests-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string external = Path.Combine(root, "external");
        string profile = Path.Combine(
            external,
            userDirectory,
            "AppData",
            "Roaming",
            "SrvSurvey",
            "SrvSurvey",
            "1.1.0.0"
        );
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "F123-live.json"), "{}");
        if (configuration == "external-library")
        {
            string steam = Path.Combine(home, ".local", "share", "Steam", "steamapps");
            Directory.CreateDirectory(steam);
            File.WriteAllText(
                Path.Combine(steam, "libraryfolders.vdf"),
                "\"libraryfolders\" { \"1\" { \"path\" \""
                    + external.Replace("\\", "\\\\", StringComparison.Ordinal)
                    + "\" } }"
            );
        }
        try
        {
            var paths = AppDataPaths.Resolve(
                DesktopPlatform.Linux,
                home,
                string.Empty,
                string.Empty,
                key => key == configuration ? external : null
            );
            Assert.Contains(
                LegacyProfileLocator.Discover(paths.LegacyProfileCandidates),
                candidate => candidate.Path == profile
            );
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void WindowsSeparatesNewDataFromLegacyProfile()
    {
        var paths = AppDataPaths.Resolve(
            DesktopPlatform.Windows,
            @"C:\Users\Cmdr",
            @"C:\Users\Cmdr\AppData\Roaming",
            @"C:\Users\Cmdr\AppData\Local"
        );

        Assert.Equal(@"C:\Users\Cmdr\AppData\Roaming\SrvSurvey", paths.ConfigDirectory);
        Assert.Equal(@"C:\Users\Cmdr\AppData\Roaming\SrvSurvey\cross-platform", paths.DataDirectory);
        Assert.Equal(@"C:\Users\Cmdr\AppData\Local\SrvSurvey\cache", paths.CacheDirectory);
        Assert.Equal(3, paths.LegacyProfileCandidates.Count);
        Assert.Equal(
            @"C:\Users\Cmdr\AppData\Roaming\SrvSurvey\SrvSurvey\1.1.0.0",
            paths.LegacyProfileCandidates[0].Path
        );
        Assert.Equal(
            @"C:\Users\Cmdr\AppData\Local\Packages\35333NosmohtSoftware.142860789C73F_p4c193bsm1z5a\LocalCache\Roaming\SrvSurvey\SrvSurvey\1.1.0.0",
            paths.LegacyProfileCandidates[1].Path
        );
        Assert.DoesNotContain(paths.LegacyProfileCandidates, candidate => candidate.Path == paths.DataDirectory);
    }

    /// <summary>Verifies Linux XDG storage roots and the separate location for operational journal upload queues.</summary>
    [Fact]
    public void LinuxUsesXdgDirectories()
    {
        var environment = new Dictionary<string, string?>
        {
            ["XDG_CONFIG_HOME"] = "/mnt/config",
            ["XDG_DATA_HOME"] = "/mnt/data",
            ["XDG_CACHE_HOME"] = "/mnt/cache",
        };

        var paths = AppDataPaths.Resolve(
            DesktopPlatform.Linux,
            "/home/cmdr",
            string.Empty,
            string.Empty,
            name => environment.GetValueOrDefault(name)
        );

        Assert.Equal(Path.GetFullPath("/mnt/config/SrvSurvey"), paths.ConfigDirectory);
        Assert.Equal(Path.GetFullPath("/mnt/data/SrvSurvey"), paths.DataDirectory);
        Assert.Equal(Path.GetFullPath("/mnt/cache/SrvSurvey"), paths.CacheDirectory);
        Assert.Empty(paths.LegacyProfileCandidates);
        Assert.Equal(Path.GetFullPath("/mnt/data/SrvSurvey/journal-uploads"), paths.JournalUploadDirectory);
        AppDataPaths firstCommander = paths with { SettingsFrontierId = "F123" };
        AppDataPaths secondCommander = paths with { SettingsFrontierId = "F456" };
        Assert.Equal(
            Path.GetFullPath("/mnt/data/SrvSurvey/journal-uploads/profiles/F123"),
            firstCommander.JournalUploadDirectory
        );
        Assert.Equal(
            Path.GetFullPath("/mnt/data/SrvSurvey/journal-uploads/profiles/F456"),
            secondCommander.JournalUploadDirectory
        );
    }

    [Fact]
    public void LinuxFallsBackToFreedesktopDefaults()
    {
        var paths = AppDataPaths.Resolve(DesktopPlatform.Linux, "/home/cmdr", string.Empty, string.Empty);

        Assert.Equal(Path.GetFullPath("/home/cmdr/.config/SrvSurvey"), paths.ConfigDirectory);
        Assert.Equal(Path.GetFullPath("/home/cmdr/.local/share/SrvSurvey"), paths.DataDirectory);
        Assert.Equal(Path.GetFullPath("/home/cmdr/.cache/SrvSurvey"), paths.CacheDirectory);
    }
}
