using System.Text.Json.Nodes;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed class JourneyHistoryTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-journey-history-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task ArchiveAndRestoreSurviveRestartAndPreserveUnknownFields()
    {
        var store = new JourneyStore(directory);
        JourneyDocument completed = await CreateAsync(store, 1, true);
        JsonObject root = JsonNode.Parse(await File.ReadAllTextAsync(completed.FilePath))!.AsObject();
        root["futureField"] = "preserved";
        root["archived"] = null;
        await File.WriteAllTextAsync(completed.FilePath, root.ToJsonString());
        Assert.False((await store.LoadAsync("F123", completed.FileName)).Journey!.IsArchived);
        JourneyService service = Service(store);
        JourneyDocument archived = await service.SetArchivedAsync("F123", completed.FileName, true);
        Assert.True(archived.IsArchived);
        Assert.True(Assert.Single((await new JourneyStore(directory).LoadAllAsync("F123")).Journeys).IsArchived);
        JsonObject saved = JsonNode.Parse(await File.ReadAllTextAsync(completed.FilePath))!.AsObject();
        Assert.Equal("preserved", saved["futureField"]!.GetValue<string>());
        JourneyDocument restored = await service.SetArchivedAsync("F123", completed.FileName, false);
        Assert.False(restored.IsArchived);
        Assert.False((await store.LoadAsync("F123", completed.FileName)).Journey!.IsArchived);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteRemovesOnlyTheCompletedJourney(bool archived)
    {
        var store = new JourneyStore(directory);
        JourneyDocument completed = await CreateAsync(store, 1, true);
        JourneyDocument active = await CreateAsync(store, 2, false);
        if (archived)
        {
            await store.SetArchivedAsync("F123", completed.FileName, true);
        }

        string notes = Path.Combine(directory, "notes.txt");
        await File.WriteAllTextAsync(notes, "System notes remain");
        await Service(store).DeleteAsync("F123", completed.FileName);
        Assert.False(File.Exists(completed.FilePath));
        Assert.True(File.Exists(active.FilePath));
        Assert.Equal("System notes remain", await File.ReadAllTextAsync(notes));
        Assert.Equal(active.FileName, Assert.Single((await store.LoadAllAsync("F123")).Journeys).FileName);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.DeleteAsync("F123", completed.FileName));
    }

    [Fact]
    public async Task ActiveJourneysCannotBeArchivedOrDeleted()
    {
        var store = new JourneyStore(directory);
        JourneyDocument active = await CreateAsync(store, 1, false);
        JourneyService service = Service(store);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetArchivedAsync("F123", active.FileName, true)
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync("F123", active.FileName));
        Assert.True(File.Exists(active.FilePath));
    }

    [Theory]
    [InlineData("../F456", "test", typeof(ArgumentException))]
    [InlineData("F123", "../test", typeof(ArgumentException))]
    [InlineData("F123", "missing", typeof(InvalidDataException))]
    public async Task HistoryOperationsRejectInvalidIdentity(string fid, string name, Type exception)
    {
        var store = new JourneyStore(directory);
        await Assert.ThrowsAsync(exception, () => store.SetArchivedAsync(fid, name, true));
        await Assert.ThrowsAsync(exception, () => store.DeleteAsync(fid, name));
    }

    [Fact]
    public async Task HistoryOperationsRejectMalformedAndMismatchedCommanderData()
    {
        var store = new JourneyStore(directory);
        JourneyDocument completed = await CreateAsync(store, 1, true);
        string original = await File.ReadAllTextAsync(completed.FilePath);
        await File.WriteAllTextAsync(completed.FilePath, "{broken");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.DeleteAsync("F123", completed.FileName));
        await File.WriteAllTextAsync(completed.FilePath, original.Replace("F123", "F456", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SetArchivedAsync("F123", completed.FileName, true));
        Assert.True(File.Exists(completed.FilePath));
    }

    [Fact]
    public async Task CancelledOperationsKeepTheJourneyAndReleaseTheirLocks()
    {
        var store = new JourneyStore(directory);
        JourneyDocument completed = await CreateAsync(store, 1, true);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        JourneyService service = Service(store);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SetArchivedAsync("F123", completed.FileName, true, cancellation.Token)
        );
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.DeleteAsync("F123", completed.FileName, cancellation.Token)
        );
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.DeleteAsync("F123", completed.FileName, cancellation.Token)
        );
        Assert.False((await store.LoadAsync("F123", completed.FileName)).Journey!.IsArchived);
        await service.SetArchivedAsync("F123", completed.FileName, true);
    }

    [Fact]
    public async Task RewardValuesBeyondTwoBillionRoundTripAndAggregate()
    {
        var store = new JourneyStore(directory);
        JourneyDocument document = await CreateAsync(store, 1, true);
        var visit = new JourneySystemVisit(
            new("Sol", 1, new(0, 0, 0)),
            document.StartTime,
            null,
            JourneyCounts.Empty with
            {
                ExobiologyRewards = 3_000_000_000,
                ExplorationRewards = 4_000_000_000,
            },
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
        await store.SaveAsync(document with { VisitedSystems = [visit, visit] });
        JourneyDocument recovered = (await store.LoadAsync("F123", document.FileName)).Journey!;
        JourneyCounts totals = JourneyStatistics.Calculate(recovered).Counts;
        Assert.Equal(6_000_000_000, totals.ExobiologyRewards);
        Assert.Equal(8_000_000_000, totals.ExplorationRewards);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedActiveReferencesAreClearedBeforeTheHistoryCanBeDeleted(bool odyssey)
    {
        var store = new JourneyStore(directory);
        JourneyDocument completed = await CreateAsync(store, 1, true);
        var profiles = new CommanderProfileStore(directory);
        await profiles.SaveActiveJourneyAsync("F123", "Drew", odyssey, completed.FileName);
        JourneyService service = Service(store);
        JourneyServiceResult initialized = await service.InitializeActiveAsync("F123", odyssey);
        Assert.Equal(completed.FileName, initialized.Journey!.FileName);
        Assert.Null(service.ActiveJourney);
        Assert.Null((await profiles.LoadAsync("F123", odyssey)).Data!.ActiveJourneyFileName);
        await service.DeleteAsync("F123", completed.FileName);
        Assert.Empty((await service.InitializeActiveAsync("F123", odyssey)).Errors);
    }

    private JourneyService Service(JourneyStore store) =>
        new(store, new(directory), new CommanderProfileStore(directory), new ExobiologyReferenceCatalog([]));

    [Fact]
    public async Task FailedProfileCleanupCannotKeepRecordingOrRecreateADeletedJourney()
    {
        var store = new JourneyStore(directory);
        JourneyDocument active = await CreateAsync(store, 1, false);
        await WriteStartJournalAsync();
        var profiles = new CommanderProfileStore(directory);
        await profiles.SaveActiveJourneyAsync("F123", "Drew", true, active.FileName);
        JourneyService service = Service(store);
        await service.InitializeActiveAsync("F123", true);
        Assert.NotNull(service.ActiveJourney);
        string profilePath = profiles.GetProfilePath("F123", true);
        File.Delete(profilePath);
        Directory.CreateDirectory(profilePath);
        Exception? failure = await Record.ExceptionAsync(() =>
            service.ConcludeActiveAsync("Drew", active.StartTime.AddHours(1))
        );
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Null(service.ActiveJourney);
        Assert.False((await store.LoadAsync("F123", active.FileName)).Journey!.IsActive);
        Directory.Delete(profilePath);
        await profiles.SaveActiveJourneyAsync("F123", "Drew", true, active.FileName);
        await service.DeleteAsync("F123", active.FileName);
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"timestamp":"2026-10-30T10:00:00Z","event":"Screenshot"}""",
                out JournalEventEnvelope? entry,
                out _
            )
        );
        Assert.Equal(0, (await service.ApplyLiveAsync([entry!])).ProcessedEventCount);
        Assert.False(File.Exists(active.FilePath));
    }

    [Fact]
    public async Task OrdinarySaveCannotUndoANewerArchiveDecision()
    {
        var store = new JourneyStore(directory);
        JourneyDocument stale = await CreateAsync(store, 1, true);
        await WriteStartJournalAsync();
        await store.SetArchivedAsync("F123", stale.FileName, true);
        await store.SaveAsync(stale with { Description = "Updated description" });
        JourneyDocument saved = (await store.LoadAsync("F123", stale.FileName)).Journey!;
        Assert.True(saved.IsArchived);
        Assert.Equal("Updated description", saved.Description);
        Assert.True((await Service(store).SaveAsync(stale)).IsArchived);
        Assert.True((await Service(store).ReprocessAsync(stale, true)).Journey!.IsArchived);
    }

    [Fact]
    public async Task DeleteClearsOnlyMatchingActiveReferencesInBothGalaxyProfiles()
    {
        var store = new JourneyStore(directory);
        JourneyDocument completed = await CreateAsync(store, 1, true);
        var profiles = new CommanderProfileStore(directory);
        await profiles.SaveActiveJourneyAsync("F123", "Current name", true, completed.FileName);
        await profiles.SaveActiveJourneyAsync("F123", "Current name", false, "another-expedition");
        await Service(store).DeleteAsync("F123", completed.FileName);
        Assert.False(File.Exists(completed.FilePath));
        Assert.Null((await profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName);
        Assert.Equal("another-expedition", (await profiles.LoadAsync("F123", false)).Data!.ActiveJourneyFileName);
    }

    [Fact]
    public async Task ClearingAConcludedJourneyKeepsTheCurrentCommanderName()
    {
        var store = new JourneyStore(directory);
        JourneyDocument completed = await CreateAsync(store, 1, true);
        var profiles = new CommanderProfileStore(directory);
        await profiles.SaveActiveJourneyAsync("F123", "Renamed commander", true, completed.FileName);
        await Service(store).InitializeActiveAsync("F123", true);
        Assert.Equal("Renamed commander", (await profiles.LoadAsync("F123", true)).Data!.CommanderName);
    }

    private static async Task<JourneyDocument> CreateAsync(JourneyStore store, int second, bool complete)
    {
        DateTimeOffset at = DateTimeOffset.UnixEpoch.AddSeconds(second);
        JourneyDocument journey = await store.CreateAsync(
            new("F123", "Drew", $"Expedition {second}", "", "Journal.test.log", at)
        );
        if (complete)
        {
            journey = journey with { EndTime = at.AddHours(1) };
            await store.SaveAsync(journey);
        }
        return journey;
    }

    private Task WriteStartJournalAsync() =>
        File.WriteAllTextAsync(
            Path.Combine(directory, "Journal.test.log"),
            """
            {"timestamp":"1970-01-01T00:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"1970-01-01T00:00:00Z","event":"Commander","Name":"Drew","FID":"F123"}
            """
        );

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
