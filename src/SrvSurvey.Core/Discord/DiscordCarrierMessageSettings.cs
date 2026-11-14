namespace SrvSurvey.Core.Discord;

public sealed record DiscordCarrierMessageTemplate(string Title, string Message, bool IncludeDetails = true);

/// <summary>Commander-local templates and the independently selected images for each carrier.</summary>
public sealed record DiscordCarrierMessageSettings(
    DiscordCarrierMessageTemplate? Scheduled = null,
    DiscordCarrierMessageTemplate? Cancelled = null,
    DiscordCarrierMessageTemplate? Arrived = null,
    string PersonalImagePath = "",
    string SquadronImagePath = "",
    DiscordCarrierImageCrop? PersonalImageCrop = null,
    DiscordCarrierImageCrop? SquadronImageCrop = null
)
{
    public DiscordCarrierMessageTemplate ForEvent(CarrierAnnouncementKind kind)
    {
        DiscordCarrierMessageTemplate fallback = DefaultTemplate(kind);
        DiscordCarrierMessageTemplate? template = kind switch
        {
            CarrierAnnouncementKind.Scheduled => Scheduled,
            CarrierAnnouncementKind.Cancelled => Cancelled,
            _ => Arrived,
        };
        return template is null
            ? fallback
            : template with
            {
                Title = template.Title ?? fallback.Title,
                Message = template.Message ?? fallback.Message,
            };
    }

    public static DiscordCarrierMessageTemplate DefaultTemplate(CarrierAnnouncementKind kind) =>
        new(
            kind switch
            {
                CarrierAnnouncementKind.Scheduled => "Carrier jump scheduled",
                CarrierAnnouncementKind.Cancelled => "Carrier jump cancelled",
                _ => "Carrier jump completed",
            },
            "{carrier_kind}: {carrier}"
        );
}
