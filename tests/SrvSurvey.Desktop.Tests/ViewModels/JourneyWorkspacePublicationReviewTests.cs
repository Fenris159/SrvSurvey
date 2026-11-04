using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class JourneyWorkspaceViewModelTests
{
    [Fact]
    public async Task KnownRecordsWithNewOrderingRefreshTheReadoutWithoutDiscardingDrafts()
    {
        const string file = "Journal.2026-07-01T000000.01.log";
        const string preamble =
            "{\"timestamp\":\"2026-07-01T00:00:00Z\",\"event\":\"Fileheader\",\"Odyssey\":true}\n{\"timestamp\":\"2026-07-01T00:00:01Z\",\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n{\"timestamp\":\"2026-07-01T00:05:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\",\"SystemAddress\":42,\"StarPos\":[0,0,0]}\n";
        const string shot = "{\"timestamp\":\"2026-07-01T00:06:00Z\",\"event\":\"Screenshot\"}\n";
        const string arrival =
            "{\"timestamp\":\"2026-07-01T00:06:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sirius\",\"SystemAddress\":44,\"StarPos\":[5,0,0]}\n";
        await WriteJournalAsync(preamble);
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        await model.StartNewJourneyAsync();
        model.NewJourneyName = "Live corrections";
        await model.BeginJourneyAsync();
        await File.AppendAllTextAsync(Path.Combine(JournalDirectory, file), shot);
        await model.ApplyJournalEventsAsync(
            (await new JournalDirectoryMonitor(JournalDirectory, "F123").PollAsync()).JournalEvents
        );
        string partial = Path.Combine(temporaryDirectory, "partial");
        Directory.CreateDirectory(partial);
        await File.WriteAllTextAsync(Path.Combine(partial, file), preamble + arrival);
        await model.ApplyJournalEventsAsync(
            (await new JournalDirectoryMonitor(partial, "F123").PollAsync()).JournalEvents
        );
        Assert.Equal(1, model.VisitedSystems.Single(item => item.Name == "Sol").Visit.Counts.Screenshots);
        Assert.Equal(0, model.VisitedSystems.Single(item => item.Name == "Sirius").Visit.Counts.Screenshots);
        model.JourneyDescription = "Unsaved description";
        string complete = Path.Combine(temporaryDirectory, "complete");
        Directory.CreateDirectory(complete);
        await File.WriteAllTextAsync(Path.Combine(complete, file), preamble + arrival + shot);
        await model.ApplyJournalEventsAsync(
            (await new JournalDirectoryMonitor(complete, "F123").PollAsync()).JournalEvents
        );
        JourneyDocument stored = (
            await new JourneyStore(DataDirectory).LoadAsync("F123", model.SelectedJourney!.Document.FileName)
        ).Journey!;
        Assert.Equal([0, 1], stored.VisitedSystems.Select(visit => visit.Counts.Screenshots));
        Assert.Equal(0, model.VisitedSystems.Single(item => item.Name == "Sol").Visit.Counts.Screenshots);
        Assert.Equal(1, model.VisitedSystems.Single(item => item.Name == "Sirius").Visit.Counts.Screenshots);
        Assert.Equal("Unsaved description", model.JourneyDescription);
        Assert.True(model.IsDirty);
    }
}
