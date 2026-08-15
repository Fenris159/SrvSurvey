using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class BoxelSurveyStatsExporterTests
{
    [Fact]
    public void ExportMinimumUsesVisitedCount()
    {
        var snapshot = CreateSnapshot();
        Assert.False(BoxelSurveyStatsExporter.MeetsExportMinimum(snapshot, 5));
        Assert.True(BoxelSurveyStatsExporter.MeetsExportMinimum(snapshot, 1));
    }

    [Fact]
    public void JsonAndCsvContainPrefixHeliumAndClassCounts()
    {
        var state = new BoxelSurveyStatsState();
        state.Apply(Parse(
            """{"timestamp":"2026-07-10T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-0","SystemAddress":2001}"""));
        state.Apply(Parse(
            """{"event":"Scan","SystemAddress":2001,"BodyID":2,"PlanetClass":"Water world","MassEM":1,"AtmosphereComposition":[{"Name":"Helium","Percent":28.5}]}"""));
        Assert.True(state.TryCreateDocument("Praea Euq IL-P c5-", out var document));
        Assert.True(state.TryGet("Praea Euq IL-P c5-", out var snapshot));

        var json = BoxelSurveyStatsExporter.ToJson(document);
        var csv = BoxelSurveyStatsExporter.ToDetailCsv(snapshot);
        var index = BoxelSurveyStatsExporter.ToIndexCsv([snapshot]);

        Assert.Contains("Praea Euq IL-P c5-", json, StringComparison.Ordinal);
        Assert.Contains("28.5", json, StringComparison.Ordinal);
        Assert.Contains(
            $"\"class\": {(int)BoxelPlanetClass.WaterWorld}",
            json,
            StringComparison.Ordinal);
        using var exported = JsonDocument.Parse(json);
        var system = Assert.Single(exported.RootElement.GetProperty("systems").EnumerateArray());
        var body = Assert.Single(system.GetProperty("bodies").EnumerateArray());
        Assert.Equal("Water world", body.GetProperty("planetClass").GetString());
        Assert.Equal(1, body.GetProperty("massEm").GetDouble());
        Assert.False(body.GetProperty("wasDiscovered").GetBoolean());
        Assert.False(body.GetProperty("wasMapped").GetBoolean());
        Assert.False(body.GetProperty("dssComplete").GetBoolean());
        Assert.False(body.GetProperty("dssEfficiencyBonus").GetBoolean());
        Assert.Equal(
            document.Systems[0].ScanValue,
            system.GetProperty("scanValue").GetInt64());
        Assert.NotEqual(JsonValueKind.Null, system.GetProperty("lastVisited").ValueKind);
        Assert.Contains("Prefix,Praea Euq IL-P c5-", csv, StringComparison.Ordinal);
        Assert.Contains("WaterWorld", csv, StringComparison.Ordinal);
        Assert.Contains("Visited,ImpliedPopulation", index, StringComparison.Ordinal);
        Assert.Contains("Praea Euq IL-P c5-,c,1,1", index, StringComparison.Ordinal);
    }

    private static BoxelSurveyBoxelSnapshot CreateSnapshot()
    {
        var state = new BoxelSurveyStatsState();
        state.Apply(Parse(
            """{"timestamp":"2026-07-10T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-0","SystemAddress":2001}"""));
        Assert.True(state.TryGet("Praea Euq IL-P c5-", out var snapshot));
        return snapshot;
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }
}
