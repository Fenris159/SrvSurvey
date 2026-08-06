using System.Text.Json.Nodes;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Storage;

internal static class LegacySystemSnapshotParser
{
    public static SystemScanSnapshot Parse(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var systemName = ReadRequiredString(root, "name");
        var systemAddress = ReadRequiredInt64(root, "address");
        if (systemAddress <= 0)
        {
            throw new InvalidDataException(
                "The legacy system address is not positive.");
        }

        var bodies = ReadBodies(root, systemName);
        var expectedBodyCount = ReadOptionalInt32(root, "bodyCount") ?? 0;
        return new SystemScanSnapshot(
            systemName,
            systemAddress,
            ReadCoordinate(root["starPos"]),
            ReadOptionalInt64(root, "population") ?? 0,
            expectedBodyCount,
            ReadOptionalBoolean(root, "honked") ?? false,
            ReadOptionalBoolean(root, "fssAllBodies") ?? false,
            bodies.Count(body => body.CountsTowardFss),
            bodies.Count(body => body.IsScanned),
            bodies.Count(body => body.IsDssComplete),
            bodies.Sum(body => (long)body.CurrentScanValue),
            0,
            0,
            null,
            null,
            bodies);
    }

    private static List<SystemScanBodySnapshot> ReadBodies(
        JsonObject root,
        string systemName)
    {
        if (root["bodies"] is null)
        {
            return [];
        }

        if (root["bodies"] is not JsonArray array)
        {
            throw new InvalidDataException(
                "The legacy system bodies value is not an array.");
        }

        var bodies = new List<SystemScanBodySnapshot>(array.Count);
        var bodyIds = new HashSet<int>();
        foreach (var node in array)
        {
            if (node is not JsonObject body)
            {
                throw new InvalidDataException(
                    "A legacy system body is not an object.");
            }

            var bodyId = ReadRequiredInt32(body, "id");
            if (bodyId < 0 || !bodyIds.Add(bodyId))
            {
                throw new InvalidDataException(
                    $"The legacy system body ID {bodyId} is invalid or duplicated.");
            }

            var name = ReadRequiredString(body, "name");
            var kind = ReadBodyKind(ReadOptionalString(body, "type"));
            var parents = ReadParents(body);
            var organisms = ReadOrganisms(body);
            var geologicalSignals = ReadAnalyzedGeologicalSignals(body);
            var biologicalSignalCount = Math.Max(
                ReadOptionalInt32(body, "bioSignalCount") ?? 0,
                organisms.Count);
            var geologicalSignalCount = Math.Max(
                ReadOptionalInt32(body, "geoSignalCount") ?? 0,
                geologicalSignals.Length);
            bodies.Add(new SystemScanBodySnapshot(
                bodyId,
                name,
                GetShortName(name, systemName),
                kind,
                ReadOptionalString(body, "starType"),
                ReadOptionalString(body, "planetClass"),
                kind == SystemBodyKind.LandablePlanet,
                ReadOptionalBoolean(body, "terraformable") ?? false,
                ReadOptionalBoolean(body, "scanned") ?? false,
                ReadOptionalBoolean(body, "dssComplete") ?? false,
                ReadOptionalBoolean(body, "wasDiscovered") ?? false,
                ReadOptionalBoolean(body, "wasMapped") ?? false,
                ReadOptionalBoolean(body, "wasFootfalled"),
                ReadOptionalBoolean(body, "firstFootFall") ?? false,
                parents.Count > 0
                    && parents[0].Kind == SystemBodyParentKind.Ring,
                ReadOptionalBoolean(body, "tidalLock"),
                ReadOptionalDouble(body, "mass") ?? 0,
                ReadOptionalDouble(body, "distanceFromArrivalLS") ?? 0,
                ReadOptionalDouble(body, "radius") ?? 0,
                ReadOptionalDouble(body, "surfaceGravity") ?? 0,
                ReadOptionalDouble(body, "surfaceTemperature") ?? 0,
                ReadOptionalDouble(body, "surfacePressure") ?? 0,
                ReadOptionalDouble(body, "semiMajorAxis") ?? 0,
                ReadOptionalDouble(body, "absoluteMagnitude") ?? 0,
                ReadOptionalString(body, "atmosphere", allowEmpty: true),
                ReadOptionalString(body, "atmosphereType"),
                ReadOptionalString(body, "volcanism", allowEmpty: true),
                biologicalSignalCount,
                Math.Min(
                    biologicalSignalCount,
                    organisms.Count(organism => organism.IsAnalyzed)),
                geologicalSignalCount,
                Math.Min(geologicalSignalCount, geologicalSignals.Length),
                ReadOptionalInt32(body, "reward") ?? 0,
                ReadOptionalInt32(body, "reward") ?? 0,
                ReadOptionalInt32(body, "reward") ?? 0,
                0,
                ReadComposition(body, "atmosphereComposition"),
                ReadComposition(body, "materials"),
                ReadRings(body),
                parents,
                organisms,
                geologicalSignals));
        }

        return bodies;
    }

    private static List<SystemOrganismSnapshot> ReadOrganisms(
        JsonObject body)
    {
        if (body["organisms"] is null)
        {
            return [];
        }

        if (body["organisms"] is not JsonArray array)
        {
            throw new InvalidDataException(
                "A legacy system body organisms value is not an array.");
        }

        var organisms = new List<SystemOrganismSnapshot>(array.Count);
        foreach (var node in array)
        {
            if (node is not JsonObject organism)
            {
                throw new InvalidDataException(
                    "A legacy system organism is not an object.");
            }

            var genus = ReadRequiredString(organism, "genus");
            organisms.Add(new SystemOrganismSnapshot(
                genus,
                ReadOptionalString(organism, "genusLocalized"),
                ReadOptionalString(organism, "species"),
                ReadOptionalString(organism, "speciesLocalized"),
                ReadOptionalString(organism, "variant"),
                ReadOptionalString(organism, "variantLocalized"),
                ReadOptionalInt64(organism, "entryId"),
                ReadOptionalInt64(organism, "reward"),
                ReadOptionalBoolean(organism, "scanned") ?? false,
                ReadOptionalBoolean(organism, "analyzed") ?? false,
                ReadOptionalBoolean(organism, "isNewEntry") ?? false));
        }

        return organisms;
    }

    private static string[] ReadAnalyzedGeologicalSignals(
        JsonObject body)
    {
        if (body["geoSignals"] is null)
        {
            return [];
        }

        if (body["geoSignals"] is not JsonArray array)
        {
            throw new InvalidDataException(
                "A legacy system body geoSignals value is not an array.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in array)
        {
            if (node is not JsonObject signal)
            {
                throw new InvalidDataException(
                    "A legacy geological signal is not an object.");
            }

            var name = ReadOptionalString(signal, "nameLocalized")
                ?? ReadOptionalString(signal, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names.Order(StringComparer.Ordinal).ToArray();
    }

    private static Dictionary<string, double> ReadComposition(
        JsonObject owner,
        string propertyName)
    {
        if (owner[propertyName] is null)
        {
            return new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);
        }

        if (owner[propertyName] is not JsonObject values)
        {
            throw new InvalidDataException(
                $"The legacy {propertyName} value is not an object.");
        }

        var result = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            if (pair.Value is not JsonValue value
                || !value.TryGetValue<double>(out var number)
                || !double.IsFinite(number))
            {
                throw new InvalidDataException(
                    $"The legacy {propertyName} entry '{pair.Key}' is not numeric.");
            }

            result[pair.Key] = number;
        }

        return result;
    }

    private static List<SystemRingSnapshot> ReadRings(JsonObject body)
    {
        if (body["rings"] is null)
        {
            return [];
        }

        if (body["rings"] is not JsonArray array)
        {
            throw new InvalidDataException(
                "A legacy system body rings value is not an array.");
        }

        var rings = new List<SystemRingSnapshot>(array.Count);
        foreach (var node in array)
        {
            if (node is not JsonObject ring)
            {
                throw new InvalidDataException(
                    "A legacy system ring is not an object.");
            }

            rings.Add(new SystemRingSnapshot(
                ReadRequiredString(ring, "name"),
                ReadOptionalString(ring, "ringClass")
                    ?? ReadOptionalString(ring, "type"),
                ReadOptionalDouble(ring, "innerRad")
                    ?? ReadOptionalDouble(ring, "innerRadius")
                    ?? 0,
                ReadOptionalDouble(ring, "outerRad")
                    ?? ReadOptionalDouble(ring, "outerRadius")
                    ?? 0));
        }

        return rings;
    }

    private static List<SystemBodyParentSnapshot> ReadParents(
        JsonObject body)
    {
        if (body["parents"] is null)
        {
            return [];
        }

        if (body["parents"] is not JsonArray array)
        {
            throw new InvalidDataException(
                "A legacy system body parents value is not an array.");
        }

        var parents = new List<SystemBodyParentSnapshot>(array.Count);
        foreach (var node in array)
        {
            if (node is not JsonObject parent)
            {
                throw new InvalidDataException(
                    "A legacy system body parent is not an object.");
            }

            if (TryReadStoredParent(parent, out var storedParent))
            {
                parents.Add(storedParent);
                continue;
            }

            if (parent.Count != 1)
            {
                throw new InvalidDataException(
                    "A legacy system body parent is invalid.");
            }

            var pair = parent.GetAt(0);
            if (!Enum.TryParse<SystemBodyParentKind>(
                    pair.Key,
                    ignoreCase: true,
                    out var kind)
                || pair.Value is not JsonValue value
                || !value.TryGetValue<int>(out var bodyId)
                || bodyId < 0)
            {
                throw new InvalidDataException(
                    "A legacy system body parent is invalid.");
            }

            parents.Add(new SystemBodyParentSnapshot(kind, bodyId));
        }

        return parents;
    }

    private static bool TryReadStoredParent(
        JsonObject parent,
        out SystemBodyParentSnapshot snapshot)
    {
        snapshot = default!;
        if (parent["type"] is null && parent["id"] is null)
        {
            return false;
        }

        var type = ReadRequiredString(parent, "type");
        var bodyId = ReadRequiredInt32(parent, "id");
        if (!Enum.TryParse<SystemBodyParentKind>(
                type,
                ignoreCase: true,
                out var kind)
            || bodyId < 0)
        {
            throw new InvalidDataException(
                "A legacy system body parent is invalid.");
        }

        snapshot = new SystemBodyParentSnapshot(kind, bodyId);
        return true;
    }

    private static SystemBodyKind ReadBodyKind(string? value)
    {
        return value?.ToUpperInvariant() switch
        {
            "STAR" => SystemBodyKind.Star,
            "GIANT" or "GASGIANT" => SystemBodyKind.GasGiant,
            "SOLIDBODY" or "PLANET" => SystemBodyKind.Planet,
            "LANDABLEBODY" or "LANDABLEPLANET" =>
                SystemBodyKind.LandablePlanet,
            "ASTEROID" => SystemBodyKind.Asteroid,
            "PLANETARYRING" or "RING" => SystemBodyKind.Ring,
            "BARYCENTRE" => SystemBodyKind.Barycentre,
            null or "UNKNOWN" => SystemBodyKind.Unknown,
            _ => throw new InvalidDataException(
                $"The legacy system body type '{value}' is not recognized."),
        };
    }

    private static GalacticCoordinate? ReadCoordinate(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is not JsonArray values || values.Count < 3)
        {
            throw new InvalidDataException(
                "The legacy system starPos value is not a coordinate array.");
        }

        var coordinates = values.Take(3)
            .Select(value => value is JsonValue scalar
                && scalar.TryGetValue<double>(out var number)
                    ? number
                    : double.NaN)
            .ToArray();
        if (coordinates.Any(value => !double.IsFinite(value)))
        {
            throw new InvalidDataException(
                "The legacy system starPos coordinate is not numeric.");
        }

        return new GalacticCoordinate(
            coordinates[0],
            coordinates[1],
            coordinates[2]);
    }

    private static string GetShortName(string bodyName, string systemName)
    {
        var shortName = bodyName.StartsWith(
            systemName,
            StringComparison.Ordinal)
                ? bodyName[systemName.Length..]
                : bodyName;
        return shortName.Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string ReadRequiredString(
        JsonObject owner,
        string propertyName)
    {
        return ReadOptionalString(owner, propertyName)
            ?? throw new InvalidDataException(
                $"The legacy {propertyName} value is missing or invalid.");
    }

    private static string? ReadOptionalString(
        JsonObject owner,
        string propertyName,
        bool allowEmpty = false)
    {
        if (owner[propertyName] is null)
        {
            return null;
        }

        if (owner[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var text)
            && (allowEmpty || !string.IsNullOrWhiteSpace(text)))
        {
            return text;
        }

        throw new InvalidDataException(
            $"The legacy {propertyName} value is not a valid string.");
    }

    private static bool? ReadOptionalBoolean(
        JsonObject owner,
        string propertyName)
    {
        if (owner[propertyName] is null)
        {
            return null;
        }

        if (owner[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result))
        {
            return result;
        }

        throw new InvalidDataException(
            $"The legacy {propertyName} value is not Boolean.");
    }

    private static int ReadRequiredInt32(
        JsonObject owner,
        string propertyName)
    {
        return ReadOptionalInt32(owner, propertyName)
            ?? throw new InvalidDataException(
                $"The legacy {propertyName} value is missing or invalid.");
    }

    private static int? ReadOptionalInt32(
        JsonObject owner,
        string propertyName)
    {
        var number = ReadOptionalInt64(owner, propertyName);
        if (number is null)
        {
            return null;
        }

        if (number is < int.MinValue or > int.MaxValue)
        {
            throw new InvalidDataException(
                $"The legacy {propertyName} value is outside the supported range.");
        }

        return (int)number.Value;
    }

    private static long ReadRequiredInt64(
        JsonObject owner,
        string propertyName)
    {
        return ReadOptionalInt64(owner, propertyName)
            ?? throw new InvalidDataException(
                $"The legacy {propertyName} value is missing or invalid.");
    }

    private static long? ReadOptionalInt64(
        JsonObject owner,
        string propertyName)
    {
        if (owner[propertyName] is null)
        {
            return null;
        }

        if (owner[propertyName] is JsonValue value
            && value.TryGetValue<long>(out var result))
        {
            return result;
        }

        throw new InvalidDataException(
            $"The legacy {propertyName} value is not an integer.");
    }

    private static double? ReadOptionalDouble(
        JsonObject owner,
        string propertyName)
    {
        if (owner[propertyName] is null)
        {
            return null;
        }

        if (owner[propertyName] is JsonValue value
            && value.TryGetValue<double>(out var result)
            && double.IsFinite(result))
        {
            return result;
        }

        throw new InvalidDataException(
            $"The legacy {propertyName} value is not finite and numeric.");
    }
}
