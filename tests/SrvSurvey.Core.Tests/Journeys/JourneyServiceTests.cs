using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed class JourneyServiceTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-journey-service-tests-{Guid.NewGuid():N}");

    private string DataDirectory => Path.Combine(temporaryDirectory, "data");

    private string JournalDirectory => Path.Combine(temporaryDirectory, "journals");

    [Fact]
    public async Task BeginCatchesUpPersistsAndActivatesJourney()
    {
        await WriteJournalAsync(
            "Journal.2026-07-01T000000.01.log",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            """
            {"timestamp":"2026-07-01T00:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-07-01T00:00:01Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2026-07-01T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-01T00:06:00Z","event":"Screenshot"}
            """);
        var service = CreateService();
        var start = await service.FindLatestStartAsync("F123", true, 42);

        var result = await service.BeginAsync(new JourneyBeginRequest(
            "F123",
            "Drew",
            true,
            "  The black  ",
            "First expedition",
            start.Entry!));

        Assert.NotNull(result.Journey);
        Assert.Equal("The black", result.Journey.Name);
        Assert.Equal(2, result.ProcessedEventCount);
        Assert.Equal(1, result.Journey.CurrentSystem!.Counts.Screenshots);
        Assert.Equal(result.Journey, service.ActiveJourney);
        Assert.True(File.Exists(result.Journey.FilePath));

        var profile = await new CommanderProfileStore(DataDirectory)
            .LoadAsync("F123", true);
        Assert.Equal(result.Journey.FileName, profile.Data?.ActiveJourneyFileName);
    }

    [Fact]
    public async Task InitializeActiveCatchesUpOnlyNewEvents()
    {
        await WriteJournalAsync(
            "Journal.2026-07-01T000000.01.log",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal(
                "2026-07-01T00:05:00Z",
                """{"timestamp":"2026-07-01T00:06:00Z","event":"Screenshot"}"""));
        var firstService = CreateService();
        var start = await firstService.FindLatestStartAsync("F123", true, 42);
        var begun = await firstService.BeginAsync(new JourneyBeginRequest(
            "F123", "Drew", true, "Journey", string.Empty, start.Entry!));
        Assert.Equal(1, begun.Journey!.CurrentSystem!.Counts.Screenshots);

        await WriteJournalAsync(
            "Journal.2026-07-02T000000.01.log",
            new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc),
            """
            {"timestamp":"2026-07-02T00:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-07-02T00:00:01Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2026-07-02T00:01:00Z","event":"Screenshot"}
            """);

        var resumed = await CreateService().InitializeActiveAsync("F123", true);

        Assert.NotNull(resumed.Journey);
        Assert.Equal(3, resumed.ProcessedEventCount);
        Assert.Equal(2, resumed.Journey.CurrentSystem!.Counts.Screenshots);
        var stored = await new JourneyStore(DataDirectory).LoadAsync(
            "F123",
            resumed.Journey.FileName);
        Assert.Equal(2, stored.Journey!.CurrentSystem!.Counts.Screenshots);
    }

    [Fact]
    public async Task LiveUpdatePersistsAndConcludeClearsActivePointer()
    {
        await WriteJournalAsync(
            "Journal.2026-07-01T000000.01.log",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal("2026-07-01T00:05:00Z"));
        var service = CreateService();
        var start = await service.FindLatestStartAsync("F123", true, 42);
        var begun = await service.BeginAsync(new JourneyBeginRequest(
            "F123", "Drew", true, "Journey", string.Empty, start.Entry!));

        var live = await service.ApplyLiveAsync(
        [
            Parse("""{"timestamp":"2026-07-01T00:07:00Z","event":"Screenshot"}"""),
        ]);
        var concluded = await service.ConcludeActiveAsync(
            "Drew",
            DateTimeOffset.Parse("2026-07-01T00:08:00Z"));

        Assert.Equal(1, live.ProcessedEventCount);
        Assert.Equal(1, concluded!.CurrentSystem!.Counts.Screenshots);
        Assert.Equal(DateTimeOffset.Parse("2026-07-01T00:08:00Z"), concluded.EndTime);
        Assert.Null(service.ActiveJourney);
        var stored = await new JourneyStore(DataDirectory).LoadAsync(
            "F123",
            begun.Journey!.FileName);
        Assert.Equal(concluded.EndTime, stored.Journey!.EndTime);
        var profile = await new CommanderProfileStore(DataDirectory)
            .LoadAsync("F123", true);
        Assert.Null(profile.Data?.ActiveJourneyFileName);
    }

    [Fact]
    public async Task ActiveJourneyBlocksAnotherBeginAndTracksSystemNotes()
    {
        await WriteJournalAsync(
            "Journal.2026-07-01T000000.01.log",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal("2026-07-01T00:05:00Z"));
        var service = CreateService();
        var start = await service.FindLatestStartAsync("F123", true, 42);
        var request = new JourneyBeginRequest(
            "F123", "Drew", true, "Journey", string.Empty, start.Entry!);
        var begun = await service.BeginAsync(request);

        Assert.True(await service.IncrementNoteCountAsync(42));
        Assert.False(await service.IncrementNoteCountAsync(999));
        await service.ApplyLiveAsync(
        [
            Parse("""{"timestamp":"2026-07-01T00:06:00Z","event":"FSDJump","StarSystem":"Achenar","SystemAddress":43,"StarPos":[1,2,3]}"""),
        ]);
        Assert.False(await service.IncrementNoteCountAsync(42));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.BeginAsync(request));

        var stored = await new JourneyStore(DataDirectory).LoadAsync(
            "F123",
            begun.Journey!.FileName);
        Assert.Equal(1, stored.Journey!.VisitedSystems[0].Counts.Notes);
        Assert.Equal(0, stored.Journey.CurrentSystem!.Counts.Notes);
        Assert.Equal(1, service.ActiveJourney!.VisitedSystems[0].Counts.Notes);
        Assert.Equal(0, service.ActiveJourney.CurrentSystem!.Counts.Notes);
    }

    [Fact]
    public async Task ReprocessStopsConcludedJourneyAtItsEndTime()
    {
        await WriteJournalAsync(
            "Journal.2026-07-01T000000.01.log",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal(
                "2026-07-01T00:05:00Z",
                """
                {"timestamp":"2026-07-01T00:06:00Z","event":"Screenshot"}
                {"timestamp":"2026-07-01T00:08:00Z","event":"Screenshot"}
                """));
        var store = new JourneyStore(DataDirectory);
        var created = await store.CreateAsync(new JourneyCreationRequest(
            "F123",
            "Drew",
            "Historic",
            string.Empty,
            "Journal.2026-07-01T000000.01.log",
            DateTimeOffset.Parse("2026-07-01T00:05:00Z")));
        created = created with
        {
            EndTime = DateTimeOffset.Parse("2026-07-01T00:07:00Z"),
        };
        await store.SaveAsync(created);

        var result = await CreateService().ReprocessAsync(created, true);

        Assert.NotNull(result.Journey);
        Assert.Equal(1, result.Journey.CurrentSystem!.Counts.Screenshots);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-01T00:06:00Z"),
            result.Journey.Watermark);
    }

    [Fact]
    public async Task InitializeReportsMalformedCommanderProfile()
    {
        Directory.CreateDirectory(DataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(DataDirectory, "F123-live.json"),
            "{\"fid\":");

        var result = await CreateService().InitializeActiveAsync("F123", true);

        Assert.Null(result.Journey);
        Assert.Single(result.Errors);
        Assert.Contains("Could not read", result.Errors[0]);
    }

    private JourneyService CreateService()
    {
        return new JourneyService(
            new JourneyStore(DataDirectory),
            new JourneyJournalHistoryReader(JournalDirectory),
            new CommanderProfileStore(DataDirectory),
            new ExobiologyReferenceCatalog([]));
    }

    private async Task WriteJournalAsync(
        string fileName,
        DateTime lastWriteTime,
        string content)
    {
        Directory.CreateDirectory(JournalDirectory);
        var path = Path.Combine(JournalDirectory, fileName);
        await File.WriteAllTextAsync(path, content);
        File.SetLastWriteTimeUtc(path, lastWriteTime);
    }

    private static string BasicJournal(
        string jumpTimestamp,
        string trailingEvents = "")
    {
        return $$"""
            {"timestamp":"2026-07-01T00:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-07-01T00:00:01Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"{{jumpTimestamp}}","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {{trailingEvents}}
            """;
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return journalEvent!;
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
