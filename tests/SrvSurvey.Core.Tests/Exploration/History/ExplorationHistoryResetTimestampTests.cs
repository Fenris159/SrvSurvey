using SrvSurvey.Core.Exploration.History;

namespace SrvSurvey.Core.Tests.Exploration.History;

public sealed partial class ExplorationHistoryCatalogTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public async Task ANewJournalInTheResetSecondCountsAfterTheSavedSourceFrontierAndReplay(int resetMilliseconds)
    {
        using var fixture = new HistoryFixture(
            new FrozenLedgerTime(
                new DateTimeOffset(2026, 10, 1, 14, 0, 0, TimeSpan.Zero).AddMilliseconds(resetMilliseconds)
            )
        );
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        await fixture.Catalog.ResetSurveyValueAsync();
        await WriteResetSecondSourceAsync(fixture, "14:00:00", "140000");
        ExplorationHistorySummary result = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.True(result.IsSurveyValueAvailable);
        Assert.Equal(23106, result.TrackedUnsoldValue);
        Assert.Equal(46212, result.UnsoldValue);
        string changed = (await File.ReadAllTextAsync(fixture.JournalPath)).Replace(
            "\"StarSystem\":",
            "\"StarSystem\":      ",
            StringComparison.Ordinal
        );
        await File.WriteAllTextAsync(fixture.JournalPath, changed);
        var reopened = new ExplorationHistoryCatalog(fixture.Data, "F100", true);
        ExplorationHistorySummary replayed = await reopened.SynchronizeAsync([fixture.Journals]);
        Assert.True(replayed.IsSurveyValueAvailable);
        Assert.Equal(23106, replayed.TrackedUnsoldValue);
        Assert.Equal(46212, replayed.UnsoldValue);
    }

    [Fact]
    public async Task AnOlderUnseenSourceInTheResetSecondRemainsUnavailableUntilANewExplicitPeriod()
    {
        using var fixture = new HistoryFixture(
            new FrozenLedgerTime(new DateTimeOffset(2026, 10, 1, 14, 0, 0, TimeSpan.Zero).AddMilliseconds(500))
        );
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        await fixture.Catalog.ResetSurveyValueAsync();
        await WriteResetSecondSourceAsync(fixture, "13:00:00", "130000");
        ExplorationHistorySummary ambiguous = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.False(ambiguous.IsSurveyValueAvailable);
        Assert.Null(ambiguous.TrackedUnsoldValue);
        Assert.Equal(46212, ambiguous.UnsoldValue);
        ExplorationHistorySummary fresh = await fixture.Catalog.ResetSurveyValueAsync();
        Assert.True(fresh.IsSurveyValueAvailable);
        Assert.Equal(0, fresh.TrackedUnsoldValue);
        Assert.Equal(46212, fresh.UnsoldValue);
    }

    [Fact]
    public async Task UnconsumedSourcesDoNotAdvanceTheResetSecondsConsumedFrontier()
    {
        using var fixture = new HistoryFixture(
            new FrozenLedgerTime(new DateTimeOffset(2026, 10, 1, 14, 0, 0, TimeSpan.Zero).AddMilliseconds(500))
        );
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint consumed = await fixture.Catalog.CaptureSurveyResetPointAsync();
        await WriteResetSecondSourceAsync(fixture, "14:00:00", "140001");
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint point = await fixture.Catalog.CaptureSurveyResetPointAsync(consumed.Sources);
        await fixture.Catalog.ApplySurveyResetPointAsync(point);
        await WriteResetSecondSourceAsync(fixture, "14:00:00", "140000");
        ExplorationHistorySummary result = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.True(result.IsSurveyValueAvailable);
        Assert.Equal(23106, result.TrackedUnsoldValue);
        Assert.Equal(46212, result.UnsoldValue);
    }

    private static Task WriteResetSecondSourceAsync(HistoryFixture fixture, string started, string name) =>
        File.WriteAllLinesAsync(
            Path.Combine(fixture.Journals, $"Journal.2026-10-01T{name}.01.log"),
            [
                $$"""{"timestamp":"2026-10-01T{{started}}Z","event":"Fileheader","Odyssey":true}""",
                $$"""{"timestamp":"2026-10-01T{{started}}Z","event":"Commander","FID":"F100"}""",
                """{"timestamp":"2026-10-01T14:00:00Z","event":"Location","StarSystem":"Beta","SystemAddress":200}""",
                """{"timestamp":"2026-10-01T14:00:00Z","event":"Scan","SystemAddress":200,"BodyID":0,"BodyName":"Beta A","StarType":"NS","StellarMass":1.4,"WasDiscovered":true,"WasMapped":true}""",
            ]
        );
}
