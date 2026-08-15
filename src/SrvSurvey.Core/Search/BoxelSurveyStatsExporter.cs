using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Search;

public static class BoxelSurveyStatsExporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public static bool MeetsExportMinimum(
        BoxelSurveyBoxelSnapshot snapshot,
        int minSystemsForExport)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Visited >= Math.Max(1, minSystemsForExport);
    }

    public static string ToJson(BoxelSurveyBoxelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var systems = new JsonArray();
        foreach (var system in document.Systems)
        {
            var bodies = new JsonArray();
            foreach (var body in system.Bodies)
            {
                bodies.Add(new JsonObject
                {
                    ["bodyId"] = body.BodyId,
                    ["class"] = (int)body.Class,
                    ["planetClass"] = BoxelPlanetClassifier.ToPlanetClassString(body.Class),
                    ["terraformable"] = body.Terraformable,
                    ["landable"] = body.Landable,
                    ["atmospheric"] = body.Atmospheric,
                    ["massEm"] = body.MassEm,
                    ["heliumPercent"] = body.HeliumPercent,
                    ["scanValue"] = body.ScanValue,
                    ["currentValue"] = body.CurrentValue,
                    ["mappedPotentialValue"] = body.MappedPotentialValue,
                    ["wasDiscovered"] = body.WasDiscovered,
                    ["wasMapped"] = body.WasMapped,
                    ["dssComplete"] = body.DssComplete,
                    ["dssEfficiencyBonus"] = body.DssEfficiencyBonus,
                });
            }

            systems.Add(new JsonObject
            {
                ["generatedName"] = system.GeneratedName,
                ["systemAddress"] = system.SystemAddress,
                ["n2"] = system.N2,
                ["lastVisited"] = system.LastVisited,
                ["fssDiscoveryBodyCount"] = system.FssDiscoveryBodyCount,
                ["allBodiesFound"] = system.AllBodiesFound,
                ["navBeaconScanned"] = system.NavBeaconScanned,
                ["minHeliumPercent"] = system.MinHeliumPercent,
                ["maxHeliumPercent"] = system.MaxHeliumPercent,
                ["scanValue"] = system.ScanValue,
                ["currentValue"] = system.CurrentValue,
                ["mappedPotentialValue"] = system.MappedPotentialValue,
                ["bodies"] = bodies,
            });
        }

        return JsonSerializer.Serialize(
            new JsonObject
            {
                ["prefix"] = document.Prefix,
                ["boxelId64"] = document.BoxelId64,
                ["lastVisited"] = document.LastVisited,
                ["minHeliumPercent"] = document.MinHeliumPercent,
                ["maxHeliumPercent"] = document.MaxHeliumPercent,
                ["systems"] = systems,
            },
            SerializerOptions);
    }

    public static string ToDetailCsv(
        BoxelSurveyBoxelSnapshot snapshot,
        BoxelSurveyAverageFormat format = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var csv = new StringBuilder();
        csv.AppendLine("Metric,Value");
        Append(csv, "Prefix", snapshot.Prefix);
        Append(csv, "Visited", snapshot.Visited);
        Append(csv, "ImpliedPopulation", snapshot.ImpliedPopulation);
        Append(csv, "FssComplete", snapshot.FssCompleteCount);
        Append(csv, "NavBeacon", snapshot.NavBeaconCount);
        Append(csv, "MinHeliumPercent", snapshot.MinHeliumPercent);
        Append(csv, "MaxHeliumPercent", snapshot.MaxHeliumPercent);
        Append(csv, "CurrentValue", snapshot.CurrentValue);
        Append(csv, "MappedPotentialValue", snapshot.MappedPotentialValue);
        Append(csv, "FssBodies", snapshot.FssDiscoveryBodyCountSum);
        csv.AppendLine();
        csv.AppendLine("Class,Code,Count,Average,Terraformable,Landable,Atmospheric");
        foreach (var classified in Enum.GetValues<BoxelPlanetClass>())
        {
            if (classified == BoxelPlanetClass.Unknown)
            {
                continue;
            }

            var counts = snapshot.CountsOf(classified);
            csv.Append(Escape(classified.ToString())).Append(',');
            csv.Append(Escape(BoxelPlanetClassifier.ToPlanetClassString(classified))).Append(',');
            csv.Append(counts.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(Escape(BoxelSurveyAverageFormatter.Format(counts.Count, snapshot.Visited, format))).Append(',');
            csv.Append(counts.Terraformable.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(counts.Landable.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.AppendLine(counts.Atmospheric.ToString(CultureInfo.InvariantCulture));
        }

        return csv.ToString();
    }

    public static string ToIndexCsv(IEnumerable<BoxelSurveyBoxelSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var csv = new StringBuilder();
        csv.Append(
            "Prefix,MassCode,Visited,ImpliedPopulation,FssComplete,NavBeacon,MinHeliumPercent,MaxHeliumPercent,CurrentValue,MappedPotentialValue");
        foreach (var classified in Enum.GetValues<BoxelPlanetClass>())
        {
            if (classified != BoxelPlanetClass.Unknown)
            {
                csv.Append(',').Append(classified);
            }
        }

        csv.AppendLine();
        foreach (var snapshot in snapshots)
        {
            csv.Append(Escape(snapshot.Prefix)).Append(',');
            csv.Append(snapshot.MassCode).Append(',');
            csv.Append(snapshot.Visited.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(snapshot.ImpliedPopulation.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(snapshot.FssCompleteCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(snapshot.NavBeaconCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(FormatNumber(snapshot.MinHeliumPercent)).Append(',');
            csv.Append(FormatNumber(snapshot.MaxHeliumPercent)).Append(',');
            csv.Append(snapshot.CurrentValue.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(snapshot.MappedPotentialValue.ToString(CultureInfo.InvariantCulture));
            foreach (var classified in Enum.GetValues<BoxelPlanetClass>())
            {
                if (classified == BoxelPlanetClass.Unknown)
                {
                    continue;
                }

                csv.Append(',').Append(
                    snapshot.CountsOf(classified).Count.ToString(CultureInfo.InvariantCulture));
            }

            csv.AppendLine();
        }

        return csv.ToString();
    }

    private static void Append(StringBuilder csv, string name, object? value)
    {
        csv.Append(Escape(name)).Append(',').AppendLine(Escape(FormatNumber(value)));
    }

    private static string FormatNumber(object? value)
        => value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture)
                ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };

    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Contains(',', StringComparison.Ordinal)
            || text.Contains('"', StringComparison.Ordinal)
            || text.Contains('\n', StringComparison.Ordinal))
        {
            return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return text;
    }
}
