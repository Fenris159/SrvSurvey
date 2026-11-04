using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed partial class JourneyServiceTests
{
    private const string BoundaryJournal = "Journal.2026-07-01T000000.01.log";
    private const string BoundaryShot = "{\"timestamp\":\"2026-07-01T00:06:00Z\",\"event\":\"Screenshot\"}";

    [Fact]
    public async Task LegacyBoundaryRemainsUnknownUntilNewerActivityArrives()
    {
        JourneyDocument saved = await BeginBoundaryJourneyAsync();
        await new JourneyStore(DataDirectory).SaveAsync(
            saved with
            {
                WatermarkRecords = null,
                WatermarkRecordsComplete = null,
                RecordTrackingStart = null,
                RecordedJournalRecords = null,
            }
        );
        JourneyService legacy = BoundaryService([JournalDirectory, Path.Combine(temporaryDirectory, "unavailable")]);
        await legacy.InitializeActiveAsync("F123", true);
        var monitor = new JournalDirectoryMonitor(JournalDirectory, "F123");
        await monitor.PollAsync();
        await File.AppendAllTextAsync(Path.Combine(JournalDirectory, BoundaryJournal), BoundaryShot + "\n");
        await legacy.ApplyLiveAsync((await monitor.PollAsync()).JournalEvents);
        Assert.Equal(2, legacy.ActiveJourney!.CurrentSystem!.Counts.Screenshots);

        JourneyService resumed = BoundaryService([JournalDirectory, Path.Combine(temporaryDirectory, "unavailable")]);
        await resumed.InitializeActiveAsync("F123", true);
        Assert.Equal(2, resumed.ActiveJourney!.CurrentSystem!.Counts.Screenshots);
    }

    [Fact]
    public async Task EqualWatermarkCatchUpSurvivesAnEditAndRestart()
    {
        await BeginBoundaryJourneyAsync();
        await File.AppendAllTextAsync(Path.Combine(JournalDirectory, BoundaryJournal), BoundaryShot + "\n");
        JourneyService resumed = BoundaryService([JournalDirectory, Path.Combine(temporaryDirectory, "unavailable")]);
        JourneyServiceResult result = await resumed.InitializeActiveAsync("F123", true);
        Assert.Equal(2, result.Journey!.CurrentSystem!.Counts.Screenshots);
        JourneyDocument edited = await resumed.SaveMetadataAsync(result.Journey, "Renamed", "Kept");
        Assert.Equal(2, edited.CurrentSystem!.Counts.Screenshots);
        JourneyDocument stored = (await new JourneyStore(DataDirectory).LoadAsync("F123", edited.FileName)).Journey!;
        Assert.Equal(2, stored.CurrentSystem!.Counts.Screenshots);
    }

    [Fact]
    public async Task ShiftedCopyResumeDoesNotDuplicateSavedBoundaryCaptures()
    {
        JourneyDocument saved = await BeginBoundaryJourneyAsync();
        string shifted = Path.Combine(temporaryDirectory, "shifted");
        Directory.CreateDirectory(shifted);
        string original = Path.Combine(JournalDirectory, BoundaryJournal);
        string contents = await File.ReadAllTextAsync(original);
        await File.WriteAllTextAsync(
            Path.Combine(shifted, BoundaryJournal),
            contents.Replace(
                BoundaryShot,
                "{\"timestamp\":\"2026-07-01T00:05:30Z\",\"event\":\"Music\"}\n" + BoundaryShot,
                StringComparison.Ordinal
            )
        );
        File.Delete(original);
        JourneyService resumed = BoundaryService([shifted, Path.Combine(temporaryDirectory, "unavailable")]);
        JourneyServiceResult result = await resumed.InitializeActiveAsync("F123", true);
        Assert.Equal(1, result.Journey!.CurrentSystem!.Counts.Screenshots);
        JourneyDocument edited = await resumed.SaveMetadataAsync(saved, "Relocated", "");
        Assert.Equal(1, edited.CurrentSystem!.Counts.Screenshots);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CoveringBoundaryCannotDiscardEarlierUnsavedActivity(
        bool newerStoredWatermark,
        bool divergentActivity
    )
    {
        await WriteJournalAsync(
            BoundaryJournal,
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal("2026-07-01T00:05:00Z") + "\n"
        );
        bool reject = false;
        var store = new JourneyStore(
            DataDirectory,
            (path, token) =>
                reject
                    ? Task.FromException<FileStream>(new IOException("Storage temporarily unavailable"))
                    : FileMutationLease.AcquireAsync(path, token)
        );
        JourneyService pending = BoundaryService([JournalDirectory], store);
        JourneyJournalSystemSearchResult start = await pending.FindLatestStartAsync("F123", true, 42);
        JourneyDocument saved = (
            await pending.BeginAsync(new("F123", "Test", true, "Pending", "", start.Entry!))
        ).Journey!;
        reject = true;
        await Assert.ThrowsAsync<IOException>(() =>
            pending.ApplyLiveAsync([Parse(BoundaryShot) with { SourceRecordIdentity = "J1:1" }])
        );
        reject = false;
        JourneyService other = CreateService();
        await other.InitializeActiveAsync("F123", true);
        JournalEventEnvelope music = Parse("{\"timestamp\":\"2026-07-01T00:07:00Z\",\"event\":\"Music\"}") with
        {
            SourceRecordIdentity = "J2:1",
        };
        await other.ApplyLiveAsync([music]);
        if (newerStoredWatermark)
        {
            await other.ApplyLiveAsync([
                Parse("{\"timestamp\":\"2026-07-01T00:08:00Z\",\"event\":\"Music\"}") with
                {
                    SourceRecordIdentity = "J2:2",
                },
            ]);
        }
        if (divergentActivity)
        {
            JournalEventEnvelope touchdown = Parse(
                "{\"timestamp\":\"2026-07-01T00:07:00Z\",\"event\":\"Touchdown\",\"Body\":\"Sol A 1\"}"
            ) with
            {
                SourceRecordIdentity = "J2:2",
            };
            await other.ApplyLiveAsync([touchdown]);
            await pending.ApplyLiveAsync([music]);
            JourneyDocument conflicted = (
                await new JourneyStore(DataDirectory).LoadAsync("F123", saved.FileName)
            ).Journey!;
            Assert.Equal(1, conflicted.CurrentSystem!.Counts.Touchdowns);
            Assert.Equal(1, conflicted.CurrentSystem.Counts.Screenshots);
            Assert.Equal(1, pending.ActiveJourney!.CurrentSystem!.Counts.Screenshots);
            await pending.ApplyLiveAsync([touchdown]);
            Assert.Equal(1, pending.ActiveJourney.CurrentSystem.Counts.Touchdowns);
        }
        else
        {
            await pending.ApplyLiveAsync([music]);
        }
        Assert.Equal(1, pending.ActiveJourney!.CurrentSystem!.Counts.Screenshots);
        JourneyDocument stored = (await new JourneyStore(DataDirectory).LoadAsync("F123", saved.FileName)).Journey!;
        Assert.Equal(1, stored.CurrentSystem!.Counts.Screenshots);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 1, 0, newerStoredWatermark ? 8 : 7, 0, TimeSpan.Zero),
            stored.Watermark
        );
    }

    private async Task<JourneyDocument> BeginBoundaryJourneyAsync()
    {
        await WriteJournalAsync(
            BoundaryJournal,
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal("2026-07-01T00:05:00Z", BoundaryShot) + "\n"
        );
        JourneyService service = CreateService();
        JourneyJournalSystemSearchResult start = await service.FindLatestStartAsync("F123", true, 42);
        return (await service.BeginAsync(new("F123", "Test", true, "Boundary", "", start.Entry!))).Journey!;
    }

    private JourneyService BoundaryService(IReadOnlyList<string> folders, JourneyStore? store = null) =>
        new(
            store ?? new JourneyStore(DataDirectory),
            new JourneyJournalHistoryReader(folders),
            new CommanderProfileStore(DataDirectory),
            new ExobiologyReferenceCatalog([])
        );
}
