using System.Text.Json;

namespace SrvSurvey.Core.Colonization;

/// <summary>
/// The current body for a colonization project, taken from journal location
/// events. Upstream SrvSurvey publishes that body's id as bodyNum and its
/// full name as bodyName.
/// </summary>
public static class ColonizationBodyJournal
{
    public static bool ClearsCurrentBody(string eventName)
    {
        return eventName is "FSDJump" or "CarrierJump" or "SupercruiseEntry";
    }

    public static bool ReportsCurrentBody(string eventName)
    {
        return eventName
            is "Docked"
                or "Location"
                or "SupercruiseExit"
                or "ApproachBody"
                or "Touchdown"
                or "ApproachSettlement";
    }

    public static int? ReadBodyId(JsonElement payload)
    {
        if (!payload.TryGetProperty("BodyID", out JsonElement value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt32(out int bodyId) ? bodyId : null;
    }

    public static string? ReadBodyName(JsonElement payload)
    {
        return ReadString(payload, "Body") ?? ReadString(payload, "BodyName");
    }

    private static string? ReadString(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? text = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
