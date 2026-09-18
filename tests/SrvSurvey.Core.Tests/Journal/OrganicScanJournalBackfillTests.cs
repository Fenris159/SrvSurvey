using System.Globalization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Journal;

public sealed class OrganicScanJournalBackfillTests
{
    [Fact]
    public async Task ReadsMatchingScanOrganicWithinJournalLookback()
    {
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "Journal.2026-09-17T120000.01.log"),
            """
            {"timestamp":"2026-09-16T10:00:00Z","event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Bacterial_Genus_Name;","Species":"$Codex_Ent_Bacterial_01_Name;","Variant":"$Codex_Ent_Bacterial_01_A_Name;"}
            {"timestamp":"2026-09-17T19:00:00Z","event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Bacterial_Genus_Name;","Species":"$Codex_Ent_Bacterial_01_Name;","Variant":"$Codex_Ent_Bacterial_01_B_Name;"}
            {"timestamp":"2026-09-17T19:05:00Z","event":"ScanOrganic","ScanType":"Log","SystemAddress":99,"Body":2,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Species":"$Codex_Ent_Aleoids_01_Name;","Variant":"$Codex_Ent_Aleoids_01_B_Name;"}
            {"timestamp":"2026-09-17T20:00:00Z","event":"Music","MusicTrack":"NoTrack"}
            {"timestamp":"2026-09-17T19:10:00Z","event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2320404}
            """
        );

        IReadOnlyList<JournalEventEnvelope> events = await OrganicScanJournalBackfill.ReadAsync(
            temp.Path,
            systemAddress: 42,
            lookback: TimeSpan.FromHours(12)
        );

        // Newest journal timestamp is 20:00; 12h lookback excludes 16th 10:00.
        JournalEventEnvelope match = Assert.Single(events);
        Assert.Equal("ScanOrganic", match.EventName);
        Assert.Equal(DateTimeOffset.Parse("2026-09-17T19:00:00Z", CultureInfo.InvariantCulture), match.Timestamp);
    }

    [Fact]
    public async Task UsesNewestJournalTimestampNotWallClock()
    {
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "Journal.2026-09-10T080000.01.log"),
            """
            {"timestamp":"2026-09-10T08:00:00Z","event":"ScanOrganic","ScanType":"Analyse","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Bacterial_Genus_Name;","Species":"$Codex_Ent_Bacterial_01_Name;","Variant":"$Codex_Ent_Bacterial_01_B_Name;"}
            {"timestamp":"2026-09-10T09:00:00Z","event":"Shutdown"}
            """
        );

        IReadOnlyList<JournalEventEnvelope> events = await OrganicScanJournalBackfill.ReadAsync(
            temp.Path,
            systemAddress: 42,
            lookback: TimeSpan.FromHours(12)
        );

        Assert.Single(events);
    }

    [Fact]
    public async Task SkipsOlderFilesOutsideCandidateWindow()
    {
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "Journal.2026-09-01T120000.01.log"),
            """
            {"timestamp":"2026-09-01T12:00:00Z","event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Bacterial_Genus_Name;","Species":"$Codex_Ent_Bacterial_01_Name;","Variant":"$Codex_Ent_Bacterial_01_B_Name;"}
            """
        );
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "Journal.2026-09-17T120000.01.log"),
            """
            {"timestamp":"2026-09-17T20:00:00Z","event":"Music","MusicTrack":"NoTrack"}
            """
        );

        IReadOnlyList<JournalEventEnvelope> events = await OrganicScanJournalBackfill.ReadAsync(
            temp.Path,
            systemAddress: 42,
            lookback: TimeSpan.FromHours(12)
        );

        Assert.Empty(events);
    }

    [Fact]
    public async Task OpensJournalsWithSharedReadWriteAccess()
    {
        using var temp = new TemporaryDirectory();
        string path = Path.Combine(temp.Path, "Journal.2026-09-17T120000.01.log");
        await File.WriteAllTextAsync(
            path,
            """
            {"timestamp":"2026-09-17T19:00:00Z","event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Bacterial_Genus_Name;","Species":"$Codex_Ent_Bacterial_01_Name;","Variant":"$Codex_Ent_Bacterial_01_B_Name;"}
            {"timestamp":"2026-09-17T20:00:00Z","event":"Music","MusicTrack":"NoTrack"}
            """
        );

        await using var writer = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete
        );

        IReadOnlyList<JournalEventEnvelope> events = await OrganicScanJournalBackfill.ReadAsync(
            temp.Path,
            systemAddress: 42,
            lookback: TimeSpan.FromHours(12)
        );

        Assert.Single(events);
    }

    [Fact]
    public async Task BackfilledScanOrganicMarksSystemOrganismScanned()
    {
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "Journal.2026-09-17T120000.01.log"),
            """
            {"timestamp":"2026-09-17T19:00:00Z","event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida","Species":"$Codex_Ent_Aleoids_01_Name;","Species_Localised":"Aleoida Arcus","Variant":"$Codex_Ent_Aleoids_01_B_Name;","Variant_Localised":"Aleoida Arcus - Green"}
            {"timestamp":"2026-09-17T20:00:00Z","event":"Music","MusicTrack":"NoTrack"}
            """
        );

        IReadOnlyList<JournalEventEnvelope> events = await OrganicScanJournalBackfill.ReadAsync(
            temp.Path,
            systemAddress: 42,
            lookback: TimeSpan.FromHours(12)
        );

        var state = new SrvSurvey.Core.Exploration.SystemScanState();
        state.Apply(
            JournalEventEnvelope.TryParse(
                """{"timestamp":"2026-09-17T18:00:00Z","event":"Location","StarSystem":"Test","SystemAddress":42}""",
                out JournalEventEnvelope? location,
                out _
            )
                ? location!
                : throw new InvalidOperationException()
        );
        state.Apply(
            JournalEventEnvelope.TryParse(
                """{"timestamp":"2026-09-17T18:00:01Z","event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}""",
                out JournalEventEnvelope? signals,
                out _
            )
                ? signals!
                : throw new InvalidOperationException()
        );
        foreach (JournalEventEnvelope organic in events)
        {
            state.Apply(organic);
        }

        SrvSurvey.Core.Exploration.SystemOrganismSnapshot organism = Assert.Single(
            Assert.Single(state.CreateSnapshot().Bodies).Organisms
        );
        Assert.True(organism.IsScanned);
        Assert.Equal("Aleoida Arcus - Green", organism.VariantLocalized);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "SrvSurvey-OrganicScan-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup for temp journal fixtures.
            }
        }
    }
}
