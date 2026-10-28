using SrvSurvey.Core.Routes;

namespace SrvSurvey.Core.Discord;

public sealed record DiscordCarrierPreferences(
    bool Enabled = false,
    string WebhookUrl = "",
    bool PersonalCarrier = true,
    bool SquadronCarrier = true,
    bool JumpScheduled = true,
    bool JumpCancelled = true,
    bool JumpArrived = true
)
{
    public bool Includes(CarrierJumpAnnouncement announcement) =>
        Enabled
        && (announcement.CarrierKind == FollowRouteCarrierKind.Squadron ? SquadronCarrier : PersonalCarrier)
        && announcement.Event switch
        {
            CarrierAnnouncementKind.Scheduled => JumpScheduled,
            CarrierAnnouncementKind.Cancelled => JumpCancelled,
            CarrierAnnouncementKind.Arrived => JumpArrived,
            _ => false,
        };
}
