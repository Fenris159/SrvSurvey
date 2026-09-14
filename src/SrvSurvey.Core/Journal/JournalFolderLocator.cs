using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SrvSurvey.Core.Journal;

public sealed record JournalFolderResolution(string? SelectedPath, IReadOnlyList<string> CandidatePaths)
{
    public bool IsFound => SelectedPath is not null;

    public IReadOnlyList<string> AvailablePaths { get; init; } = SelectedPath is null ? [] : [SelectedPath];
}

public static class JournalFolderLocator
{
    public const string EnvironmentVariableName = "SRVSURVEY_JOURNAL_DIR";

    private const string LocalDirectoryName = ".local";
    private const string ShareDirectoryName = "share";
    private const string SteamDirectoryName = "Steam";
    private const string FlatpakSteamApplicationId = "com.valvesoftware.Steam";
    private const string BottlesDirectoryName = "bottles";

    private static readonly string[] JournalSegments = ["Saved Games", "Frontier Developments", "Elite Dangerous"];
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public static JournalFolderResolution ResolveCurrent(string? configuredPath = null)
    {
        DesktopPlatform platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? DesktopPlatform.Windows
            : (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) switch
            {
                true => DesktopPlatform.Linux,
                false => DesktopPlatform.Other,
            };

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Resolve(
            configuredPath,
            Environment.GetEnvironmentVariable(EnvironmentVariableName),
            userProfile,
            platform,
            Directory.Exists,
            string.IsNullOrWhiteSpace(userProfile) ? null : GetPlatformCandidates(userProfile, platform)
        );
    }

    public static JournalFolderResolution Resolve(
        string? configuredPath,
        string? environmentPath,
        string? userProfile,
        DesktopPlatform platform,
        Func<string, bool> directoryExists,
        IReadOnlyList<string>? platformCandidates = null
    )
    {
        ArgumentNullException.ThrowIfNull(directoryExists);

        StringComparer comparer =
            platform == DesktopPlatform.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var candidates = new List<string>();
        var seen = new HashSet<string>(comparer);

        string? configuredCandidate = AddCandidate(configuredPath);
        string? environmentCandidate = AddCandidate(environmentPath);

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            foreach (string candidate in platformCandidates ?? GetPlatformDefaults(userProfile.Trim(), platform))
            {
                _ = AddCandidate(candidate);
            }
        }

        string[] available = SelectAvailableCandidates();
        return new JournalFolderResolution(available.FirstOrDefault(), candidates.AsReadOnly())
        {
            AvailablePaths = available,
        };

        string? AddCandidate(string? path)
        {
            string? candidate = path?.Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
            {
                candidates.Add(candidate);
            }

            return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
        }

        string[] SelectAvailableCandidates()
        {
            if (configuredCandidate is not null && directoryExists(configuredCandidate))
            {
                return [configuredCandidate];
            }

            if (environmentCandidate is not null && directoryExists(environmentCandidate))
            {
                return [environmentCandidate];
            }

            return candidates.Where(directoryExists).ToArray();
        }
    }

    public static IReadOnlyList<string> GetPlatformDefaults(string userProfile, DesktopPlatform platform)
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
            Join(
                DesktopPlatform.Linux,
                userProfile,
                [LocalDirectoryName, ShareDirectoryName, SteamDirectoryName, .. protonJournalSegments]
            ),
            Join(
                DesktopPlatform.Linux,
                userProfile,
                [
                    ".var",
                    "app",
                    FlatpakSteamApplicationId,
                    LocalDirectoryName,
                    ShareDirectoryName,
                    SteamDirectoryName,
                    .. protonJournalSegments,
                ]
            ),
            Join(
                DesktopPlatform.Linux,
                userProfile,
                [".var", "app", FlatpakSteamApplicationId, "data", SteamDirectoryName, .. protonJournalSegments]
            ),
        ];
    }

    public static IReadOnlyList<string> GetPlatformCandidates(string userProfile, DesktopPlatform platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfile);
        StringComparer comparer =
            platform == DesktopPlatform.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var candidates = new List<string>();
        var seen = new HashSet<string>(comparer);

        foreach (string candidate in GetPlatformDefaults(userProfile, platform))
        {
            AddCandidate(candidate);
        }

        if (platform != DesktopPlatform.Linux)
        {
            return candidates;
        }

        string home = Path.GetFullPath(userProfile);
        DiscoverGamePrefixJournalDirectories(Path.Combine(home, "Games"), AddCandidate);
        foreach ((string Path, int MaximumDepth) root in GetLinuxPrefixRoots(home))
        {
            DiscoverJournalDirectories(root.Path, root.MaximumDepth, AddCandidate);
        }

        foreach (string? prefix in ReadHeroicPrefixes(home).Concat(ReadLutrisPrefixes(home)))
        {
            DiscoverJournalDirectories(prefix, maximumDepth: 2, AddCandidate);
        }

        foreach (string steamLibrary in GetSteamLibraryRoots(home))
        {
            DiscoverJournalDirectories(
                Path.Combine(steamLibrary, "steamapps", "compatdata"),
                maximumDepth: 3,
                AddCandidate
            );
        }

        return candidates;

        void AddCandidate(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string normalized = Directory.Exists(path) ? Path.GetFullPath(path) : path;
            if (seen.Add(normalized))
            {
                candidates.Add(normalized);
            }
        }
    }

    private static IEnumerable<(string Path, int MaximumDepth)> GetLinuxPrefixRoots(string home)
    {
        yield return (Path.Combine(home, ".wine"), 2);
        yield return (
            Path.Combine(home, LocalDirectoryName, ShareDirectoryName, BottlesDirectoryName, BottlesDirectoryName),
            3
        );
        yield return (
            Path.Combine(
                home,
                ".var",
                "app",
                "com.usebottles.bottles",
                "data",
                BottlesDirectoryName,
                BottlesDirectoryName
            ),
            3
        );
    }

    private static void DiscoverGamePrefixJournalDirectories(string gamesRoot, Action<string> addCandidate)
    {
        if (!Directory.Exists(gamesRoot))
        {
            return;
        }

        try
        {
            foreach (string prefix in Directory.EnumerateDirectories(gamesRoot))
            {
                try
                {
                    FileAttributes attributes = File.GetAttributes(prefix);
                    if ((attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        DiscoverJournalDirectories(prefix, maximumDepth: 5, addCandidate);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Continue with the remaining launcher prefixes.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The Games root itself is unavailable or changed while it was enumerated.
        }
    }

    private static void DiscoverJournalDirectories(string root, int maximumDepth, Action<string> addCandidate)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        const int maximumDirectories = 4096;
        var pending = new Stack<(string Path, int Depth)>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push((Path.GetFullPath(root), 0));
        while (pending.Count > 0 && visited.Count < maximumDirectories)
        {
            (string Path, int Depth) current = pending.Pop();
            if (!visited.Add(current.Path))
            {
                continue;
            }

            if (Path.GetFileName(current.Path).Equals("drive_c", StringComparison.OrdinalIgnoreCase))
            {
                AddWineUserJournalDirectories(current.Path, addCandidate);
                continue;
            }

            if (current.Depth >= maximumDepth)
            {
                continue;
            }

            AddChildDirectories(pending, current.Path, current.Depth + 1);
        }
    }

    private static void AddChildDirectories(Stack<(string Path, int Depth)> pending, string path, int depth)
    {
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(path))
            {
                FileAttributes attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push((directory, depth));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // One inaccessible launcher prefix must not block other candidates.
        }
    }

    private static void AddWineUserJournalDirectories(string driveC, Action<string> addCandidate)
    {
        string users = Path.Combine(driveC, "users");
        if (!Directory.Exists(users))
        {
            return;
        }

        try
        {
            foreach (string user in Directory.EnumerateDirectories(users))
            {
                string journalDirectory = Path.Combine(
                    user,
                    JournalSegments[0],
                    JournalSegments[1],
                    JournalSegments[2]
                );
                if (Directory.Exists(journalDirectory))
                {
                    addCandidate(journalDirectory);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Continue with candidates discovered from other launcher roots.
        }
    }

    private static IEnumerable<string> ReadHeroicPrefixes(string home)
    {
        string[] configRoots = new[]
        {
            Path.Combine(home, ".config", "heroic", "GamesConfig"),
            Path.Combine(home, ".var", "app", "com.heroicgameslauncher.hgl", "config", "heroic", "GamesConfig"),
        };

        foreach (string? configRoot in configRoots)
        {
            foreach (string path in EnumerateFilesSafely(configRoot, "*.json"))
            {
                string[] prefixes;
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(path));
                    prefixes = FindJsonStrings(document.RootElement, "winePrefix").ToArray();
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException or JsonException)
                {
                    // Malformed launcher metadata is ignored like an unavailable prefix.
                    continue;
                }

                foreach (string prefix in prefixes)
                {
                    string expanded = ExpandHome(prefix, home);
                    if (!string.IsNullOrWhiteSpace(expanded))
                    {
                        yield return expanded;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> FindJsonStrings(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (string value in FindJsonObjectStrings(element, propertyName))
            {
                yield return value;
            }

            yield break;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement item in element.EnumerateArray())
        {
            foreach (string nested in FindJsonStrings(item, propertyName))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<string> FindJsonObjectStrings(JsonElement element, string propertyName)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (
                property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String
                && property.Value.GetString() is { } value
            )
            {
                yield return value;
            }

            foreach (string nested in FindJsonStrings(property.Value, propertyName))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<string> ReadLutrisPrefixes(string home)
    {
        string[] configRoots = new[]
        {
            Path.Combine(home, ".config", "lutris", "games"),
            Path.Combine(home, ".var", "app", "net.lutris.Lutris", "config", "lutris", "games"),
        };
        foreach (string? configRoot in configRoots)
        {
            foreach (
                string? path in EnumerateFilesSafely(configRoot, "*.yml")
                    .Concat(EnumerateFilesSafely(configRoot, "*.yaml"))
            )
            {
                if (!TryReadAllLines(path, out IReadOnlyList<string> lines))
                {
                    continue;
                }

                foreach (string line in lines)
                {
                    if (TryGetLutrisPrefix(line, home, out string expanded))
                    {
                        yield return expanded;
                    }
                }
            }
        }
    }

    private static bool TryReadAllLines(string path, out IReadOnlyList<string> lines)
    {
        try
        {
            lines = File.ReadLines(path).ToArray();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            lines = [];
            return false;
        }
    }

    private static bool TryGetLutrisPrefix(string line, string home, out string expanded)
    {
        Match match = Regex.Match(
            line,
            "^\\s*prefix:\\s*(?<path>.+?)\\s*$",
            RegexOptions.CultureInvariant,
            RegexTimeout
        );
        if (!match.Success)
        {
            expanded = string.Empty;
            return false;
        }

        string prefix = match.Groups["path"].Value.Trim().Trim('\'', '"');
        expanded = ExpandHome(prefix, home);
        return !string.IsNullOrWhiteSpace(expanded);
    }

    private static IEnumerable<string> GetSteamLibraryRoots(string home)
    {
        string[] steamRoots = new[]
        {
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, LocalDirectoryName, ShareDirectoryName, SteamDirectoryName),
            Path.Combine(
                home,
                ".var",
                "app",
                FlatpakSteamApplicationId,
                LocalDirectoryName,
                ShareDirectoryName,
                SteamDirectoryName
            ),
            Path.Combine(home, ".var", "app", FlatpakSteamApplicationId, "data", SteamDirectoryName),
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? steamRoot in steamRoots)
        {
            if (seen.Add(steamRoot))
            {
                yield return steamRoot;
            }

            string libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            string text;
            try
            {
                text = File.ReadAllText(libraryFile);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (
                Match match in Regex.Matches(
                    text,
                    "\\\"path\\\"\\s+\\\"(?<path>(?:\\\\\\\\|[^\\\"])*)\\\"",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                    RegexTimeout
                )
            )
            {
                string path = match.Groups["path"].Value.Replace("\\\\", "\\", StringComparison.Ordinal);
                string expanded = ExpandHome(path, home);
                if (!string.IsNullOrWhiteSpace(expanded) && seen.Add(expanded))
                {
                    yield return expanded;
                }
            }
        }
    }

    private static string[] EnumerateFilesSafely(string directory, string pattern)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string ExpandHome(string path, string home)
    {
        string value = path.Trim();
        if (value.Equals("~", StringComparison.Ordinal))
        {
            return home;
        }

        if (value.StartsWith("~/", StringComparison.Ordinal))
        {
            return Path.Combine(home, value[2..]);
        }

        return value
            .Replace("${HOME}", home, StringComparison.Ordinal)
            .Replace("$HOME", home, StringComparison.Ordinal);
    }

    private static string Join(DesktopPlatform platform, string root, IReadOnlyList<string> segments)
    {
        char separator = platform == DesktopPlatform.Windows ? '\\' : '/';
        string trimmedRoot = root.TrimEnd('\\', '/');
        return $"{trimmedRoot}{separator}{string.Join(separator, segments)}";
    }
}
