using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed partial class JourneyServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnEditRetainsUnpersistedLiveProgressAndAnotherWritersSavedNotes(bool noteEdit)
    {
        await WriteJournalAsync(
            "Journal.2026-07-01T000000.01.log",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal("2026-07-01T00:05:00Z")
        );
        bool rejectWrites = false;
        var store = new JourneyStore(
            DataDirectory,
            (path, token) =>
                rejectWrites
                    ? Task.FromException<FileStream>(new IOException("Temporarily unavailable Journey storage"))
                    : FileMutationLease.AcquireAsync(path, token)
        );
        var service = new JourneyService(
            store,
            new JourneyJournalHistoryReader(JournalDirectory),
            new CommanderProfileStore(DataDirectory),
            new ExobiologyReferenceCatalog([])
        );
        JourneyJournalSystemSearchResult start = await service.FindLatestStartAsync("F123", true, 42);
        JourneyDocument original = (
            await service.BeginAsync(new("F123", "Test", true, "Original", "", start.Entry!))
        ).Journey!;
        rejectWrites = true;
        await Assert.ThrowsAsync<IOException>(() =>
            service.ApplyLiveAsync([
                Parse("""{"timestamp":"2026-07-01T00:06:00Z","event":"Screenshot"}"""),
                Parse(
                    """{"timestamp":"2026-07-01T00:07:00Z","event":"FSDJump","StarSystem":"Lave","SystemAddress":43,"StarPos":[1,2,3]}"""
                ),
                Parse("""{"timestamp":"2026-07-01T00:08:00Z","event":"Screenshot"}"""),
            ])
        );
        rejectWrites = false;
        await CreateService().RecordNoteAsync(original, 42, original.VisitedSystems[0].Arrived);
        JourneyDocument saved = noteEdit
            ? await service.RecordNoteAsync(original, 43, new DateTimeOffset(2026, 7, 1, 0, 7, 0, TimeSpan.Zero))
            : await service.SaveMetadataAsync(original, "Renamed", "Field notes");
        Assert.Equal(["Sol", "Lave"], saved.VisitedSystems.Select(visit => visit.StarSystem.Name));
        Assert.Equal([1, 1], saved.VisitedSystems.Select(visit => visit.Counts.Screenshots));
        Assert.Equal([1, noteEdit ? 1 : 0], saved.VisitedSystems.Select(visit => visit.Counts.Notes));
        Assert.Equal(noteEdit ? "Original" : "Renamed", saved.Name);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 8, 0, TimeSpan.Zero), saved.Watermark);
        await service.ApplyLiveAsync([]);
        JourneyDocument stored = (await store.LoadAsync("F123", original.FileName)).Journey!;
        Assert.Equal([1, 1], stored.VisitedSystems.Select(visit => visit.Counts.Screenshots));
        Assert.Equal([1, noteEdit ? 1 : 0], service.ActiveJourney!.VisitedSystems.Select(visit => visit.Counts.Notes));
    }
}
