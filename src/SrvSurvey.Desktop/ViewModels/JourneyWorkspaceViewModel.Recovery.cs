using System.Windows.Input;
using SrvSurvey.Core.Journeys;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class JourneyWorkspaceViewModel
{
    private AsyncCommand? clearUnavailableJourneyCommand;
    private AsyncCommand? resumeJourneyCommand;
    private JourneyRecoveryReference? recoveryReference;

    public bool HasUnavailableJourney => recoveryReference is not null;

    public ICommand ClearUnavailableJourneyCommand =>
        clearUnavailableJourneyCommand ??= new AsyncCommand(
            ClearUnavailableJourneyAsync,
            () => HasUnavailableJourney && !IsBusy && !IsDirty
        );

    public bool CanResumeJourney =>
        selectedDocument is { IsActive: true } && !HasActiveJourney && !HasUnavailableJourney && !IsBusy && !IsDirty;

    public ICommand ResumeJourneyCommand =>
        resumeJourneyCommand ??= new AsyncCommand(ResumeJourneyAsync, () => CanResumeJourney);

    public async Task ResumeJourneyAsync()
    {
        if (!CanResumeJourney || selectedDocument is not { } selected || frontierId is null)
        {
            return;
        }
        int version = contextVersion;
        try
        {
            IsBusy = true;
            JourneyServiceResult result = await journeyService.ResumeAsync(frontierId, isOdyssey, selected.FileName);
            if (version != contextVersion)
            {
                return;
            }
            await RefreshCatalogAsync(result.Journey?.FileName);
            if (version != contextVersion)
            {
                return;
            }
            CaptureRecoveryReference(result.Recovery);
            SetInitializationStatus(result);
            RaiseActiveState();
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            if (version == contextVersion)
            {
                StatusMessage = "The saved journey could not be resumed: " + exception.Message;
            }
        }
        finally
        {
            if (version == contextVersion)
            {
                IsBusy = false;
            }
        }
    }

    private void CaptureRecoveryReference(JourneyRecoveryReference? reference)
    {
        recoveryReference =
            reference is not null
            && string.Equals(reference.FrontierId, frontierId, StringComparison.OrdinalIgnoreCase)
            && reference.IsOdyssey == isOdyssey
                ? reference
                : null;
        OnPropertyChanged(nameof(HasUnavailableJourney));
        clearUnavailableJourneyCommand?.RaiseCanExecuteChanged();
        resumeJourneyCommand?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanResumeJourney));
    }

    public async Task ClearUnavailableJourneyAsync()
    {
        if (recoveryReference is not { } reference || IsBusy || IsDirty)
        {
            return;
        }
        int version = contextVersion;
        try
        {
            IsBusy = true;
            bool cleared = await journeyService.ClearUnavailableJourneyAsync(reference);
            if (version != contextVersion)
            {
                return;
            }
            IsBusy = false;
            await RefreshAsync();
            if (version != contextVersion)
            {
                return;
            }
            string message = cleared
                ? "The unavailable journey's active selection was cleared. Its saved file was kept. You can begin a new journey."
                : "The active selection changed or its saved journey is readable again. Nothing was cleared.";
            StartStatus = message;
            StatusMessage = message;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            if (version == contextVersion)
            {
                StartStatus = "The active journey selection could not be cleared: " + exception.Message;
                StatusMessage = StartStatus;
            }
        }
        finally
        {
            if (version == contextVersion)
            {
                IsBusy = false;
            }
        }
    }
}
