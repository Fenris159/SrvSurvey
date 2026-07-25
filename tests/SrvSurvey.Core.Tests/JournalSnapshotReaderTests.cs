using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests;

public sealed class JournalSnapshotReaderTests
{
    [Fact]
    public async Task ReadAsyncBuildsBootstrapStateAndIgnoresMalformedTail()
    {
        var journal = """
            {"timestamp":"2026-07-24T10:00:00Z","event":"Fileheader","gameversion":"4.2.0","build":"r123","Odyssey":true}
            {"timestamp":"2026-07-24T10:00:01Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2026-07-24T10:00:02Z","event":"LoadGame","Commander":"Drew","FID":"F123","GameMode":"Open","Odyssey":true,"gameversion":"4.2.1","build":"r124"}
            {"timestamp":"2026-07-24T10:00:03Z","event":"Location","StarSystem":"Sol","SystemAddress":10477373803,"Body":"Earth","BodyType":"Planet"}
            {"timestamp":"2026-07-24T10:00:04Z","event":"ApproachBody","Body":"Earth"}
            {"timestamp":"2026-07-24T10:00:05Z","event":"FutureEvent","Value":42}
            {"timestamp":"2026-07-24T10:00:06Z","event":"Partial"
            """;

        var snapshot = await JournalSnapshotReader.ReadAsync(
            new StringReader(journal),
            "Journal.fixture.log");

        Assert.Equal("Journal.fixture.log", snapshot.SourcePath);
        Assert.Equal("4.2.1", snapshot.GameVersion);
        Assert.Equal("r124", snapshot.GameBuild);
        Assert.True(snapshot.IsOdyssey);
        Assert.Equal("Drew", snapshot.CommanderName);
        Assert.Equal("F123", snapshot.FrontierId);
        Assert.Equal("Open", snapshot.GameMode);
        Assert.Equal("Sol", snapshot.SystemName);
        Assert.Equal(10477373803, snapshot.SystemAddress);
        Assert.Equal("Earth", snapshot.BodyName);
        Assert.False(snapshot.IsShutdown);
        Assert.Equal(DateTimeOffset.Parse("2026-07-24T10:00:05Z"), snapshot.LastEventTimestamp);
        Assert.Equal(6, snapshot.ValidLineCount);
        Assert.Equal(5, snapshot.RecognizedEventCount);
        Assert.Equal(1, snapshot.MalformedLineCount);
    }

    [Fact]
    public async Task ReadAsyncTracksLegacyCurrentBodyAndShutdownSemantics()
    {
        var journal = """
            {"timestamp":"2026-07-24T11:00:00Z","event":"Location","StarSystem":"Sol","SystemAddress":1,"Body":"Earth","BodyType":"Planet"}
            {"timestamp":"2026-07-24T11:00:01Z","event":"FSDJump","StarSystem":"Achenar","SystemAddress":2,"Body":"Achenar A","BodyType":"Star"}
            {"timestamp":"2026-07-24T11:00:02Z","event":"ApproachBody","Body":"Achenar 3"}
            {"timestamp":"2026-07-24T11:00:03Z","event":"LeaveBody","Body":"Achenar 3"}
            {"timestamp":"2026-07-24T11:00:04Z","event":"Shutdown"}
            """;

        var snapshot = await JournalSnapshotReader.ReadAsync(new StringReader(journal));

        Assert.Equal("Achenar", snapshot.SystemName);
        Assert.Equal(2, snapshot.SystemAddress);
        Assert.Equal("Achenar 3", snapshot.BodyName);
        Assert.True(snapshot.IsShutdown);
        Assert.Equal(5, snapshot.ValidLineCount);
        Assert.Equal(5, snapshot.RecognizedEventCount);
        Assert.Equal(0, snapshot.MalformedLineCount);
    }

    [Fact]
    public async Task ReadLatestAsyncUsesNewestJournalByWriteTime()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey.Core.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);

        try
        {
            var olderPath = Path.Combine(testDirectory, "Journal.2026-07-23T000000.01.log");
            var newerPath = Path.Combine(testDirectory, "Journal.2026-07-24T000000.01.log");

            await File.WriteAllTextAsync(
                olderPath,
                """{"timestamp":"2026-07-23T00:00:00Z","event":"Commander","Name":"Older"}""");
            await File.WriteAllTextAsync(
                newerPath,
                """{"timestamp":"2026-07-24T00:00:00Z","event":"Commander","Name":"Newer"}""");
            File.SetLastWriteTimeUtc(olderPath, new DateTime(2026, 7, 23, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newerPath, new DateTime(2026, 7, 24, 0, 0, 0, DateTimeKind.Utc));

            var snapshot = await JournalSnapshotReader.ReadLatestAsync(testDirectory);

            Assert.Equal("Newer", snapshot.CommanderName);
            Assert.Equal(newerPath, snapshot.SourcePath);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ReadLatestAsyncFillsPartialLoginFromRecentJournals()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey.Core.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);

        try
        {
            var olderPath = Path.Combine(
                testDirectory,
                "Journal.2026-07-23T000000.01.log");
            var newerPath = Path.Combine(
                testDirectory,
                "Journal.2026-07-24T000000.01.log");
            await File.WriteAllTextAsync(
                olderPath,
                """
                {"timestamp":"2026-07-23T00:00:00Z","event":"Commander","Name":"Drew","FID":"F123"}
                {"timestamp":"2026-07-23T00:00:01Z","event":"Location","StarSystem":"Sol","SystemAddress":10477373803,"StarPos":[0,0,0]}
                {"timestamp":"2026-07-23T00:00:02Z","event":"Shutdown"}
                """);
            await File.WriteAllTextAsync(
                newerPath,
                """
                {"timestamp":"2026-07-24T00:00:00Z","event":"Fileheader","gameversion":"4.2.0","build":"r123","Odyssey":true}
                {"timestamp":"2026-07-24T00:00:01Z","event":"LoadGame","GameMode":"Solo"}
                """);
            File.SetLastWriteTimeUtc(
                olderPath,
                new DateTime(2026, 7, 23, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(
                newerPath,
                new DateTime(2026, 7, 24, 0, 0, 0, DateTimeKind.Utc));

            var snapshot = await JournalSnapshotReader.ReadLatestAsync(
                testDirectory);

            Assert.Equal(newerPath, snapshot.SourcePath);
            Assert.Equal("Drew", snapshot.CommanderName);
            Assert.Equal("F123", snapshot.FrontierId);
            Assert.Equal("Sol", snapshot.SystemName);
            Assert.Equal(10477373803, snapshot.SystemAddress);
            Assert.Equal("Solo", snapshot.GameMode);
            Assert.Equal("4.2.0", snapshot.GameVersion);
            Assert.False(snapshot.IsShutdown);
            Assert.Equal(5, snapshot.ValidLineCount);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }
}
