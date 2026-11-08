using System.Windows.Input;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationViewModel
{
    private AsyncCommand? linkCarrierToProjectsCommand;
    private AsyncCommand? unlinkCarrierFromProjectsCommand;
    private int projectCarrierLifetime;

    public ICommand LinkCarrierToSelectedProjectsCommand =>
        linkCarrierToProjectsCommand ??= new AsyncCommand(
            LinkCarrierToSelectedProjectsAsync,
            CanChangeSelectedProjectCarriers
        );

    public ICommand UnlinkCarrierFromSelectedProjectsCommand =>
        unlinkCarrierFromProjectsCommand ??= new AsyncCommand(
            UnlinkCarrierFromSelectedProjectsAsync,
            CanChangeSelectedProjectCarriers
        );

    public bool ShowProjectCarrierActions =>
        projectBuildFilter.Value is ColonizationProjectBuildFilter.Building or ColonizationProjectBuildFilter.Loading;

    public Task LinkCarrierToSelectedProjectsAsync() => ChangeSelectedProjectCarriersAsync(link: true);

    public Task UnlinkCarrierFromSelectedProjectsAsync() => ChangeSelectedProjectCarriersAsync(link: false);

    /// <summary>Requires an enabled commander, Building or Loading filter, checked carriers and a shown eligible project.</summary>
    private bool CanChangeSelectedProjectCarriers() =>
        !disposed
        && IsEnabled
        && !IsBusy
        && CommanderName is not null
        && ShowProjectCarrierActions
        && client is IRavenColonialProjectCarrierClient
        && CarrierSelection.SelectedCount > 0
        && VisibleProjects.Any(row => row.IsShown && ColonizationProjectCarrierLinks.CanLink(row.Project));

    /// <summary>Captures the visible Show selection and carrier once, continuing past individual Raven failures.</summary>
    private async Task ChangeSelectedProjectCarriersAsync(bool link)
    {
        if (!CanChangeSelectedProjectCarriers() || client is not IRavenColonialProjectCarrierClient carrierClient)
        {
            return;
        }

        int version = recovery.ProfileVersion;
        int lifetime = projectCarrierLifetime;
        bool IsCurrentOperation() => lifetime == projectCarrierLifetime && IsCurrentProjectProfile(version);
        IReadOnlyList<ColonizationFleetCarrier> carriers = CarrierSelection.ResolveCarriers();
        ColonizationProject[] selected = VisibleProjects
            .Where(row => row.IsShown)
            .Select(row => row.Project)
            .DistinctBy(project => project.BuildId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        int updated = 0;
        var failures = new List<string>();
        IsBusy = true;
        PauseCurrentSystemPlanning();
        StatusMessage = link
            ? "Linking carrier to selected projects..."
            : "Unlinking carrier from selected projects...";
        try
        {
            foreach (
                (ColonizationProject project, ColonizationFleetCarrier carrier) in selected.SelectMany(project =>
                    carriers.Select(carrier => (project, carrier))
                )
            )
            {
                if (!IsCurrentOperation())
                {
                    return;
                }
                if (
                    await TryChangeProjectCarrierAsync(
                        carrierClient,
                        project,
                        carrier,
                        link,
                        IsCurrentOperation,
                        failures
                    )
                )
                {
                    updated++;
                }
            }
            if (IsCurrentOperation())
            {
                StatusMessage = link
                    ? $"Carrier links added: {updated:N0}. Failed: {failures.Count:N0}."
                    : $"Carrier links removed: {updated:N0}. Failed: {failures.Count:N0}.";
                if (failures.Count > 0)
                {
                    StatusMessage += " " + string.Join("; ", failures);
                }
            }
        }
        finally
        {
            IsBusy = false;
            await RefreshPendingAsync(CancellationToken.None);
        }
    }

    /// <summary>Changes one eligible association, applies acknowledgements only to its owning operation and retains failures.</summary>
    private async Task<bool> TryChangeProjectCarrierAsync(
        IRavenColonialProjectCarrierClient carrierClient,
        ColonizationProject project,
        ColonizationFleetCarrier carrier,
        bool link,
        Func<bool> isCurrent,
        List<string> failures
    )
    {
        // Plans use synthetic build IDs and must never reach the project API, even in a mixed selection.
        if (!ColonizationProjectCarrierLinks.CanLink(project))
        {
            return false;
        }
        try
        {
            if (link)
            {
                await carrierClient.LinkProjectCarrierAsync(project.BuildId, carrier.MarketId, CancellationToken.None);
            }
            else
            {
                await carrierClient.UnlinkProjectCarrierAsync(
                    project.BuildId,
                    carrier.MarketId,
                    CancellationToken.None
                );
            }
            if (!isCurrent())
            {
                return false;
            }
            ApplyProjectCarrierLink(project.BuildId, carrier, link);
            return true;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            failures.Add($"{project.BuildName} / {carrier.Name}: {exception.Message}");
            return false;
        }
    }

    /// <summary>Reflects acknowledged links without reloading saved Show flags or replacing newer project data.</summary>
    private void ApplyProjectCarrierLink(string buildId, ColonizationFleetCarrier carrier, bool link)
    {
        ColonizationProject? current = recovery.Projects.FirstOrDefault(project =>
            string.Equals(project.BuildId, buildId, StringComparison.OrdinalIgnoreCase)
        );
        if (current is null)
        {
            return;
        }
        ColonizationProject updated = ColonizationProjectCarrierLinks.Apply(current, carrier, link);
        recovery.ReplaceWorkspace(
            recovery.Projects.Select(project => project == current ? updated : project),
            recovery.FleetCarriers
        );
        RebuildProjectRows();
        UpdateProjectSummary();
    }

    private void RaiseProjectCarrierCommandStates()
    {
        OnPropertyChanged(nameof(ShowProjectCarrierActions));
        linkCarrierToProjectsCommand?.RaiseCanExecuteChanged();
        unlinkCarrierFromProjectsCommand?.RaiseCanExecuteChanged();
    }
}
