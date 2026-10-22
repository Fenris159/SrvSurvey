using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Storage;

public static class LegacyProfileLocator
{
    private const string ApplicationDirectoryName = "SrvSurvey";
    private const string CrossPlatformDirectoryName = "cross-platform";
    private const string CrossPlatformUiSettingsFileName = "cross-platform-ui.json";

    public static IReadOnlyList<LegacyProfileDiscovery> Discover(IEnumerable<LegacyProfileCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var discoveries = new List<LegacyProfileDiscovery>();
        var seenPaths = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
        );

        foreach (LegacyProfileCandidate? candidate in candidates.SelectMany(ExpandVersionCandidates))
        {
            string path = Path.GetFullPath(candidate.Path);
            if (!seenPaths.Add(GetPhysicalDirectory(path)) || !ContainsProfileData(path))
            {
                continue;
            }

            int fileCount;
            try
            {
                fileCount = Directory
                    .EnumerateFiles(
                        path,
                        "*",
                        new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            IgnoreInaccessible = true,
                            AttributesToSkip = FileAttributes.ReparsePoint,
                        }
                    )
                    .Count();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (fileCount == 0)
            {
                continue;
            }

            discoveries.Add(new LegacyProfileDiscovery(candidate.Kind, path, fileCount));
        }

        return discoveries;
    }

    public static string ResolveManualSelection(string selectedPath)
    {
        return ResolveManualSelectionSource(selectedPath).DataDirectory;
    }

    public static ProfileImportSource ResolveManualSelectionSource(
        string selectedPath,
        string? currentDataDirectory = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedPath);

        string selected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selectedPath));
        string? prefixProfile = FindWinePrefixProfile(selected);
        if (prefixProfile is not null)
        {
            return new ProfileImportSource(prefixProfile, ProfileImportSourceKind.Legacy, null);
        }
        string? crossPlatformProfile = FindCrossPlatformProfile(selected, currentDataDirectory);
        if (crossPlatformProfile is not null)
        {
            string? parent = Path.GetDirectoryName(crossPlatformProfile);
            string? uiSettingsPath = parent is null ? null : Path.Combine(parent, CrossPlatformUiSettingsFileName);
            return new ProfileImportSource(
                crossPlatformProfile,
                ProfileImportSourceKind.CrossPlatform,
                File.Exists(uiSettingsPath) ? Path.GetFullPath(uiSettingsPath) : null
            );
        }

        string? versionProfile = new[]
        {
            Path.Combine(selected, ApplicationDirectoryName),
            Path.Combine(selected, ApplicationDirectoryName + "-"),
            selected,
        }
            .Select(FindNewestVersionProfile)
            .FirstOrDefault(path => path is not null);
        return new ProfileImportSource(versionProfile ?? selected, ProfileImportSourceKind.Legacy, null);
    }

    internal static bool IsWinePrefixRoot(string path) =>
        Directory.Exists(Path.Combine(path, "drive_c"))
        || Directory.Exists(Path.Combine(path, "pfx", "drive_c"))
        || string.Equals(Path.GetFileName(path), "drive_c", StringComparison.OrdinalIgnoreCase);

    private static string? FindWinePrefixProfile(string selected)
    {
        if (!IsWinePrefixRoot(selected))
        {
            return null;
        }
        var users = new List<string>();
        JournalFolderLocator.DiscoverWineUserDirectories(selected, 2, users.Add);
        return users
            .Order(StringComparer.Ordinal)
            .SelectMany(AppDataPaths.GetWineProfileParents)
            .Select(FindNewestVersionProfile)
            .FirstOrDefault(path => path is not null);
    }

    private static string? FindCrossPlatformProfile(string selected, string? currentDataDirectory)
    {
        if (
            string.Equals(Path.GetFileName(selected), CrossPlatformDirectoryName, StringComparison.OrdinalIgnoreCase)
            && ContainsCrossPlatformData(selected)
        )
        {
            return selected;
        }

        string nested = Path.Combine(selected, CrossPlatformDirectoryName);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        bool isCurrentProfile =
            currentDataDirectory is not null
            && string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(nested)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentDataDirectory)),
                comparison
            );
        return !isCurrentProfile && ContainsCrossPlatformData(nested) ? Path.GetFullPath(nested) : null;
    }

    private static string? FindNewestVersionProfile(string parent)
    {
        if (!Directory.Exists(parent))
        {
            return null;
        }

        try
        {
            return Directory
                .EnumerateDirectories(parent)
                .Select(path => new
                {
                    Path = path,
                    Version = Version.TryParse(Path.GetFileName(path), out Version? version) ? version : null,
                })
                .Where(entry => entry.Version is not null && ContainsProfileData(entry.Path))
                .OrderByDescending(entry => entry.Version)
                .Select(entry => Path.GetFullPath(entry.Path))
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool ContainsCrossPlatformData(string path) =>
        ContainsProfileData(path) || File.Exists(Path.Combine(path, "plotters.json"));

    private static string GetPhysicalDirectory(string path)
    {
        string physical = Path.GetPathRoot(path)!;
        try
        {
            foreach (
                string segment in path[physical.Length..]
                    .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            )
            {
                physical = Path.Combine(physical, segment);
                if (!Directory.Exists(physical))
                {
                    return path;
                }
                var directory = new DirectoryInfo(physical);
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    physical = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? physical;
                }
            }
            return physical;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    private static bool ContainsProfileData(string path)
    {
        try
        {
            return File.Exists(Path.Combine(path, "settings.json"))
                || Directory.EnumerateFiles(path, "F*-*.json", SearchOption.TopDirectoryOnly).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<LegacyProfileCandidate> ExpandVersionCandidates(LegacyProfileCandidate candidate)
    {
        yield return candidate;

        string path = Path.GetFullPath(candidate.Path);
        if (!Version.TryParse(Path.GetFileName(path), out _))
        {
            yield break;
        }

        string? parent = Path.GetDirectoryName(path);
        if (parent is null || !Directory.Exists(parent))
        {
            yield break;
        }

        IReadOnlyList<string> siblings;
        try
        {
            siblings = Directory
                .EnumerateDirectories(parent)
                .Select(directory => new
                {
                    Path = directory,
                    Version = Version.TryParse(Path.GetFileName(directory), out Version? version) ? version : null,
                })
                .Where(entry => entry.Version is not null)
                .OrderByDescending(entry => entry.Version)
                .Select(entry => entry.Path)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (string sibling in siblings)
        {
            yield return candidate with
            {
                Path = sibling,
            };
        }
    }
}

public sealed record LegacyProfileDiscovery(LegacyProfileLocationKind Kind, string Path, int FileCount);

public enum ProfileImportSourceKind
{
    Legacy,
    CrossPlatform,
}

public sealed record ProfileImportSource(string DataDirectory, ProfileImportSourceKind Kind, string? UiSettingsPath);
