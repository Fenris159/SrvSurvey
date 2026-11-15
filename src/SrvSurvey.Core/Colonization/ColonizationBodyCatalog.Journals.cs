using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Colonization;

public static partial class ColonizationBodyCatalog
{
    public static IReadOnlyList<ColonizationSystemBody> FromJournal(
        IEnumerable<JournalEventEnvelope> scans,
        string systemName,
        IReadOnlyDictionary<int, HashSet<string>> signalFeatures
    )
    {
        var result = new List<ColonizationSystemBody>();
        foreach (JournalEventEnvelope scan in scans)
        {
            JsonElement root = scan.Payload;
            int id = root.GetProperty("BodyID").GetInt32();
            bool barycentre = scan.EventName == "ScanBaryCentre";
            string name = barycentre
                ? $"{systemName} barycentre {id}"
                : Text(root, "BodyName") ?? throw new InvalidDataException($"Local scan {id} has no body name.");
            if (
                name.Contains("cluster", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("Ring", StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }
            string? starType = Text(root, "StarType");
            string? planetClass = Text(root, "PlanetClass");
            string type = TypeCode(starType ?? planetClass, starType is not null, barycentre);
            if (type == "un")
            {
                throw new InvalidDataException($"The local body type for '{name}' is unknown.");
            }

            ColonizationSystemBody body = new()
            {
                Number = id,
                Name = name,
                Type = type,
                Subtype = starType ?? planetClass,
                Parents = Parents(root, "Parents"),
                DistanceLightSeconds = Number(root, "DistanceFromArrivalLS") ?? 0,
                Radius = Number(root, "Radius") ?? -1,
                Temperature = Number(root, "SurfaceTemperature") ?? -1,
                Gravity = Number(root, "SurfaceGravity") is { } gravity ? gravity / StandardGravity : -1,
                Features = Features(root, true),
            };
            body.Features.UnionWith(signalFeatures.GetValueOrDefault(id) ?? []);
            result.Add(body);
            if (starType is not null)
            {
                AddBelts(result, body, root, "Rings", false);
            }
        }
        return Merge(result);
    }
}
