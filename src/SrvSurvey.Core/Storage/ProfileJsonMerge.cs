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
            if (property.Key == "scannedOrganics" && current.ContainsKey("scannedBioEntryIds"))
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

    private static JsonNode? MergeCodex(JsonNode? current, JsonObject incoming)
    {
        if (current is not JsonObject firsts)
        {
            return incoming.DeepClone();
        }
        foreach (KeyValuePair<string, JsonNode?> entry in incoming)
        {
            // A discovery is atomic: retain current manual Bingo checks and metadata.
            if (!CommanderCodexStore.TryParseFirst(firsts[entry.Key], out _))
            {
                firsts[entry.Key] = entry.Value?.DeepClone();
            }
        }
        return firsts.DeepClone();
    }
}
