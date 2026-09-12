using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests;

public sealed class JournalFolderLocatorTests
{
    [Fact]
    public void ResolvePrefersConfiguredPath()
    {
        const string configured = @"D:\Elite\Journals";
        const string environment = @"E:\Other\Journals";

        var result = JournalFolderLocator.Resolve(
            configured,
            environment,
            @"C:\Users\Cmdr",
            DesktopPlatform.Windows,
            path => path is configured or environment
        );

        Assert.Equal(configured, result.SelectedPath);
        Assert.Equal(configured, result.CandidatePaths[0]);
        Assert.Equal(environment, result.CandidatePaths[1]);
        Assert.Equal([configured], result.AvailablePaths);
    }

    [Fact]
    public void ResolveDeduplicatesWindowsPathsCaseInsensitively()
    {
        var result = JournalFolderLocator.Resolve(
            @"D:\Elite\Journals",
            @"d:\elite\journals",
            @"C:\Users\Cmdr",
            DesktopPlatform.Windows,
            _ => false
        );

        Assert.Equal(2, result.CandidatePaths.Count);
    }

    [Fact]
    public void LinuxDefaultsIncludeCommonSteamInstallations()
    {
        var paths = JournalFolderLocator.GetPlatformDefaults("/home/cmdr", DesktopPlatform.Linux);

        Assert.Equal(4, paths.Count);
        Assert.Contains(
            "/home/cmdr/.local/share/Steam/steamapps/compatdata/359320/pfx/drive_c/users/steamuser/Saved Games/Frontier Developments/Elite Dangerous",
            paths
        );
        Assert.Contains(
            "/home/cmdr/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/compatdata/359320/pfx/drive_c/users/steamuser/Saved Games/Frontier Developments/Elite Dangerous",
            paths
        );
        Assert.Contains(
            "/home/cmdr/.var/app/com.valvesoftware.Steam/data/Steam/steamapps/compatdata/359320/pfx/drive_c/users/steamuser/Saved Games/Frontier Developments/Elite Dangerous",
            paths
        );
    }

    [Fact]
    public void LinuxCandidatesDiscoverMixedLauncherPrefixes()
    {
        var home = Path.Combine(Path.GetTempPath(), $"SrvSurvey-linux-journal-candidates-{Guid.NewGuid():N}");
        try
        {
            var steam = CreateJournalDirectory(
                home,
                ".local",
                "share",
                "Steam",
                "steamapps",
                "compatdata",
                "359320",
                "pfx",
                "drive_c",
                "users",
                "steamuser"
            );
            var frontier = CreateJournalDirectory(home, ".wine", "drive_c", "users", "cmdr");
            var heroic = CreateJournalDirectory(
                home,
                "Games",
                "Heroic",
                "Prefixes",
                "default",
                "Elite Dangerous",
                "pfx",
                "drive_c",
                "users",
                "steamuser"
            );
            var lutris = CreateJournalDirectory(home, "Games", "elite-dangerous", "drive_c", "users", "cmdr");
            var bottles = CreateJournalDirectory(
                home,
                ".local",
                "share",
                "bottles",
                "bottles",
                "Elite Dangerous",
                "drive_c",
                "users",
                "cmdr"
            );

            var candidates = JournalFolderLocator.GetPlatformCandidates(home, DesktopPlatform.Linux);

            Assert.Contains(steam, candidates);
            Assert.Contains(frontier, candidates);
            Assert.Contains(heroic, candidates);
            Assert.Contains(lutris, candidates);
            Assert.Contains(bottles, candidates);
        }
        finally
        {
            if (Directory.Exists(home))
            {
                Directory.Delete(home, true);
            }
        }
    }

    [Fact]
    public async Task LinuxCandidatesReadCustomLauncherAndSteamLibraryPrefixes()
    {
        var home = Path.Combine(Path.GetTempPath(), $"SrvSurvey-linux-launcher-config-{Guid.NewGuid():N}");
        try
        {
            var heroicPrefix = Path.Combine(home, "custom", "heroic-prefix");
            var heroic = CreateJournalDirectory(heroicPrefix, "pfx", "drive_c", "users", "heroic");
            var heroicConfig = Path.Combine(home, ".config", "heroic", "GamesConfig");
            Directory.CreateDirectory(heroicConfig);
            await File.WriteAllTextAsync(
                Path.Combine(heroicConfig, "elite.json"),
                $$"""{"winePrefix":"{{heroicPrefix.Replace("\\", "\\\\", StringComparison.Ordinal)}}"}"""
            );

            var lutrisPrefix = Path.Combine(home, "custom", "lutris-prefix");
            var lutris = CreateJournalDirectory(lutrisPrefix, "drive_c", "users", "lutris");
            var lutrisConfig = Path.Combine(home, ".config", "lutris", "games");
            Directory.CreateDirectory(lutrisConfig);
            await File.WriteAllTextAsync(
                Path.Combine(lutrisConfig, "elite.yml"),
                $"game:\n  prefix: '{lutrisPrefix}'\n"
            );

            var steamLibrary = Path.Combine(home, "custom", "steam-library");
            var steam = CreateJournalDirectory(
                steamLibrary,
                "steamapps",
                "compatdata",
                "359320",
                "pfx",
                "drive_c",
                "users",
                "steamuser"
            );
            var steamConfig = Path.Combine(home, ".local", "share", "Steam", "steamapps");
            Directory.CreateDirectory(steamConfig);
            var escapedSteamLibrary = steamLibrary.Replace("\\", "\\\\", StringComparison.Ordinal);
            await File.WriteAllTextAsync(
                Path.Combine(steamConfig, "libraryfolders.vdf"),
                $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{escapedSteamLibrary}\" }} }}"
            );

            var candidates = JournalFolderLocator.GetPlatformCandidates(home, DesktopPlatform.Linux);

            Assert.Contains(heroic, candidates);
            Assert.Contains(lutris, candidates);
            Assert.Contains(steam, candidates);
        }
        finally
        {
            if (Directory.Exists(home))
            {
                Directory.Delete(home, true);
            }
        }
    }

    [Fact]
    public void ResolveExposesEveryExistingJournalRoot()
    {
        const string steam =
            "/home/cmdr/.local/share/Steam/steamapps/compatdata/359320/pfx/drive_c/users/steamuser/Saved Games/Frontier Developments/Elite Dangerous";
        const string heroic =
            "/home/cmdr/Games/Heroic/Prefixes/default/Elite Dangerous/pfx/drive_c/users/steamuser/Saved Games/Frontier Developments/Elite Dangerous";
        var existing = new HashSet<string>([steam, heroic], StringComparer.Ordinal);

        var result = JournalFolderLocator.Resolve(
            configuredPath: null,
            environmentPath: null,
            userProfile: "/home/cmdr",
            platform: DesktopPlatform.Linux,
            directoryExists: existing.Contains,
            platformCandidates: [steam, heroic]
        );

        Assert.Equal([steam, heroic], result.AvailablePaths);
    }

    private static string CreateJournalDirectory(string home, params string[] prefixSegments)
    {
        var path = Path.Combine([home, .. prefixSegments, "Saved Games", "Frontier Developments", "Elite Dangerous"]);
        Directory.CreateDirectory(path);
        return Path.GetFullPath(path);
    }
}
