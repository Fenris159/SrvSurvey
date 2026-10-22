namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationViewModel
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AutoRefreshLifetime = TimeSpan.FromHours(1);

    // The polling task owns its source; the workspace retains only a stop request.
    private Action? cancelAutoRefresh;

    public bool CanAutoRefresh => !disposed && IsEnabled && CommanderName is not null;

    /// <summary>Session-only polling; commander changes, consent withdrawal, and shutdown stop it.</summary>
    public bool IsAutoRefreshEnabled
    {
        get => cancelAutoRefresh is not null;
        set
        {
            if (!value)
            {
                StopAutoRefresh();
            }
            else if (CanAutoRefresh && cancelAutoRefresh is null)
            {
                AutoRefreshTask = RunAutoRefreshAsync();
            }
        }
    }

    internal Task AutoRefreshTask { get; private set; } = Task.CompletedTask;

    private void StopAutoRefresh()
    {
        Action? cancel = cancelAutoRefresh;
        if (cancel is null)
        {
            return;
        }
        cancelAutoRefresh = null;
        OnPropertyChanged(nameof(IsAutoRefreshEnabled));
        cancel();
    }

    private async Task RunAutoRefreshAsync()
    {
        using var session = new CancellationTokenSource();
        Action cancel = session.Cancel;
        cancelAutoRefresh = cancel;
        OnPropertyChanged(nameof(IsAutoRefreshEnabled));
        CancellationToken token = session.Token;
        try
        {
            Task expiry = delayAsync(AutoRefreshLifetime, token);
            while (!token.IsCancellationRequested)
            {
                Task tick = delayAsync(AutoRefreshInterval, token);
                if (await Task.WhenAny(expiry, tick) == expiry)
                {
                    break;
                }
                await tick;
                if (IsBusy || SelectedWorkspaceTabIndex == 3 && Nexus.IsBusy)
                {
                    continue;
                }
                Task refresh = SelectedWorkspaceTabIndex == 3 ? Nexus.RefreshAsync(token) : RefreshAsync(token);
                if (await Task.WhenAny(expiry, refresh) == expiry && ReferenceEquals(cancelAutoRefresh, cancel))
                {
                    StopAutoRefresh();
                }
                await refresh;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Stopping the toggle also cancels a project request initiated by this session.
        }
        finally
        {
            if (ReferenceEquals(cancelAutoRefresh, cancel))
            {
                StopAutoRefresh();
            }
        }
    }
}
