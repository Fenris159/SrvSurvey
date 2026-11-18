using System.Windows.Input;
using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Exploration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly AsyncCommand resetExplorationOdometerCommand;
    private readonly AsyncCommand resetExplorationValueCommand;
    private ExplorationResetScope? pendingExplorationReset;
    private string? pendingExplorationResetOwner;
    private bool pendingExplorationResetIsLive;

    private enum ExplorationResetScope
    {
        Everything,
        Odometer,
        Value,
    }

    public ICommand ResetExplorationOdometerCommand => resetExplorationOdometerCommand;
    public ICommand ResetExplorationValueCommand => resetExplorationValueCommand;
    public string ResetExplorationOdometerButtonText =>
        pendingExplorationReset == ExplorationResetScope.Odometer ? "Confirm Reset Odometer" : "Reset Odometer";
    public string ResetExplorationValueButtonText =>
        pendingExplorationReset == ExplorationResetScope.Value ? "Confirm Reset Exp Value" : "Reset Exp Value";

    public Task ResetExplorationOdometerAsync() => ResetExplorationAsync(ExplorationResetScope.Odometer);

    public Task ResetExplorationValueAsync() => ResetExplorationAsync(ExplorationResetScope.Value);

    private void SetPendingExplorationReset(ExplorationResetScope? scope)
    {
        pendingExplorationReset = scope;
        pendingExplorationResetOwner = scope is null ? null : activeProfileFrontierId;
        pendingExplorationResetIsLive = activeProfileIsOdyssey;
        OnPropertyChanged(nameof(IsResetExplorationPending));
        OnPropertyChanged(nameof(ResetExplorationButtonText));
        OnPropertyChanged(nameof(ResetExplorationOdometerButtonText));
        OnPropertyChanged(nameof(ResetExplorationValueButtonText));
        cancelResetExplorationCommand.RaiseCanExecuteChanged();
    }

    private async Task ResetExplorationAsync(ExplorationResetScope scope)
    {
        lock (journalUpdateLifetime)
        {
            if (disposed)
            {
                return;
            }
            journalUpdateUsers++;
        }
        try
        {
            await journalUpdateGate.WaitAsync(CancellationToken.None);
            try
            {
                if (!disposed && activeProfileFrontierId is not null)
                {
                    await ApplyExplorationResetAsync(scope);
                }
            }
            finally
            {
                journalUpdateGate.Release();
            }
        }
        finally
        {
            lock (journalUpdateLifetime)
            {
                journalUpdateUsers--;
                if (journalUpdateUsers == 0)
                {
                    journalUpdatesDrained?.TrySetResult();
                }
            }
        }
    }

    private async Task ApplyExplorationResetAsync(ExplorationResetScope scope)
    {
        if (
            pendingExplorationReset != scope
            || !string.Equals(pendingExplorationResetOwner, activeProfileFrontierId, StringComparison.OrdinalIgnoreCase)
            || pendingExplorationResetIsLive != activeProfileIsOdyssey
        )
        {
            SetPendingExplorationReset(scope);
            ExplorationStatusMessage = scope switch
            {
                ExplorationResetScope.Odometer =>
                    "Confirm Reset Odometer to clear hyperspace jumps and distance travelled.",
                ExplorationResetScope.Value =>
                    "Confirm Reset Exp Value to clear estimated credits and scanned, DSS, and landed counts. Automatic tracking continues from this new starting point.",
                _ =>
                    "Confirm Reset Everything to clear all six survey totals. Exploration history and payment receipts are retained.",
            };
            return;
        }
        SetPendingExplorationReset(null);
        try
        {
            if (scope != ExplorationResetScope.Odometer && ExplorationHistory.AutoResetEstimatedValue)
            {
                await ExplorationHistory.ResetSurveyValueAsync();
            }
            // Shutdown drains this owned operation before disposing its stores. Finish the
            // accepted reset through profile persistence even if shutdown began during I/O.
            switch (scope)
            {
                case ExplorationResetScope.Odometer:
                    explorationState.ResetOdometer();
                    break;
                case ExplorationResetScope.Value:
                    explorationState.ResetSurveyValue();
                    break;
                default:
                    explorationState.ResetSurveyValue();
                    explorationState.ResetOdometer();
                    break;
            }
            ExplorationSnapshot snapshot = explorationState.CreateSnapshot();
            UpdateExplorationDisplay(snapshot);
            await SaveExplorationAsync(snapshot);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or InvalidOperationException
                        or SqliteException
            )
        {
            if (!disposed)
            {
                ExplorationStatusMessage = "Survey totals could not be reset: " + exception.Message;
            }
        }
    }
}
