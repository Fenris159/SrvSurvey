using System.Net;
using System.Net.Http.Json;
using SrvSurvey.Core.Discord;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Core.Tests.Discord;

public sealed class DiscordCarrierDeliveryDiagnosticsTests
{
    private const string Webhook = "https://discord.com/api/webhooks/123/test-token";
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6avQAAAAASUVORK5CYII="
    );

    [Theory]
    [InlineData(true, true, "Message sent to Discord.")]
    [InlineData(
        false,
        true,
        "Discord accepted the message, but its confirmation has a different title. See Diagnostics → Logs."
    )]
    [InlineData(
        true,
        false,
        "Discord accepted the message, but its confirmation has no carrier image. See Diagnostics → Logs."
    )]
    [InlineData(
        false,
        false,
        "Discord accepted the message, but its confirmation has a different title and no carrier image. See Diagnostics → Logs."
    )]
    public async Task ConfirmationReportsMissingImageAndChangedTitleWithoutPostingAgain(
        bool matchingTitle,
        bool returnedImage,
        string expectedStatus
    )
    {
        string path = Path.Combine(Path.GetTempPath(), "discord-confirmation-" + Guid.NewGuid() + ".png");
        await File.WriteAllBytesAsync(path, Png);
        int posts = 0;
        var log = new List<string>();
        var sendId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        using var handler = new Handler(_ =>
        {
            posts++;
            var embed = new Dictionary<string, object>
            {
                ["title"] = matchingTitle ? "Departure to Lave" : "Carrier jump scheduled",
            };
            if (returnedImage)
            {
                embed["image"] = new
                {
                    url = "https://cdn.discordapp.com/attachments/123/456/carrier.png?signature=private",
                };
            }
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    new
                    {
                        id = "456",
                        embeds = new[] { embed },
                        attachments = Array.Empty<object>(),
                    }
                ),
            };
        });
        using var http = new HttpClient(handler);
        using var client = new DiscordCarrierWebhookClient(http, log: log.Add);
        try
        {
            DiscordWebhookResult result = await client.SendAnnouncementAsync(
                Webhook,
                Announcement,
                new DiscordCarrierMessageSettings(
                    Scheduled: new("Departure to {destination}", "Board **now**"),
                    PersonalImagePath: path
                ),
                null,
                diagnosticId: sendId
            );
            Assert.True(result.Success);
            Assert.Equal(expectedStatus, result.Message);
            Assert.Equal(1, posts);
            Assert.All(
                log,
                entry => Assert.StartsWith("[Discord carrier] send=11111111222233334444555555555555 ", entry)
            );
            Assert.Contains(
                log,
                entry =>
                    entry.Contains($"confirmed; message-id=456; title-match={matchingTitle};", StringComparison.Ordinal)
                    && entry.Contains($"image-expected=True; image-returned={returnedImage}", StringComparison.Ordinal)
            );
            Assert.DoesNotContain(
                log,
                entry =>
                    entry.Contains("test-token", StringComparison.Ordinal)
                    || entry.Contains("signature=private", StringComparison.Ordinal)
                    || entry.Contains("Departure to Lave", StringComparison.Ordinal)
                    || entry.Contains("Carrier jump scheduled", StringComparison.Ordinal)
                    || entry.Contains("Board", StringComparison.Ordinal)
                    || entry.Contains(path, StringComparison.Ordinal)
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(
        400,
        40005,
        "Discord rejected the request because it is too large (error 40005). See Diagnostics → Logs."
    )]
    [InlineData(
        400,
        50045,
        "Discord rejected the carrier image because it is too large (error 50045). See Diagnostics → Logs."
    )]
    [InlineData(
        400,
        50046,
        "Discord rejected the carrier image as invalid (error 50046). Choose a PNG or JPEG image again."
    )]
    [InlineData(400, 50035, "Discord rejected the message format (error 50035). See Diagnostics → Logs.")]
    [InlineData(413, 0, "Discord rejected the upload because it is too large. See Diagnostics → Logs.")]
    [InlineData(400, 99999, "Discord could not accept the message. Check the webhook and try again.")]
    public async Task RejectionsExposeKnownCodesWithoutEchoingPrivateResponseText(int status, int code, string expected)
    {
        var log = new List<string>();
        using var handler = new Handler(_ =>
            new((HttpStatusCode)status)
            {
                Content = JsonContent.Create(
                    new
                    {
                        code,
                        message = Webhook,
                        errors = new { private_response_text = "secret-file-path" },
                    }
                ),
            }
        );
        using var http = new HttpClient(handler);
        using var client = new DiscordCarrierWebhookClient(http, log: log.Add);
        DiscordWebhookResult result = await client.SendTestAsync(Webhook);
        Assert.False(result.Success);
        Assert.Equal(expected, result.Message);
        Assert.Contains(
            log,
            entry => entry.Contains($"rejected; http={status}; discord-code={code}", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            log,
            entry =>
                entry.Contains("test-token", StringComparison.Ordinal)
                || entry.Contains("secret-file-path", StringComparison.Ordinal)
                || entry.Contains("private_response_text", StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"id\":7}")]
    [InlineData("{\"id\":\"private-token\"}")]
    [InlineData("""{"id":"\uD800"}""")]
    [InlineData("""{"id":"456","embeds":[{"title":"\uD800"}]}""")]
    [InlineData("""{"id":"456","embeds":[{"title":"Carrier announcements connected","image":{"url":"\uD800"}}]}""")]
    public async Task UnreadableConfirmationsRemainAcceptedWithoutAnotherPost(string json)
    {
        var log = new List<string>();
        int posts = 0;
        using var handler = new Handler(_ =>
        {
            posts++;
            return new(HttpStatusCode.OK) { Content = new StringContent(json) };
        });
        using var http = new HttpClient(handler);
        using var client = new DiscordCarrierWebhookClient(http, log: log.Add);
        DiscordWebhookResult result = await client.SendTestAsync(Webhook);
        Assert.Equal(
            new DiscordWebhookResult(
                true,
                "Message sent to Discord, but its confirmation could not be read. See Diagnostics → Logs."
            ),
            result
        );
        Assert.Equal(1, posts);
        Assert.Contains(log, entry => entry.Contains("confirmation-unavailable;", StringComparison.Ordinal));
        Assert.DoesNotContain(log, entry => entry.Contains("private-token", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(65536, "Message sent to Discord.")]
    [InlineData(65537, "Message sent to Discord, but its confirmation could not be read. See Diagnostics → Logs.")]
    public async Task ConfirmationSizeIsBoundedWithoutLosingTheAcceptedResult(int bytes, string expected)
    {
        const string json = """{"id":"456","embeds":[{"title":"Carrier announcements connected"}]}""";
        using var handler = new Handler(_ =>
            new(HttpStatusCode.OK) { Content = new StringContent(json.PadRight(bytes)) }
        );
        using var http = new HttpClient(handler);
        using var client = new DiscordCarrierWebhookClient(http);
        Assert.Equal(new DiscordWebhookResult(true, expected), await client.SendTestAsync(Webhook));
    }

    [Theory]
    [InlineData("io")]
    [InlineData("http")]
    [InlineData("cancelled")]
    public async Task ConfirmationReadFailuresRemainAcceptedAndPrivate(string failure)
    {
        Exception error = failure switch
        {
            "io" => new IOException(Webhook),
            "http" => new HttpRequestException(Webhook),
            _ => new OperationCanceledException(Webhook),
        };
        int posts = 0;
        var log = new List<string>();
        using var handler = new Handler(_ =>
        {
            posts++;
            return new(HttpStatusCode.OK) { Content = new UnreadableContent(error) };
        });
        using var http = new HttpClient(handler);
        using var client = new DiscordCarrierWebhookClient(http, log: log.Add);
        Assert.Equal(
            new DiscordWebhookResult(
                true,
                "Message sent to Discord, but its confirmation could not be read. See Diagnostics → Logs."
            ),
            await client.SendTestAsync(Webhook)
        );
        Assert.Equal(1, posts);
        Assert.Contains(log, entry => entry.Contains("confirmation-unavailable; error=", StringComparison.Ordinal));
        Assert.DoesNotContain(log, entry => entry.Contains("test-token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishingRevokedDuringDiagnosticLoggingPreventsThePost()
    {
        bool allowed = true;
        int posts = 0;
        using var handler = new Handler(_ =>
        {
            posts++;
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    new { id = "456", embeds = new[] { new { title = "Carrier jump scheduled" } } }
                ),
            };
        });
        using var http = new HttpClient(handler);
        using var client = new DiscordCarrierWebhookClient(
            http,
            log: entry =>
            {
                if (entry.Contains(" posting;", StringComparison.Ordinal))
                {
                    allowed = false;
                }
            }
        );
        DiscordWebhookResult result = await client.SendAnnouncementAsync(Webhook, Announcement, new(), () => allowed);
        Assert.Equal(
            new DiscordWebhookResult(false, "This announcement was skipped because its settings changed."),
            result
        );
        Assert.Equal(0, posts);
    }

    [Fact]
    public async Task DiagnosticWriterFailuresDoNotPreventDelivery()
    {
        int posts = 0;
        using var handler = new Handler(_ =>
        {
            posts++;
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    new { id = "456", embeds = new[] { new { title = "Carrier announcements connected" } } }
                ),
            };
        });
        using var http = new HttpClient(handler);
        using var client = new DiscordCarrierWebhookClient(http, log: _ => throw new IOException(Webhook));
        Assert.Equal(new DiscordWebhookResult(true, "Message sent to Discord."), await client.SendTestAsync(Webhook));
        Assert.Equal(1, posts);
    }

    private static CarrierJumpAnnouncement Announcement =>
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
            ""
        );

    private sealed class UnreadableContent(Exception error) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(error);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(send(request));
    }
}
