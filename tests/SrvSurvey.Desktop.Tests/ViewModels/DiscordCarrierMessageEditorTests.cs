using System.Net;
using System.Text.Json;
using SrvSurvey.Core.Discord;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class DiscordCarrierMessageEditorTests
{
    [Fact]
    public async Task EventDraftsAndCarrierImagesOnlyPersistAfterApplyingAndSavingSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), "carrier-editor-" + Guid.NewGuid());
        string path = Path.Combine(root, "settings.json");
        var store = new DiscordCarrierSettingsStore(path);
        await using var announcements = new DiscordCarrierAnnouncementsViewModel(store, dispatch: action => action());
        try
        {
            var editor = new DiscordCarrierMessageEditorViewModel(
                announcements.MessageSettings,
                previewTime: DateTimeOffset.UnixEpoch
            );
            Assert.Equal("No carrier image selected", editor.ImageName);
            Assert.Equal("Carrier jump scheduled", editor.Title);
            Assert.Equal("Personal fleet carrier: Your carrier · ABC-123", editor.Preview.Description);
            editor.Title = "**Attention**";
            editor.Message = "**{carrier}** leaves {origin} for {destination} {countdown}";
            editor.IncludeDetails = false;
            editor.ImagePath = "personal.png";
            editor.SelectedCarrier = 1;
            editor.ImagePath = "squadron.jpg";
            Assert.Equal("squadron.jpg", editor.ImageName);
            Assert.Equal("**Your carrier · ABC-123** leaves Sol for Lave <t:900:R>", editor.Preview.Description);
            Assert.Empty(editor.Preview.Fields);
            editor.SelectedEvent = 1;
            editor.Title = "Cancelled";
            editor.Message = "Staying in {origin}";
            editor.SelectedEvent = 2;
            editor.Title = "Arrived";
            editor.Message = "Dock at {body}";
            Assert.Equal("Dock at Lave 1", editor.Preview.Description);
            editor.SelectedEvent = 0;
            Assert.Equal("**Attention**", editor.Title);
            Assert.Equal("59 / 4096 characters", editor.CharacterCount);
            Assert.False(announcements.HasChanges);
            Assert.Null(store.Load().MessageSettings);
            announcements.ApplyMessageSettings(editor.Settings);
            Assert.True(announcements.HasChanges);
            Assert.Null(store.Load().MessageSettings);
            await announcements.SaveAsync();
            Assert.Equal(editor.Settings, store.Load().MessageSettings);
            Assert.False(announcements.HasChanges);
            editor.RestoreDefault();
            Assert.Equal("Carrier jump scheduled", editor.Title);
            Assert.True(editor.IncludeDetails);
            Assert.Equal("squadron.jpg", editor.ImagePath);
            editor.SelectedCarrier = 0;
            Assert.Equal("personal.png", editor.ImagePath);
            Assert.Equal("**Attention**", store.Load().MessageSettings!.Scheduled!.Title);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LiveDeliveryUsesTheSavedTemplateRatherThanUnappliedDrafts()
    {
        string root = Path.Combine(Path.GetTempPath(), "carrier-delivery-" + Guid.NewGuid());
        var store = new DiscordCarrierSettingsStore(Path.Combine(root, "settings.json"));
        store.Save(new(true, "https://discord.com/api/webhooks/123/test-token"));
        string? sentTitle = null;
        using var handler = new Handler(async request =>
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            sentTitle = payload.RootElement.GetProperty("embeds")[0].GetProperty("title").GetString();
            return new(HttpStatusCode.OK);
        });
        using var http = new HttpClient(handler);
        await using var announcements = new DiscordCarrierAnnouncementsViewModel(
            store,
            new DiscordCarrierWebhookClient(http),
            dispatch: action => action()
        );
        try
        {
            announcements.ApplyMessageSettings(
                new(Scheduled: new("Custom departure to {destination}", "Board now {countdown}"))
            );
            Assert.True(
                JournalEventEnvelope.TryParse(
                    """{"timestamp":"2026-10-28T12:00:00Z","event":"CarrierJumpRequest","CarrierID":1,"SystemName":"Lave","DepartureTime":"2026-10-28T12:15:00Z"}""",
                    out JournalEventEnvelope? first,
                    out _
                )
            );
            announcements.ApplyJournalEvents("F1", [first!], true);
            await announcements.PendingDelivery.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Carrier jump scheduled", sentTitle);
            await announcements.SaveAsync();
            Assert.True(
                JournalEventEnvelope.TryParse(
                    """{"timestamp":"2026-10-28T13:00:00Z","event":"CarrierJumpRequest","CarrierID":1,"SystemName":"Colonia","DepartureTime":"2026-10-28T13:15:00Z"}""",
                    out JournalEventEnvelope? second,
                    out _
                )
            );
            announcements.ApplyJournalEvents("F1", [second!], true);
            await announcements.PendingDelivery.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Custom departure to Colonia", sentTitle);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PreviewUsesTheSelectedCarriersOwnIdentityAndLocation()
    {
        var personal = new FrontierCarrierSnapshot(
            "ABC-123",
            "Explorer",
            "Sol",
            "",
            "",
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            [],
            [],
            [],
            [],
            [],
            []
        );
        FrontierCarrierSnapshot squadron = personal with
        {
            Callsign = "XYZ-789",
            Name = "Squadron flagship",
            System = "Colonia",
        };
        var account = new FrontierAccountSnapshot(
            "Commander",
            0,
            0,
            false,
            true,
            "Sol",
            "",
            null,
            [],
            [],
            [],
            personal,
            DateTimeOffset.UnixEpoch,
            SquadronCarrier: squadron
        );
        var editor = new DiscordCarrierMessageEditorViewModel(new(), account, DateTimeOffset.UnixEpoch);
        Assert.Equal("Personal fleet carrier: Explorer · ABC-123", editor.Preview.Description);
        Assert.Equal("Sol → Lave", editor.Preview.Fields[0].Value);
        editor.SelectedCarrier = 1;
        Assert.Equal("Squadron carrier: Squadron flagship · XYZ-789", editor.Preview.Description);
        Assert.Equal("Colonia → Lave", editor.Preview.Fields[0].Value);
    }

    [Fact]
    public void NullValuesInOlderSettingsUseDefaultsInTheEditor()
    {
        DiscordCarrierMessageSettings settings = JsonSerializer.Deserialize<DiscordCarrierMessageSettings>(
            """{"Scheduled":{"Title":null,"Message":null},"PersonalImagePath":null}"""
        )!;
        var editor = new DiscordCarrierMessageEditorViewModel(settings, previewTime: DateTimeOffset.UnixEpoch);
        Assert.Equal("Carrier jump scheduled", editor.Title);
        Assert.Equal("{carrier_kind}: {carrier}", editor.Message);
        Assert.Equal("", editor.ImagePath);
        Assert.True(editor.CanApply);
    }

    [Fact]
    public void InvalidHiddenEventPreventsApplyUntilItIsFixed()
    {
        var editor = new DiscordCarrierMessageEditorViewModel(new(), previewTime: DateTimeOffset.UnixEpoch);
        editor.SelectedEvent = 1;
        editor.Message = "{unknown}";
        editor.SelectedEvent = 0;
        Assert.False(editor.CanApply);
        Assert.Equal("Unknown field {unknown}. Choose a field from Insert field.", editor.ValidationMessage);
        editor.SelectedEvent = 1;
        editor.RestoreDefault();
        Assert.True(editor.CanApply);
        editor.Title = "";
        Assert.False(editor.CanApply);
        editor.RestoreDefault();
        editor.Message = "";
        Assert.False(editor.CanApply);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavingSettingsDuringImagePreparationSkipsTheObsoleteSendAndContinuesDelivery(bool changeWebhook)
    {
        string root = Path.Combine(Path.GetTempPath(), "carrier-settings-race-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "carrier.png");
        await File.WriteAllBytesAsync(
            path,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6avQAAAAASUVORK5CYII="
            )
        );
        var store = new DiscordCarrierSettingsStore(Path.Combine(root, "settings.json"));
        store.Save(
            new(
                true,
                "https://discord.com/api/webhooks/123/test-token",
                MessageSettings: new(PersonalImagePath: path, PersonalImageCrop: new())
            )
        );
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var sent = new List<string>();
        using var handler = new Handler(request =>
        {
            sent.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var http = new HttpClient(handler);
        await using var announcements = new DiscordCarrierAnnouncementsViewModel(
            store,
            new DiscordCarrierWebhookClient(
                http,
                imageCropper: (image, _) =>
                {
                    preparing.TrySetResult();
                    Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                    return image;
                }
            ),
            dispatch: action => action()
        );
        try
        {
            Assert.True(
                JournalEventEnvelope.TryParse(
                    """{"timestamp":"2026-11-14T12:00:00Z","event":"CarrierJumpRequest","CarrierID":1,"SystemName":"Lave","DepartureTime":"2026-11-14T12:15:00Z"}""",
                    out JournalEventEnvelope? first,
                    out _
                )
            );
            announcements.ApplyJournalEvents("F1", [first!], true);
            await preparing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (changeWebhook)
            {
                announcements.WebhookUrl = "https://discord.com/api/webhooks/456/new-token";
            }
            else
            {
                announcements.Enabled = false;
            }
            await announcements.SaveAsync();
            release.Set();
            await announcements.PendingDelivery.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(sent);
            announcements.Enabled = true;
            announcements.WebhookUrl = "https://discord.com/api/webhooks/456/new-token";
            announcements.ApplyMessageSettings(new());
            await announcements.SaveAsync();
            Assert.True(
                JournalEventEnvelope.TryParse(
                    """{"timestamp":"2026-11-14T13:00:00Z","event":"CarrierJumpRequest","CarrierID":1,"SystemName":"Colonia","DepartureTime":"2026-11-14T13:15:00Z"}""",
                    out JournalEventEnvelope? second,
                    out _
                )
            );
            announcements.ApplyJournalEvents("F1", [second!], true);
            await announcements.PendingDelivery.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("/api/webhooks/456/new-token", Assert.Single(sent));
        }
        finally
        {
            release.Set();
            await announcements.DisposeAsync();
            Directory.Delete(root, true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => send(request);
    }
}
