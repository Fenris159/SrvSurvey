using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Storage;

public sealed record AppDataPaths(
    string ConfigDirectory,
    string DataDirectory,
    string CacheDirectory,
    IReadOnlyList<LegacyProfileCandidate> LegacyProfileCandidates)
{
    private const string ApplicationDirectoryName = "SrvSurvey";
    private const string LegacyVersionDirectoryName = "1.1.0.0";
    private const string StorePackageDirectoryName =
        "35333NosmohtSoftware.142860789C73F_p4c193bsm1z5a";

    public string UiSettingsPath => Path.Combine(ConfigDirectory, "cross-platform-ui.json");

    public static AppDataPaths ResolveCurrent()
    {
        var platform = OperatingSystem.IsWindows()
            ? DesktopPlatform.Windows
            : OperatingSystem.IsLinux()
                ? DesktopPlatform.Linux
                : DesktopPlatform.Other;

        return Resolve(
            platform,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable);
    }

    public static AppDataPaths Resolve(
        DesktopPlatform platform,
        string homeDirectory,
        string roamingApplicationDataDirectory,
        string localApplicationDataDirectory,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeDirectory);
        getEnvironmentVariable ??= _ => null;

        var home = Path.GetFullPath(homeDirectory);
        var roaming = ResolveOptionalRoot(
            roamingApplicationDataDirectory,
            Path.Combine(home, ".config"));
        var local = ResolveOptionalRoot(
            localApplicationDataDirectory,
            Path.Combine(home, ".local", "share"));

        string configDirectory;
        string dataDirectory;
        string cacheDirectory;

        if (platform == DesktopPlatform.Linux)
        {
            configDirectory = Path.Combine(
                ResolveXdgRoot(getEnvironmentVariable("XDG_CONFIG_HOME"), home, ".config"),
                ApplicationDirectoryName);
            dataDirectory = Path.Combine(
                ResolveXdgRoot(getEnvironmentVariable("XDG_DATA_HOME"), home, ".local", "share"),
                ApplicationDirectoryName);
            cacheDirectory = Path.Combine(
                ResolveXdgRoot(getEnvironmentVariable("XDG_CACHE_HOME"), home, ".cache"),
                ApplicationDirectoryName);
        }
        else
        {
            configDirectory = Path.Combine(roaming, ApplicationDirectoryName);
            dataDirectory = Path.Combine(roaming, ApplicationDirectoryName, "cross-platform");
            cacheDirectory = Path.Combine(local, ApplicationDirectoryName, "cache");
        }

        var candidates = platform == DesktopPlatform.Windows
            ? BuildWindowsLegacyCandidates(roaming, local)
            : Array.Empty<LegacyProfileCandidate>();

        return new AppDataPaths(
            Path.GetFullPath(configDirectory),
            Path.GetFullPath(dataDirectory),
            Path.GetFullPath(cacheDirectory),
            candidates);
    }

    private static IReadOnlyList<LegacyProfileCandidate> BuildWindowsLegacyCandidates(
        string roaming,
        string local)
    {
        var normal = Path.Combine(
            roaming,
            ApplicationDirectoryName,
            ApplicationDirectoryName,
            LegacyVersionDirectoryName);
        var redirectedRoot = Path.Combine(
            local,
            "Packages",
            StorePackageDirectoryName,
            "LocalCache",
            "Roaming",
            ApplicationDirectoryName);

        return
        [
            new LegacyProfileCandidate(
                LegacyProfileLocationKind.Desktop,
                Path.GetFullPath(normal)),
            new LegacyProfileCandidate(
                LegacyProfileLocationKind.MicrosoftStore,
                Path.GetFullPath(Path.Combine(
                    redirectedRoot,
                    ApplicationDirectoryName,
                    LegacyVersionDirectoryName))),
            new LegacyProfileCandidate(
                LegacyProfileLocationKind.MicrosoftStoreBackup,
                Path.GetFullPath(Path.Combine(
                    redirectedRoot,
                    $"{ApplicationDirectoryName}-",
                    LegacyVersionDirectoryName))),
        ];
    }

    private static string ResolveOptionalRoot(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string ResolveXdgRoot(
        string? configuredValue,
        string home,
        params string[] fallbackSegments)
    {
        return string.IsNullOrWhiteSpace(configuredValue)
            ? Path.Combine([home, .. fallbackSegments])
            : Path.GetFullPath(configuredValue);
    }
}
