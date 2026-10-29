using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Core.Discord;

public sealed record DiscordWebhookResult(bool Success, string Message);

/// <summary>Sends bounded Discord embeds without leaking webhook tokens through errors or following redirects.</summary>
public sealed class DiscordCarrierWebhookClient : IDisposable
{
    private const int MaximumResponseBytes = 16 * 1024;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    public DiscordCarrierWebhookClient(HttpClient? client = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        ownsClient = client is null;
        this.delay = delay ?? Task.Delay;
    }

    public static Uri? ParseWebhookUrl(string? text)
    {
        if (
            !Uri.TryCreate(text?.Trim(), UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0
            || uri.Fragment.Length > 0
            || uri.Host is not ("discord.com" or "discordapp.com" or "canary.discord.com" or "ptb.discord.com")
        )
        {
            return null;
        }
        string[] path = uri.AbsolutePath.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        int offset = path.Length == 5 && path[1] == "v10" ? 1 : 0;
        if (
            path.Length != 4 + offset
            || path[0] != "api"
            || path[1 + offset] != "webhooks"
            || !IsSnowflake(path[2 + offset])
            || path[3 + offset].Length is < 1 or > 256
            || !path[3 + offset].All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
        )
        {
            return null;
        }
        string? query = CreateExecutionQuery(uri);
        return query is null ? null : new UriBuilder(uri) { Query = query }.Uri;
    }

    private static string? CreateExecutionQuery(Uri uri)
    {
        var query = new List<string> { "wait=true" };
        foreach (string item in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = item.Split('=', 2);
            if (pair.Length != 2 || (pair[0] != "wait" && (pair[0] != "thread_id" || !IsSnowflake(pair[1]))))
            {
                return null;
            }
            if (pair[0] == "thread_id")
            {
                query.Add("thread_id=" + pair[1]);
            }
        }
        return string.Join('&', query);
    }

    private static bool IsSnowflake(string value) => value.Length is > 0 and <= 20 && value.All(char.IsAsciiDigit);

    public Task<DiscordWebhookResult> SendTestAsync(string url, CancellationToken cancellationToken = default) =>
        SendAsync(
            url,
            new
            {
                allowed_mentions = new { parse = Array.Empty<string>() },
                embeds = new[]
                {
                    new
                    {
                        title = "Carrier announcements connected",
                        description = "SrvSurvey will post selected personal and squadron carrier jump announcements to this channel.",
                        color = 3447003,
                    },
                },
            },
            cancellationToken
        );

    public Task<DiscordWebhookResult> SendAnnouncementAsync(
        string url,
        CarrierJumpAnnouncement announcement,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(announcement);
        var fields = new List<object>
        {
            Field(
                "Itinerary",
                $"{(string.IsNullOrWhiteSpace(announcement.Origin) ? "Departure system not reported" : announcement.Origin)} → {announcement.Destination}",
                false
            ),
        };
        if (!string.IsNullOrWhiteSpace(announcement.Body))
        {
            fields.Add(Field("Destination body", announcement.Body));
        }
        if (announcement.Departure is { } departure)
        {
            fields.Add(Field("Scheduled departure", DiscordTime(departure)));
        }
        if (announcement.Event == CarrierAnnouncementKind.Arrived)
        {
            fields.Add(Field("Arrival confirmed", DiscordTime(announcement.ObservedAt)));
        }
        if (!string.IsNullOrWhiteSpace(announcement.Route))
        {
            fields.Add(Field("Route", announcement.Route, false));
        }
        string title = announcement.Event switch
        {
            CarrierAnnouncementKind.Scheduled => "Carrier jump scheduled",
            CarrierAnnouncementKind.Cancelled => "Carrier jump cancelled",
            _ => "Carrier jump completed",
        };
        string kind =
            announcement.CarrierKind == FollowRouteCarrierKind.Squadron ? "Squadron carrier" : "Personal fleet carrier";
        string identity = string.Join(
            " · ",
            new[] { announcement.Name, announcement.Callsign }.Where(value => !string.IsNullOrWhiteSpace(value))
        );
        return SendAsync(
            url,
            new
            {
                allowed_mentions = new { parse = Array.Empty<string>() },
                embeds = new[]
                {
                    new
                    {
                        title,
                        description = Limit(
                            $"{kind}: {(identity.Length == 0 ? announcement.CarrierId.ToString(CultureInfo.InvariantCulture) : identity)}"
                        ),
                        color = announcement.Event switch
                        {
                            CarrierAnnouncementKind.Cancelled => 15158332,
                            CarrierAnnouncementKind.Arrived => 3066993,
                            _ => 3447003,
                        },
                        fields,
                        timestamp = announcement
                            .ObservedAt.ToUniversalTime()
                            .ToString("O", CultureInfo.InvariantCulture),
                        footer = new { text = "SrvSurvey-XP" },
                    },
                },
            },
            cancellationToken
        );
    }

    private static string DiscordTime(DateTimeOffset time) => $"<t:{time.ToUnixTimeSeconds()}:F>";

    private static object Field(string name, string value, bool inline = true) =>
        new
        {
            name,
            value = Limit(value),
            inline,
        };

    private static string Limit(string value) => value.Length <= 500 ? value : value[..499] + "…";

    private async Task<DiscordWebhookResult> SendAsync(string url, object payload, CancellationToken cancellationToken)
    {
        Uri? endpoint = ParseWebhookUrl(url);
        if (endpoint is null)
        {
            return new(false, "Enter a valid Discord webhook URL.");
        }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            return await SendWithRetriesAsync(endpoint, payload, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "Discord did not respond in time. Check your connection before trying again.");
        }
        catch (Exception exception)
            when (exception is HttpRequestException or JsonException or IOException or InvalidDataException)
        {
            return new(false, "Could not deliver the Discord message. Check your connection and webhook URL.");
        }
    }

    private async Task<DiscordWebhookResult> SendWithRetriesAsync(
        Uri endpoint,
        object payload,
        CancellationToken cancellationToken
    )
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload),
            };
            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return new(true, "Message sent to Discord.");
            }
            if (response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                return new(
                    false,
                    response.StatusCode switch
                    {
                        HttpStatusCode.NotFound or HttpStatusCode.Unauthorized =>
                            "The Discord webhook was deleted or its URL is no longer valid.",
                        HttpStatusCode.Forbidden => "Discord denied this webhook access to the channel.",
                        _ => "Discord could not accept the message. Check the webhook and try again.",
                    }
                );
            }
            TimeSpan? retry = await RetryDelayAsync(response, cancellationToken).ConfigureAwait(false);
            if (attempt == 2 || retry is null || retry < TimeSpan.Zero || retry > TimeSpan.FromSeconds(30))
            {
                break;
            }
            await delay(retry.Value, cancellationToken).ConfigureAwait(false);
        }
        return new(false, "Discord is rate limiting this webhook. Try again later.");
    }

    private static async Task<TimeSpan?> RetryDelayAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        if (response.Headers.RetryAfter?.Delta is { } header)
        {
            return header;
        }
        using JsonDocument body = await BoundedHttpContent
            .ReadJsonDocumentAsync(
                response.Content,
                MaximumResponseBytes,
                "Discord rate-limit response",
                cancellationToken
            )
            .ConfigureAwait(false);
        return
            body.RootElement.ValueKind == JsonValueKind.Object
            && body.RootElement.TryGetProperty("retry_after", out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out double seconds)
            && double.IsFinite(seconds)
            && seconds >= 0
            ? TimeSpan.FromSeconds(Math.Min(seconds, 60))
            : null;
    }

    public void Dispose()
    {
        if (ownsClient)
        {
            client.Dispose();
        }
    }
}
