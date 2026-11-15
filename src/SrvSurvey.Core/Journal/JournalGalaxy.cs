using System.Globalization;
using System.Text.Json;

namespace SrvSurvey.Core.Journal;

/// <summary>Reads galaxy membership from Fileheader, independent of expansion ownership in LoadGame.</summary>
public static class JournalGalaxy
{
    public static bool? IsLive(JsonElement? header)
    {
        if (header is not { } root)
        {
            return null;
        }
        if (
            root.TryGetProperty("Odyssey", out JsonElement galaxy)
            && galaxy.ValueKind is JsonValueKind.True or JsonValueKind.False
        )
        {
            return galaxy.GetBoolean();
        }
        string? version =
            root.TryGetProperty("gameversion", out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        return int.TryParse(version?.Split('.')[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major)
            ? major >= 4
            : null;
    }
}
