using System.Text.Json;

namespace SrvSurvey.Core.Colonization;

public static partial class ColonizationBodyCatalog
{
    /// <summary>Resolves EDSM records without a journal ID against the other catalog's full body names.</summary>
    public static IReadOnlyList<ColonizationSystemBody> FromProviders(
        JsonElement spansh,
        JsonElement edsm,
        long systemAddress,
        string systemName,
        IReadOnlyList<ColonizationSystemBody>? existing = null
    )
    {
        if (
            spansh.ValueKind != JsonValueKind.Object
            || !spansh.TryGetProperty("system", out JsonElement system)
            || system.ValueKind != JsonValueKind.Object
        )
        {
            throw new InvalidDataException("Spansh returned no system data.");
        }

        ValidateIdentity(system, systemAddress, systemName, "Spansh");
        ValidateIdentity(edsm, systemAddress, systemName, "EDSM");
        var result = new List<ColonizationSystemBody>();
        AddProviderBodies(result, system, false, existing ?? []);
        AddProviderBodies(result, edsm, true, [.. result, .. existing ?? []]);
        return Merge(result);
    }

    private static void ValidateIdentity(JsonElement system, long address, string name, string provider)
    {
        if (
            system.ValueKind != JsonValueKind.Object
            || !system.TryGetProperty("id64", out JsonElement id)
            || id.ValueKind != JsonValueKind.Number
            || !id.TryGetInt64(out long actual)
            || actual != address
            || !string.Equals(Text(system, "name"), name, StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidDataException($"{provider} returned a different or unidentified system.");
        }
    }

    private static void AddProviderBodies(
        List<ColonizationSystemBody> result,
        JsonElement system,
        bool edsm,
        IReadOnlyList<ColonizationSystemBody> known
    )
    {
        if (!system.TryGetProperty("bodies", out JsonElement bodies) || bodies.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The external system response has no body list.");
        }

        foreach (JsonElement body in bodies.EnumerateArray())
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("An external body record is not an object.");
            }
            string name = Text(body, "name") ?? throw new InvalidDataException("An external body has no name.");
            int? id =
                body.TryGetProperty("bodyId", out JsonElement number)
                && number.ValueKind == JsonValueKind.Number
                && number.TryGetInt32(out int parsed)
                    ? parsed
                    : known
                        .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
                        ?.Number;
            if (id is null or < 0)
            {
                throw new InvalidDataException($"The journal BodyID for '{name}' could not be resolved.");
            }

            string? subtype = Text(body, "subType");
            bool star = Text(body, "type") == "Star";
            string type = TypeCode(subtype, star, Text(body, "type") == "Barycentre");
            if (type == "un")
            {
                throw new InvalidDataException($"The body type for '{name}' could not be resolved.");
            }

            ColonizationSystemBody normalized = new()
            {
                Number = id.Value,
                Name = name,
                Type = type,
                Subtype = subtype,
                Parents = Parents(body, "parents"),
                Features = Features(body, false),
                DistanceLightSeconds = Number(body, "distanceToArrival") ?? 0,
                Radius = ProviderRadius(body),
                Temperature = Number(body, "surfaceTemperature") ?? -1,
                Gravity = Number(body, "gravity") ?? -1,
            };
            AddSignals(normalized.Features, body, "signals");
            if (star)
            {
                AddBelts(result, normalized, body, "belts", edsm);
            }

            result.Add(normalized);
        }
    }

    private static double ProviderRadius(JsonElement body)
    {
        if (Number(body, "radius") is > 0 and var radius)
        {
            return radius * 1000;
        }
        return Number(body, "solarRadius") is > 0 and var solar ? solar * SolarRadiusMeters : -1;
    }

    private static void AddSignals(HashSet<string> features, JsonElement body, string key)
    {
        if (!body.TryGetProperty(key, out JsonElement signals) || signals.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!signals.TryGetProperty("signals", out JsonElement counts) || counts.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        foreach (JsonProperty signal in counts.EnumerateObject())
        {
            if (
                signal.Value.ValueKind != JsonValueKind.Number
                || !signal.Value.TryGetInt32(out int count)
                || count <= 0
            )
            {
                continue;
            }

            if (signal.Name.Contains("Biological", StringComparison.OrdinalIgnoreCase))
            {
                features.Add("bio");
            }

            if (signal.Name.Contains("Geological", StringComparison.OrdinalIgnoreCase))
            {
                features.Add("geo");
            }
        }
    }
}
