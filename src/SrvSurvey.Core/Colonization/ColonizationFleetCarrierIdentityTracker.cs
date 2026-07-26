using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Colonization;

public sealed class ColonizationFleetCarrierIdentityTracker
{
    private const int MaximumCandidates = 256;
    private readonly List<string> candidates = [];

    public void Apply(JournalEventEnvelope journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        var candidate = journalEvent.EventName switch
        {
            "ReceiveText" => GetString(journalEvent.Payload, "From"),
            "FSSSignalDiscovered" when string.Equals(
                GetString(journalEvent.Payload, "SignalType"),
                "FleetCarrier",
                StringComparison.OrdinalIgnoreCase) =>
                    GetString(journalEvent.Payload, "SignalName_Localised")
                    ?? GetString(journalEvent.Payload, "SignalName"),
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        candidates.Add(candidate.Trim());
        if (candidates.Count > MaximumCandidates)
        {
            candidates.RemoveRange(0, candidates.Count - MaximumCandidates);
        }
    }

    public string ResolveDisplayName(string carrierName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierName);
        var normalizedName = carrierName.Trim();
        for (var index = candidates.Count - 1; index >= 0; index--)
        {
            var candidate = candidates[index];
            if (!candidate.EndsWith(
                    normalizedName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var displayName = candidate[..^normalizedName.Length].TrimEnd();
            if (displayName.EndsWith('|'))
            {
                displayName = displayName[..^1].TrimEnd();
            }

            return displayName;
        }

        return string.Empty;
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }
}
