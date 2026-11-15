using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Journal;

public sealed class ColonizationBodyJournalHistoryReaderTests
{
    [Fact]
    public async Task ModernJournalsRemainRecentWhenTheDirectoryContainsALegacyFilenameArchive()
    {
        string directory = Path.Combine(Path.GetTempPath(), "raven-body-mixed-dates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            for (int part = 1; part <= 100; part++)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(directory, $"Journal.220901120000.{part:D2}.log"),
                    $$"""
                    {"event":"Fileheader","Odyssey":true,"part":{{part}}}
                    {"event":"Commander","FID":"F1","Name":"Test Cmdr"}
                    {"event":"FSDJump","SystemAddress":43}
                    """
                );
            }
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Journal.2026-11-15T120000.01.log"),
                """
                {"event":"Fileheader","Odyssey":true,"part":1}
                {"event":"Commander","FID":"F1","Name":"Test Cmdr"}
                {"event":"Scan","SystemAddress":42,"BodyID":0,"BodyName":"Example","StarType":"G"}
                {"event":"FSSAllBodiesFound","SystemAddress":42,"Count":1}
                """
            );
            var catalog = new ColonizationBodyJournalCatalog(42, "Example");
            catalog.Apply(await ColonizationBodyJournalHistoryReader.ReadAsync(directory, 42, "F1"));
            Assert.True(catalog.CanUpload);
            Assert.Equal([0], catalog.CreateBodies().Select(body => body.Number));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ThreeDigitContinuationPartsFollowNumericOrder()
    {
        string directory = Path.Combine(Path.GetTempPath(), "raven-body-numeric-parts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Journal.2026-11-15T120000.99.log"),
                """
                {"event":"Fileheader","Odyssey":true,"part":99}
                {"event":"Commander","FID":"F1","Name":"Test Cmdr"}
                {"event":"Scan","SystemAddress":42,"BodyID":0,"BodyName":"Example","StarType":"G"}
                """
            );
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Journal.2026-11-15T120000.100.log"),
                """
                {"event":"Fileheader","Odyssey":true,"part":100}
                {"event":"Scan","SystemAddress":42,"BodyID":1,"BodyName":"Example 1","PlanetClass":"Rocky body"}
                {"event":"FSSAllBodiesFound","SystemAddress":42,"Count":2}
                """
            );
            var catalog = new ColonizationBodyJournalCatalog(42, "Example");
            catalog.Apply(await ColonizationBodyJournalHistoryReader.ReadAsync(directory, 42, "F1"));
            Assert.True(catalog.CanUpload);
            Assert.Equal([0, 1], catalog.CreateBodies().Select(body => body.Number));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyGalaxyVisitsCannotEvictLiveHorizonsScansFromTheRecentWindow()
    {
        string directory = Path.Combine(Path.GetTempPath(), "raven-body-galaxies-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Journal.2026-11-14T120000.01.log"),
                """
                {"event":"Fileheader","gameversion":"4.1.0.0","part":1}
                {"event":"Commander","FID":"F1","Name":"Test Cmdr"}
                {"event":"LoadGame","FID":"F1","Commander":"Test Cmdr","Odyssey":false}
                {"event":"Scan","SystemAddress":42,"BodyID":0,"BodyName":"Example","StarType":"G"}
                {"event":"Scan","SystemAddress":42,"BodyID":1,"BodyName":"Example 1","PlanetClass":"Rocky body"}
                {"event":"FSSAllBodiesFound","SystemAddress":42,"Count":2}
                """
            );
            string legacy = """
                {"event":"Fileheader","gameversion":"3.8.0.0","part":1}
                {"event":"Commander","FID":"F1","Name":"Test Cmdr"}
                """;
            legacy += "\n" + string.Join('\n', Enumerable.Repeat("{\"event\":\"FSDJump\",\"SystemAddress\":43}", 11));
            legacy +=
                "\n{\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":99,\"BodyName\":\"Example legacy\",\"PlanetClass\":\"Rocky body\"}";
            await File.WriteAllTextAsync(Path.Combine(directory, "Journal.2026-11-15T120000.01.log"), legacy);
            // A new Live part cannot inherit the Legacy part's commander identity.
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Journal.2026-11-15T120000.02.log"),
                """
                {"event":"Fileheader","Odyssey":true,"part":2}
                {"event":"Scan","SystemAddress":42,"BodyID":88,"BodyName":"Example unattributed","PlanetClass":"Rocky body"}
                """
            );
            var catalog = new ColonizationBodyJournalCatalog(42, "Example");
            catalog.Apply(await ColonizationBodyJournalHistoryReader.ReadAsync(directory, 42, "F1"));
            Assert.True(catalog.CanUpload);
            Assert.Equal([0, 1], catalog.CreateBodies().Select(body => body.Number));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

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
                {"event":"Fileheader","Odyssey":true,"part":1}
                {"event":"Commander","FID":"F1","Name":"Test Cmdr"}
                {"event":"Scan","SystemAddress":42,"BodyID":0,"BodyName":"Example","StarType":"G"}
                """
            );
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Journal.2026-11-15T120000.02.log"),
                """
                {"event":"Fileheader","Odyssey":true,"part":2}
                {"event":"Scan","SystemAddress":42,"BodyID":1,"BodyName":"Example 1","PlanetClass":"Rocky body"}
                {"event":"FSSAllBodiesFound","SystemAddress":42,"Count":2}
                """
            );
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Journal.2026-11-15T120000.04.log"),
                """
                {"event":"Fileheader","Odyssey":true,"part":4}
                {"event":"Scan","SystemAddress":42,"BodyID":88}
                """
            );
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Journal.2026-11-15T130000.01.log"),
                """
                {"event":"Fileheader","Odyssey":true,"part":1}
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
                {"event":"Fileheader","Odyssey":true}
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
                {"event":"Fileheader","Odyssey":true}
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
                {"event":"Fileheader","Odyssey":true}
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
