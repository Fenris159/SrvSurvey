using System.Windows.Input;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationNexusViewModel
{
    private readonly WorkspaceCommand confirmSystemRemovalCommand;
    private string? removalNexusId;
    private ColonizationNexusScope? removalSystem;
    public ICommand RemoveSystemCommand { get; }
    public ICommand ConfirmSystemRemovalCommand => confirmSystemRemovalCommand;
    public ICommand CancelSystemRemovalCommand { get; }
    public bool CanRemoveSystems => CanEditSelected && Detail!.Systems.Count > 1;
    public bool IsLastSystem => Detail?.Systems.Count == 1;
    public bool IsSystemRemovalVisible => removalSystem is not null;
    public string SystemRemovalWarning =>
        $"Remove '{removalSystem?.SystemName}' from this Nexus? Its build projects will remain on Raven Colonial.";
    public string SystemRemovalHint =>
        IsLastSystem
            ? "Keep at least one system. Use Delete Nexus to remove the final system."
            : "Remove this system from the Nexus";

    public void RequestSystemRemoval(ColonizationNexusScope system)
    {
        if (!CanRemoveSystems || !Detail!.Systems.Contains(system))
        {
            return;
        }
        removalNexusId = Detail.Nexus.Id;
        removalSystem = system;
        Changed(nameof(SystemRemovalWarning));
        RefreshSystemRemoval();
    }

    private void ClearSystemRemoval()
    {
        removalNexusId = null;
        removalSystem = null;
        RefreshSystemRemoval();
    }

    private void RefreshSystemRemoval()
    {
        Changed(nameof(CanRemoveSystems));
        Changed(nameof(IsLastSystem));
        Changed(nameof(SystemRemovalHint));
        Changed(nameof(IsSystemRemovalVisible));
        confirmSystemRemovalCommand.Refresh();
    }

    public async Task ConfirmSystemRemovalAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRemoveSystems || removalSystem is not { } target || Detail!.Nexus.Id != removalNexusId)
        {
            return;
        }
        string id = Detail.Nexus.Id;
        string name = commander!;
        string key = apiKey!;
        ColonizationNexusDetailViewModel current = Detail;
        ClearSystemRemoval();
        CancelRequest();
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Action cancel = session.Cancel;
        cancelRequest = cancel;
        IsBusy = true;
        StatusMessage = "Removing system from Nexus...";
        try
        {
            // setSystems replaces the list: preserve systems added since the last snapshot.
            ColonizationNexus? latest = await reader!.GetNexusAsync(id, name, key, session.Token);
            session.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(cancel))
            {
                return;
            }
            if (latest is null)
            {
                StatusMessage = "This Nexus is no longer available. Refresh Nexus before trying again.";
                return;
            }
            string[] remaining = latest
                .Systems.Where(system => system.SystemAddress != target.SystemAddress)
                .Select(system => system.Name)
                .ToArray();
            if (remaining.Length == 0)
            {
                Detail = MakeDetail(
                    latest,
                    current.SelectedScope.SystemAddress,
                    current.SelectedSystem?.SystemAddress ?? 0
                );
                StatusMessage = "Keep at least one system. Use Delete Nexus to remove the final system.";
                return;
            }
            ColonizationNexus saved =
                remaining.Length == latest.Systems.Count
                    ? latest
                    : await ((IRavenColonialNexusWriter)reader).SetNexusSystemsAsync(
                        id,
                        remaining,
                        name,
                        key,
                        session.Token
                    );
            session.Token.ThrowIfCancellationRequested();
            if (IsCurrent(cancel))
            {
                Detail = MakeDetail(
                    saved,
                    current.SelectedScope.SystemAddress,
                    current.SelectedSystem?.SystemAddress ?? 0
                );
                StatusMessage = "Nexus systems updated.";
            }
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or OperationCanceledException or OverflowException)
        {
            if (IsCurrent(cancel))
            {
                StatusMessage = "System removal could not be confirmed. Refresh Nexus before trying again.";
            }
        }
        finally
        {
            if (IsCurrent(cancel))
            {
                cancelRequest = null;
                IsBusy = false;
            }
        }
    }
}
