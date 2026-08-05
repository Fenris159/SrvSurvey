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
            : (OperatingSystem.IsLinux()) switch
            {
                true => DesktopPlatform.Linux,
                false => DesktopPlatform.Other
            };

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

        var home = NormalizeRoot(platform, homeDirectory);
        var roaming = ResolveOptionalRoot(
            roamingApplicationDataDirectory,
            Combine(platform, home, ".config"));
        var local = ResolveOptionalRoot(
            localApplicationDataDirectory,
            Combine(platform, home, ".local", "share"));

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
            configDirectory = Combine(platform, roaming, ApplicationDirectoryName);
            dataDirectory = Combine(
                platform,
                roaming,
                ApplicationDirectoryName,
                "cross-platform");
            cacheDirectory = Combine(
                platform,
                local,
                ApplicationDirectoryName,
                "cache");
        }

        var candidates = platform == DesktopPlatform.Windows
            ? BuildWindowsLegacyCandidates(roaming, local)
            : Array.Empty<LegacyProfileCandidate>();

        return new AppDataPaths(
            NormalizeRoot(platform, configDirectory),
            NormalizeRoot(platform, dataDirectory),
            NormalizeRoot(platform, cacheDirectory),
            candidates);
    }

    private static IReadOnlyList<LegacyProfileCandidate> BuildWindowsLegacyCandidates(
        string roaming,
        string local)
    {
        var normal = Combine(
            DesktopPlatform.Windows,
            roaming,
            ApplicationDirectoryName,
            ApplicationDirectoryName,
            LegacyVersionDirectoryName);
        var redirectedRoot = Combine(
            DesktopPlatform.Windows,
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
                NormalizeRoot(DesktopPlatform.Windows, normal)),
            new LegacyProfileCandidate(
                LegacyProfileLocationKind.MicrosoftStore,
                NormalizeRoot(DesktopPlatform.Windows, Combine(
                    DesktopPlatform.Windows,
                    redirectedRoot,
                    ApplicationDirectoryName,
                    LegacyVersionDirectoryName))),
            new LegacyProfileCandidate(
                LegacyProfileLocationKind.MicrosoftStoreBackup,
                NormalizeRoot(DesktopPlatform.Windows, Combine(
                    DesktopPlatform.Windows,
                    redirectedRoot,
                    $"{ApplicationDirectoryName}-",
                    LegacyVersionDirectoryName))),
        ];
    }

    private static string ResolveOptionalRoot(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string Combine(
        DesktopPlatform platform,
        params string[] segments)
    {
        if (platform != DesktopPlatform.Windows || OperatingSystem.IsWindows())
        {
            return Path.Combine(segments);
        }

        return string.Join(
            '\\',
            segments.Select((segment, index) => index == 0
                ? segment.TrimEnd('\\', '/')
                : segment.Trim('\\', '/')));
    }

    private static string NormalizeRoot(DesktopPlatform platform, string path)
    {
        if (platform != DesktopPlatform.Windows || OperatingSystem.IsWindows())
        {
            return Path.GetFullPath(path);
        }

        return path.Replace('/', '\\');
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
