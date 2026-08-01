using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Routes;

internal static class FollowRouteExportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly string[] CsvHeaders =
    [
        "Sequence",
        "System",
        "SystemAddress",
        "X",
        "Y",
        "Z",
        "Notes",
        "Refuel",
        "Neutron",
        "Body",
        "BodyId",
        "BodySubtype",
        "DistanceToArrivalLs",
        "EstimatedScanValue",
        "EstimatedMappingValue",
        "EstimatedBiologyValue",
        "Terraformable",
        "Biological",
        "Species",
        "BodyCompleted",
    ];

    public static async Task WriteSpanshAsync(
        FollowRouteDocument route,
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(route);
        var kind = ResolveSpanshKind(route);
        var root = new JsonObject
        {
            ["status"] = "ok",
            ["result"] = CreateSpanshResult(route, kind),
        };
        await WriteJsonAsync(path, root, cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteCsvAsync(
        FollowRouteDocument route,
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous);
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteLineAsync(string.Join(',', CsvHeaders))
            .ConfigureAwait(false);

        for (var hopIndex = 0; hopIndex < route.Hops.Count; hopIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hop = route.Hops[hopIndex];
            if (hop.BioTargets.Count == 0)
            {
                await writer.WriteLineAsync(CreateCsvRow(hopIndex, hop, null))
                    .ConfigureAwait(false);
                continue;
            }

            foreach (var target in hop.BioTargets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(CreateCsvRow(hopIndex, hop, target))
                    .ConfigureAwait(false);
            }
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static SpanshRouteKind ResolveSpanshKind(FollowRouteDocument route)
    {
        if (route.Kind == FollowRouteKind.FleetCarrier)
        {
            return SpanshRouteKind.FleetCarrier;
        }

        if (route.SourceSpanshKind is { } sourceKind)
        {
            return sourceKind;
        }

        if (route.Hops.SelectMany(hop => hop.BioTargets).Any(target =>
            target.IsBiological || target.Species.Count > 0))
        {
            return SpanshRouteKind.Exobiology;
        }

        return route.Hops.Any(hop => hop.BioTargets.Count > 0)
            ? SpanshRouteKind.Riches
            : SpanshRouteKind.Generic;
    }

    private static JsonNode CreateSpanshResult(
        FollowRouteDocument route,
        SpanshRouteKind kind)
    {
        if (kind == SpanshRouteKind.Trade)
        {
            return CreateTradeLegs(route.Hops);
        }

        var rows = new JsonArray(route.Hops
            .Select(hop => (JsonNode?)CreateSpanshHop(hop, kind))
            .ToArray());
        return kind switch
        {
            SpanshRouteKind.Tourist or SpanshRouteKind.Neutron =>
                new JsonObject { ["system_jumps"] = rows },
            SpanshRouteKind.Galaxy
                or SpanshRouteKind.FleetCarrier
                or SpanshRouteKind.Colonisation =>
                new JsonObject { ["jumps"] = rows },
            _ => rows,
        };
    }

    private static JsonObject CreateSpanshHop(
        FollowRouteHop hop,
        SpanshRouteKind kind)
    {
        var root = new JsonObject();
        root[kind is SpanshRouteKind.Tourist or SpanshRouteKind.Neutron
            ? "system"
            : "name"] = hop.Name;
        WriteOptional(root, "id64", hop.SystemAddress);
        WritePosition(root, hop);
        WriteOptional(root, "notes", hop.Notes);
        if (hop.Refuel)
        {
            root["must_refuel"] = true;
        }

        if (hop.Neutron)
        {
            root[kind is SpanshRouteKind.Tourist or SpanshRouteKind.Neutron
                ? "neutron_star"
                : "has_neutron"] = true;
        }

        if (kind == SpanshRouteKind.FleetCarrier
            && hop.Notes?.Contains("restock", StringComparison.OrdinalIgnoreCase) == true)
        {
            root["must_restock"] = true;
        }

        if (hop.BioTargets.Count > 0)
        {
            root["bodies"] = new JsonArray(hop.BioTargets
                .Select(target => (JsonNode?)CreateSpanshBody(hop.Name, target))
                .ToArray());
        }

        return root;
    }

    private static JsonObject CreateSpanshBody(
        string systemName,
        FollowRouteBioTarget target)
    {
        var root = new JsonObject
        {
            ["name"] = GetFullBodyName(systemName, target.BodyName),
        };
        WriteOptional(root, "id", target.BodyId);
        WriteOptional(root, "subtype", target.Subtype);
        WriteOptional(root, "distance_to_arrival", target.DistanceToArrivalLs);
        WriteOptional(root, "estimated_scan_value", target.EstimatedScanValue);
        WriteOptional(root, "estimated_mapping_value", target.EstimatedMappingValue);
        WriteOptional(root, "landmark_value", target.EstimatedBiologyValue);
        if (target.IsTerraformable)
        {
            root["terraforming_state"] = "Candidate for terraforming";
        }

        if (target.Species.Count > 0)
        {
            root["landmarks"] = new JsonArray(target.Species
                .Where(species => !string.IsNullOrWhiteSpace(species))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(species => (JsonNode?)new JsonObject
                {
                    ["subtype"] = species.Trim(),
                })
                .ToArray());
        }

        return root;
    }

    private static JsonArray CreateTradeLegs(IReadOnlyList<FollowRouteHop> hops)
    {
        var result = new JsonArray();
        for (var index = 0; index + 1 < hops.Count; index++)
        {
            result.Add(new JsonObject
            {
                ["source"] = CreateTradeStop(hops[index]),
                ["destination"] = CreateTradeStop(hops[index + 1]),
            });
        }

        return result;
    }

    private static JsonObject CreateTradeStop(FollowRouteHop hop)
    {
        var root = new JsonObject
        {
            ["system"] = hop.Name,
        };
        WriteOptional(root, "system_id64", hop.SystemAddress);
        WritePosition(root, hop);
        const string stationPrefix = "Station:";
        if (hop.Notes?.StartsWith(
                stationPrefix,
                StringComparison.OrdinalIgnoreCase) == true)
        {
            WriteOptional(root, "station", hop.Notes[stationPrefix.Length..].Trim());
        }

        return root;
    }

    private static void WritePosition(JsonObject root, FollowRouteHop hop)
    {
        if (hop.Position is not { } position)
        {
            return;
        }

        root["x"] = position.X;
        root["y"] = position.Y;
        root["z"] = position.Z;
    }

    private static string GetFullBodyName(string systemName, string bodyName)
    {
        var trimmedSystem = systemName.Trim();
        var trimmedBody = bodyName.Trim();
        return trimmedBody.StartsWith(
            trimmedSystem + " ",
            StringComparison.OrdinalIgnoreCase)
                ? trimmedBody
                : $"{trimmedSystem} {trimmedBody}";
    }

    private static string CreateCsvRow(
        int hopIndex,
        FollowRouteHop hop,
        FollowRouteBioTarget? target)
    {
        var values = new string?[]
        {
            (hopIndex + 1).ToString(CultureInfo.InvariantCulture),
            hop.Name,
            Format(hop.SystemAddress),
            Format(hop.Position?.X),
            Format(hop.Position?.Y),
            Format(hop.Position?.Z),
            hop.Notes,
            Format(hop.Refuel),
            Format(hop.Neutron),
            target?.BodyName,
            Format(target?.BodyId),
            target?.Subtype,
            Format(target?.DistanceToArrivalLs),
            Format(target?.EstimatedScanValue),
            Format(target?.EstimatedMappingValue),
            Format(target?.EstimatedBiologyValue),
            target is null ? null : Format(target.IsTerraformable),
            target is null ? null : Format(target.IsBiological),
            target is null ? null : string.Join("; ", target.Species),
            target is null ? null : Format(target.IsCompleted),
        };
        return string.Join(',', values.Select(EscapeCsv));
    }

    private static string? Format<T>(T? value) where T : struct, IFormattable
    {
        return value?.ToString(null, CultureInfo.InvariantCulture);
    }

    private static string Format(bool value)
    {
        return value ? "true" : "false";
    }

    private static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        return text.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? text
            : $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static void WriteOptional<T>(
        JsonObject root,
        string propertyName,
        T? value)
    {
        if (value is null)
        {
            return;
        }

        root[propertyName] = JsonValue.Create(value);
    }

    private static async Task WriteJsonAsync(
        string path,
        JsonObject root,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(
                stream,
                root,
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
