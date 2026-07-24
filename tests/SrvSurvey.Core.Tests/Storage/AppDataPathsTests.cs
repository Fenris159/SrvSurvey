using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class AppDataPathsTests
{
    [Fact]
    public void WindowsSeparatesNewDataFromLegacyProfile()
    {
        var paths = AppDataPaths.Resolve(
            DesktopPlatform.Windows,
            @"C:\Users\Cmdr",
            @"C:\Users\Cmdr\AppData\Roaming",
            @"C:\Users\Cmdr\AppData\Local");

        Assert.Equal(
            Path.GetFullPath(@"C:\Users\Cmdr\AppData\Roaming\SrvSurvey"),
            paths.ConfigDirectory);
        Assert.Equal(
            Path.GetFullPath(@"C:\Users\Cmdr\AppData\Roaming\SrvSurvey\cross-platform"),
            paths.DataDirectory);
        Assert.Equal(
            Path.GetFullPath(@"C:\Users\Cmdr\AppData\Local\SrvSurvey\cache"),
            paths.CacheDirectory);
        Assert.Equal(3, paths.LegacyProfileCandidates.Count);
        Assert.Equal(
            Path.GetFullPath(
                @"C:\Users\Cmdr\AppData\Roaming\SrvSurvey\SrvSurvey\1.1.0.0"),
            paths.LegacyProfileCandidates[0].Path);
        Assert.DoesNotContain(
            paths.LegacyProfileCandidates,
            candidate => candidate.Path == paths.DataDirectory);
    }

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
            name => environment.GetValueOrDefault(name));

        Assert.Equal(Path.GetFullPath("/mnt/config/SrvSurvey"), paths.ConfigDirectory);
        Assert.Equal(Path.GetFullPath("/mnt/data/SrvSurvey"), paths.DataDirectory);
        Assert.Equal(Path.GetFullPath("/mnt/cache/SrvSurvey"), paths.CacheDirectory);
        Assert.Empty(paths.LegacyProfileCandidates);
    }

    [Fact]
    public void LinuxFallsBackToFreedesktopDefaults()
    {
        var paths = AppDataPaths.Resolve(
            DesktopPlatform.Linux,
            "/home/cmdr",
            string.Empty,
            string.Empty);

        Assert.Equal(
            Path.GetFullPath("/home/cmdr/.config/SrvSurvey"),
            paths.ConfigDirectory);
        Assert.Equal(
            Path.GetFullPath("/home/cmdr/.local/share/SrvSurvey"),
            paths.DataDirectory);
        Assert.Equal(
            Path.GetFullPath("/home/cmdr/.cache/SrvSurvey"),
            paths.CacheDirectory);
    }
}
