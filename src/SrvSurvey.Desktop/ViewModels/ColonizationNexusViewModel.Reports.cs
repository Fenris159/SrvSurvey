using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationNexusViewModel
{
    private Action<string, long>? reportOpener;

    internal void SetReportOpener(Action<string, long>? opener)
    {
        reportOpener = opener;
        if (Detail is { } current)
        {
            Detail = MakeDetail(
                current.Nexus,
                current.SelectedScope.SystemAddress,
                current.SelectedSystem?.SystemAddress ?? 0
            );
        }
    }

    private ColonizationNexusDetailViewModel MakeDetail(
        ColonizationNexus nexus,
        long systemAddress = 0,
        long hubAddress = 0
    ) =>
        new(
            ColonizationNexusPlan.Create(nexus),
            catalog,
            openProject,
            systemAddress,
            reportOpener is null ? null : address => reportOpener?.Invoke(nexus.Id, address),
            hubAddress
        );

    internal ColonizationProjectPreviewViewModel CreateReport(string id, long systemAddress, Func<int> shipCapacity)
    {
        if (
            !CanLoad
            || Detail?.Nexus.Id != id
            || systemAddress != 0 && !Detail.Nexus.Systems.Any(system => system.SystemAddress == systemAddress)
        )
        {
            throw new InvalidOperationException("Select a loaded Nexus system before opening its report.");
        }
        int version = contextVersion;
        string name = commander!;
        string key = apiKey!;
        string? systemName = Detail.Nexus.Systems.FirstOrDefault(system => system.SystemAddress == systemAddress)?.Name;
        return new ColonizationProjectPreviewViewModel(
            async token =>
            {
                ColonizationNexus? nexus = await reader!.GetNexusAsync(id, name, key, token);
                token.ThrowIfCancellationRequested();
                if (!CanLoad || contextVersion != version)
                {
                    throw new OperationCanceledException("The active Nexus account changed.", token);
                }
                return nexus;
            },
            systemName,
            () => CanLoad && contextVersion == version,
            shipCapacity
        );
    }
}
