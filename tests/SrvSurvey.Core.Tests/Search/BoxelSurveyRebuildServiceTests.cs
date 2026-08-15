using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class BoxelSurveyRebuildServiceTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-BoxelSurveyRebuild-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PassAIngestsSystemFilesAndIgnoresReward()
    {
        var systemDirectory = Path.Combine(temporaryDirectory, "systems", "F123");
        Directory.CreateDirectory(systemDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(systemDirectory, "Praea Euq IL-P c5-0_2001.json"),
            """
            {
              "name": "Praea Euq IL-P c5-0",
              "address": 2001,
              "lastVisited": "2026-07-10T12:00:00Z",
              "bodyCount": 4,
              "fssAllBodies": true,
              "bodies": [
                {
                  "id": 1,
                  "name": "Praea Euq IL-P c5-0 A",
                  "type": "Planet",
                  "planetClass": "Water world",
                  "terraformable": true,
                  "mass": 1,
                  "scanned": true,
                  "dssComplete": true,
                  "wasDiscovered": false,
                  "wasMapped": false,
                  "reward": 999999,
                  "atmosphereComposition": { "Helium": 27.4 }
                }
              ]
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(systemDirectory, "Sol_10477373803.json"),
            """
            {
              "name": "Sol",
              "address": 10477373803,
              "bodies": []
            }
            """);

        var state = new BoxelSurveyStatsState();
        var service = new BoxelSurveyRebuildService(temporaryDirectory, temporaryDirectory);
        var result = await service.RebuildAsync("F123", state);

        Assert.Equal(1, result.SystemFilesIngested);
        Assert.True(state.TryGet("Praea Euq IL-P c5-", out var snapshot));
        Assert.Equal(1, snapshot.CountsOf(BoxelPlanetClass.WaterWorld).Count);
        Assert.Equal(27.4, snapshot.MinHeliumPercent);
        Assert.NotEqual(999999, snapshot.CurrentValue);
        Assert.True(snapshot.CurrentValue > 0);
        Assert.Equal(1, state.BoxelCount);
    }

    [Fact]
    public async Task PassBReplaysMatchingJournalsAndSkipsOpenAndOtherCommanders()
    {
        var journalDirectory = Path.Combine(temporaryDirectory, "journals");
        Directory.CreateDirectory(journalDirectory);
        var matching = Path.Combine(journalDirectory, "Journal.2026-07-10T120000.01.log");
        var horizons = Path.Combine(journalDirectory, "Journal.2026-07-09T120000.01.log");
        var other = Path.Combine(journalDirectory, "Journal.2026-07-08T120000.01.log");
        var current = Path.Combine(journalDirectory, "Journal.2026-07-11T120000.01.log");
        await File.WriteAllTextAsync(
            matching,
            """
            {"timestamp":"2026-07-10T12:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-07-10T12:00:01Z","event":"Commander","FID":"F123","Name":"Drew"}
            {"timestamp":"2026-07-10T12:01:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-0","SystemAddress":2001}
            {"timestamp":"2026-07-10T12:02:00Z","event":"Scan","SystemAddress":2001,"BodyID":2,"PlanetClass":"Ammonia world","MassEM":0.8}
            {"timestamp":"2026-07-10T12:03:00Z","event":"Commander","FID":"F-OTHER","Name":"Other"}
            {"timestamp":"2026-07-10T12:04:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-9","SystemAddress":2099}
            {"timestamp":"2026-07-10T12:05:00Z","event":"Commander","FID":"F123","Name":"Drew"}

            """);
        await File.WriteAllTextAsync(
            horizons,
            """
            {"timestamp":"2026-07-09T12:00:00Z","event":"Fileheader","Odyssey":false}
            {"timestamp":"2026-07-09T12:00:01Z","event":"LoadGame","FID":"F123","Odyssey":false}
            {"timestamp":"2026-07-09T12:01:00Z","event":"FSDJump","StarSystem":"Wregoe BU-Y b2-0","SystemAddress":2002}
            {"timestamp":"2026-07-09T12:02:00Z","event":"Scan","SystemAddress":2002,"BodyID":1,"PlanetClass":"Icy body","MassEM":0.3}

            """);
        await File.WriteAllTextAsync(
            other,
            """
            {"timestamp":"2026-07-08T12:00:00Z","event":"Commander","FID":"F-OTHER","Name":"Other"}
            {"timestamp":"2026-07-08T12:01:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-1","SystemAddress":2011}

            """);
        await File.WriteAllTextAsync(
            current,
            """
            {"timestamp":"2026-07-11T12:00:00Z","event":"Commander","FID":"F123","Name":"Drew"}
            {"timestamp":"2026-07-11T12:01:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-4","SystemAddress":2014}

            """);

        var state = new BoxelSurveyStatsState();
        var service = new BoxelSurveyRebuildService(temporaryDirectory, journalDirectory);
        var result = await service.RebuildAsync("F123", state, current);

        Assert.Equal(2, result.JournalFilesProcessed);
        Assert.Equal(2, result.JournalFilesSkipped);
        Assert.True(state.TryGet("Praea Euq IL-P c5-", out var cubeA));
        Assert.Equal(1, cubeA.CountsOf(BoxelPlanetClass.AmmoniaWorld).Count);
        Assert.DoesNotContain(cubeA.Systems, system => system.N2 == 9);
        Assert.DoesNotContain(cubeA.Systems, system => system.N2 == 4);
        Assert.True(state.TryGet("Wregoe BU-Y b2-", out var cubeB));
        Assert.Equal(1, cubeB.CountsOf(BoxelPlanetClass.Icy).Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
