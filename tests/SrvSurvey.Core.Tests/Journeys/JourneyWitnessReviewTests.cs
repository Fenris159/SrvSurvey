using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed partial class JourneyServiceTests
{
    private const string RepeatedOrderMusic =
        "{\"timestamp\":\"2026-07-01T00:06:00Z\",\"event\":\"Music\",\"MusicTrack\":\"Exploration\"}";

    [Fact]
    public async Task PartialCopyKeepsARepeatedScreenshotBeforeItsFollowingArrival()
    {
        string[] folders = ["a-arrival", "b-placement", "c-repeated"];
        string[] suffixes =
        [
            OrderArrival,
            string.Join('\n', RepeatedOrderMusic, BoundaryShot, OrderArrival),
            BoundaryShot + "\n" + BoundaryShot,
        ];
        for (int index = 0; index < folders.Length; index++)
        {
            folders[index] = Path.Combine(temporaryDirectory, folders[index]);
            Directory.CreateDirectory(folders[index]);
            await File.WriteAllTextAsync(
                Path.Combine(folders[index], BoundaryJournal),
                BasicJournal("2026-07-01T00:05:00Z", suffixes[index] + "\n" + OrderMusic) + "\n"
            );
        }
        JourneyService service = BoundaryService(folders);
        JourneyJournalSystemSearchResult start = await service.FindLatestStartAsync("F123", true, 42);
        JourneyServiceResult result = await service.BeginAsync(
            new("F123", "Test", true, "Placement", "", start.Entry!)
        );

        Assert.Empty(result.Errors);
        Assert.Equal(["Sol", "Sirius"], result.Journey!.VisitedSystems.Select(visit => visit.StarSystem.Name));
        Assert.Equal([1, 1], result.Journey.VisitedSystems.Select(visit => visit.Counts.Screenshots));
        JourneyServiceResult resumed = await BoundaryService(folders).InitializeActiveAsync("F123", true);
        Assert.Empty(resumed.Errors);
        Assert.Equal([1, 1], resumed.Journey!.VisitedSystems.Select(visit => visit.Counts.Screenshots));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UniqueVisitOrderSurvivesRepeatedIntermediariesAndRejectsConflicts(bool conflicting)
    {
        string[] folders =
        [
            Path.Combine(temporaryDirectory, "a-first"),
            Path.Combine(temporaryDirectory, "b-chain"),
            Path.Combine(temporaryDirectory, "c-repeats"),
        ];
        string[] suffixes =
        [
            conflicting ? BoundaryShot + "\n" + OrderArrival : BoundaryShot,
            string.Join('\n', OrderArrival, RepeatedOrderMusic, BoundaryShot),
            RepeatedOrderMusic + "\n" + RepeatedOrderMusic,
        ];
        for (int index = 0; index < folders.Length; index++)
        {
            Directory.CreateDirectory(folders[index]);
            await File.WriteAllTextAsync(
                Path.Combine(folders[index], BoundaryJournal),
                BasicJournal("2026-07-01T00:05:00Z", suffixes[index] + "\n" + OrderMusic) + "\n"
            );
        }
        JourneyService service = BoundaryService(folders);
        JourneyJournalSystemSearchResult start = await service.FindLatestStartAsync("F123", true, 42);
        JourneyServiceResult result = await service.BeginAsync(
            new("F123", "Test", true, "Witnesses", "", start.Entry!)
        );
        if (conflicting)
        {
            Assert.Contains(
                result.Errors,
                error => error.Contains("conflicting record order", StringComparison.Ordinal)
            );
            Assert.Empty(
                (await new JourneyStore(DataDirectory).LoadAsync("F123", result.Journey!.FileName))
                    .Journey!
                    .VisitedSystems
            );
            await File.WriteAllTextAsync(
                Path.Combine(folders[0], BoundaryJournal),
                BasicJournal("2026-07-01T00:05:00Z", BoundaryShot + "\n" + OrderMusic) + "\n"
            );
            result = await service.InitializeActiveAsync("F123", true);
        }
        Assert.Empty(result.Errors);
        Assert.Equal(["Sol", "Sirius"], result.Journey!.VisitedSystems.Select(visit => visit.StarSystem.Name));
        Assert.Equal([0, 1], result.Journey.VisitedSystems.Select(visit => visit.Counts.Screenshots));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveSourceGrowthPreservesVisitAttributionAcrossPollingBoundaries(bool splitPoll)
    {
        await WriteJournalAsync(
            BoundaryJournal,
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal("2026-07-01T00:05:00Z") + "\n"
        );
        JourneyService service = CreateService();
        JourneyJournalSystemSearchResult start = await service.FindLatestStartAsync("F123", true, 42);
        await service.BeginAsync(new("F123", "Test", true, "Growing source", "", start.Entry!));
        const string file = "Journal.2026-07-01T000600.01.log";
        await WriteJournalAsync(
            file,
            new DateTime(2026, 7, 1, 0, 6, 0, DateTimeKind.Utc),
            BasicJournal("2026-07-01T00:05:00Z", OrderArrival) + "\n"
        );
        await service.ApplyLiveAsync(
            (await new JournalDirectoryMonitor(JournalDirectory, "F123").PollAsync()).JournalEvents
        );
        string copy = Path.Combine(temporaryDirectory, "live-copy");
        Directory.CreateDirectory(copy);
        string content =
            new string('\n', 1000) + BasicJournal("2026-07-01T00:05:00Z", BoundaryShot + "\n" + OrderArrival) + "\n";
        await File.WriteAllTextAsync(Path.Combine(copy, file), content + (splitPoll ? "" : BoundaryShot + "\n"));
        var monitor = new JournalDirectoryMonitor(copy, "F123");
        await service.ApplyLiveAsync((await monitor.PollAsync()).JournalEvents);
        if (splitPoll)
        {
            Assert.Equal([1, 0], service.ActiveJourney!.VisitedSystems.Select(visit => visit.Counts.Screenshots));
            await File.AppendAllTextAsync(Path.Combine(copy, file), BoundaryShot + "\n");
            await service.ApplyLiveAsync((await monitor.PollAsync()).JournalEvents);
        }
        Assert.Equal([1, 1], service.ActiveJourney!.VisitedSystems.Select(visit => visit.Counts.Screenshots));
        JourneyServiceResult resumed = await BoundaryService([JournalDirectory, copy])
            .InitializeActiveAsync("F123", true);
        Assert.Equal([1, 1], resumed.Journey!.VisitedSystems.Select(visit => visit.Counts.Screenshots));
    }
}
