namespace SrvSurvey.Core.Storage;

public static class LegacyProfileLocator
{
    public static IReadOnlyList<LegacyProfileDiscovery> Discover(
        IEnumerable<LegacyProfileCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var discoveries = new List<LegacyProfileDiscovery>();
        var seenPaths = new HashSet<string>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);

        foreach (var candidate in candidates.SelectMany(ExpandVersionCandidates))
        {
            var path = Path.GetFullPath(candidate.Path);
            if (!seenPaths.Add(path) || !Directory.Exists(path))
            {
                continue;
            }

            int fileCount;
            try
            {
                fileCount = Directory.EnumerateFiles(
                    path,
                    "*",
                    SearchOption.AllDirectories).Count();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (fileCount == 0)
            {
                continue;
            }

            discoveries.Add(new LegacyProfileDiscovery(
                candidate.Kind,
                path,
                fileCount));
        }

        return discoveries;
    }

    private static IEnumerable<LegacyProfileCandidate> ExpandVersionCandidates(
        LegacyProfileCandidate candidate)
    {
        yield return candidate;

        var path = Path.GetFullPath(candidate.Path);
        if (!Version.TryParse(Path.GetFileName(path), out _))
        {
            yield break;
        }

        var parent = Path.GetDirectoryName(path);
        if (parent is null || !Directory.Exists(parent))
        {
            yield break;
        }

        IReadOnlyList<string> siblings;
        try
        {
            siblings = Directory.EnumerateDirectories(parent)
                .Select(directory => new
                {
                    Path = directory,
                    Version = Version.TryParse(
                        Path.GetFileName(directory),
                        out var version)
                            ? version
                            : null,
                })
                .Where(entry => entry.Version is not null)
                .OrderByDescending(entry => entry.Version)
                .Select(entry => entry.Path)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var sibling in siblings)
        {
            yield return candidate with { Path = sibling };
        }
    }
}

public sealed record LegacyProfileDiscovery(
    LegacyProfileLocationKind Kind,
    string Path,
    int FileCount);
