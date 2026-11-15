using System.Net;
using System.Text.Json;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Discord;

public sealed partial class DiscordCarrierWebhookClient
{
    private const int MaximumConfirmationBytes = 64 * 1024;
    private const string UnreadableConfirmation =
        "Message sent to Discord, but its confirmation could not be read. See Diagnostics → Logs.";

    private async Task<DiscordWebhookResult> ConfirmMessageAsync(
        HttpResponseMessage response,
        WebhookSubmission submission,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using JsonDocument body = await BoundedHttpContent
                .ReadJsonDocumentAsync(
                    response.Content,
                    MaximumConfirmationBytes,
                    "Discord message confirmation",
                    cancellationToken
                )
                .ConfigureAwait(false);
            string? messageId = StringProperty(body.RootElement, "id");
            if (messageId is null || !IsSnowflake(messageId))
            {
                diagnostics.Write("confirmation-unavailable", "reason=unexpected-message-shape", submission.Id);
                return new(true, UnreadableConfirmation);
            }
            JsonElement embeds = Property(body.RootElement, "embeds");
            JsonElement embed =
                embeds.ValueKind == JsonValueKind.Array && embeds.GetArrayLength() > 0 ? embeds[0] : default;
            string? title = StringProperty(embed, "title");
            bool titleMatches = string.Equals(title, submission.Title, StringComparison.Ordinal);
            bool returnedImage = !string.IsNullOrWhiteSpace(StringProperty(Property(embed, "image"), "url"));
            diagnostics.Write(
                "confirmed",
                $"message-id={messageId}; title-match={titleMatches}; "
                    + $"returned-title-sha256={(title is null ? "missing" : DiscordCarrierDiagnostics.Fingerprint(title))}; "
                    + $"returned-title-length={title?.Length ?? 0}; image-expected={submission.Image is not null}; image-returned={returnedImage}",
                submission.Id
            );
            string? mismatch = (titleMatches, submission.Image is not null && !returnedImage) switch
            {
                (false, true) => "a different title and no carrier image",
                (false, false) => "a different title",
                (true, true) => "no carrier image",
                _ => null,
            };
            return new(
                true,
                mismatch is null
                    ? SentMessage
                    : $"Discord accepted the message, but its confirmation has {mismatch}. See Diagnostics → Logs."
            );
        }
        catch (Exception exception) when (IsResponseReadFailure(exception))
        {
            // The POST was accepted. A bad or interrupted confirmation must not prompt a duplicate POST.
            diagnostics.Write("confirmation-unavailable", $"error={exception.GetType().Name}", submission.Id);
            return new(true, UnreadableConfirmation);
        }
    }

    private async Task<string> RejectionMessageAsync(
        HttpResponseMessage response,
        Guid sendId,
        CancellationToken cancellationToken
    )
    {
        int? code = null;
        try
        {
            using JsonDocument body = await BoundedHttpContent
                .ReadJsonDocumentAsync(response.Content, MaximumResponseBytes, "Discord rejection", cancellationToken)
                .ConfigureAwait(false);
            JsonElement value = Property(body.RootElement, "code");
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
            {
                code = number;
            }
        }
        catch (Exception exception) when (IsResponseReadFailure(exception))
        {
            diagnostics.Write("rejection-body-unavailable", $"error={exception.GetType().Name}", sendId);
        }
        diagnostics.Write(
            "rejected",
            $"http={(int)response.StatusCode}; discord-code={code?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}",
            sendId
        );
        return response.StatusCode switch
        {
            HttpStatusCode.NotFound or HttpStatusCode.Unauthorized =>
                "The Discord webhook was deleted or its URL is no longer valid.",
            HttpStatusCode.Forbidden => "Discord denied this webhook access to the channel.",
            HttpStatusCode.RequestEntityTooLarge =>
                "Discord rejected the upload because it is too large. See Diagnostics → Logs.",
            _ => code switch
            {
                40005 => "Discord rejected the request because it is too large (error 40005). See Diagnostics → Logs.",
                50045 =>
                    "Discord rejected the carrier image because it is too large (error 50045). See Diagnostics → Logs.",
                50046 =>
                    "Discord rejected the carrier image as invalid (error 50046). Choose a PNG or JPEG image again.",
                50035 => "Discord rejected the message format (error 50035). See Diagnostics → Logs.",
                _ => "Discord could not accept the message. Check the webhook and try again.",
            },
        };
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value)
            ? value
            : default;

    private static string? StringProperty(JsonElement element, string name)
    {
        JsonElement value = Property(element, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool IsResponseReadFailure(Exception exception) =>
        exception
            is JsonException
                or IOException
                or InvalidDataException
                or InvalidOperationException
                or HttpRequestException
                or OperationCanceledException;
}
