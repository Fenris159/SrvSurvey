using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationProjectPreviewViewModel
{
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
            string buildings = ColonizationProjectCsvExporter.PreparedBuildingsText(project);
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
