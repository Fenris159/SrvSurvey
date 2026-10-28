using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SrvSurvey.Core.Discord;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Core.Tests.Discord;

public sealed class DiscordCarrierWebhookClientTests
{
    private const string Webhook = "https://discord.com/api/webhooks/123456/sample-token";

    [Theory]
    [InlineData("https://discord.com/api/webhooks/123/token", true)]
    [InlineData(" https://discordapp.com/api/v10/webhooks/123/token_-/?wait=false&thread_id=456 ", true)]
    [InlineData("https://canary.discord.com/api/webhooks/123/token", true)]
    [InlineData("https://ptb.discord.com/api/webhooks/123/token", true)]
    [InlineData("http://discord.com/api/webhooks/123/token", false)]
    [InlineData("https://discord.com.evil.test/api/webhooks/123/token", false)]
    [InlineData("https://discord.com:444/api/webhooks/123/token", false)]
    [InlineData("https://user@discord.com/api/webhooks/123/token", false)]
    [InlineData("https://discord.com/api/webhooks/123/token#fragment", false)]
    [InlineData("https://discord.com/api/webhooks/abc/token", false)]
    [InlineData("https://discord.com/api/webhooks/123/token%20value", false)]
    [InlineData("https://discord.com/api/webhooks/123/token?thread_id=abc", false)]
    [InlineData("https://discord.com/api/webhooks/123/token?other=456", false)]
    [InlineData("https://discord.com/api/webhooks/123/token?wait", false)]
    [InlineData("https://discord.com/api/webhooks/123", false)]
    [InlineData("https://discord.com/", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyDiscordExecutionUrlsAreAccepted(string? text, bool valid)
    {
        Uri? parsed = DiscordCarrierWebhookClient.ParseWebhookUrl(text);
        Assert.Equal(valid, parsed is not null);
        if (parsed is not null)
        {
            Assert.Contains("wait=true", parsed.Query, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(CarrierAnnouncementKind.Scheduled, FollowRouteCarrierKind.Personal, "Carrier jump scheduled")]
    [InlineData(CarrierAnnouncementKind.Cancelled, FollowRouteCarrierKind.Squadron, "Carrier jump cancelled")]
    [InlineData(CarrierAnnouncementKind.Arrived, FollowRouteCarrierKind.Personal, "Carrier jump completed")]
    public async Task SendsCarrierIdentityItineraryAndConfirmedTimesWithoutMentionPings(
        CarrierAnnouncementKind kind,
        FollowRouteCarrierKind carrier,
        string title
    )
    {
        string? json = null;
        using var handler = new Handler(
            async (request, token) =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Contains("wait=true", request.RequestUri!.Query, StringComparison.Ordinal);
                json = await request.Content!.ReadAsStringAsync(token);
                return new(HttpStatusCode.OK);
            }
        );
        using var http = new HttpClient(handler);
        using var client = new DiscordCarrierWebhookClient(http);
        var message = new CarrierJumpAnnouncement(
            kind,
            carrier,
            42,
            "Explorer @everyone",
            "ABC-123",
            "Sol",
            "Lave",
            "Lave 1",
            DateTimeOffset.UnixEpoch.AddMinutes(15),
            DateTimeOffset.UnixEpoch,
            "A → B → C"
        );
        Assert.True((await client.SendAnnouncementAsync(Webhook, message)).Success);
        using var payload = JsonDocument.Parse(json!);
        Assert.Empty(payload.RootElement.GetProperty("allowed_mentions").GetProperty("parse").EnumerateArray());
        JsonElement embed = payload.RootElement.GetProperty("embeds")[0];
        Assert.Equal(title, embed.GetProperty("title").GetString());
        Assert.Contains("ABC-123", embed.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains(
            embed.GetProperty("fields").EnumerateArray(),
            field => field.GetProperty("value").GetString() == "Sol → Lave"
        );
        Assert.Contains(
            embed.GetProperty("fields").EnumerateArray(),
            field => field.GetProperty("name").GetString() == "Destination body"
        );
        Assert.Equal(
            kind == CarrierAnnouncementKind.Arrived,
            embed
                .GetProperty("fields")
                .EnumerateArray()
                .Any(field => field.GetProperty("name").GetString() == "Arrival confirmed")
        );
    }

    [Fact]
    public async Task OptionalFieldsAreOmittedAndLongFieldsStayWithinDiscordLimits()
    {
        string? json = null;
        using var handler = new Handler(
            async (request, token) =>
            {
                json = await request.Content!.ReadAsStringAsync(token);
                return new(HttpStatusCode.NoContent);
            }
        );
        using var http = new HttpClient(handler);
        using var client = new DiscordCarrierWebhookClient(http);
        var minimal = new CarrierJumpAnnouncement(
            CarrierAnnouncementKind.Scheduled,
            FollowRouteCarrierKind.Personal,
            42,
            "",
            "",
            "",
            "Lave",
            "",
            null,
            DateTimeOffset.UnixEpoch
        );
        Assert.True((await client.SendAnnouncementAsync(Webhook, minimal)).Success);
        using (var payload = JsonDocument.Parse(json!))
        {
            JsonElement embed = payload.RootElement.GetProperty("embeds")[0];
            Assert.Contains("42", embed.GetProperty("description").GetString(), StringComparison.Ordinal);
            Assert.Single(embed.GetProperty("fields").EnumerateArray());
        }
        Assert.True(
            (
                await client.SendAnnouncementAsync(
                    Webhook,
                    minimal with
                    {
                        Name = new string('x', 2000),
                        Body = new string('y', 2000),
                        Route = new string('z', 2000),
                    }
                )
            ).Success
        );
        using var longPayload = JsonDocument.Parse(json!);
        Assert.True(
            longPayload.RootElement.GetProperty("embeds")[0].GetProperty("description").GetString()!.Length <= 500
        );
        Assert.All(
            longPayload.RootElement.GetProperty("embeds")[0].GetProperty("fields").EnumerateArray(),
            field => Assert.True(field.GetProperty("value").GetString()!.Length <= 500)
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RetriesAtDiscordRequestedRateLimitAndPreservesMessage(bool useHeader)
    {
        int attempts = 0;
        var waits = new List<TimeSpan>();
        using var handler = new Handler(
            (_, _) =>
            {
                attempts++;
                var response = new HttpResponseMessage(
                    attempts == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK
                )
                {
                    Content = new StringContent("{\"retry_after\":0.25}"),
                };
                if (useHeader)
                {
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
                }
                return Task.FromResult(response);
            }
        );
        using var http = new HttpClient(handler);
        using var client = new DiscordCarrierWebhookClient(
            http,
            (duration, _) =>
            {
                waits.Add(duration);
                return Task.CompletedTask;
            }
        );
        Assert.True((await client.SendTestAsync(Webhook)).Success);
        Assert.Equal(2, attempts);
        Assert.Equal(TimeSpan.FromSeconds(useHeader ? 2 : 0.25), Assert.Single(waits));
    }

    [Theory]
    [InlineData(404, "deleted")]
    [InlineData(401, "deleted")]
    [InlineData(403, "denied")]
    [InlineData(400, "could not accept")]
    [InlineData(302, "could not accept")]
    [InlineData(500, "could not accept")]
    public async Task DiscordFailuresAreReportedWithoutReturningSensitiveResponseBodies(int status, string expected)
    {
        using var handler = new Handler(
            (_, _) =>
                Task.FromResult(
                    new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(Webhook) }
                )
        );
        using var http = new HttpClient(handler);
        using var client = new DiscordCarrierWebhookClient(http);
        DiscordWebhookResult result = await client.SendTestAsync(Webhook);
        Assert.False(result.Success);
        Assert.Contains(expected, result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sample-token", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"retry_after\":60}")]
    [InlineData("{\"retry_after\":0}")]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{\"retry_after\":\"wrong type\"}")]
    [InlineData("{\"retry_after\":-1}")]
    public async Task RateLimitRetriesAreBoundedAndMalformedBodiesDoNotEscape(string body)
    {
        int attempts = 0;
        using var handler = new Handler(
            (_, _) =>
            {
                attempts++;
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent(body) }
                );
            }
        );
        using var http = new HttpClient(handler);
        using var client = new DiscordCarrierWebhookClient(http, (_, _) => Task.CompletedTask);
        Assert.False((await client.SendTestAsync(Webhook)).Success);
        Assert.InRange(attempts, 1, 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectionAndTimeoutErrorsNeverExposeTheWebhook(bool timeout)
    {
        using var handler = new Handler(
            (_, _) => throw (timeout ? new OperationCanceledException(Webhook) : new HttpRequestException(Webhook))
        );
        using var http = new HttpClient(handler);
        using var client = new DiscordCarrierWebhookClient(http);
        DiscordWebhookResult result = await client.SendTestAsync(Webhook);
        Assert.False(result.Success);
        Assert.DoesNotContain("sample-token", result.Message, StringComparison.Ordinal);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendTestAsync(Webhook, cancelled.Token));
    }

    [Fact]
    public async Task InvalidUrlNeverSendsAndOwnedClientDisposesCleanly()
    {
        using var client = new DiscordCarrierWebhookClient();
        Assert.False((await client.SendTestAsync("not-a-webhook")).Success);
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SendAnnouncementAsync(Webhook, null!));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    public async Task SuccessBodiesAreNotReadAndRateLimitBodiesHaveAnExplicitSizeLimit(
        HttpStatusCode status,
        bool success
    )
    {
        using var handler = new Handler(
            (_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new OversizedContent() })
        );
        using var http = new HttpClient(handler);
        using var client = new DiscordCarrierWebhookClient(http);
        Assert.Equal(success, (await client.SendTestAsync(Webhook)).Success);
    }

    private sealed class OversizedContent : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 64 * 1024;
            return true;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("The response body should not be read.");
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => send(request, cancellationToken);
    }
}
