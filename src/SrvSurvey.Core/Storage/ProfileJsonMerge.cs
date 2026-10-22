using System.Globalization;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Exobiology;

namespace SrvSurvey.Core.Storage;

public static class ProfileJsonMerge
{
    /// <summary>Fills missing properties without replacing current values or transient lists.</summary>
    public static JsonObject MergeMissing(JsonObject current, JsonObject incoming)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);
        var merged = (JsonObject)current.DeepClone();
        foreach (KeyValuePair<string, JsonNode?> property in incoming)
        {
            if (KeepCurrentClaims(current, property.Key))
            {
                continue;
            }
            if (!merged.ContainsKey(property.Key))
            {
                merged[property.Key] = property.Value?.DeepClone();
            }
            else if (property.Key == "codexFirsts" && property.Value is JsonObject firsts)
            {
                merged[property.Key] = MergeCodex(merged[property.Key], firsts);
            }
            else if (merged[property.Key] is JsonObject existing && property.Value is JsonObject added)
            {
                merged[property.Key] = MergeMissing(existing, added);
            }
            else if (
                property.Key is "decodeTheRuins" or "decodeTheLogs"
                && merged[property.Key] is JsonArray known
                && property.Value is JsonArray discoveries
            )
            {
                MergeCompletionSet(known, discoveries);
            }
        }
        return merged;
    }

    private static bool KeepCurrentClaims(JsonObject current, string property) =>
        property == "scannedOrganics" && current.ContainsKey("scannedBioEntryIds")
        || property == "explRewardsBySystem" && (current.ContainsKey("explRewards") || current.ContainsKey(property));

    private static void MergeCompletionSet(JsonArray known, JsonArray discoveries)
    {
        foreach (
            JsonNode? discovery in discoveries.Where(discovery =>
                !known.Any(item => JsonNode.DeepEquals(item, discovery))
            )
        )
        {
            known.Add(discovery?.DeepClone());
        }
    }

    private static JsonObject MergeCodex(JsonNode? current, JsonObject incoming)
    {
        var firsts = new JsonObject();
        if (current is JsonObject existing)
        {
            AddCodexEntries(firsts, existing, preserveValid: false);
        }
        var normalizedIncoming = new JsonObject();
        AddCodexEntries(normalizedIncoming, incoming, preserveValid: false);
        AddCodexEntries(firsts, normalizedIncoming, preserveValid: true);
        return firsts;
    }

    private static void AddCodexEntries(JsonObject target, JsonObject source, bool preserveValid)
    {
        foreach (KeyValuePair<string, JsonNode?> entry in source)
        {
            string key = long.TryParse(entry.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)
                ? id.ToString(CultureInfo.InvariantCulture)
                : entry.Key;
            bool validCurrent = CommanderCodexStore.TryParseFirst(target[key], out _);
            if (!validCurrent || !preserveValid && CommanderCodexStore.TryParseFirst(entry.Value, out _))
            {
                target[key] = entry.Value?.DeepClone();
            }
        }
    }
}
