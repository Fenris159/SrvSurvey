namespace SrvSurvey.Core.Colonization;

public static partial class ColonizationProjectCsvExporter
{
    private static readonly ColonizationBuildCatalog PreparedBuildCatalog = ColonizationBuildCatalog.LoadEmbedded();

    /// <summary>Combines positive preparation counts by normalized layout and retains names missing from the bundled catalog.</summary>
    public static string PreparedBuildingsText(ColonizationProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return string.Join(
            ", ",
            (project.PreparedBuilds ?? [])
                .Where(pair => pair.Value > 0)
                .GroupBy(
                    pair => ColonizationBuildCatalog.NormalizeSiteBuildTypeKey(pair.Key),
                    StringComparer.OrdinalIgnoreCase
                )
                .Where(group => group.Key.Length > 0)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => $"{group.Sum(pair => (long)pair.Value):N0}x {PreparedBuildingName(group.Key)}")
        );
    }

    /// <summary>Uses the catalog's canonical layout spelling and facility name without exposing Raven's question-mark suffix.</summary>
    private static string PreparedBuildingName(string key)
    {
        if (!PreparedBuildCatalog.TryResolveSiteBuildType(key, out ColonizationBuildCost? build) || build is null)
        {
            return key;
        }
        string layout =
            build.Layouts.FirstOrDefault(candidate =>
                string.Equals(
                    ColonizationBuildCatalog.NormalizeSiteBuildTypeKey(candidate),
                    key,
                    StringComparison.OrdinalIgnoreCase
                )
            ) ?? key;
        return $"{layout}-layout {build.DisplayName}";
    }
}
