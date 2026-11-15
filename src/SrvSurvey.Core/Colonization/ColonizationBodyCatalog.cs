using System.Text.Json;

namespace SrvSurvey.Core.Colonization;

/// <summary>Normalizes journal and community body records into Raven's replacement catalog.</summary>
public static partial class ColonizationBodyCatalog
{
    private const string JournalRings = "Rings";
    private const double LightSecondMeters = 299_792_458d;
    private const double SolarRadiusMeters = 695_700_000d;
    private const double StandardGravity = 9.80665d;

    public static void Validate(IReadOnlyList<ColonizationSystemBody> bodies)
    {
        if (
            bodies.Count == 0
            || bodies.Any(body =>
                body is null
                || body.Number < 0
                || string.IsNullOrWhiteSpace(body.Name)
                || string.IsNullOrWhiteSpace(body.Type)
            )
        )
        {
            throw new InvalidDataException("The body catalog is empty or contains an incomplete body identity.");
        }
        if (bodies.Select(body => body.Number).Distinct().Count() != bodies.Count)
        {
            throw new InvalidDataException("The body catalog contains conflicting body identities.");
        }
    }

    /// <summary>Combines recovered records by journal BodyID, preserving known Raven fields on matching bodies.</summary>
    public static IReadOnlyList<ColonizationSystemBody> Merge(
        IEnumerable<ColonizationSystemBody> recovered,
        IReadOnlyList<ColonizationSystemBody>? existing = null
    )
    {
        var merged = new Dictionary<int, ColonizationSystemBody>();
        foreach (ColonizationSystemBody body in recovered)
        {
            merged[body.Number] = merged.TryGetValue(body.Number, out ColonizationSystemBody? prior)
                ? FillMissing(prior, body)
                : body;
        }
        foreach (ColonizationSystemBody body in existing ?? [])
        {
            if (merged.TryGetValue(body.Number, out ColonizationSystemBody? recoveredBody))
            {
                merged[body.Number] = FillMissing(body, recoveredBody);
            }
        }
        ColonizationSystemBody[] result = merged.Values.OrderBy(body => body.Number).ToArray();
        Validate(result);
        return OrderBelts(result);
    }

    private static ColonizationSystemBody FillMissing(ColonizationSystemBody preferred, ColonizationSystemBody fallback)
    {
        if (!string.Equals(preferred.Name.Trim(), fallback.Name.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Body {preferred.Number} has conflicting names: '{preferred.Name}' and '{fallback.Name}'."
            );
        }
        return preferred with
        {
            Type = preferred.Type is "" or "un" ? fallback.Type : preferred.Type,
            Subtype = preferred.Subtype ?? fallback.Subtype,
            Parents = preferred.Parents.Count > 0 ? preferred.Parents : fallback.Parents,
            DistanceLightSeconds = PreferKnownPhysicalValue(
                preferred.DistanceLightSeconds,
                fallback.DistanceLightSeconds
            ),
            Radius = PreferKnownPhysicalValue(preferred.Radius, fallback.Radius),
            Temperature = PreferKnownPhysicalValue(preferred.Temperature, fallback.Temperature),
            Gravity = PreferKnownPhysicalValue(preferred.Gravity, fallback.Gravity),
            Features = [.. preferred.Features, .. fallback.Features],
            ExtensionData = fallback
                .ExtensionData.Concat(preferred.ExtensionData)
                .GroupBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().Value.Clone(), StringComparer.Ordinal),
        };
    }

    private static double PreferKnownPhysicalValue(double known, double recovered) =>
        double.IsFinite(known) && known > 0 ? known : recovered;

    private static List<ColonizationSystemBody> OrderBelts(IEnumerable<ColonizationSystemBody> source)
    {
        var result = source.Where(body => body.Type != "ac").ToList();
        foreach (
            ColonizationSystemBody belt in source
                .Where(body => body.Type == "ac")
                .OrderBy(body => body.DistanceLightSeconds)
        )
        {
            int parent = belt.Parents.FirstOrDefault(-1);
            int index = result.FindIndex(body =>
                body.Parents.Contains(parent) && body.DistanceLightSeconds > belt.DistanceLightSeconds
            );
            if (index < 0)
            {
                index =
                    result.FindLastIndex(body =>
                        body.Number == parent
                        || body.Parents.Contains(parent)
                            && body.DistanceLightSeconds > 0
                            && body.DistanceLightSeconds <= belt.DistanceLightSeconds
                    ) + 1;
            }

            result.Insert(Math.Clamp(index, 0, result.Count), belt);
        }
        return result;
    }

    private static string TypeCode(string? value, bool star, bool barycentre)
    {
        if (barycentre)
        {
            return "bc";
        }

        if (star)
        {
            return StellarTypeCode(value);
        }
        if (value?.Contains("gas giant", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "gg";
        }

        return value?.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant() switch
        {
            "earthlike world" or "earthlike body" => "elw",
            "water world" => "ww",
            "ammonia world" => "aw",
            "high metal content body" or "high metal content world" => "hmc",
            "metal rich body" or "metalrich body" => "mrb",
            "rocky body" => "rb",
            "rocky ice body" or "rocky icy body" or "rocky ice world" => "ri",
            "icy body" or "ice body" => "ib",
            "water giant" or "water giant with life" => "wg",
            _ => "un",
        };
    }

    private static string StellarTypeCode(string? value)
    {
        if (
            value is "H" or "SupermassiveBlackHole"
            || value?.Contains("Black Hole", StringComparison.OrdinalIgnoreCase) == true
        )
        {
            return "bh";
        }

        if (value is "N" or "NS" || value?.Contains("Neutron", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "ns";
        }

        return
            value?.StartsWith('D') == true || value?.Contains("White Dwarf", StringComparison.OrdinalIgnoreCase) == true
            ? "wd"
            : "st";
    }

    private static List<int> Parents(JsonElement body, string key)
    {
        var result = new List<int>();
        if (body.TryGetProperty(key, out JsonElement parents) && parents.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement parent in parents.EnumerateArray())
            {
                if (parent.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (JsonElement value in parent.EnumerateObject().Select(pair => pair.Value))
                {
                    if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                    {
                        result.Add(number);
                    }
                }
            }
        }
        return result;
    }

    internal static string? Text(JsonElement root, string key) =>
        root.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static double? Number(JsonElement root, string key) =>
        root.TryGetProperty(key, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out double number)
        && double.IsFinite(number)
            ? number
            : null;

    private static bool Flag(JsonElement root, string key) =>
        root.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static bool BodyFlag(JsonElement body, bool journal, string local, string external) =>
        Flag(body, journal ? local : external);

    private static bool HasSubstance(string? value, string absent) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Equals(absent, StringComparison.OrdinalIgnoreCase)
        && !value.Equals("None", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> Features(JsonElement body, bool journal)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (BodyFlag(body, journal, "Landable", "isLandable"))
        {
            result.Add("landable");
        }

        if (BodyFlag(body, journal, "TidalLock", "rotationalPeriodTidallyLocked"))
        {
            result.Add("tidal");
        }

        string? atmosphere = Text(body, journal ? "Atmosphere" : "atmosphereType");
        if (HasSubstance(atmosphere, "No atmosphere"))
        {
            result.Add("atmosphere");
        }

        string? volcanism = Text(body, journal ? "Volcanism" : "volcanismType");
        if (HasSubstance(volcanism, "No volcanism"))
        {
            result.Add("volcanism");
        }

        string? terraform = Text(body, journal ? "TerraformState" : "terraformingState");
        if (terraform is "Terraformable" or "Terraforming" or "Terraformed" or "Candidate for terraforming")
        {
            result.Add("terraformable");
        }

        if (
            body.TryGetProperty(journal ? JournalRings : "rings", out JsonElement rings)
            && rings.ValueKind == JsonValueKind.Array
            && rings.GetArrayLength() > 0
        )
        {
            result.Add("rings");
        }

        return result;
    }

    private static void AddBelts(
        List<ColonizationSystemBody> target,
        ColonizationSystemBody star,
        JsonElement body,
        string key,
        bool kilometers,
        IReadOnlyList<ColonizationSystemBody>? existing = null
    )
    {
        if (!body.TryGetProperty(key, out JsonElement rings) || rings.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement ring in rings.EnumerateArray())
        {
            if (ring.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("An asteroid belt record is not an object.");
            }
            string? name = Text(ring, key == JournalRings ? "Name" : "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidDataException("An asteroid belt has no name.");
            }

            double radius = Number(ring, key == JournalRings ? "InnerRad" : "innerRadius") ?? 0;
            star.Features.Add("rings");
            int number = ResolveBeltNumber(target, existing ?? [], star.Number, name);
            target.Add(
                new ColonizationSystemBody
                {
                    Number = number,
                    Name = name,
                    Parents = [star.Number, .. star.Parents],
                    Type = "ac",
                    Subtype = Text(ring, key == JournalRings ? "RingClass" : "type")
                        ?.Replace("eRingClass_", string.Empty, StringComparison.Ordinal),
                    DistanceLightSeconds =
                        star.DistanceLightSeconds + radius * (kilometers ? 1000 : 1) / LightSecondMeters,
                }
            );
        }
    }

    private static int ResolveBeltNumber(
        List<ColonizationSystemBody> target,
        IReadOnlyList<ColonizationSystemBody> existing,
        int parent,
        string name
    )
    {
        ColonizationSystemBody[] matches = target
            .Concat(existing)
            .Where(item =>
                item.Type == "ac"
                && item.Parents.FirstOrDefault(-1) == parent
                && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)
            )
            .DistinctBy(item => item.Number)
            .ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidDataException($"The asteroid belt identity for '{name}' is ambiguous.");
        }
        ColonizationSystemBody? known = matches.SingleOrDefault();
        int number = known?.Number ?? checked(100_000 + parent * 100);
        while (
            known is null
            && (target.Exists(item => item.Number == number) || existing.Any(item => item.Number == number))
        )
        {
            number = checked(number + 1);
        }
        return number;
    }
}
