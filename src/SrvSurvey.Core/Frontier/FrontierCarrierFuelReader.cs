using System.Text.Json;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Core.Frontier;

/// <summary>Preserves missing fuel data and invariant commodity identifiers for automatic route inputs.</summary>
internal static class FrontierCarrierFuelReader
{
    public static FleetCarrierFuelSnapshot Read(
        JsonElement root,
        string callsign,
        bool squadron,
        DateTimeOffset fetchedAt
    )
    {
        long? id = Identifier(root, "id") ?? Identifier(root, "carrierId") ?? Identifier(root, "marketId");
        if (
            id is null
            && root.TryGetProperty("market", out JsonElement market)
            && market.ValueKind == JsonValueKind.Object
        )
        {
            id = Identifier(market, "id");
        }
        double? used = null;
        if (root.TryGetProperty("capacity", out JsonElement capacity) && capacity.ValueKind == JsonValueKind.Object)
        {
            double?[] quantities = capacity
                .EnumerateObject()
                .Where(field =>
                    !field.Name.Contains("microresource", StringComparison.OrdinalIgnoreCase)
                    && !field.Name.Equals("freeSpace", StringComparison.OrdinalIgnoreCase)
                )
                .Select(field =>
                    field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetDouble(out double value)
                        ? (double?)value
                        : null
                )
                .ToArray();
            used = quantities.Length == 0 ? null : quantities.Aggregate((double?)0, (sum, value) => sum + value);
        }
        return new FleetCarrierFuelSnapshot(
            squadron ? FollowRouteCarrierKind.Squadron : FollowRouteCarrierKind.Personal,
            callsign,
            id,
            SystemName(root),
            Number(root, "fuel"),
            StoredTritium(root),
            used,
            fetchedAt
        );
    }

    private static string SystemName(JsonElement root)
    {
        if (root.TryGetProperty("currentStarSystem", out JsonElement location))
        {
            if (location.ValueKind == JsonValueKind.String)
            {
                return location.GetString()!;
            }
            else if (
                location.ValueKind == JsonValueKind.Object
                && location.TryGetProperty("name", out JsonElement systemName)
                && systemName.ValueKind == JsonValueKind.String
            )
            {
                return systemName.GetString()!;
            }
        }
        return string.Empty;
    }

    private static double? StoredTritium(JsonElement root)
    {
        if (!root.TryGetProperty("cargo", out JsonElement cargo) || cargo.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        double total = 0;
        foreach (JsonElement item in cargo.EnumerateArray())
        {
            if (
                item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("commodity", out JsonElement commodity)
                || commodity.ValueKind != JsonValueKind.String
            )
            {
                return null;
            }
            if (!string.Equals(commodity.GetString(), "tritium", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (Number(item, "qty") is not { } quantity || quantity < 0)
            {
                return null;
            }
            total += quantity;
        }
        return total;
    }

    private static long? Identifier(JsonElement root, string key) =>
        root.TryGetProperty(key, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out long number)
            ? number
            : null;

    private static double? Number(JsonElement root, string key) =>
        root.TryGetProperty(key, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out double number)
        && double.IsFinite(number)
            ? number
            : null;
}
