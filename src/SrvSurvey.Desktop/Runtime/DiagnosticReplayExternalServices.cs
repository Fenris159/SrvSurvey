using SrvSurvey.Core.Frontier;
using SrvSurvey.Desktop.Platform.Frontier;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Runtime;

internal sealed class DiagnosticReplayFrontierAccountService
    : IFrontierAccountService
{
    public event EventHandler? AuthorizationCallbackReceived
    {
        add
        {
            _ = value;
        }
        remove
        {
            _ = value;
        }
    }

    public void SetActiveCommander(string? frontierId, string? commanderName)
    {
    }

    public Task<IReadOnlyList<FrontierLinkedCommander>> GetLinkedCommandersAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<FrontierLinkedCommander>>([]);
    }

    public Task<FrontierAccountState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new FrontierAccountState(
            IsLinked: false,
            Snapshot: null,
            LastCapiRefreshAt: null));
    }

    public Task<FrontierAccountSnapshot> ConnectAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<FrontierAccountSnapshot>(Unavailable());
    }

    public Task CancelConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<FrontierAccountSnapshot> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        return ConnectAsync(cancellationToken);
    }

    public Task UnlinkAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }

    private static InvalidOperationException Unavailable() => new(
        "Frontier account access is unavailable during diagnostic replay.");
}

internal sealed class DiagnosticReplayGameWindowSwitcher : IGameWindowSwitcher
{
    public int GetAvailableWindowCount() => 1;

    public bool TryActivateCurrent() => false;

    public bool TryActivateNext() => false;

    public void Dispose()
    {
    }
}

internal sealed class DiagnosticReplayScreenshotProcessingService
    : IScreenshotProcessingService
{
    public Task<ScreenshotProcessingResult> ProcessAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        ScreenshotProcessingPreferences preferences,
        string? commanderName,
        IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>?
            guardianContexts = null,
        ScreenshotNavigationContext? navigationContext = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var warnings = preferences.Enabled
            && journalEvents.Any(item => item.EventName == "Screenshot")
                ? new[]
                {
                    "Screenshot file processing is unavailable during diagnostic replay.",
                }
                : [];
        return Task.FromResult(new ScreenshotProcessingResult([], warnings));
    }
}
