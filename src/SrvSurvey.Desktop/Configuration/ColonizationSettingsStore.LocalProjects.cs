using System.Text.Json;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.Configuration;

public sealed partial class ColonizationSettingsStore
{
    private readonly UiSettingsDocumentStore localProjectsDocumentStore;

    internal ColonizationProjectScopeState? LoadLocalProjects(string commander)
    {
        try
        {
            ColonizationProjectScopeState? state = localProjectsDocumentStore
                .Load()[commander.ToUpperInvariant()]
                ?.Deserialize<ColonizationProjectScopeState>();
            return
                state is not null
                && state.AffiliatedBuildIds is not null
                && state.AffiliatedSystemNames is not null
                && state.RetainedProjects is not null
                && state.RetainedProjects.All(entry =>
                    entry?.Project is not null && !string.IsNullOrWhiteSpace(entry.Project.BuildId)
                )
                ? state
                : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    internal void SaveLocalProjects(string commander, ColonizationProjectScopeState state) =>
        localProjectsDocumentStore.Update(root =>
            root[commander.ToUpperInvariant()] = JsonSerializer.SerializeToNode(state)
        );
}
