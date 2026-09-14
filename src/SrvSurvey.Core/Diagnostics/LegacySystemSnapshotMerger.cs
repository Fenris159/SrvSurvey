using System.Globalization;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Exploration;

namespace SrvSurvey.Core.Diagnostics;

public static class LegacySystemSnapshotMerger
{
    public static JsonObject Merge(
        JsonObject? existing,
        SystemScanSnapshot snapshot,
        string? commanderName,
        DateTimeOffset firstVisited,
        DateTimeOffset lastVisited
    )
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (
            snapshot.SystemAddress is not { } systemAddress
            || systemAddress <= 0
            || string.IsNullOrWhiteSpace(snapshot.SystemName)
        )
        {
            throw new ArgumentException(
                "A named system snapshot with a positive address is required.",
                nameof(snapshot)
            );
        }

        JsonObject root = existing?.DeepClone() as JsonObject ?? [];
        root["name"] = snapshot.SystemName;
        root["address"] = systemAddress;
        if (snapshot.StarPosition is { } position)
        {
            root["starPos"] = new JsonArray(position.X, position.Y, position.Z);
        }

        if (!string.IsNullOrWhiteSpace(commanderName))
        {
            root["commander"] = commanderName;
        }

        WriteEarlierTimestamp(root, "firstVisited", firstVisited);
        WriteLaterTimestamp(root, "lastVisited", lastVisited);
        WriteMaximum(root, "bodyCount", snapshot.ExpectedBodyCount);
        WriteTrue(root, "honked", snapshot.HasDiscoveryScan);
        WriteTrue(root, "fssAllBodies", snapshot.AllBodiesFound);
        if (snapshot.Population > 0 || root["population"] is null)
        {
            root["population"] = snapshot.Population;
        }

        JsonArray bodies = GetOrCreateArray(root, "bodies");
        foreach (SystemScanBodySnapshot bodySnapshot in snapshot.Bodies)
        {
            JsonObject body = FindOrCreateBody(bodies, bodySnapshot);
            MergeBody(body, bodySnapshot);
        }

        return root;
    }

    private static void MergeBody(JsonObject body, SystemScanBodySnapshot snapshot)
    {
        body["name"] = snapshot.Name;
        body["id"] = snapshot.BodyId;
        if (snapshot.Kind != SystemBodyKind.Unknown)
        {
            body["type"] = GetLegacyBodyType(snapshot.Kind);
        }

        WriteTrue(body, "scanned", snapshot.IsScanned);
        WriteTrue(body, "dssComplete", snapshot.IsDssComplete);
        WriteTrue(body, "terraformable", snapshot.IsTerraformable);
        WriteTrue(body, "wasDiscovered", snapshot.WasDiscovered);
        WriteTrue(body, "wasMapped", snapshot.WasMapped);
        WriteTrue(body, "firstFootFall", snapshot.IsFirstFootfall);
        if (snapshot.WasFootfalled is { } wasFootfalled)
        {
            body["wasFootfalled"] = ReadBoolean(body["wasFootfalled"]) ?? wasFootfalled;
            if (wasFootfalled)
            {
                body["wasFootfalled"] = true;
            }
        }

        if (snapshot.TidalLock is { } tidalLock)
        {
            body["tidalLock"] = tidalLock;
        }

        WriteString(body, "starType", snapshot.StarClass);
        WriteString(body, "planetClass", snapshot.PlanetClass);
        WriteString(body, "atmosphere", snapshot.Atmosphere, allowEmpty: true);
        WriteString(body, "atmosphereType", snapshot.AtmosphereType);
        WriteString(body, "volcanism", snapshot.Volcanism, allowEmpty: true);
        WriteNonZero(body, "mass", snapshot.Mass);
        WriteNonZero(body, "distanceFromArrivalLS", snapshot.DistanceFromArrivalLs);
        WriteNonZero(body, "radius", snapshot.RadiusMeters);
        WriteNonZero(body, "surfaceGravity", snapshot.SurfaceGravity);
        WriteNonZero(body, "surfaceTemperature", snapshot.SurfaceTemperature);
        WriteNonZero(body, "surfacePressure", snapshot.SurfacePressure);
        WriteNonZero(body, "semiMajorAxis", snapshot.SemiMajorAxis);
        WriteNonZero(body, "absoluteMagnitude", snapshot.AbsoluteMagnitude);
        WriteMaximum(body, "bioSignalCount", snapshot.BiologicalSignalCount);
        WriteMaximum(body, "geoSignalCount", snapshot.GeologicalSignalCount);
        MergeComposition(body, "atmosphereComposition", snapshot.AtmosphereComposition);
        MergeComposition(body, "materials", snapshot.Materials);
        MergeRings(body, snapshot.Rings);
        if (body["parents"] is null && snapshot.Parents.Count > 0)
        {
            body["parents"] = new JsonArray(
                snapshot
                    .Parents.Select(parent =>
                        (JsonNode)new JsonObject { ["type"] = parent.Kind.ToString(), ["id"] = parent.BodyId }
                    )
                    .ToArray()
            );
        }

        MergeOrganisms(body, snapshot.Organisms);
    }

    private static void MergeComposition(
        JsonObject owner,
        string propertyName,
        IReadOnlyDictionary<string, double> values
    )
    {
        if (values.Count == 0)
        {
            return;
        }

        JsonObject composition = GetOrCreateObject(owner, propertyName);
        foreach (KeyValuePair<string, double> pair in values)
        {
            composition[pair.Key] ??= pair.Value;
        }
    }

    private static void MergeRings(JsonObject body, IReadOnlyList<SystemRingSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        JsonArray rings = GetOrCreateArray(body, "rings");
        foreach (SystemRingSnapshot snapshot in snapshots)
        {
            JsonObject ring =
                rings
                    .OfType<JsonObject>()
                    .FirstOrDefault(candidate =>
                        string.Equals(ReadString(candidate["name"]), snapshot.Name, StringComparison.OrdinalIgnoreCase)
                    )
                ?? [];
            if (ring.Parent is null)
            {
                rings.Add(ring);
            }

            ring["name"] = snapshot.Name;
            WriteString(ring, "ringClass", snapshot.RingClass);
            WriteNonZero(ring, "innerRad", snapshot.InnerRadius);
            WriteNonZero(ring, "outerRad", snapshot.OuterRadius);
        }
    }

    private static void MergeOrganisms(JsonObject body, IReadOnlyList<SystemOrganismSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        JsonArray organisms = GetOrCreateArray(body, "organisms");
        foreach (SystemOrganismSnapshot snapshot in snapshots)
        {
            JsonObject organism = FindOrganism(organisms, snapshot) ?? [];
            if (organism.Parent is null)
            {
                organisms.Add(organism);
            }

            organism["genus"] = snapshot.Genus;
            WriteString(organism, "genusLocalized", snapshot.GenusLocalized);
            WriteString(organism, "species", snapshot.Species);
            WriteString(organism, "speciesLocalized", snapshot.SpeciesLocalized);
            WriteString(organism, "variant", snapshot.Variant);
            WriteString(organism, "variantLocalized", snapshot.VariantLocalized);
            if (snapshot.EntryId is > 0)
            {
                organism["entryId"] ??= snapshot.EntryId.Value;
            }

            if (snapshot.Reward is > 0)
            {
                organism["reward"] ??= snapshot.Reward.Value;
            }

            WriteTrue(organism, "scanned", snapshot.IsScanned);
            WriteTrue(organism, "analyzed", snapshot.IsAnalyzed);
            WriteTrue(organism, "isNewEntry", snapshot.IsRegionalFirst);
        }
    }

    private static JsonObject? FindOrganism(JsonArray organisms, SystemOrganismSnapshot snapshot)
    {
        return OrganismIdentityMatcher.FindBestMatch(
            organisms.OfType<JsonObject>(),
            new OrganismIdentity(snapshot.Genus, snapshot.EntryId, snapshot.Variant, snapshot.Species),
            candidate => new OrganismIdentity(
                ReadString(candidate["genus"]),
                ReadInt64(candidate["entryId"]),
                ReadString(candidate["variant"]),
                ReadString(candidate["species"])
            )
        );
    }

    private static JsonObject FindOrCreateBody(JsonArray bodies, SystemScanBodySnapshot snapshot)
    {
        foreach (JsonNode? node in bodies)
        {
            if (
                node is JsonObject candidate
                && (
                    ReadInt32(candidate["id"]) == snapshot.BodyId
                    || string.Equals(ReadString(candidate["name"]), snapshot.Name, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                return candidate;
            }
        }

        var body = new JsonObject();
        bodies.Add(body);
        return body;
    }

    private static JsonArray GetOrCreateArray(JsonObject owner, string propertyName)
    {
        if (owner[propertyName] is null)
        {
            var created = new JsonArray();
            owner[propertyName] = created;
            return created;
        }

        return owner[propertyName] as JsonArray
            ?? throw new InvalidDataException(
                $"The legacy '{propertyName}' value is malformed and was not overwritten."
            );
    }

    private static JsonObject GetOrCreateObject(JsonObject owner, string propertyName)
    {
        if (owner[propertyName] is null)
        {
            var created = new JsonObject();
            owner[propertyName] = created;
            return created;
        }

        return owner[propertyName] as JsonObject
            ?? throw new InvalidDataException(
                $"The legacy '{propertyName}' value is malformed and was not overwritten."
            );
    }

    private static string GetLegacyBodyType(SystemBodyKind kind)
    {
        return kind switch
        {
            SystemBodyKind.Star => "Star",
            SystemBodyKind.GasGiant => "Giant",
            SystemBodyKind.Planet => "SolidBody",
            SystemBodyKind.LandablePlanet => "LandableBody",
            SystemBodyKind.Asteroid => "Asteroid",
            SystemBodyKind.Ring => "PlanetaryRing",
            SystemBodyKind.Barycentre => "Barycentre",
            _ => "Unknown",
        };
    }

    private static void WriteEarlierTimestamp(JsonObject owner, string propertyName, DateTimeOffset value)
    {
        DateTimeOffset? existing = ReadTimestamp(owner[propertyName]);
        if (existing is null || value < existing)
        {
            owner[propertyName] = value.ToString("O", CultureInfo.InvariantCulture);
        }
    }

    private static void WriteLaterTimestamp(JsonObject owner, string propertyName, DateTimeOffset value)
    {
        DateTimeOffset? existing = ReadTimestamp(owner[propertyName]);
        if (existing is null || value > existing)
        {
            owner[propertyName] = value.ToString("O", CultureInfo.InvariantCulture);
        }
    }

    private static void WriteString(JsonObject owner, string propertyName, string? value, bool allowEmpty = false)
    {
        if (value is not null && (allowEmpty || !string.IsNullOrWhiteSpace(value)))
        {
            owner[propertyName] = value;
        }
    }

    private static void WriteNonZero(JsonObject owner, string propertyName, double value)
    {
        if (double.IsFinite(value) && (value != 0 || owner[propertyName] is null))
        {
            owner[propertyName] = value;
        }
    }

    private static void WriteMaximum(JsonObject owner, string propertyName, int value)
    {
        int existing = ReadInt32(owner[propertyName]) ?? 0;
        if (value > existing || owner[propertyName] is null)
        {
            owner[propertyName] = Math.Max(existing, value);
        }
    }

    private static void WriteTrue(JsonObject owner, string propertyName, bool value)
    {
        if (value || owner[propertyName] is null)
        {
            owner[propertyName] = value || ReadBoolean(owner[propertyName]) == true;
        }
    }

    private static string? ReadString(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out string? result) ? result : null;
    }

    private static int? ReadInt32(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out int result))
        {
            return result;
        }

        return
            value.TryGetValue<string>(out string? text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
            ? result
            : null;
    }

    private static long? ReadInt64(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out long result))
        {
            return result;
        }

        return
            value.TryGetValue<string>(out string? text)
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
            ? result
            : null;
    }

    private static bool? ReadBoolean(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<bool>(out bool result) ? result : null;
    }

    private static DateTimeOffset? ReadTimestamp(JsonNode? node)
    {
        return
            node is JsonValue value
            && value.TryGetValue<string>(out string? text)
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out global::System.DateTimeOffset result
            )
            ? result
            : null;
    }
}
