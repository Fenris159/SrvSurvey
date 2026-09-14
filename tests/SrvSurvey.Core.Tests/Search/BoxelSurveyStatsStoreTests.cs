using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class BoxelSurveyStatsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-BoxelSurveyStatsStoreTests-" + Guid.NewGuid().ToString("N")
    );

    [Fact]
    public async Task SaveLoadAndIndexPreserveSystemBodiesAndHelium()
    {
        var store = new BoxelSurveyStatsStore(temporaryDirectory);
        var source = new BoxelSurveyStatsState();
        source.Apply(
            Parse(
                """{"timestamp":"2026-07-10T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-0","SystemAddress":2001}"""
            )
        );
        source.Apply(
            Parse(
                """{"event":"Scan","SystemAddress":2001,"BodyID":2,"PlanetClass":"Water world","MassEM":1.1,"AtmosphereComposition":[{"Name":"Helium","Percent":27.4}]}"""
            )
        );
        Assert.True(source.TryCreateDocument("Praea Euq IL-P c5-", out BoxelSurveyBoxelDocument? document));

        await store.SaveBoxelAsync("F123", document);
        BoxelSurveyBoxelDocument? loaded = await store.LoadBoxelAsync("F123", "Praea Euq IL-P c5-");
        IReadOnlyList<BoxelSurveyIndexEntry> index = await store.ListIndexAsync("F123");
        BoxelSurveyStatsCatalog catalog = await store.LoadCatalogAsync("F123");

        Assert.NotNull(loaded);
        Assert.Equal(document.Prefix, loaded.Prefix);
        BoxelSurveySystemContribution system = Assert.Single(loaded.Systems);
        BoxelSurveyBodyContribution body = Assert.Single(system.Bodies);
        Assert.Equal(BoxelPlanetClass.WaterWorld, body.Class);
        Assert.Equal(27.4, body.HeliumPercent);
        BoxelSurveyIndexEntry entry = Assert.Single(index);
        Assert.Equal("Praea Euq IL-P c5-", entry.Prefix);
        Assert.Equal('c', entry.MassCode);
        Assert.Equal(1, entry.VisitedSystemCount);
        Assert.Equal(27.4, entry.MinHeliumPercent);
        Assert.Equal("F123", catalog.FrontierId);
        Assert.False(File.Exists(Path.Combine(temporaryDirectory, "F123-live.json")));
        Assert.True(
            File.Exists(
                Path.Combine(temporaryDirectory, BoxelSurveyStatsStore.StoreDirectoryName, "F123", "index.json")
            )
        );
    }

    [Fact]
    public async Task CommandersAreIsolatedByFrontierId()
    {
        var store = new BoxelSurveyStatsStore(temporaryDirectory);
        BoxelSurveyBoxelDocument first = CreateDocument("Praea Euq IL-P c5-", "Praea Euq IL-P c5-0", 2001);
        BoxelSurveyBoxelDocument second = CreateDocument("Wregoe BU-Y b2-", "Wregoe BU-Y b2-0", 2002);
        await store.SaveBoxelAsync("F-A", first);
        await store.SaveBoxelAsync("F-B", second);

        Assert.Equal("Praea Euq IL-P c5-", Assert.Single(await store.ListIndexAsync("F-A")).Prefix);
        Assert.Equal("Wregoe BU-Y b2-", Assert.Single(await store.ListIndexAsync("F-B")).Prefix);
        Assert.Null(await store.LoadBoxelAsync("F-A", "Wregoe BU-Y b2-"));
        Assert.Null(await store.LoadBoxelAsync("F-B", "Praea Euq IL-P c5-"));
    }

    [Fact]
    public async Task BatchSaveWritesEveryDocumentIntoOneCatalog()
    {
        var store = new BoxelSurveyStatsStore(temporaryDirectory);
        BoxelSurveyBoxelDocument first = CreateDocument("Praea Euq IL-P c5-", "Praea Euq IL-P c5-0", 2001);
        BoxelSurveyBoxelDocument second = CreateDocument("Wregoe BU-Y b2-", "Wregoe BU-Y b2-0", 2002);

        await store.SaveBoxelsAsync("F123", [first, second]);

        IReadOnlyList<BoxelSurveyIndexEntry> index = await store.ListIndexAsync("F123");
        Assert.Equal(2, index.Count);
        Assert.NotNull(await store.LoadBoxelAsync("F123", first.Prefix));
        Assert.NotNull(await store.LoadBoxelAsync("F123", second.Prefix));
    }

    [Fact]
    public async Task DamagedBoxelFileDoesNotHideTheIndex()
    {
        var store = new BoxelSurveyStatsStore(temporaryDirectory);
        await store.SaveBoxelAsync("F123", CreateDocument("Praea Euq IL-P c5-", "Praea Euq IL-P c5-0", 2001));
        string commanderDirectory = Path.Combine(temporaryDirectory, BoxelSurveyStatsStore.StoreDirectoryName, "F123");
        File.Delete(Path.Combine(commanderDirectory, "index.json"));
        await File.WriteAllTextAsync(Path.Combine(commanderDirectory, "broken.json"), "not json");

        BoxelSurveyIndexEntry entry = Assert.Single(await store.ListIndexAsync("F123"));
        Assert.Equal("Praea Euq IL-P c5-", entry.Prefix);
    }

    [Fact]
    public async Task CollidingSanitizedPrefixesRemainLoadableWhenEitherIsResaved()
    {
        var store = new BoxelSurveyStatsStore(temporaryDirectory);
        BoxelSurveyBoxelDocument source = CreateDocument("Praea Euq IL-P c5-", "Praea Euq IL-P c5-0", 2001);
        BoxelSurveyBoxelDocument first = source with { Prefix = "Odd:name" };
        BoxelSurveyBoxelDocument second = source with { Prefix = "Odd/name" };

        await store.SaveBoxelAsync("F123", first);
        await store.SaveBoxelAsync("F123", second);

        Assert.Equal(first.Prefix, (await store.LoadBoxelAsync("F123", first.Prefix))?.Prefix);
        Assert.Equal(second.Prefix, (await store.LoadBoxelAsync("F123", second.Prefix))?.Prefix);

        await store.SaveBoxelAsync("F123", second);

        Assert.Equal(first.Prefix, (await store.LoadBoxelAsync("F123", first.Prefix))?.Prefix);
        Assert.Equal(second.Prefix, (await store.LoadBoxelAsync("F123", second.Prefix))?.Prefix);
        string commanderDirectory = Path.Combine(temporaryDirectory, BoxelSurveyStatsStore.StoreDirectoryName, "F123");
        string[] boxelFiles = Directory
            .EnumerateFiles(commanderDirectory, "*.json")
            .Where(path => !string.Equals(Path.GetFileName(path), "index.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal(2, boxelFiles.Length);
    }

    [Fact]
    public async Task MissingBoxelReturnsNull()
    {
        var store = new BoxelSurveyStatsStore(temporaryDirectory);
        Assert.Null(await store.LoadBoxelAsync("F123", "Praea Euq IL-P c5-"));
        Assert.Empty(await store.ListIndexAsync("F123"));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData("a/b")]
    public async Task RejectsInvalidFrontierId(string frontierId)
    {
        var store = new BoxelSurveyStatsStore(temporaryDirectory);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ListIndexAsync(frontierId));
        await Assert.ThrowsAsync<ArgumentException>(() => store.LoadBoxelAsync(frontierId, "Praea Euq IL-P c5-"));
    }

    [Fact]
    public void SanitizeKeepsTrailingHyphenAndReplacesIllegalCharacters()
    {
        Assert.Equal("Praea Euq IL-P c5-", BoxelSurveyStatsStore.SanitizePrefix("Praea Euq IL-P c5-"));
        Assert.Equal("Odd_name_here", BoxelSurveyStatsStore.SanitizePrefix("Odd:name/here"));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static BoxelSurveyBoxelDocument CreateDocument(string prefix, string generatedName, long address)
    {
        var state = new BoxelSurveyStatsState();
        state.Apply(
            Parse(
                $$"""
                {"timestamp":"2026-07-10T12:00:00Z","event":"FSDJump","StarSystem":"{{generatedName}}","SystemAddress":{{address}}}
                """
            )
        );
        Assert.True(state.TryCreateDocument(prefix, out BoxelSurveyBoxelDocument? document));
        return document;
    }

    private static SrvSurvey.Core.Journal.JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            SrvSurvey.Core.Journal.JournalEventEnvelope.TryParse(
                json,
                out JournalEventEnvelope? journalEvent,
                out string? error
            ),
            error
        );
        return Assert.IsType<SrvSurvey.Core.Journal.JournalEventEnvelope>(journalEvent);
    }
}
