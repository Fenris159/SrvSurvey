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
            if (!seenPaths.Add(path) || !Directory.Exists(path))
            {
                continue;
            }

            int fileCount;
            try
            {
                fileCount = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count();
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

    public static ProfileImportSource ResolveManualSelectionSource(string selectedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedPath);

        string selected = Path.GetFullPath(selectedPath);
        string? crossPlatformProfile = FindCrossPlatformProfile(selected);
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

        string nestedApplicationDirectory = Path.Combine(selected, ApplicationDirectoryName);
        string versionParent = Directory.Exists(nestedApplicationDirectory) ? nestedApplicationDirectory : selected;
        string? versionProfile = FindNewestVersionProfile(versionParent);
        return new ProfileImportSource(versionProfile ?? selected, ProfileImportSourceKind.Legacy, null);
    }

    private static string? FindCrossPlatformProfile(string selected)
    {
        if (
            string.Equals(Path.GetFileName(selected), CrossPlatformDirectoryName, StringComparison.OrdinalIgnoreCase)
            && ContainsFiles(selected)
        )
        {
            return selected;
        }

        string nested = Path.Combine(selected, CrossPlatformDirectoryName);
        return ContainsFiles(nested) ? Path.GetFullPath(nested) : null;
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
                .Where(entry => entry.Version is not null && ContainsFiles(entry.Path))
                .OrderByDescending(entry => entry.Version)
                .Select(entry => Path.GetFullPath(entry.Path))
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool ContainsFiles(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any();
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
