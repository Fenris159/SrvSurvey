using System.Runtime.InteropServices;

namespace SrvSurvey.Core.Journal;

public sealed record JournalFolderResolution(
    string? SelectedPath,
    IReadOnlyList<string> CandidatePaths)
{
    public bool IsFound => SelectedPath is not null;
}

public static class JournalFolderLocator
{
    public const string EnvironmentVariableName = "SRVSURVEY_JOURNAL_DIR";

    private static readonly string[] JournalSegments =
    [
        "Saved Games",
        "Frontier Developments",
        "Elite Dangerous",
    ];

    public static JournalFolderResolution ResolveCurrent(string? configuredPath = null)
    {
        var platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? DesktopPlatform.Windows
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? DesktopPlatform.Linux
                : DesktopPlatform.Other;

        return Resolve(
            configuredPath,
            Environment.GetEnvironmentVariable(EnvironmentVariableName),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            platform,
            Directory.Exists);
    }

    public static JournalFolderResolution Resolve(
        string? configuredPath,
        string? environmentPath,
        string? userProfile,
        DesktopPlatform platform,
        Func<string, bool> directoryExists)
    {
        ArgumentNullException.ThrowIfNull(directoryExists);

        var comparer = platform == DesktopPlatform.Windows
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var candidates = new List<string>();
        var seen = new HashSet<string>(comparer);

        AddCandidate(configuredPath);
        AddCandidate(environmentPath);

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            foreach (var candidate in GetPlatformDefaults(userProfile.Trim(), platform))
            {
                AddCandidate(candidate);
            }
        }

        return new JournalFolderResolution(
            candidates.FirstOrDefault(directoryExists),
            candidates.AsReadOnly());

        void AddCandidate(string? path)
        {
            var candidate = path?.Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
            {
                candidates.Add(candidate);
            }
        }
    }

    public static IReadOnlyList<string> GetPlatformDefaults(
        string userProfile,
        DesktopPlatform platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfile);

        if (platform == DesktopPlatform.Windows)
        {
            return [Join(DesktopPlatform.Windows, userProfile, JournalSegments)];
        }

        if (platform != DesktopPlatform.Linux)
        {
            return [];
        }

        string[] protonJournalSegments =
        [
            "steamapps",
            "compatdata",
            "359320",
            "pfx",
            "drive_c",
            "users",
            "steamuser",
            .. JournalSegments,
        ];

        return
        [
            Join(DesktopPlatform.Linux, userProfile, [".steam", "steam", .. protonJournalSegments]),
            Join(DesktopPlatform.Linux, userProfile, [".local", "share", "Steam", .. protonJournalSegments]),
            Join(
                DesktopPlatform.Linux,
                userProfile,
                [".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", .. protonJournalSegments]),
            Join(
                DesktopPlatform.Linux,
                userProfile,
                [".var", "app", "com.valvesoftware.Steam", "data", "Steam", .. protonJournalSegments]),
        ];
    }

    private static string Join(
        DesktopPlatform platform,
        string root,
        IReadOnlyList<string> segments)
    {
        var separator = platform == DesktopPlatform.Windows ? '\\' : '/';
        var trimmedRoot = root.TrimEnd('\\', '/');
        return $"{trimmedRoot}{separator}{string.Join(separator, segments)}";
    }
}
