using System.Text.Json.Nodes;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed partial class JourneyServiceTests
{
    [Fact]
    public async Task AnIndependentMetadataWriterKeepsChangesCommittedWhileWaitingForTheSharedLease()
    {
        await WriteJournalAsync(
            "Journal.2026-07-01T000000.01.log",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal("2026-07-01T00:05:00Z")
        );
        JourneyService initial = CreateService();
        JourneyJournalSystemSearchResult start = await initial.FindLatestStartAsync("F123", true, 42);
        JourneyDocument original = (
            await initial.BeginAsync(new("F123", "Test", true, "Original", "", start.Entry!))
        ).Journey!;
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var editingStore = new JourneyStore(
            DataDirectory,
            (path, token) =>
            {
                requested.TrySetResult();
                return FileMutationLease.AcquireAsync(path, token);
            }
        );
        var editing = new JourneyService(
            editingStore,
            new JourneyJournalHistoryReader(JournalDirectory),
            new CommanderProfileStore(DataDirectory),
            new ExobiologyReferenceCatalog([])
        );
        var otherStore = new JourneyStore(DataDirectory);
        Task<JourneyDocument> save;
        await using (FileStream lease = await otherStore.AcquireMutationAsync("F123", CancellationToken.None))
        {
            save = editing.SaveMetadataAsync(original, "Renamed", "Edited metadata");
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            JsonObject root = JsonNode.Parse(await File.ReadAllTextAsync(original.FilePath))!.AsObject();
            root["visitedSystems"]![0]!["count"]!["notes"] = 7;
            await File.WriteAllTextAsync(original.FilePath, root.ToJsonString());
        }
        JourneyDocument result = await save;
        Assert.Equal("Renamed", result.Name);
        Assert.Equal(7, Assert.Single(result.VisitedSystems).Counts.Notes);
        JourneyDocument stored = (await otherStore.LoadAsync("F123", original.FileName)).Journey!;
        Assert.Equal("Renamed", stored.Name);
        Assert.Equal(7, Assert.Single(stored.VisitedSystems).Counts.Notes);
    }
}
