using SrvSurvey.Core.Exobiology;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class CommanderCodexJournalImporterTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-CodexJournalImport-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ImportsOnlyTargetCommanderAcrossBoundedBatches()
    {
        var journalDirectory = Path.Combine(temporaryDirectory, "journals");
        var dataDirectory = Path.Combine(temporaryDirectory, "data");
        Directory.CreateDirectory(journalDirectory);
        var targetLines = new List<string>
        {
            """{"timestamp":"2026-07-24T10:00:00Z","event":"Commander","Name":"Cmdr Target","FID":"F123"}""",
            """{"timestamp":"2026-07-24T10:01:00Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}""",
        };
        targetLines.AddRange(Enumerable.Range(0, 2_048).Select(index =>
            $$"""{"timestamp":"2026-07-24T10:02:00Z","event":"Music","MusicTrack":"Track {{index}}"}"""));
        targetLines.Add(
            """{"timestamp":"2026-07-24T10:03:00Z","event":"CodexEntry","EntryID":2310101,"SystemAddress":42,"BodyID":3}""");
        targetLines.Add("not json");
        await File.WriteAllLinesAsync(
            Path.Combine(journalDirectory, "Journal.01.log"),
            targetLines);
        await File.WriteAllLinesAsync(
            Path.Combine(journalDirectory, "Journal.02.log"),
        [
            """{"timestamp":"2026-07-24T11:00:00Z","event":"Commander","Name":"Cmdr Other","FID":"F999"}""",
            """{"timestamp":"2026-07-24T11:01:00Z","event":"Location","StarSystem":"Other","SystemAddress":99,"StarPos":[1,2,3]}""",
            """{"timestamp":"2026-07-24T11:02:00Z","event":"CodexEntry","EntryID":2310206,"SystemAddress":99,"BodyID":4}""",
        ]);
        var store = new CommanderCodexStore(dataDirectory);
        var importer = new CommanderCodexJournalImporter(
            journalDirectory,
            store);

        var result = await importer.ImportAsync("F123");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.JournalFileCount);
        Assert.Equal(1, result.MalformedLineCount);
        Assert.Equal(1, result.DiscoveryEventCount);
        Assert.Equal(2, result.ChangedEntryCount);
        var global = await store.LoadAsync("F123", null);
        Assert.Equal(3, Assert.Single(global.Data!.Firsts).Value.BodyId);
        Assert.False(File.Exists(store.ResolvePath("F999")));
        Assert.Single(Directory.GetFiles(dataDirectory, "F123-codex-*.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
