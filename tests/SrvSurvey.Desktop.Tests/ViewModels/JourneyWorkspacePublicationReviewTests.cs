using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.ViewModels;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed partial class JourneyWorkspaceViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOriginalSystemRetainsItsNoteDraftAcrossCommanderChanges(bool initialReadFails)
    {
        JourneyDocument original = await SeedHistoricalJourneyAsync(1);
        var notes = new SystemNoteStore(DataDirectory);
        if (initialReadFails)
        {
            await notes.SaveAsync(new("F123", "Drew", "Sol", 42, new(0, 0, 0)), "Stored baseline");
        }
        JourneyWorkspaceViewModel model = CreateViewModel(
            noteStore: initialReadFails ? new FirstNoteReadFailure(notes) : notes
        );
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        Assert.Empty(model.SelectedSystemNotes);
        model.SelectedSystemNotes = "Sol only";
        await model.UpdateContextAsync("F456", "Other", true, "Sirius", 44);
        var store = new JourneyStore(DataDirectory);
        await store.SaveAsync(
            original with
            {
                VisitedSystems = [original.VisitedSystems[0] with { StarSystem = new("Sirius", 44, new(5, 0, 0)) }],
            }
        );
        await model.UpdateContextAsync("F123", "Drew", true, "Sirius", 44);
        Assert.Null(model.SelectedSystem);
        Assert.False(model.CanEditSystemNotes);
        Assert.Equal("Sol only", model.SelectedSystemNotes);
        await model.SaveAsync();
        Assert.True(model.IsDirty);
        Assert.Contains("notes were retained", model.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(string.Empty, (await new SystemNoteStore(DataDirectory).LoadAsync("F123", "Sirius", 44)).Notes);

        await model.UpdateContextAsync("F456", "Other", true, "Sirius", 44);
        await store.SaveAsync(original);
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        string expected = initialReadFails ? "Stored baseline" + Environment.NewLine + "Sol only" : "Sol only";
        Assert.Equal("Sol", model.SelectedSystemName);
        Assert.Equal(expected, model.SelectedSystemNotes);
        await model.SaveAsync();
        Assert.False(model.IsDirty, model.StatusMessage);
        Assert.Equal(expected, (await notes.LoadAsync("F123", "Sol", 42)).Notes);
        Assert.Equal(2, model.SelectedSystem!.Visit.Counts.Notes);
    }

    private sealed class FirstNoteReadFailure(ISystemNoteStore store) : ISystemNoteStore
    {
        private int failNextRead = 1;

        public async Task<SystemNoteLoadResult> LoadAsync(
            string frontierId,
            string systemName,
            long systemAddress,
            CancellationToken cancellationToken = default
        )
        {
            bool fail = Interlocked.Exchange(ref failNextRead, 0) == 1;
            SystemNoteLoadResult result = await store.LoadAsync(
                frontierId,
                systemName,
                systemAddress,
                cancellationToken
            );
            return fail ? result with { Notes = null, Error = "Injected note read failure." } : result;
        }

        public Task<string> SaveAsync(
            SystemNoteContext context,
            string? notes,
            CancellationToken cancellationToken = default
        ) => store.SaveAsync(context, notes, cancellationToken);
    }

    [AvaloniaFact]
    public async Task RemovedVisitKeepsUnsavedNotesWithTheirOriginalSystem()
    {
        const string file = "Journal.2026-07-01T000000.01.log";
        const string preamble =
            "{\"timestamp\":\"2026-07-01T00:00:00Z\",\"event\":\"Fileheader\",\"Odyssey\":true}\n{\"timestamp\":\"2026-07-01T00:00:01Z\",\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n{\"timestamp\":\"2026-07-01T00:05:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\",\"SystemAddress\":42,\"StarPos\":[0,0,0]}\n";
        const string location =
            "{\"timestamp\":\"2026-07-01T00:06:00Z\",\"event\":\"Location\",\"StarSystem\":\"Sol\",\"SystemAddress\":42,\"StarPos\":[0,0,0]}\n";
        const string arrival =
            "{\"timestamp\":\"2026-07-01T00:06:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sirius\",\"SystemAddress\":44,\"StarPos\":[5,0,0]}\n";
        await WriteJournalAsync(preamble);
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        await model.StartNewJourneyAsync();
        model.NewJourneyName = "Notes correction";
        await model.BeginJourneyAsync();
        await File.AppendAllTextAsync(Path.Combine(JournalDirectory, file), arrival);
        await model.ApplyJournalEventsAsync(
            (await new JournalDirectoryMonitor(JournalDirectory, "F123").PollAsync()).JournalEvents
        );
        string partial = Path.Combine(temporaryDirectory, "partial-location");
        Directory.CreateDirectory(partial);
        await File.WriteAllTextAsync(Path.Combine(partial, file), preamble + location);
        await model.ApplyJournalEventsAsync(
            (await new JournalDirectoryMonitor(partial, "F123").PollAsync()).JournalEvents
        );
        Assert.Equal(3, model.VisitedSystems.Count);
        model.SelectedSystem = model.VisitedSystems.Single(item => item.Name == "Sol" && item.Visit.Departed is null);
        model.SelectedSystemNotes = "Sol draft";
        await WithBoundJourneyAsync(
            model,
            async () =>
            {
                string complete = Path.Combine(temporaryDirectory, "complete-location");
                Directory.CreateDirectory(complete);
                await File.WriteAllTextAsync(Path.Combine(complete, file), preamble + location + arrival);
                await model.ApplyJournalEventsAsync(
                    (await new JournalDirectoryMonitor(complete, "F123").PollAsync()).JournalEvents
                );

                Assert.Equal(2, model.VisitedSystems.Count);
                Assert.Equal("Sol", model.SelectedSystemName);
                Assert.Equal("Sol draft", model.SelectedSystemNotes);
                Assert.True(model.IsDirty);
                await model.SaveAsync();
                Assert.False(model.IsDirty);
                Assert.Equal(
                    "Sol draft",
                    (await new SystemNoteStore(DataDirectory).LoadAsync("F123", "Sol", 42)).Notes
                );
                Assert.Equal(
                    string.Empty,
                    (await new SystemNoteStore(DataDirectory).LoadAsync("F123", "Sirius", 44)).Notes
                );
                Assert.Equal(1, model.VisitedSystems.Single(item => item.Name == "Sol").Visit.Counts.Notes);
                Assert.Equal(0, model.VisitedSystems.Single(item => item.Name == "Sirius").Visit.Counts.Notes);
            }
        );
    }

    private static async Task WithBoundJourneyAsync(JourneyWorkspaceViewModel model, Func<Task> action)
    {
        var view = new JourneysView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = 1000,
            Height = 800,
        };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Same(model.SelectedSystem, view.FindControl<ListBox>("JourneyVisitList")!.SelectedItem);
            await action();
        }
        finally
        {
            window.Close();
        }
    }

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
