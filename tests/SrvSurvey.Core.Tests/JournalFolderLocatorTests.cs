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
            path => path is configured or environment);

        Assert.Equal(configured, result.SelectedPath);
        Assert.Equal(configured, result.CandidatePaths[0]);
        Assert.Equal(environment, result.CandidatePaths[1]);
    }

    [Fact]
    public void ResolveDeduplicatesWindowsPathsCaseInsensitively()
    {
        var result = JournalFolderLocator.Resolve(
            @"D:\Elite\Journals",
            @"d:\elite\journals",
            @"C:\Users\Cmdr",
            DesktopPlatform.Windows,
            _ => false);

        Assert.Equal(2, result.CandidatePaths.Count);
    }

    [Fact]
    public void LinuxDefaultsIncludeCommonSteamInstallations()
    {
        var paths = JournalFolderLocator.GetPlatformDefaults(
            "/home/cmdr",
            DesktopPlatform.Linux);

        Assert.Equal(4, paths.Count);
        Assert.Contains(
            "/home/cmdr/.local/share/Steam/steamapps/compatdata/359320/pfx/drive_c/users/steamuser/Saved Games/Frontier Developments/Elite Dangerous",
            paths);
        Assert.Contains(
            "/home/cmdr/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/compatdata/359320/pfx/drive_c/users/steamuser/Saved Games/Frontier Developments/Elite Dangerous",
            paths);
        Assert.Contains(
            "/home/cmdr/.var/app/com.valvesoftware.Steam/data/Steam/steamapps/compatdata/359320/pfx/drive_c/users/steamuser/Saved Games/Frontier Developments/Elite Dangerous",
            paths);
    }
}
