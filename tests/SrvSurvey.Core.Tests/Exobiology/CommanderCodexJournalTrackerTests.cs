using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class CommanderCodexJournalTrackerTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-CommanderCodexTracker-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReplaysGlobalAndRegionalFirstsInBatches()
    {
        var store = new CommanderCodexStore(temporaryDirectory);
        var tracker = new CommanderCodexJournalTracker(store);

        var result = await tracker.ApplyAsync(
        [
            Parse("""{"timestamp":"2026-07-24T10:00:00Z","event":"Commander","Name":"Cmdr Test","FID":"F123"}"""),
            Parse("""{"timestamp":"2026-07-24T10:01:00Z","event":"Location","StarSystem":"Sol","SystemAddress":10477373803,"StarPos":[0,0,0]}"""),
            Parse("""{"timestamp":"2026-07-24T10:03:00Z","event":"CodexEntry","EntryID":2310101,"SystemAddress":10477373803,"BodyID":3}"""),
            Parse("""{"timestamp":"2026-07-24T10:02:00Z","event":"CodexEntry","EntryID":2310101,"SystemAddress":10477373803,"BodyID":2}"""),
            Parse("""{"timestamp":"2026-07-24T10:04:00Z","event":"CodexEntry","EntryID":"2320101","SystemAddress":"10477373803","BodyID":"4"}"""),
        ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.DiscoveryEventCount);
        Assert.Equal(4, result.ChangedEntryCount);
        Assert.Equal(2, result.ChangedFileCount);
        var global = await store.LoadAsync("F123", null);
        Assert.Equal(2, global.Data!.Firsts.Count);
        Assert.Equal(2, global.Data.Firsts[2310101].BodyId);
        var regionalFiles = Directory.GetFiles(
            temporaryDirectory,
            "F123-codex-*.json");
        var regionalPath = Assert.Single(regionalFiles);
        var regionId = int.Parse(
            Path.GetFileNameWithoutExtension(regionalPath).Split('-')[2]);
        var regional = await store.LoadAsync("F123", null, regionId);
        Assert.Equal(2, regional.Data!.Firsts.Count);
        Assert.False(string.IsNullOrWhiteSpace(regional.Data.RegionName));
    }

    [Fact]
    public async Task KeepsContextAcrossIncrementalUpdates()
    {
        var store = new CommanderCodexStore(temporaryDirectory);
        var tracker = new CommanderCodexJournalTracker(store);
        await tracker.ApplyAsync(
        [
            Parse("""{"timestamp":"2026-07-24T10:00:00Z","event":"Commander","Name":"Cmdr Test","FID":"F123"}"""),
            Parse("""{"timestamp":"2026-07-24T10:01:00Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}"""),
        ]);

        var result = await tracker.ApplyAsync(
        [
            Parse("""{"timestamp":"2026-07-24T10:02:00Z","event":"CodexEntry","EntryID":2310101,"BodyID":1}"""),
        ]);

        Assert.True(result.HasChanges);
        var global = await store.LoadAsync("F123", null);
        Assert.Equal(42, Assert.Single(global.Data!.Firsts).Value.SystemAddress);
    }

    [Fact]
    public async Task UsesJournalRegionWhenStarPositionIsUnavailable()
    {
        var store = new CommanderCodexStore(temporaryDirectory);
        var tracker = new CommanderCodexJournalTracker(store);

        var result = await tracker.ApplyAsync(
        [
            Parse("""{"timestamp":"2026-07-24T10:00:00Z","event":"Commander","Name":"Cmdr Test","FID":"F123"}"""),
            Parse("""{"timestamp":"2026-07-24T10:01:00Z","event":"CodexEntry","EntryID":2310101,"SystemAddress":42,"BodyID":1,"Region":"$Codex_RegionName_18;"}"""),
        ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.ChangedFileCount);
        var regional = await store.LoadAsync("F123", null, 18);
        Assert.True(regional.Exists);
        Assert.Equal(
            42,
            Assert.Single(regional.Data!.Firsts).Value.SystemAddress);
        Assert.Equal(
            GalacticRegionMap.Regions.Single(region => region.Id == 18).Name,
            regional.Data.RegionName);
    }

    [Fact]
    public async Task ReportsIncompleteCodexEventsWithoutWriting()
    {
        var tracker = new CommanderCodexJournalTracker(
            new CommanderCodexStore(temporaryDirectory));

        var result = await tracker.ApplyAsync(
        [
            Parse("""{"event":"CodexEntry","EntryID":2310101,"SystemAddress":42}"""),
        ]);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.DiscoveryEventCount);
        Assert.Empty(Directory.Exists(temporaryDirectory)
            ? Directory.GetFiles(temporaryDirectory)
            : []);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private static JournalEventEnvelope Parse(string json)
    {
        var success = JournalEventEnvelope.TryParse(
            json,
            out var journalEvent,
            out var error);
        Assert.True(success, error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }
}
