using System.Globalization;
using System.Text.RegularExpressions;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Core.Discord;

public sealed record DiscordCarrierMessageField(string Name, string Value, bool Inline = true);

public sealed record DiscordCarrierMessage(
    string Title,
    string Description,
    int Color,
    IReadOnlyList<DiscordCarrierMessageField> Fields
);

/// <summary>One bounded rendering path shared by the editor preview and webhook delivery.</summary>
public static partial class DiscordCarrierMessageFormatter
{
    public const int MaximumTitleLength = 256;
    public const int MaximumMessageLength = 4096;
    public static IReadOnlyList<string> Tokens { get; } =
        Array.AsReadOnly(
            new[]
            {
                "carrier",
                "carrier_name",
                "callsign",
                "carrier_kind",
                "origin",
                "destination",
                "body",
                "departure",
                "countdown",
                "arrival",
                "route",
            }
        );

    public static string? Validate(DiscordCarrierMessageTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.Title) || template.Title.Length > MaximumTitleLength)
        {
            return "Enter a title of 1–256 characters.";
        }
        if (string.IsNullOrWhiteSpace(template.Message) || template.Message.Length > MaximumMessageLength)
        {
            return "Enter a message of 1–4096 characters.";
        }
        Match? unknown = TokenPattern()
            .Matches(template.Title + "\n" + template.Message)
            .FirstOrDefault(match => !Tokens.Contains(match.Groups[1].Value, StringComparer.Ordinal));
        if (unknown is not null)
        {
            return $"Unknown field {unknown.Value}. Choose a field from Insert field.";
        }
        return null;
    }

    public static DiscordCarrierMessage Render(
        CarrierJumpAnnouncement announcement,
        DiscordCarrierMessageTemplate template
    )
    {
        string identity = string.Join(
            " · ",
            new[] { Bound(announcement.Name, 160), Bound(announcement.Callsign, 40) }.Where(value =>
                !string.IsNullOrWhiteSpace(value)
            )
        );
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["carrier"] =
                identity.Length == 0 ? announcement.CarrierId.ToString(CultureInfo.InvariantCulture) : identity,
            ["carrier_name"] = Bound(announcement.Name, 160),
            ["callsign"] = Bound(announcement.Callsign, 40),
            ["carrier_kind"] =
                announcement.CarrierKind == FollowRouteCarrierKind.Squadron
                    ? "Squadron carrier"
                    : "Personal fleet carrier",
            ["origin"] = string.IsNullOrWhiteSpace(announcement.Origin)
                ? "Departure system not reported"
                : Bound(announcement.Origin, 160),
            ["destination"] = Bound(announcement.Destination, 160),
            ["body"] = Bound(announcement.Body, 160),
            ["departure"] = announcement.Departure is { } departure
                ? Time(departure, "F")
                : "Departure time not reported",
            ["countdown"] =
                announcement.Event == CarrierAnnouncementKind.Scheduled && announcement.Departure is { } countdown
                    ? Time(countdown, "R")
                    : "",
            ["arrival"] =
                announcement.Event == CarrierAnnouncementKind.Arrived ? Time(announcement.ObservedAt, "F") : "",
            ["route"] = Bound(announcement.Route, 500),
        };
        string Expand(string text, int maximum) =>
            Bound(
                TokenPattern().Replace(text, match => values.GetValueOrDefault(match.Groups[1].Value, match.Value)),
                maximum
            );
        var fields = new List<DiscordCarrierMessageField>();
        if (template.IncludeDetails)
        {
            fields.Add(new("Itinerary", Bound($"{values["origin"]} → {announcement.Destination}", 500), false));
            AddField(fields, "Destination body", announcement.Body);
            if (announcement.Departure is not null)
            {
                AddField(fields, "Scheduled departure", values["departure"]);
            }
            AddField(fields, "Countdown", values["countdown"]);
            AddField(fields, "Arrival confirmed", values["arrival"]);
            AddField(fields, "Route", announcement.Route, false);
        }
        // Reserve the embed's remaining 6000-character budget for its standard details and footer.
        int descriptionBudget = Math.Min(
            MaximumMessageLength,
            6000
                - MaximumTitleLength
                - "SrvSurvey-XP".Length
                - fields.Sum(field => field.Name.Length + field.Value.Length)
        );
        return new(
            Expand(template.Title, MaximumTitleLength),
            Expand(template.Message, descriptionBudget),
            announcement.Event switch
            {
                CarrierAnnouncementKind.Cancelled => 15158332,
                CarrierAnnouncementKind.Arrived => 3066993,
                _ => 3447003,
            },
            fields
        );
    }

    private static void AddField(List<DiscordCarrierMessageField> fields, string name, string value, bool inline = true)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.Add(new(name, Bound(value, 500), inline));
        }
    }

    private static string Time(DateTimeOffset value, string style) => $"<t:{value.ToUnixTimeSeconds()}:{style}>";

    private static string Bound(string value, int maximum) =>
        value.Length <= maximum ? value : value[..(maximum - 1)] + "…";

    [GeneratedRegex(@"\{([^{}\r\n]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
