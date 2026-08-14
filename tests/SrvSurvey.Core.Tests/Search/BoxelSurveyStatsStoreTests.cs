using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class BoxelSurveyStatsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-BoxelSurveyStatsStoreTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveLoadAndIndexPreserveSystemBodiesAndHelium()
    {
        var store = new BoxelSurveyStatsStore(temporaryDirectory);
        var source = new BoxelSurveyStatsState();
        source.Apply(Parse(
            """{"timestamp":"2026-07-10T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-0","SystemAddress":2001}"""));
        source.Apply(Parse(
            """{"event":"Scan","SystemAddress":2001,"BodyID":2,"PlanetClass":"Water world","MassEM":1.1,"AtmosphereComposition":[{"Name":"Helium","Percent":27.4}]}"""));
        Assert.True(source.TryCreateDocument("Praea Euq IL-P c5-", out var document));

        await store.SaveBoxelAsync("F123", document);
        var loaded = await store.LoadBoxelAsync("F123", "Praea Euq IL-P c5-");
        var index = await store.ListIndexAsync("F123");
        var catalog = await store.LoadCatalogAsync("F123");

        Assert.NotNull(loaded);
        Assert.Equal(document.Prefix, loaded.Prefix);
        var system = Assert.Single(loaded.Systems);
        var body = Assert.Single(system.Bodies);
        Assert.Equal(BoxelPlanetClass.WaterWorld, body.Class);
        Assert.Equal(27.4, body.HeliumPercent);
        var entry = Assert.Single(index);
        Assert.Equal("Praea Euq IL-P c5-", entry.Prefix);
        Assert.Equal('c', entry.MassCode);
        Assert.Equal(1, entry.VisitedSystemCount);
        Assert.Equal(27.4, entry.MinHeliumPercent);
        Assert.Equal("F123", catalog.FrontierId);
        Assert.False(File.Exists(Path.Combine(temporaryDirectory, "F123-live.json")));
        Assert.True(File.Exists(Path.Combine(
            temporaryDirectory,
            BoxelSurveyStatsStore.StoreDirectoryName,
            "F123",
            "index.json")));
    }

    [Fact]
    public async Task CommandersAreIsolatedByFrontierId()
    {
        var store = new BoxelSurveyStatsStore(temporaryDirectory);
        var first = CreateDocument("Praea Euq IL-P c5-", "Praea Euq IL-P c5-0", 2001);
        var second = CreateDocument("Wregoe BU-Y b2-", "Wregoe BU-Y b2-0", 2002);
        await store.SaveBoxelAsync("F-A", first);
        await store.SaveBoxelAsync("F-B", second);

        Assert.Equal("Praea Euq IL-P c5-", Assert.Single(await store.ListIndexAsync("F-A")).Prefix);
        Assert.Equal("Wregoe BU-Y b2-", Assert.Single(await store.ListIndexAsync("F-B")).Prefix);
        Assert.Null(await store.LoadBoxelAsync("F-A", "Wregoe BU-Y b2-"));
        Assert.Null(await store.LoadBoxelAsync("F-B", "Praea Euq IL-P c5-"));
    }

    [Fact]
    public async Task DamagedBoxelFileDoesNotHideTheIndex()
    {
        var store = new BoxelSurveyStatsStore(temporaryDirectory);
        await store.SaveBoxelAsync(
            "F123",
            CreateDocument("Praea Euq IL-P c5-", "Praea Euq IL-P c5-0", 2001));
        var commanderDirectory = Path.Combine(
            temporaryDirectory,
            BoxelSurveyStatsStore.StoreDirectoryName,
            "F123");
        File.Delete(Path.Combine(commanderDirectory, "index.json"));
        await File.WriteAllTextAsync(
            Path.Combine(commanderDirectory, "broken.json"),
            "not json");

        var entry = Assert.Single(await store.ListIndexAsync("F123"));
        Assert.Equal("Praea Euq IL-P c5-", entry.Prefix);
    }

    [Fact]
    public async Task MissingBoxelReturnsNull()
    {
        var store = new BoxelSurveyStatsStore(temporaryDirectory);
        Assert.Null(await store.LoadBoxelAsync("F123", "Praea Euq IL-P c5-"));
        Assert.Empty(await store.ListIndexAsync("F123"));
    }

    [Fact]
    public void SanitizeKeepsTrailingHyphenAndReplacesIllegalCharacters()
    {
        Assert.Equal(
            "Praea Euq IL-P c5-",
            BoxelSurveyStatsStore.SanitizePrefix("Praea Euq IL-P c5-"));
        Assert.Equal(
            "Odd_name_here",
            BoxelSurveyStatsStore.SanitizePrefix("Odd:name/here"));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static BoxelSurveyBoxelDocument CreateDocument(
        string prefix,
        string generatedName,
        long address)
    {
        var state = new BoxelSurveyStatsState();
        state.Apply(Parse(
            $$"""
            {"timestamp":"2026-07-10T12:00:00Z","event":"FSDJump","StarSystem":"{{generatedName}}","SystemAddress":{{address}}}
            """));
        Assert.True(state.TryCreateDocument(prefix, out var document));
        return document;
    }

    private static SrvSurvey.Core.Journal.JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            SrvSurvey.Core.Journal.JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return Assert.IsType<SrvSurvey.Core.Journal.JournalEventEnvelope>(journalEvent);
    }
}
