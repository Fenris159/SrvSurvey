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

        foreach (var candidate in candidates)
        {
            var path = Path.GetFullPath(candidate.Path);
            if (!seenPaths.Add(path) || !Directory.Exists(path))
            {
                continue;
            }

            var fileCount = Directory.EnumerateFiles(
                path,
                "*",
                SearchOption.AllDirectories).Count();
            discoveries.Add(new LegacyProfileDiscovery(
                candidate.Kind,
                path,
                fileCount));
        }

        return discoveries;
    }
}

public sealed record LegacyProfileDiscovery(
    LegacyProfileLocationKind Kind,
    string Path,
    int FileCount);
