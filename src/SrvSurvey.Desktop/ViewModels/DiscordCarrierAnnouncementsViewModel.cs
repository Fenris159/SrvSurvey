using System.ComponentModel;
using System.Threading.Channels;
using System.Windows.Input;
using Avalonia.Threading;
using SrvSurvey.Core.Discord;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Edits the opt-in and delivers live announcements off the journal/UI thread in carrier-event order.</summary>
public sealed class DiscordCarrierAnnouncementsViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly DiscordCarrierSettingsStore store;
    private readonly DiscordCarrierWebhookClient client;
    private readonly CarrierJumpAnnouncementTracker tracker = new();
    private readonly Func<bool> networkAllowed;
    private readonly Action<Action> dispatch;
    private readonly Channel<Delivery> queue = Channel.CreateBounded<Delivery>(100);
    private readonly CancellationTokenSource shutdown = new();
    private readonly Command saveCommand;
    private readonly Command testCommand;
    private DiscordCarrierPreferences saved;
    private DiscordCarrierPreferences draft;
    private Task? worker;
    private Task lastDelivery = Task.CompletedTask;
    private Task pendingTest = Task.CompletedTask;
    private int revision;
    private bool isBusy;
    private bool disposed;
    private string statusMessage = "Announcements are off until you save and enable a Discord webhook.";

    public DiscordCarrierAnnouncementsViewModel(
        DiscordCarrierSettingsStore store,
        DiscordCarrierWebhookClient? webhookClient = null,
        Func<bool>? networkAllowed = null,
        Action<Action>? dispatch = null
    )
    {
        this.store = store;
        client = webhookClient ?? new DiscordCarrierWebhookClient();
        this.networkAllowed = networkAllowed ?? (() => true);
        this.dispatch = dispatch ?? (action => Dispatcher.UIThread.Post(action));
        saved = store.Load();
        draft = saved;
        saveCommand = new Command(SaveAsync, () => !isBusy && HasChanges && (!Enabled || IsWebhookValid));
        testCommand = new Command(TestAsync, () => !isBusy && IsWebhookValid && this.networkAllowed());
        if (saved.Enabled)
        {
            statusMessage = "Listening for live carrier jump events while SrvSurvey is running.";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand SaveCommand => saveCommand;
    public ICommand TestCommand => testCommand;
    public bool HasChanges => draft != saved;
    public bool IsWebhookValid => DiscordCarrierWebhookClient.ParseWebhookUrl(WebhookUrl) is not null;
    public bool HasInvalidWebhookUrl => !IsWebhookValid && (Enabled || WebhookUrl.Length > 0);
    public string StatusMessage => statusMessage;
    public string WebhookUrl
    {
        get => draft.WebhookUrl;
        set => Edit(draft with { WebhookUrl = value ?? "" });
    }
    public bool Enabled
    {
        get => draft.Enabled;
        set => Edit(draft with { Enabled = value });
    }
    public bool PersonalCarrier
    {
        get => draft.PersonalCarrier;
        set => Edit(draft with { PersonalCarrier = value });
    }
    public bool SquadronCarrier
    {
        get => draft.SquadronCarrier;
        set => Edit(draft with { SquadronCarrier = value });
    }
    public bool JumpScheduled
    {
        get => draft.JumpScheduled;
        set => Edit(draft with { JumpScheduled = value });
    }
    public bool JumpCancelled
    {
        get => draft.JumpCancelled;
        set => Edit(draft with { JumpCancelled = value });
    }
    public bool JumpArrived
    {
        get => draft.JumpArrived;
        set => Edit(draft with { JumpArrived = value });
    }
    internal Task PendingDelivery => lastDelivery;

    private void Edit(DiscordCarrierPreferences next)
    {
        draft = next;
        Changed();
    }

    private void Changed()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        saveCommand.RaiseCanExecuteChanged();
        testCommand.RaiseCanExecuteChanged();
    }

    public Task SaveAsync()
    {
        if (Enabled && !IsWebhookValid)
        {
            Report("Enter a valid Discord webhook URL before enabling announcements.");
            return Task.CompletedTask;
        }
        try
        {
            DiscordCarrierPreferences next = draft with { WebhookUrl = WebhookUrl.Trim() };
            store.Save(next);
            saved = next;
            draft = next;
            Interlocked.Increment(ref revision);
            Report(
                Enabled
                    ? "Saved. Listening for live carrier jump events."
                    : "Saved. Discord carrier announcements are off."
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Report("Could not save Discord settings. Check that the settings folder is writable.");
        }
        return Task.CompletedTask;
    }

    public Task TestAsync()
    {
        if (disposed || isBusy || !networkAllowed())
        {
            return Task.CompletedTask;
        }
        pendingTest = SendTestAsync();
        return pendingTest;
    }

    private async Task SendTestAsync()
    {
        isBusy = true;
        Report("Sending a test message to Discord…");
        try
        {
            DiscordWebhookResult result = await client.SendTestAsync(WebhookUrl, shutdown.Token);
            Report(result.Message);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            // Closing the application cancels the optional test request.
        }
        finally
        {
            isBusy = false;
            Changed();
        }
    }

    public void ApplyJournalEvents(
        string commander,
        IReadOnlyList<JournalEventEnvelope> events,
        bool allowPublishing,
        FrontierAccountSnapshot? account = null,
        FollowRouteDocument? route = null,
        CarrierJournalIdentity? initialIdentity = null,
        FollowRouteDocument? squadronRoute = null
    )
    {
        if (disposed || !networkAllowed())
        {
            return;
        }
        foreach (CarrierJumpAnnouncement announcement in tracker.Apply(commander, events, account, initialIdentity))
        {
            if (!allowPublishing || !saved.Includes(announcement))
            {
                continue;
            }
            CarrierJumpAnnouncement message = WithRoute(
                announcement,
                announcement.CarrierKind == FollowRouteCarrierKind.Squadron ? squadronRoute ?? route : route
            );
            var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!queue.Writer.TryWrite(new Delivery(message, saved, Volatile.Read(ref revision), complete)))
            {
                Report("Discord announcements are queued faster than they can be sent. Some messages were skipped.");
                continue;
            }
            lastDelivery = complete.Task;
            worker ??= Task.Run(DeliverAsync, CancellationToken.None);
        }
    }

    private static CarrierJumpAnnouncement WithRoute(CarrierJumpAnnouncement announcement, FollowRouteDocument? route)
    {
        if (
            route is not { IsActive: true }
            || route.CarrierKind != announcement.CarrierKind
            || !route.Hops.Any(hop =>
                string.Equals(hop.Name, announcement.Destination, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return announcement;
        }
        return announcement with
        {
            Route =
                $"{route.Name ?? "Carrier route"}: {string.Join(" → ", route.GetNavigationHops().Take(8).Select(hop => hop.Name))}",
        };
    }

    private async Task DeliverAsync()
    {
        try
        {
            await foreach (Delivery delivery in queue.Reader.ReadAllAsync(shutdown.Token).ConfigureAwait(false))
            {
                try
                {
                    if (delivery.Revision != Volatile.Read(ref revision) || !networkAllowed())
                    {
                        continue;
                    }
                    DiscordWebhookResult result = await client
                        .SendAnnouncementAsync(delivery.Preferences.WebhookUrl, delivery.Announcement, shutdown.Token)
                        .ConfigureAwait(false);
                    dispatch(() =>
                    {
                        if (!disposed && delivery.Revision == Volatile.Read(ref revision))
                        {
                            Report(result.Message);
                        }
                    });
                }
                finally
                {
                    delivery.Complete.TrySetResult();
                }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            // Shutdown interrupts rate-limit waits and outstanding requests.
        }
        finally
        {
            while (queue.Reader.TryRead(out Delivery? remaining))
            {
                remaining.Complete.TrySetResult();
            }
        }
    }

    private void Report(string message)
    {
        statusMessage = message;
        Changed();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        await shutdown.CancelAsync();
        queue.Writer.TryComplete();
        if (worker is not null)
        {
            await worker.ConfigureAwait(false);
        }
        await pendingTest.ConfigureAwait(false);
        client.Dispose();
        shutdown.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record Delivery(
        CarrierJumpAnnouncement Announcement,
        DiscordCarrierPreferences Preferences,
        int Revision,
        TaskCompletionSource Complete
    );

    private sealed class Command(Func<Task> execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public async void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                await execute();
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
