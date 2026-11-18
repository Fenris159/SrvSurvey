using System.Windows.Input;
using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Exploration.History;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly AsyncCommand resetExplorationOdometerCommand;
    private readonly AsyncCommand resetExplorationValueCommand;
    private bool activeExplorationProfileReady;
    private long explorationResetGeneration;

    private sealed record ExplorationResetIntent(string Owner, bool IsLive, long Generation, bool IsConfirmation);

    private ExplorationResetScope? pendingExplorationReset;

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
        explorationResetGeneration++;
        pendingExplorationReset = scope;
        OnPropertyChanged(nameof(IsResetExplorationPending));
        OnPropertyChanged(nameof(ResetExplorationButtonText));
        OnPropertyChanged(nameof(ResetExplorationOdometerButtonText));
        OnPropertyChanged(nameof(ResetExplorationValueButtonText));
        cancelResetExplorationCommand.RaiseCanExecuteChanged();
    }

    private bool CanResetExploration() => activeProfileFrontierId is not null && activeExplorationProfileReady;

    private async Task ResetExplorationAsync(ExplorationResetScope scope)
    {
        ExplorationResetIntent intent;
        lock (journalUpdateLifetime)
        {
            if (disposed || !activeExplorationProfileReady || activeProfileFrontierId is null)
            {
                return;
            }
            intent = new(
                activeProfileFrontierId,
                activeProfileIsOdyssey,
                explorationResetGeneration,
                pendingExplorationReset == scope
            );
            journalUpdateUsers++;
        }
        try
        {
            await journalUpdateGate.WaitAsync(CancellationToken.None);
            try
            {
                if (
                    activeExplorationProfileReady
                    && (!disposed || intent.IsConfirmation)
                    && intent.Generation == explorationResetGeneration
                    && string.Equals(intent.Owner, activeProfileFrontierId, StringComparison.OrdinalIgnoreCase)
                    && intent.IsLive == activeProfileIsOdyssey
                )
                {
                    await ApplyExplorationResetAsync(scope, intent);
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

    private async Task ApplyExplorationResetAsync(ExplorationResetScope scope, ExplorationResetIntent intent)
    {
        if (!intent.IsConfirmation)
        {
            SetPendingExplorationReset(scope);
            ExplorationStatusMessage = scope switch
            {
                ExplorationResetScope.Odometer =>
                    "Confirm Reset Odometer to clear hyperspace jumps and distance travelled.",
                ExplorationResetScope.Value =>
                    "Confirm Reset Exp Value to clear estimated credits and scanned, DSS, and landed counts.",
                _ =>
                    "Confirm Reset Everything to clear all six survey totals. Exploration history and payment receipts are retained.",
            };
            return;
        }
        SetPendingExplorationReset(null);
        try
        {
            long acceptedGeneration = explorationResetGeneration;
            if (journalMonitor is not null)
            {
                // Bring the live counters through records already visible to history. The
                // consumed references below then give both stores the same reset boundary.
                JournalMonitorUpdate pending = await journalMonitor.PollAsync(CancellationToken.None);
                await ApplyMonitorUpdateAsync(pending, isManualRefresh: false, CancellationToken.None);
            }
            if (
                !activeExplorationProfileReady
                || acceptedGeneration != explorationResetGeneration
                || !string.Equals(intent.Owner, activeProfileFrontierId, StringComparison.OrdinalIgnoreCase)
                || intent.IsLive != activeProfileIsOdyssey
            )
            {
                return;
            }
            if (scope != ExplorationResetScope.Odometer && ExplorationHistory.AutoResetEstimatedValue)
            {
                ExplorationSurveyResetPoint point = await ExplorationHistory.CaptureSurveyResetPointAsync(
                    projectionState.ConsumedExplorationRecords.Values.ToArray()
                );
                ExplorationSnapshot target = SnapshotAfterExplorationReset(scope);
                // The profile owns the reset intent and totals in one atomic file write. If
                // catalog I/O is interrupted, startup reapplies this same point, never a later one.
                await commanderProfileStore.SaveExplorationResetAsync(
                    activeProfileFrontierId!,
                    activeProfileCommanderName,
                    activeProfileIsOdyssey,
                    target,
                    point,
                    CancellationToken.None
                );
                Task apply = ExplorationHistory.ApplySurveyResetPointAsync(point);
                ApplyExplorationResetToState(scope);
                UpdateExplorationDisplay(explorationState.CreateSnapshot());
                await apply;
                ExplorationStatusMessage = "Survey totals and their starting point were saved.";
            }
            else
            {
                ApplyExplorationResetToState(scope);
                ExplorationSnapshot snapshot = explorationState.CreateSnapshot();
                UpdateExplorationDisplay(snapshot);
                await SaveExplorationAsync(snapshot);
            }
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

    private ExplorationSnapshot SnapshotAfterExplorationReset(ExplorationResetScope scope) =>
        scope == ExplorationResetScope.Everything
            ? ExplorationSnapshot.Empty
            : new(0, explorationState.DistanceTravelled, explorationState.JumpCount, 0, 0, 0);

    private void ApplyExplorationResetToState(ExplorationResetScope scope)
    {
        if (scope != ExplorationResetScope.Odometer)
        {
            explorationState.ResetSurveyValue();
        }

        if (scope != ExplorationResetScope.Value)
        {
            explorationState.ResetOdometer();
        }
    }
}
