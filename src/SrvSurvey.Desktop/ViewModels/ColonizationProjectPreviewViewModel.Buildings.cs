using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationProjectPreviewViewModel
{
    private static readonly ColonizationBuildCatalog PreparedBuildCatalog = ColonizationBuildCatalog.LoadEmbedded();

    /// <summary>Combines positive preparation counts by normalized layout and retains names missing from the bundled catalog.</summary>
    private static string PreparedBuildingsText(ColonizationProject project) =>
        string.Join(
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

    /// <summary>Loading previews show their building manifest instead of construction-site coordinates and affiliation.</summary>
    private static PreviewField[] ProjectDetailFields(ColonizationProjectPreview snapshot)
    {
        if (snapshot.IsCombined)
        {
            return ColonizationProjectCsvExporter
                .BuildDetails(snapshot)
                .Select(pair => new PreviewField(pair.Key, pair.Value))
                .ToArray();
        }
        ColonizationProject project = snapshot.Project;
        if (project.IsFleetCarrierLoading)
        {
            string buildings = PreparedBuildingsText(project);
            return
            [
                new("Build ID", project.BuildId),
                new("Project name", project.BuildName),
                new("Build type", "Fleet Carrier loading"),
                new("Buildings:", buildings.Length > 0 ? buildings : "Building list unavailable.")
                {
                    IsFullWidth = true,
                },
                new("Architect", project.ArchitectName ?? string.Empty),
                new("Notes", project.Notes ?? string.Empty),
            ];
        }
        return ColonizationProjectCsvExporter
            .Details(snapshot)
            .Take(8)
            .Concat([KeyValuePair.Create("Notes", project.Notes ?? string.Empty)])
            .Select(pair => new PreviewField(pair.Key, pair.Value))
            .ToArray();
    }
}
