using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SrvSurvey.Core.Discord;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Core.Tests.Discord;

public sealed class DiscordCarrierMessageTests
{
    private const string Webhook = "https://discord.com/api/webhooks/123/test-token";
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6avQAAAAASUVORK5CYII="
    );
    private static CarrierJumpAnnouncement Scheduled =>
        new(
            CarrierAnnouncementKind.Scheduled,
            FollowRouteCarrierKind.Personal,
            42,
            "Explorer",
            "ABC-123",
            "Sol",
            "Lave",
            "Lave 1",
            DateTimeOffset.UnixEpoch.AddMinutes(15),
            DateTimeOffset.UnixEpoch,
            "Sol → Lave"
        );

    [Theory]
    [InlineData(CarrierAnnouncementKind.Scheduled, "<t:900:R>", "", 3447003)]
    [InlineData(CarrierAnnouncementKind.Cancelled, "", "", 15158332)]
    [InlineData(CarrierAnnouncementKind.Arrived, "", "<t:0:F>", 3066993)]
    public void TemplatesExpandJourneyFieldsAndOnlyScheduledJumpsHaveACountdown(
        CarrierAnnouncementKind kind,
        string countdown,
        string arrival,
        int color
    )
    {
        var template = new DiscordCarrierMessageTemplate(
            "{carrier_name} to {destination}",
            "**{carrier}**\n{carrier_kind}\n{origin} → {destination}\n{body}\n{departure}\n{countdown}\n{arrival}\n{route}",
            false
        );
        DiscordCarrierMessage message = DiscordCarrierMessageFormatter.Render(
            Scheduled with
            {
                Event = kind,
            },
            template
        );
        Assert.Equal("Explorer to Lave", message.Title);
        Assert.Equal(
            $"**Explorer · ABC-123**\nPersonal fleet carrier\nSol → Lave\nLave 1\n<t:900:F>\n{countdown}\n{arrival}\nSol → Lave",
            message.Description
        );
        Assert.Empty(message.Fields);
        Assert.Equal(color, message.Color);
    }

    [Theory]
    [InlineData(CarrierAnnouncementKind.Scheduled, "Carrier jump scheduled")]
    [InlineData(CarrierAnnouncementKind.Cancelled, "Carrier jump cancelled")]
    [InlineData(CarrierAnnouncementKind.Arrived, "Carrier jump completed")]
    public void DefaultMessagesRetainItineraryAndUseLiveDiscordRelativeTime(CarrierAnnouncementKind kind, string title)
    {
        DiscordCarrierMessage message = DiscordCarrierMessageFormatter.Render(
            Scheduled with
            {
                Event = kind,
            },
            new DiscordCarrierMessageSettings().ForEvent(kind)
        );
        Assert.Equal(title, message.Title);
        Assert.Equal("Personal fleet carrier: Explorer · ABC-123", message.Description);
        Assert.Equal("Sol → Lave", message.Fields[0].Value);
        Assert.Equal("<t:900:F>", Assert.Single(message.Fields, field => field.Name == "Scheduled departure").Value);
        Assert.Equal(kind == CarrierAnnouncementKind.Scheduled, message.Fields.Any(field => field.Name == "Countdown"));
        if (kind == CarrierAnnouncementKind.Scheduled)
        {
            Assert.Equal("<t:900:R>", Assert.Single(message.Fields, field => field.Name == "Countdown").Value);
        }
    }

    [Theory]
    [InlineData("", "Hello", "Enter a title of 1–256 characters.")]
    [InlineData("Title", "", "Enter a message of 1–4096 characters.")]
    [InlineData("{unknown}", "Hello", "Unknown field {unknown}. Choose a field from Insert field.")]
    public void InvalidTemplatesExplainHowToFixThem(string title, string body, string error) =>
        Assert.Equal(error, DiscordCarrierMessageFormatter.Validate(new(title, body)));

    [Fact]
    public void MaximumLengthTemplatesAreAcceptedAndExpandedPayloadStaysWithinDiscordLimits()
    {
        Assert.Null(DiscordCarrierMessageFormatter.Validate(new(new string('t', 256), new string('m', 4096))));
        Assert.Equal(
            "Enter a title of 1–256 characters.",
            DiscordCarrierMessageFormatter.Validate(new(new string('t', 257), "body"))
        );
        Assert.Equal(
            "Enter a message of 1–4096 characters.",
            DiscordCarrierMessageFormatter.Validate(new("title", new string('m', 4097)))
        );
        DiscordCarrierMessage message = DiscordCarrierMessageFormatter.Render(
            Scheduled with
            {
                Destination = new string('d', 5000),
                Route = new string('r', 5000),
                Body = new string('b', 5000),
            },
            new(new string('t', 250) + "{carrier}", new string('m', 4090) + "{route}")
        );
        Assert.Equal(256, message.Title.Length);
        Assert.Equal(4096, message.Description.Length);
        Assert.Equal(500, Assert.Single(message.Fields, field => field.Name == "Route").Value.Length);
        Assert.InRange(
            message.Title.Length
                + message.Description.Length
                + message.Fields.Sum(field => field.Name.Length + field.Value.Length)
                + 12,
            1,
            6000
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedCarrierImageAndCustomTemplateSurviveRateLimitRetries(bool squadron)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        await File.WriteAllBytesAsync(path, Png);
        int attempts = 0;
        try
        {
            using var handler = new Handler(async request =>
            {
                HttpContent[] parts = Assert.IsType<MultipartFormDataContent>(request.Content).ToArray();
                Assert.Equal(2, parts.Length);
                Assert.Equal("application/json", parts[0].Headers.ContentType!.MediaType);
                using var json = JsonDocument.Parse(await parts[0].ReadAsStringAsync());
                JsonElement embed = json.RootElement.GetProperty("embeds")[0];
                Assert.Equal("Custom departure", embed.GetProperty("title").GetString());
                Assert.Equal("**Lave** <t:900:R>", embed.GetProperty("description").GetString());
                Assert.Empty(embed.GetProperty("fields").EnumerateArray());
                Assert.Equal("attachment://carrier.png", embed.GetProperty("image").GetProperty("url").GetString());
                Assert.Empty(json.RootElement.GetProperty("allowed_mentions").GetProperty("parse").EnumerateArray());
                Assert.Equal(Png, await parts[1].ReadAsByteArrayAsync());
                Assert.Equal("image/png", parts[1].Headers.ContentType!.MediaType);
                return ++attempts == 1
                    ? new(HttpStatusCode.TooManyRequests) { Content = JsonContent.Create(new { retry_after = 0 }) }
                    : new(HttpStatusCode.OK);
            });
            using var http = new HttpClient(handler);
            using var client = new DiscordCarrierWebhookClient(http, (_, _) => Task.CompletedTask);
            var settings = new DiscordCarrierMessageSettings(
                Scheduled: new("Custom departure", "**{destination}** {countdown}", false),
                PersonalImagePath: squadron ? "missing-personal.png" : path,
                SquadronImagePath: squadron ? path : "missing-squadron.png"
            );
            DiscordWebhookResult result = await client.SendAnnouncementAsync(
                Webhook,
                Scheduled with
                {
                    CarrierKind = squadron ? FollowRouteCarrierKind.Squadron : FollowRouteCarrierKind.Personal,
                },
                settings
            );
            Assert.Equal(new DiscordWebhookResult(true, "Message sent to Discord."), result);
            Assert.Equal(2, attempts);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MissingImageStillDeliversAnnouncementWithAnActionableStatus()
    {
        string? delivered = null;
        using var handler = new Handler(async request =>
        {
            delivered = await request.Content!.ReadAsStringAsync();
            using var payload = JsonDocument.Parse(delivered);
            Assert.False(payload.RootElement.GetProperty("embeds")[0].TryGetProperty("image", out _));
            return new(HttpStatusCode.OK);
        });
        using var http = new HttpClient(handler);
        using var client = new DiscordCarrierWebhookClient(http);
        DiscordWebhookResult result = await client.SendAnnouncementAsync(
            Webhook,
            Scheduled,
            new DiscordCarrierMessageSettings(
                PersonalImagePath: Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png")
            )
        );
        Assert.True(result.Success);
        using var deliveredPayload = JsonDocument.Parse(Assert.IsType<string>(delivered));
        Assert.Equal(
            "Carrier jump scheduled",
            deliveredPayload.RootElement.GetProperty("embeds")[0].GetProperty("title").GetString()
        );
        Assert.Equal(
            "Message sent to Discord without its image. Choose the carrier image again in the message editor.",
            result.Message
        );
        Assert.Equal(
            new DiscordWebhookResult(false, "Enter a title of 1–256 characters."),
            await client.SendAnnouncementAsync(
                Webhook,
                Scheduled,
                new DiscordCarrierMessageSettings(Scheduled: new("", "text"))
            )
        );
    }

    [Fact]
    public async Task ImageLoaderAcceptsPngAndJpegAndRejectsInvalidOrOversizedFiles()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".image");
        try
        {
            Assert.Null(await DiscordCarrierImage.LoadAsync(""));
            await File.WriteAllBytesAsync(path, Png);
            Assert.Equal("carrier.png", (await DiscordCarrierImage.LoadAsync(path))!.FileName);
            await File.WriteAllBytesAsync(path, new byte[] { 255, 216, 255, 224, 0, 0, 0, 0 });
            Assert.Equal("image/jpeg", (await DiscordCarrierImage.LoadAsync(path))!.MediaType);
            await File.WriteAllTextAsync(path, "not an image");
            await Assert.ThrowsAsync<InvalidDataException>(() => DiscordCarrierImage.LoadAsync(path));
            await File.WriteAllBytesAsync(path, new byte[8 * 1024 * 1024 + 1]);
            await Assert.ThrowsAsync<InvalidDataException>(() => DiscordCarrierImage.LoadAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("invalid-file")]
    [InlineData("oversized-file")]
    [InlineData("invalid-crop")]
    [InlineData("failed-crop")]
    [InlineData("missing-encoder")]
    public async Task ImageFailuresSendTextWithoutExposingTheUncroppedOriginal(string failure)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        byte[] bytes = failure switch
        {
            "invalid-file" => [0, 1, 2],
            "oversized-file" => new byte[8 * 1024 * 1024 + 1],
            _ => Png,
        };
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            using var handler = new Handler(async request =>
            {
                Assert.IsNotType<MultipartFormDataContent>(request.Content);
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                JsonElement embed = payload.RootElement.GetProperty("embeds")[0];
                Assert.Equal("Carrier jump scheduled", embed.GetProperty("title").GetString());
                Assert.False(embed.TryGetProperty("image", out _));
                return new(HttpStatusCode.OK);
            });
            using var http = new HttpClient(handler);
            using var client = new DiscordCarrierWebhookClient(
                http,
                imageCropper: failure == "missing-encoder"
                    ? null
                    : (_, _) =>
                        throw failure switch
                        {
                            "invalid-crop" => new InvalidDataException("Invalid image"),
                            _ => new IOException("Encoder failed"),
                        }
            );
            DiscordWebhookResult result = await client.SendAnnouncementAsync(
                Webhook,
                Scheduled,
                new DiscordCarrierMessageSettings(PersonalImagePath: path, PersonalImageCrop: new())
            );
            Assert.Equal(
                new DiscordWebhookResult(
                    true,
                    "Message sent to Discord without its image. Choose the carrier image again in the message editor."
                ),
                result
            );
        }
        finally
        {
            File.Delete(path);
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
