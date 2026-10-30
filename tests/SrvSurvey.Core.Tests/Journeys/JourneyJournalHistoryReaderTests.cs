using SrvSurvey.Core.Journeys;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed class JourneyJournalHistoryReaderTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-journey-history-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task FindsEveryDistinctArrivalByEventDateAndKeepsCommanderAndGameModesSeparate()
    {
        const string first = "Journal.2026-07-01T000000.01.log";
        await WriteJournalAsync(
            first,
            new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
            Header("F123", true)
                + "\n"
                + """
                {"timestamp":"2026-07-01T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
                {"timestamp":"2026-07-02T00:00:00Z","event":"Commander","FID":"F999"}
                {"timestamp":"2026-07-02T00:05:00Z","event":"FSDJump","StarSystem":"Foreign","SystemAddress":42,"StarPos":[0,0,0]}
                {"timestamp":"2026-07-02T00:06:00Z","event":"Screenshot","foreign":true}
                {"timestamp":"2026-07-03T00:00:00Z","event":"LoadGame","FID":"F123"}
                {"timestamp":"2026-07-03T00:05:00Z","event":"CarrierJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
                {"event":"FSDJump","StarSystem":"No timestamp","SystemAddress":42,"StarPos":[0,0,0]}
                {"timestamp":"2026-07-04T00:05:00Z","event":"FSDJump","StarSystem":"Bad position","SystemAddress":42,"StarPos":[null,0,0]}
                {"timestamp":"2026-07-05T00:05:00Z","event":"FSDJump","StarSystem":"Wrong address","SystemAddress":43,"StarPos":[0,0,0],"Description":"Sol"}
                malformed
                """
        );
        await WriteJournalAsync(
            "Journal.2026-07-06T000000.01.log",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            Header("F123", true)
                + "\n"
                + """
                {"timestamp":"2026-07-01T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
                {"timestamp":"2026-07-06T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
                """
        );
        await WriteJournalAsync(
            "Journal.2026-07-07T000000.01.log",
            DateTime.UtcNow,
            Header("F123", false)
                + "\n"
                + """
                {"timestamp":"2026-07-07T00:05:00Z","event":"FSDJump","StarSystem":"Legacy Sol","SystemAddress":42,"StarPos":[0,0,0]}
                """
        );
        var reader = new JourneyJournalHistoryReader(temporaryDirectory);
        JourneyJournalSystemVisitsResult result = await reader.FindArrivalsAsync("F123", true, 42);
        Assert.Equal([6, 3, 1], result.Entries.Select(entry => entry.Event.Timestamp!.Value.Day));
        Assert.Equal("CarrierJump", result.Entries[1].Event.EventName);
        Assert.All(result.Entries, entry => Assert.Equal("Sol", entry.System.Name));
        Assert.Single(result.Errors);
        Assert.Equal(
            result.Entries.Select(entry => entry.Event.Timestamp),
            (await reader.FindArrivalsAsync("F123", true, " sOL ")).Entries.Select(entry => entry.Event.Timestamp)
        );
        Assert.Equal(
            result.Entries.Select(entry => entry.Event.Timestamp),
            (await reader.FindArrivalsAsync("F123", true, "so")).Entries.Select(entry => entry.Event.Timestamp)
        );
        Assert.Empty((await reader.FindArrivalsAsync("F123", true, "Unvisited")).Entries);
        await Assert.ThrowsAsync<ArgumentException>(() => reader.FindArrivalsAsync("F123", true, " "));
        Assert.Equal(
            "Legacy Sol",
            Assert.Single((await reader.FindArrivalsAsync("F123", false, 42)).Entries).System.Name
        );
        JourneyJournalReadResult replay = await reader.ReadFromAsync(first, "F123", true);
        Assert.Contains(replay.Events, entry => entry.EventName == "FSDJump" && entry.Timestamp?.Day == 6);
        Assert.DoesNotContain(replay.Events, entry => entry.Payload.TryGetProperty("foreign", out _));
        Assert.DoesNotContain(
            replay.Events,
            entry =>
                entry.Payload.TryGetProperty("StarSystem", out System.Text.Json.JsonElement name)
                && name.GetString() == "Foreign"
        );
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reader.FindArrivalsAsync("F123", true, 42, canceled.Token)
        );
    }

    [Fact]
    public async Task FindsLatestMatchingCommanderFsdJump()
    {
        await WriteJournalAsync(
            "Journal.2026-07-01T000000.01.log",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            """
            {"timestamp":"2026-07-01T00:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-07-01T00:00:01Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2026-07-01T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            """
        );
        await WriteJournalAsync(
            "Journal.2026-07-02T000000.01.log",
            new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc),
            """
            {"timestamp":"2026-07-02T00:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-07-02T00:00:01Z","event":"Commander","Name":"Other","FID":"F999"}
            {"timestamp":"2026-07-02T00:05:00Z","event":"FSDJump","StarSystem":"Wrong","SystemAddress":42,"StarPos":[9,9,9]}
            """
        );
        await WriteJournalAsync(
            "Journal.2026-07-03T000000.01.log",
            new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
            """
            {"timestamp":"2026-07-03T00:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-07-03T00:00:01Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2026-07-03T00:05:00Z","event":"FSDJump","StarSystem":"Achenar","SystemAddress":42,"StarPos":[1,2,3]}
            """
        );
        var reader = new JourneyJournalHistoryReader(temporaryDirectory);

        JourneyJournalSystemSearchResult result = await reader.FindLatestFsdJumpAsync("F123", true, 42);

        Assert.NotNull(result.Entry);
        Assert.Equal("Journal.2026-07-03T000000.01.log", result.Entry.JournalFileName);
        Assert.Equal("Achenar", result.Entry.System.Name);
        Assert.Equal(1, result.Entry.System.Position.X);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ReplayStartsAtRequestedFileAndFiltersCommanderAndPlatform()
    {
        await WriteJournalAsync(
            "Journal.2026-07-01T000000.01.log",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            Header("F123", true) + "\n" + Event("2026-07-01T00:01:00Z", "Screenshot")
        );
        await WriteJournalAsync(
            "Journal.2026-07-02T000000.01.log",
            new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc),
            Header("F999", true) + "\n" + Event("2026-07-02T00:01:00Z", "WrongCommander")
        );
        await WriteJournalAsync(
            "Journal.2026-07-03T000000.01.log",
            new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
            Header("F123", false) + "\n" + Event("2026-07-03T00:01:00Z", "WrongPlatform")
        );
        await WriteJournalAsync(
            "Journal.2026-07-04T000000.01.log",
            new DateTime(2026, 7, 4, 0, 0, 0, DateTimeKind.Utc),
            Header("F123", true) + "\nnot-json\n" + Event("2026-07-04T00:01:00Z", "Touchdown")
        );
        var reader = new JourneyJournalHistoryReader(temporaryDirectory);

        JourneyJournalReadResult result = await reader.ReadFromAsync("Journal.2026-07-01T000000.01.log", "F123", true);

        Assert.Contains(result.Events, entry => entry.EventName == "Screenshot");
        Assert.Contains(result.Events, entry => entry.EventName == "Touchdown");
        Assert.DoesNotContain(result.Events, entry => entry.EventName == "WrongCommander");
        Assert.DoesNotContain(result.Events, entry => entry.EventName == "WrongPlatform");
        Assert.Single(result.Errors);
        Assert.Contains("Journal.2026-07-04T000000.01.log, line 3", result.Errors[0]);
    }

    [Fact]
    public async Task ReplayRejectsPathsOutsideJournalDirectory()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var reader = new JourneyJournalHistoryReader(temporaryDirectory);

        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadFromAsync("../Journal.outside.log", "F123", true));
    }

    private async Task WriteJournalAsync(string fileName, DateTime lastWriteTime, string content)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, fileName);
        await File.WriteAllTextAsync(path, content);
        File.SetLastWriteTimeUtc(path, lastWriteTime);
    }

    private static string Header(string frontierId, bool isOdyssey)
    {
        return $$"""
            {"timestamp":"2026-07-01T00:00:00Z","event":"Fileheader","Odyssey":{{isOdyssey.ToString().ToLowerInvariant()}}}
            {"timestamp":"2026-07-01T00:00:01Z","event":"Commander","Name":"Drew","FID":"{{frontierId}}"}
            """;
    }

    private static string Event(string timestamp, string eventName)
    {
        return $$"""{"timestamp":"{{timestamp}}","event":"{{eventName}}"}""";
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
