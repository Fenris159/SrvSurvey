using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Core.Discord;

public sealed record DiscordWebhookResult(bool Success, string Message);

/// <summary>Sends bounded Discord embeds without leaking webhook tokens through errors or following redirects.</summary>
public sealed partial class DiscordCarrierWebhookClient : IDisposable
{
    private const string SentMessage = "Message sent to Discord.";
    private const int MaximumResponseBytes = 16 * 1024;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly Func<DiscordCarrierImage, DiscordCarrierImageCrop, DiscordCarrierImage>? imageCropper;
    private readonly DiscordCarrierDiagnostics diagnostics;

    public DiscordCarrierWebhookClient(
        HttpClient? client = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DiscordCarrierImage, DiscordCarrierImageCrop, DiscordCarrierImage>? imageCropper = null,
        Action<string>? log = null
    )
    {
        this.client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        ownsClient = client is null;
        this.delay = delay ?? Task.Delay;
        this.imageCropper = imageCropper;
        diagnostics = new(log);
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

    public Task<DiscordWebhookResult> SendTestAsync(string url, CancellationToken cancellationToken = default)
    {
        var sendId = Guid.NewGuid();
        diagnostics.Write("connection-test", "image-expected=False", sendId);
        return SendAsync(
            url,
            new WebhookSubmission(
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
                "Carrier announcements connected",
                null,
                sendId
            ),
            null,
            cancellationToken
        );
    }

    public Task<DiscordWebhookResult> SendAnnouncementAsync(
        string url,
        CarrierJumpAnnouncement announcement,
        CancellationToken cancellationToken = default
    ) => SendAnnouncementAsync(url, announcement, new DiscordCarrierMessageSettings(), cancellationToken);

    public Task<DiscordWebhookResult> SendAnnouncementAsync(
        string url,
        CarrierJumpAnnouncement announcement,
        DiscordCarrierMessageSettings settings,
        CancellationToken cancellationToken = default
    ) => SendAnnouncementAsync(url, announcement, settings, null, cancellationToken);

    public Task<DiscordWebhookResult> SendAnnouncementAsync(
        string url,
        CarrierJumpAnnouncement announcement,
        DiscordCarrierMessageSettings settings,
        Func<bool>? canSend,
        CancellationToken cancellationToken = default
    ) => SendAnnouncementAsync(url, announcement, settings, canSend, Guid.NewGuid(), cancellationToken);

    public async Task<DiscordWebhookResult> SendAnnouncementAsync(
        string url,
        CarrierJumpAnnouncement announcement,
        DiscordCarrierMessageSettings settings,
        Func<bool>? canSend,
        Guid diagnosticId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(announcement);
        ArgumentNullException.ThrowIfNull(settings);
        Guid sendId = diagnosticId;
        DiscordCarrierMessageTemplate template = settings.ForEvent(announcement.Event);
        string? validation = DiscordCarrierMessageFormatter.Validate(template);
        if (validation is not null)
        {
            diagnostics.Write("skipped", "reason=invalid-template", sendId);
            return new(false, validation);
        }
        DiscordCarrierMessage message = DiscordCarrierMessageFormatter.Render(announcement, template);
        string path =
            announcement.CarrierKind == FollowRouteCarrierKind.Squadron
                ? settings.SquadronImagePath
                : settings.PersonalImagePath;
        DiscordCarrierImage? image = null;
        bool imageUnavailable = false;
        string imagePhase = "load";
        diagnostics.Write(
            "rendered",
            $"event={announcement.Event}; carrier={announcement.CarrierKind}; "
                + $"template={DiscordCarrierDiagnostics.TemplateFingerprint(template)}; custom-template={template != DiscordCarrierMessageSettings.DefaultTemplate(announcement.Event)}; "
                + $"title-sha256={DiscordCarrierDiagnostics.Fingerprint(message.Title)}; title-length={message.Title.Length}; image-selected={!string.IsNullOrWhiteSpace(path)}",
            sendId
        );
        try
        {
            image = await DiscordCarrierImage.LoadAsync(path, cancellationToken).ConfigureAwait(false);
            DiscordCarrierImageCrop? crop =
                announcement.CarrierKind == FollowRouteCarrierKind.Squadron
                    ? settings.SquadronImageCrop
                    : settings.PersonalImageCrop;
            if (image is not null && crop is not null)
            {
                imagePhase = "crop";
                image =
                    imageCropper?.Invoke(image, crop)
                    ?? throw new NotSupportedException("An image crop encoder is required.");
            }
            imagePhase = "size-check";
            if (image is { Bytes.Length: > DiscordCarrierImage.MaximumBytes })
            {
                throw new InvalidDataException("The carrier image exceeds the upload size limit.");
            }
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or InvalidDataException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            image = null;
            imageUnavailable = true;
            diagnostics.Write("image-unavailable", $"phase={imagePhase}; error={exception.GetType().Name}", sendId);
        }
        diagnostics.Write(
            "prepared",
            $"image-attached={image is not null}; image-bytes={image?.Bytes.Length ?? 0}; "
                + $"image-sha256={(image is null ? "none" : DiscordCarrierDiagnostics.Fingerprint(image.Bytes))}",
            sendId
        );
        var embed = new Dictionary<string, object>
        {
            ["title"] = message.Title,
            ["description"] = message.Description,
            ["color"] = message.Color,
            ["fields"] = message.Fields.Select(field => new
            {
                name = field.Name,
                value = field.Value,
                inline = field.Inline,
            }),
            ["timestamp"] = announcement.ObservedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["footer"] = new { text = "SrvSurvey-XP" },
        };
        if (image is not null)
        {
            embed["image"] = new { url = "attachment://" + image.FileName };
        }
        DiscordWebhookResult result = await SendAsync(
                url,
                new WebhookSubmission(
                    new { allowed_mentions = new { parse = Array.Empty<string>() }, embeds = new[] { embed } },
                    message.Title,
                    image,
                    sendId
                ),
                canSend,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (!result.Success || !imageUnavailable)
        {
            return result;
        }
        string confirmationWarning = result.Message == SentMessage ? "" : " " + result.Message;
        return new(
            true,
            "Message sent to Discord without its image. Choose the carrier image again in the message editor."
                + confirmationWarning
        );
    }

    private async Task<DiscordWebhookResult> SendAsync(
        string url,
        WebhookSubmission submission,
        Func<bool>? canSend,
        CancellationToken cancellationToken
    )
    {
        Uri? endpoint = ParseWebhookUrl(url);
        if (endpoint is null)
        {
            diagnostics.Write("skipped", "reason=invalid-webhook-url", submission.Id);
            return new(false, "Enter a valid Discord webhook URL.");
        }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            return await SendWithRetriesAsync(endpoint, submission, canSend, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            diagnostics.Write("failed", "error=timeout-before-confirmation", submission.Id);
            return new(false, "Discord did not respond in time. Check your connection before trying again.");
        }
        catch (Exception exception)
            when (exception is HttpRequestException or JsonException or IOException or InvalidDataException)
        {
            diagnostics.Write("failed", $"error={exception.GetType().Name}", submission.Id);
            return new(false, "Could not deliver the Discord message. Check your connection and webhook URL.");
        }
    }

    private async Task<DiscordWebhookResult> SendWithRetriesAsync(
        Uri endpoint,
        WebhookSubmission submission,
        Func<bool>? canSend,
        CancellationToken cancellationToken
    )
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = CreateContent(submission.Payload, submission.Image),
            };
            diagnostics.Write(
                "posting",
                $"attempt={attempt + 1}; transport={(submission.Image is null ? "json" : "multipart")}; content-length={request.Content.Headers.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}",
                submission.Id
            );
            if (canSend?.Invoke() == false)
            {
                diagnostics.Write("skipped", "reason=settings-changed-or-network-disabled", submission.Id);
                return new(false, "This announcement was skipped because its settings changed.");
            }
            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            diagnostics.Write(
                "response",
                $"attempt={attempt + 1}; http={(int)response.StatusCode}; version={response.Version}",
                submission.Id
            );
            if (response.IsSuccessStatusCode)
            {
                return await ConfirmMessageAsync(response, submission, cancellationToken).ConfigureAwait(false);
            }
            if (response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                return new(
                    false,
                    await RejectionMessageAsync(response, submission.Id, cancellationToken).ConfigureAwait(false)
                );
            }
            TimeSpan? retry = await RetryDelayAsync(response, cancellationToken).ConfigureAwait(false);
            if (attempt == 2 || retry is null || retry < TimeSpan.Zero || retry > TimeSpan.FromSeconds(30))
            {
                break;
            }
            diagnostics.Write("retry", $"delay-ms={retry.Value.TotalMilliseconds}", submission.Id);
            await delay(retry.Value, cancellationToken).ConfigureAwait(false);
        }
        return new(false, "Discord is rate limiting this webhook. Try again later.");
    }

    private sealed record WebhookSubmission(object Payload, string Title, DiscordCarrierImage? Image, Guid Id);

    private static HttpContent CreateContent(object payload, DiscordCarrierImage? image)
    {
        if (image is null)
        {
            return JsonContent.Create(payload);
        }
        var content = new MultipartFormDataContent { { JsonContent.Create(payload), "payload_json" } };
        var file = new ByteArrayContent(image.Bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(image.MediaType);
        content.Add(file, "files[0]", image.FileName);
        return content;
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
