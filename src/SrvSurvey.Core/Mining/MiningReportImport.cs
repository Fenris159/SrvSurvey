using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace SrvSurvey.Core.Mining;

public sealed record MiningImportedReport(double Tons, int Asteroids, int Cores, int Engineering, Dictionary<string, string> Fields);

/// <summary>Imports summary CSVs without fabricating per-asteroid or per-refinement journal events.</summary>
public static class MiningReportImport
{
    public static IReadOnlyList<MiningSession> ReadCsv(string csv)
    {
        using var parser = new TextFieldParser(new StringReader(csv)) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var header = parser.ReadFields() ?? throw new JsonException("Missing report columns.");
        if (!header.Contains("timestamp_utc") && !header.Contains("Started")) throw new JsonException("Expected an EliteMining or SrvSurvey session CSV.");
        var output = new List<MiningSession>();
        while (!parser.EndOfData)
        {
            var row = parser.ReadFields()!;
            if (row.Length != header.Length) throw new JsonException("Report column count does not match its header.");
            output.Add(ReadRow(header, row));
        }
        return output;
    }
    private static MiningSession ReadRow(string[] header, string[] row)
    {
        var fields = header.Zip(row).ToDictionary(p => p.First, p => p.Second);
        string Get(string reference, string own) => fields.GetValueOrDefault(reference) ?? fields.GetValueOrDefault(own) ?? "";
        double Number(string reference, string own) => double.TryParse(Get(reference, own), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0 ? value : 0;
        if (!DateTimeOffset.TryParse(Get("timestamp_utc", "Started"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var started)) throw new JsonException("Invalid session date.");
        var duration = TimeSpan.TryParse(Get("elapsed", ""), CultureInfo.InvariantCulture, out var elapsed) ? elapsed : TimeSpan.FromMinutes(Number("", "ActiveMinutes"));
        if (duration < TimeSpan.Zero || duration > TimeSpan.FromDays(366)) throw new JsonException("Invalid session duration.");
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", started.ToString("O"), Get("system", "System"), Get("body", "Ring"))))[..16]);
        return new MiningSession
        {
            Id = id,
            Started = started,
            Ended = started + duration,
            System = Get("system", "System"),
            Ring = Get("body", "Ring"),
            Ship = Get("", "Ship"),
            Notes = Get("comment", "Notes"),
            ProspectorLimpets = (int)Number("prospectors_used", "Prospectors"),
            CollectorLimpets = (int)Number("", "Collectors"),
            Imported = new(Number("total_tons", "Tons"), (int)Number("asteroids_prospected", "Asteroids"), (int)Number("", "CoreHits"), (int)Number("", "EngineeringMaterials"), fields)
        };
    }
}
