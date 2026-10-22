using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationNexusViewModel
{
    public async Task TogglePrivacyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanEditSelected || reader is not IRavenColonialNexusWriter writer || Detail is not { } current)
        {
            return;
        }
        CancelRequest();
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Action cancel = session.Cancel;
        cancelRequest = cancel;
        IsBusy = true;
        StatusMessage = "Saving Nexus privacy...";
        try
        {
            ColonizationNexus saved = await writer.SetNexusPrivacyAsync(
                current.Nexus.Id,
                current.Nexus.Open,
                commander!,
                apiKey!,
                session.Token
            );
            session.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(cancel) || Detail?.Nexus.Id != current.Nexus.Id)
            {
                return;
            }
            ColonizationNexusDetailViewModel updated = MakeDetail(
                saved,
                current.SelectedScope.SystemAddress,
                current.SelectedSystem?.SystemAddress ?? 0
            );
            Nexuses = Nexuses.Select(item => item.Id == saved.Id ? item with { Open = saved.Open } : item).ToArray();
            selectedNexus = Nexuses.FirstOrDefault(item => item.Id == saved.Id);
            Changed(nameof(SelectedNexus));
            Detail = updated;
            StatusMessage = saved.Open ? "Nexus is now public." : "Nexus is now private.";
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or OperationCanceledException or OverflowException)
        {
            if (IsCurrent(cancel))
            {
                StatusMessage = "Nexus privacy could not be confirmed. Refresh Nexus before trying again.";
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
