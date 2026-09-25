using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests;

public sealed class JournalFolderLocatorTests
{
    [Fact]
    public void ResolvePrefersConfiguredPath()
    {
        const string configured = @"D:\Elite\Journals";
        const string environment = @"E:\Other\Journals";

        JournalFolderResolution result = JournalFolderLocator.Resolve(
            configured,
            environment,
            @"C:\Users\Cmdr",
            DesktopPlatform.Windows,
            path => path is configured or environment
        );

        Assert.Equal(configured, result.SelectedPath);
        Assert.Equal(configured, result.CandidatePaths[0]);
        Assert.Equal(environment, result.CandidatePaths[1]);
        Assert.Equal([configured, environment], result.AvailablePaths);
    }

    [Fact]
    public void ResolveDeduplicatesWindowsPathsCaseInsensitively()
    {
        JournalFolderResolution result = JournalFolderLocator.Resolve(
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
        IReadOnlyList<string> paths = JournalFolderLocator.GetPlatformDefaults("/home/cmdr", DesktopPlatform.Linux);

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
        string home = Path.Combine(Path.GetTempPath(), $"SrvSurvey-linux-journal-candidates-{Guid.NewGuid():N}");
        try
        {
            string steam = CreateJournalDirectory(
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
            string frontier = CreateJournalDirectory(home, ".wine", "drive_c", "users", "cmdr");
            string heroic = CreateJournalDirectory(
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
            string lutris = CreateJournalDirectory(home, "Games", "elite-dangerous", "drive_c", "users", "cmdr");
            string bottles = CreateJournalDirectory(
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

            IReadOnlyList<string> candidates = JournalFolderLocator.GetPlatformCandidates(home, DesktopPlatform.Linux);

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
    public void LinuxCandidatesGiveSiblingGamePrefixesIndependentDirectoryBudgets()
    {
        string home = Path.Combine(Path.GetTempPath(), $"SrvSurvey-linux-prefix-budget-{Guid.NewGuid():N}");
        try
        {
            string heroic = CreateJournalDirectory(
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
            string unrelated = Path.Combine(home, "Games", "Unrelated");
            for (int group = 0; group < 65; group++)
            {
                for (int leaf = 0; leaf < 65; leaf++)
                {
                    Directory.CreateDirectory(Path.Combine(unrelated, $"group-{group:D2}", $"leaf-{leaf:D2}"));
                }
            }

            IReadOnlyList<string> candidates = JournalFolderLocator.GetPlatformCandidates(home, DesktopPlatform.Linux);

            Assert.Contains(heroic, candidates);
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
        string home = Path.Combine(Path.GetTempPath(), $"SrvSurvey-linux-launcher-config-{Guid.NewGuid():N}");
        try
        {
            string heroicPrefix = Path.Combine(home, "custom", "heroic-prefix");
            string heroic = CreateJournalDirectory(heroicPrefix, "pfx", "drive_c", "users", "heroic");
            string heroicConfig = Path.Combine(home, ".config", "heroic", "GamesConfig");
            Directory.CreateDirectory(heroicConfig);
            await File.WriteAllTextAsync(
                Path.Combine(heroicConfig, "elite.json"),
                $$"""{"winePrefix":"{{heroicPrefix.Replace("\\", "\\\\", StringComparison.Ordinal)}}"}"""
            );

            string lutrisPrefix = Path.Combine(home, "custom", "lutris-prefix");
            string lutris = CreateJournalDirectory(lutrisPrefix, "drive_c", "users", "lutris");
            string lutrisConfig = Path.Combine(home, ".config", "lutris", "games");
            Directory.CreateDirectory(lutrisConfig);
            await File.WriteAllTextAsync(
                Path.Combine(lutrisConfig, "elite.yml"),
                $"game:\n  prefix: '{lutrisPrefix}'\n"
            );

            string steamLibrary = Path.Combine(home, "custom", "steam-library");
            string steam = CreateJournalDirectory(
                steamLibrary,
                "steamapps",
                "compatdata",
                "359320",
                "pfx",
                "drive_c",
                "users",
                "steamuser"
            );
            string steamConfig = Path.Combine(home, ".local", "share", "Steam", "steamapps");
            Directory.CreateDirectory(steamConfig);
            string escapedSteamLibrary = steamLibrary.Replace("\\", "\\\\", StringComparison.Ordinal);
            await File.WriteAllTextAsync(
                Path.Combine(steamConfig, "libraryfolders.vdf"),
                $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{escapedSteamLibrary}\" }} }}"
            );

            IReadOnlyList<string> candidates = JournalFolderLocator.GetPlatformCandidates(home, DesktopPlatform.Linux);

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

        JournalFolderResolution result = JournalFolderLocator.Resolve(
            configuredPath: null,
            environmentPath: null,
            userProfile: "/home/cmdr",
            platform: DesktopPlatform.Linux,
            directoryExists: existing.Contains,
            platformCandidates: [steam, heroic]
        );

        Assert.Equal([steam, heroic], result.AvailablePaths);
    }

    [Fact]
    public void ConfiguredSteamFolderDoesNotHideDiscoveredEpicFolder()
    {
        const string steam =
            "/home/cmdr/personal/SteamLibrary/steamapps/compatdata/359320/pfx/drive_c/users/steamuser/Saved Games/Frontier Developments/Elite Dangerous";
        const string epic =
            "/home/cmdr/Games/Heroic/Prefixes/Elite Dangerous/drive_c/users/steamuser/Saved Games/Frontier Developments/Elite Dangerous";
        var existing = new HashSet<string>([steam, epic], StringComparer.Ordinal);

        JournalFolderResolution result = JournalFolderLocator.Resolve(
            configuredPath: steam,
            environmentPath: null,
            userProfile: "/home/cmdr",
            platform: DesktopPlatform.Linux,
            directoryExists: existing.Contains,
            platformCandidates: [steam, epic]
        );

        Assert.Equal([steam, epic], result.AvailablePaths);
    }

    [Fact]
    public void CommandLineFolderIsExclusiveToItsInstance()
    {
        const string steam = "/home/cmdr/steam";
        const string epic = "/home/cmdr/epic";

        JournalFolderResolution result = JournalFolderLocator.ResolveWithSettings(
            [steam],
            null,
            "/home/cmdr",
            DesktopPlatform.Linux,
            path => path is steam or epic,
            [epic],
            exclusiveConfiguredPath: true
        );

        Assert.Equal([steam], result.AvailablePaths);
    }

    [Fact]
    public async Task LinuxCandidatesReadLutrisShareGameConfiguration()
    {
        string home = Path.Combine(Path.GetTempPath(), $"SrvSurvey-lutris-share-{Guid.NewGuid():N}");
        try
        {
            string prefix = Path.Combine(home, "custom", "epic-prefix");
            string epic = CreateJournalDirectory(prefix, "drive_c", "users", "cmdr");
            string config = Path.Combine(home, ".local", "share", "lutris", "games");
            Directory.CreateDirectory(config);
            await File.WriteAllTextAsync(Path.Combine(config, "elite.yml"), $"game:\n  prefix: '{prefix}'\n");

            Assert.Contains(epic, JournalFolderLocator.GetPlatformCandidates(home, DesktopPlatform.Linux));
        }
        finally
        {
            if (Directory.Exists(home))
            {
                Directory.Delete(home, true);
            }
        }
    }

    private static string CreateJournalDirectory(string home, params string[] prefixSegments)
    {
        string path = Path.Combine([
            home,
            .. prefixSegments,
            "Saved Games",
            "Frontier Developments",
            "Elite Dangerous",
        ]);
        Directory.CreateDirectory(path);
        return Path.GetFullPath(path);
    }
}
