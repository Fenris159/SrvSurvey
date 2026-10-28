using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using SrvSurvey.Core.Discord;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ProfileSync;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class DiscordCarrierAnnouncementsTests
{
    private const string Webhook = "https://discord.com/api/webhooks/123/test-token";

    [Fact]
    public async Task SettingsRequireAnExplicitSaveAndDefaultToOff()
    {
        await using var fixture = new Fixture();
        DiscordCarrierAnnouncementsViewModel model = fixture.Model;
        int changes = 0;
        model.PropertyChanged += (_, _) => changes++;
        model.SaveCommand.CanExecuteChanged += (_, _) => changes++;
        model.TestCommand.CanExecuteChanged += (_, _) => changes++;
        Assert.False(model.Enabled);
        Assert.False(model.HasInvalidWebhookUrl);
        Assert.False(model.HasChanges);
        Assert.False(model.SaveCommand.CanExecute(null));
        Assert.False(model.TestCommand.CanExecute(null));
        model.Enabled = true;
        Assert.True(model.HasInvalidWebhookUrl);
        Assert.False(model.SaveCommand.CanExecute(null));
        await model.SaveAsync();
        Assert.Contains("valid Discord", model.StatusMessage, StringComparison.Ordinal);
        model.WebhookUrl = " " + Webhook + " ";
        model.PersonalCarrier = false;
        model.SquadronCarrier = false;
        model.JumpScheduled = false;
        model.JumpCancelled = false;
        model.JumpArrived = false;
        Assert.True(model.HasChanges);
        Assert.True(model.IsWebhookValid);
        Assert.False(model.HasInvalidWebhookUrl);
        Assert.False(fixture.Store.Load().Enabled);
        model.SaveCommand.Execute(null);
        Assert.False(model.HasChanges);
        Assert.Equal(
            new DiscordCarrierPreferences(true, Webhook, false, false, false, false, false),
            fixture.Store.Load()
        );
        Assert.True(changes > 10);
        Assert.Contains("Listening", model.StatusMessage, StringComparison.Ordinal);
        model.Enabled = false;
        await model.SaveAsync();
        Assert.Contains("off", model.StatusMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"DiscordCarrierAnnouncements\":{\"Enabled\":\"invalid\"}}")]
    [InlineData("not-json")]
    [InlineData("{\"DiscordCarrierAnnouncements\":{\"WebhookUrl\":null}}")]
    public async Task InvalidSettingsFailClosedAndPreserveUnrelatedSettings(string json)
    {
        await using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.Path, json);
        Assert.False(fixture.Store.Load().Enabled);
        Assert.NotNull(fixture.Store.Load().WebhookUrl);
        await File.WriteAllTextAsync(fixture.Path, "{\"Unrelated\":42}");
        fixture.Store.Save(new(true, Webhook));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.Path));
        Assert.Equal(42, document.RootElement.GetProperty("Unrelated").GetInt32());
        Assert.Null(
            ProfileSyncPolicy.IsMachineValue(
                "config",
                "cross-platform-ui.json",
                "/DiscordCarrierAnnouncements/WebhookUrl"
            )
        );
        Assert.Null(
            ProfileSyncPolicy.IsMachineValue("config", "cross-platform-ui.json", "/DiscordCarrierAnnouncements/Enabled")
        );
    }

    [Fact]
    public async Task ExplicitTestCanPostBeforeOptInAndReportsSafeDeliveryStatus()
    {
        await using var fixture = new Fixture();
        fixture.Model.WebhookUrl = Webhook;
        Assert.True(fixture.Model.TestCommand.CanExecute(null));
        await fixture.Model.TestAsync();
        Assert.Single(fixture.Payloads);
        Assert.False(fixture.Model.Enabled);
        Assert.Contains("sent", fixture.Model.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("test-token", fixture.Model.StatusMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CarrierAnnouncementKind.Scheduled, false)]
    [InlineData(CarrierAnnouncementKind.Cancelled, false)]
    [InlineData(CarrierAnnouncementKind.Arrived, false)]
    [InlineData(CarrierAnnouncementKind.Scheduled, true)]
    [InlineData(CarrierAnnouncementKind.Cancelled, true)]
    [InlineData(CarrierAnnouncementKind.Arrived, true)]
    public async Task EachCarrierAndEventCanBeIndependentlyEnabled(CarrierAnnouncementKind kind, bool squadron)
    {
        await using var fixture = new Fixture();
        fixture.Model.WebhookUrl = Webhook;
        fixture.Model.Enabled = true;
        fixture.Model.PersonalCarrier = !squadron;
        fixture.Model.SquadronCarrier = squadron;
        fixture.Model.JumpScheduled = kind == CarrierAnnouncementKind.Scheduled;
        fixture.Model.JumpCancelled = kind == CarrierAnnouncementKind.Cancelled;
        fixture.Model.JumpArrived = kind == CarrierAnnouncementKind.Arrived;
        await fixture.Model.SaveAsync();
        fixture.Model.ApplyJournalEvents("F1", [Request(1, false), Request(2, true)], true);
        fixture.Model.ApplyJournalEvents(
            "F1",
            kind == CarrierAnnouncementKind.Cancelled
                ? [Event("CarrierJumpCancelled", 1, 20), Event("CarrierJumpCancelled", 2, 20)]
                : [Event("CarrierLocation", 1, 20), Event("CarrierLocation", 2, 20)],
            true
        );
        await fixture.Model.PendingDelivery.WaitAsync(TimeSpan.FromSeconds(5));
        using var document = JsonDocument.Parse(Assert.Single(fixture.Payloads));
        string description = document.RootElement.GetProperty("embeds")[0].GetProperty("description").GetString()!;
        Assert.StartsWith(
            squadron ? "Squadron carrier" : "Personal fleet carrier",
            description,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task BootstrapBuildsPendingItineraryWithoutRepostingAndAnArrivalPostsOnce()
    {
        await using var fixture = new Fixture(new(true, Webhook));
        fixture.Model.ApplyJournalEvents("F1", [Request(1, false)], false);
        Assert.Empty(fixture.Payloads);
        fixture.Model.ApplyJournalEvents("F1", [Event("CarrierLocation", 1, 20)], true);
        await fixture.Model.PendingDelivery.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(fixture.Payloads);
        fixture.Model.ApplyJournalEvents("F1", [Event("CarrierLocation", 1, 21)], true);
        Assert.Single(fixture.Payloads);
    }

    [Fact]
    public async Task ExplicitCompletionWithoutARequestIsSilentAtBootstrapAndPublishedOnlyWhenLive()
    {
        await using var fixture = new Fixture(new(true, Webhook));
        fixture.Model.ApplyJournalEvents("F1", [Event("CarrierStats", 1, 0), Event("CarrierJump", 1, 1)], false);
        Assert.Empty(fixture.Payloads);
        fixture.Model.ApplyJournalEvents(
            "F1",
            [Parse("""{"timestamp":"2026-10-28T13:00:00Z","event":"CarrierJump","MarketID":1,"StarSystem":"Sol"}""")],
            true
        );
        await fixture.Model.PendingDelivery.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(fixture.Payloads);
    }

    [Theory]
    [InlineData(true, FollowRouteCarrierKind.Personal, "Lave", true)]
    [InlineData(false, FollowRouteCarrierKind.Personal, "Lave", false)]
    [InlineData(true, FollowRouteCarrierKind.Squadron, "Lave", false)]
    [InlineData(true, FollowRouteCarrierKind.Personal, "Other", false)]
    public async Task OnlyTheMatchingActiveCarrierItineraryIsIncluded(
        bool active,
        FollowRouteCarrierKind carrier,
        string destination,
        bool expected
    )
    {
        await using var fixture = new Fixture(new(true, Webhook));
        var route = new FollowRouteDocument(
            "F1",
            "",
            active,
            false,
            0,
            [new("Sol", null, null, null, false, false), new(destination, null, null, null, false, false)],
            CarrierKind: carrier
        );
        fixture.Model.ApplyJournalEvents("F1", [Request(1, false)], true, route: route);
        await fixture.Model.PendingDelivery.WaitAsync(TimeSpan.FromSeconds(5));
        using var document = JsonDocument.Parse(Assert.Single(fixture.Payloads));
        Assert.Equal(
            expected,
            document
                .RootElement.GetProperty("embeds")[0]
                .GetProperty("fields")
                .EnumerateArray()
                .Any(field => field.GetProperty("name").GetString() == "Route")
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowDiscordDoesNotBlockJournalAndDisablingDiscardsOlderQueuedAnnouncements(bool startReplay)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool allowed = true;
        await using var fixture = new Fixture(
            new(true, Webhook),
            async token =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            },
            () => allowed
        );
        fixture.Model.ApplyJournalEvents("F1", [Request(1, false), Request(2, true)], true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(fixture.Model.PendingDelivery.IsCompleted);
        if (startReplay)
        {
            allowed = false;
        }
        else
        {
            fixture.Model.Enabled = false;
            await fixture.Model.SaveAsync();
        }
        release.TrySetResult();
        await fixture.Model.PendingDelivery.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(fixture.Payloads);
    }

    [Fact]
    public async Task DiagnosticReplayCannotPostEvenAnExplicitTest()
    {
        await using var fixture = new Fixture(new(true, Webhook), allowed: () => false);
        Assert.False(fixture.Model.TestCommand.CanExecute(null));
        await fixture.Model.TestAsync();
        fixture.Model.ApplyJournalEvents("F1", [Request(1, false)], true);
        Assert.Empty(fixture.Payloads);
    }

    [Fact]
    public async Task ShutdownCancelsOutstandingDeliveryWithoutStoppingTheApplication()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new Fixture(
            new(true, Webhook),
            async token =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
        );
        fixture.Model.ApplyJournalEvents("F1", [Request(1, false)], true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Model.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Model.ApplyJournalEvents("F1", [Request(2, true)], true);
    }

    [Fact]
    public async Task SettingsWriteFailureIsVisibleAndDoesNotEnablePublishing()
    {
        await using var fixture = new Fixture();
        string blocked = System.IO.Path.Combine(fixture.Root, "blocked");
        await File.WriteAllTextAsync(blocked, "file");
        await using var model = new DiscordCarrierAnnouncementsViewModel(
            new DiscordCarrierSettingsStore(System.IO.Path.Combine(blocked, "ui.json")),
            dispatch: action => action()
        );
        model.Enabled = true;
        model.WebhookUrl = Webhook;
        await model.SaveAsync();
        Assert.Contains("Could not save", model.StatusMessage, StringComparison.Ordinal);
        Assert.True(model.HasChanges);
    }

    [Fact]
    public async Task ClosingDuringAnExplicitTestCancelsItAndPreventsConcurrentTests()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new Fixture(
            new(true, Webhook),
            async token =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
        );
        Task test = fixture.Model.TestAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(fixture.Model.TestCommand.CanExecute(null));
        await fixture.Model.TestAsync();
        await fixture.Model.DisposeAsync();
        await test.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Model.TestAsync();
        Assert.Empty(fixture.Payloads);
    }

    private static JournalEventEnvelope Request(long id, bool squadron) =>
        Parse(
            $$"""{"timestamp":"2026-10-28T12:00:00Z","event":"CarrierJumpRequest","CarrierID":{{id}},"CarrierType":"{{(squadron ? "SquadronCarrier" : "FleetCarrier")}}","SystemName":"Lave","DepartureTime":"2026-10-28T12:00:10Z"} """
        );

    private static JournalEventEnvelope Event(string name, long id, int second) =>
        Parse(
            $$"""{"timestamp":"2026-10-28T12:00:{{second:00}}Z","event":"{{name}}","CarrierID":{{id}},"StarSystem":"Lave"} """
        );

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out string? error), error);
        return entry!;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "discord-tests-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Root, "ui.json");
        public DiscordCarrierSettingsStore Store { get; }
        public DiscordCarrierAnnouncementsViewModel Model { get; }
        public ConcurrentQueue<string> Payloads { get; } = new();
        private readonly HttpClient http;

        public Fixture(
            DiscordCarrierPreferences? preferences = null,
            Func<CancellationToken, Task>? wait = null,
            Func<bool>? allowed = null
        )
        {
            Directory.CreateDirectory(Root);
            Store = new DiscordCarrierSettingsStore(Path);
            if (preferences is not null)
            {
                Store.Save(preferences);
            }
            http = new HttpClient(new Handler(Payloads, wait));
            Model = new DiscordCarrierAnnouncementsViewModel(
                Store,
                new DiscordCarrierWebhookClient(http),
                allowed,
                action => action()
            );
        }

        public async ValueTask DisposeAsync()
        {
            await Model.DisposeAsync();
            http.Dispose();
            Directory.Delete(Root, true);
        }
    }

    private sealed class Handler(ConcurrentQueue<string> payloads, Func<CancellationToken, Task>? wait)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (wait is not null)
            {
                await wait(cancellationToken);
            }
            payloads.Enqueue(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new(HttpStatusCode.OK);
        }
    }
}
