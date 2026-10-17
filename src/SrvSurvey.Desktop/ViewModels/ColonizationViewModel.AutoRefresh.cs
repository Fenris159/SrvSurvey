namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationViewModel
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AutoRefreshLifetime = TimeSpan.FromHours(1);
    private CancellationTokenSource? autoRefreshCancellation;

    public bool CanAutoRefresh => !disposed && IsEnabled && CommanderName is not null;

    /// <summary>Session-only polling; commander changes, consent withdrawal, and shutdown stop it.</summary>
    public bool IsAutoRefreshEnabled
    {
        get => autoRefreshCancellation is not null;
        set
        {
            if (!value)
            {
                StopAutoRefresh();
            }
            else if (CanAutoRefresh && autoRefreshCancellation is null)
            {
                var session = new CancellationTokenSource();
                autoRefreshCancellation = session;
                OnPropertyChanged();
                AutoRefreshTask = RunAutoRefreshAsync(session);
            }
        }
    }

    internal Task AutoRefreshTask { get; private set; } = Task.CompletedTask;

    private void StopAutoRefresh()
    {
        CancellationTokenSource? session = autoRefreshCancellation;
        if (session is null)
        {
            return;
        }
        autoRefreshCancellation = null;
        OnPropertyChanged(nameof(IsAutoRefreshEnabled));
        session.Cancel();
    }

    private async Task RunAutoRefreshAsync(CancellationTokenSource session)
    {
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
                if (IsBusy)
                {
                    continue;
                }
                Task refresh = RefreshAsync(token);
                if (await Task.WhenAny(expiry, refresh) == expiry && ReferenceEquals(autoRefreshCancellation, session))
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
            if (ReferenceEquals(autoRefreshCancellation, session))
            {
                StopAutoRefresh();
            }
            session.Dispose();
        }
    }
}
