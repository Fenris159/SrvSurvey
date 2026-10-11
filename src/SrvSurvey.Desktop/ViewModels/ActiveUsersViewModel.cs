using System.Globalization;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Displays a cached community count after the first commander session loads, without blocking startup.</summary>
public sealed class ActiveUsersViewModel : WorkspaceObservable, IAsyncDisposable
{
    private readonly DailyActiveUsersClient client;
    private readonly bool enabled;
    private readonly TimeProvider clock;
    private readonly CancellationTokenSource lifetime = new();
    private DateOnly? refreshDay;
    private bool disposed;
    private long? count;

    /// <summary>Owns the daily client and disables external reporting during diagnostic replay.</summary>
    internal ActiveUsersViewModel(DailyActiveUsersClient client, bool enabled = true, TimeProvider? clock = null)
    {
        this.client = client;
        this.enabled = enabled;
        this.clock = clock ?? TimeProvider.System;
    }

    public string CountText => count?.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
    internal Task PendingRefresh { get; private set; } = Task.CompletedTask;

    /// <summary>Starts a daily background refresh once a commander is known, including sessions spanning midnight.</summary>
    internal void SessionLoaded(bool hasCommander)
    {
        if (!hasCommander || !enabled || disposed)
        {
            return;
        }
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (refreshDay == today || !PendingRefresh.IsCompleted)
        {
            return;
        }
        refreshDay = today;
        PendingRefresh = RefreshAsync();
    }

    /// <summary>Loads the daily count off the UI thread and publishes it through normal property notifications.</summary>
    private async Task RefreshAsync()
    {
        long? result = await Task.Run(() => client.RefreshAsync(lifetime.Token), CancellationToken.None)
            .ConfigureAwait(false);
        if (!disposed)
        {
            count = result;
            Changed(nameof(CountText));
        }
    }

    /// <summary>Cancels and observes pending network work before releasing owned resources.</summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        await lifetime.CancelAsync().ConfigureAwait(false);
        await PendingRefresh.ConfigureAwait(false);
        client.Dispose();
        lifetime.Dispose();
    }
}
