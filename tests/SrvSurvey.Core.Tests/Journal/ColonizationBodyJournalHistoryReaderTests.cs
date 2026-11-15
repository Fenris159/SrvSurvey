using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Journal;

public sealed class ColonizationBodyJournalHistoryReaderTests
{
    [Fact]
    public async Task ContinuationPartsInheritOnlyAVerifiedAdjacentSessionIdentity()
    {
        string directory = Path.Combine(Path.GetTempPath(), "raven-body-parts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Journal.2026-11-15T120000.01.log"),
                """
                {"event":"Fileheader","part":1}
                {"event":"Commander","FID":"F1","Name":"Test Cmdr"}
                {"event":"Scan","SystemAddress":42,"BodyID":0,"BodyName":"Example","StarType":"G"}
                """
            );
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Journal.2026-11-15T120000.02.log"),
                """
                {"event":"Fileheader","part":2}
                {"event":"Scan","SystemAddress":42,"BodyID":1,"BodyName":"Example 1","PlanetClass":"Rocky body"}
                {"event":"FSSAllBodiesFound","SystemAddress":42,"Count":2}
                """
            );
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Journal.2026-11-15T120000.04.log"),
                """
                {"event":"Fileheader","part":4}
                {"event":"Scan","SystemAddress":42,"BodyID":88}
                """
            );
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Journal.2026-11-15T130000.01.log"),
                """
                {"event":"Fileheader","part":1}
                {"event":"Scan","SystemAddress":42,"BodyID":99}
                """
            );

            IReadOnlyList<JournalEventEnvelope> entries = await ColonizationBodyJournalHistoryReader.ReadAsync(
                directory,
                42,
                "F1"
            );
            var catalog = new ColonizationBodyJournalCatalog(42, "Example");
            catalog.Apply(entries);
            Assert.True(catalog.CanUpload);
            Assert.Equal([0, 1], catalog.CreateBodies().Select(body => body.Number));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OldDetailsCanFillRecentBodiesWithoutCountingBodiesOutsideTheTenJumpWindow()
    {
        string directory = Path.Combine(Path.GetTempPath(), "raven-body-window-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string journal = """
                {"event":"Fileheader"}
                {"event":"Commander","FID":"F1","Name":"Test Cmdr"}
                {"event":"Scan","SystemAddress":42,"BodyID":99,"BodyName":"Example old","PlanetClass":"Rocky body"}
                {"event":"Scan","SystemAddress":42,"BodyID":1,"BodyName":"Example 1","PlanetClass":"Rocky body","TidalLock":true}
                """;
            journal +=
                "\n"
                + string.Join(
                    '\n',
                    Enumerable.Range(1, 11).Select(_ => "{\"event\":\"FSDJump\",\"SystemAddress\":43}")
                );
            journal +=
                "\n"
                + """
                    {"event":"Scan","SystemAddress":42,"BodyID":0,"BodyName":"Example","StarType":"G"}
                    {"event":"Scan","SystemAddress":42,"BodyID":1,"BodyName":"Example 1","PlanetClass":"Rocky body"}
                    {"event":"FSSAllBodiesFound","SystemAddress":42,"Count":2}
                    """;
            await File.WriteAllTextAsync(Path.Combine(directory, "Journal.2026-11-15T120000.01.log"), journal);
            IReadOnlyList<JournalEventEnvelope> entries = await ColonizationBodyJournalHistoryReader.ReadAsync(
                directory,
                42,
                "F1"
            );
            var catalog = new ColonizationBodyJournalCatalog(42, "Example");
            catalog.Apply(entries);
            Assert.Equal(2, catalog.ScannedBodyCount);
            Assert.True(catalog.CanUpload);
            Assert.Contains("tidal", catalog.CreateBodies().Single(body => body.Number == 1).Features);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RecoversOnlyTheRequestedCommanderAndSystemAndRetainsDetailedScans()
    {
        string directory = Path.Combine(Path.GetTempPath(), "raven-body-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Journal.2026-11-14T120000.01.log"),
                """
                {"event":"Fileheader"}
                {"event":"Commander","FID":"F1","Name":"Test Cmdr"}
                {"event":"Scan","SystemAddress":42,"BodyID":0,"BodyName":"Example","StarType":"G","Radius":695700000}
                {"event":"Scan","SystemAddress":42,"BodyID":1,"BodyName":"Example 1","PlanetClass":"Rocky body","Radius":1000000,"TidalLock":true}
                {"event":"Scan","SystemAddress":43,"BodyID":99}
                {"event":"Music","MusicTrack":"Exploration"}
                malformed
                """
            );
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Journal.2026-11-15T120000.01.log"),
                """
                {"event":"Fileheader"}
                {"event":"Commander","FID":"F1","Name":"Test Cmdr"}
                {"event":"Scan","SystemAddress":42,"BodyID":1,"BodyName":"Example 1","PlanetClass":"Rocky body"}
                {"event":"FSSAllBodiesFound","SystemAddress":42,"Count":2}
                {"event":"LoadGame","FID":"F2","Commander":"Other Cmdr"}
                {"event":"Scan","SystemAddress":42,"BodyID":88}
                """
            );

            IReadOnlyList<JournalEventEnvelope> entries = await ColonizationBodyJournalHistoryReader.ReadAsync(
                directory,
                42,
                "F1"
            );
            var catalog = new ColonizationBodyJournalCatalog(42, "Example");
            catalog.Apply(entries);

            Assert.Equal(4, entries.Count);
            Assert.Equal(2, catalog.ScannedBodyCount);
            Assert.True(catalog.CanUpload);
            ColonizationSystemBody planet = catalog.CreateBodies().Single(body => body.Number == 1);
            Assert.Equal(1000000, planet.Radius);
            Assert.Contains("tidal", planet.Features);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MissingHistoryDoesNotPreventWebRediscovery()
    {
        Assert.Empty(
            await ColonizationBodyJournalHistoryReader.ReadAsync(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
                42,
                "F1"
            )
        );
    }
}
