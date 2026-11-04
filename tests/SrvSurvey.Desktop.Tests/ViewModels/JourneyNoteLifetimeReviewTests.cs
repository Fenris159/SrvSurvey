using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class JourneyWorkspaceViewModelTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoteSaveFinishingAfterCommanderSwitchDoesNotRestoreAnAlreadySavedDraft(bool beforeWrite)
    {
        await SeedHistoricalJourneyAsync(1);
        var notes = new PausedNoteStore(new SystemNoteStore(DataDirectory), pauseWrite: true);
        JourneyWorkspaceViewModel model = CreateViewModel(noteStore: notes);
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        if (beforeWrite)
        {
            notes.PauseNextRead();
        }
        model.SelectedSystemNotes = "Committed during switch";
        Task save = model.SaveAsync();
        await notes.Entered.Task;
        await model.UpdateContextAsync("F456", "Other", true, "Sol", 42);
        notes.Release.SetResult();
        await save;
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);

        Assert.False(model.IsDirty);
        Assert.Equal("Committed during switch", model.SelectedSystemNotes);
        await model.SaveAsync();
        Assert.Equal("Committed during switch", (await notes.LoadAsync("F123", "Sol", 42)).Notes);
        Assert.Equal(2, model.SelectedSystem!.Visit.Counts.Notes);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveReadoutChangesWaitForTheOriginalNoteBaseline(bool switchCommander)
    {
        await SeedHistoricalJourneyAsync(1);
        var store = new SystemNoteStore(DataDirectory);
        await store.SaveAsync(new("F123", "Drew", "Sol", 42, new(0, 0, 0)), "Stored baseline");
        var notes = new PausedNoteStore(store, pauseWrite: false);
        JourneyWorkspaceViewModel model = CreateViewModel(noteStore: notes);
        Task initialize = model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        await notes.Entered.Task;
        model.SelectedSystemNotes = "Draft addition";
        if (switchCommander)
        {
            await model.UpdateContextAsync("F456", "Other", true, "Sol", 42);
        }
        else
        {
            await model.SetPreferencesAsync(true, true);
        }
        notes.Release.SetResult();
        await initialize;
        if (switchCommander)
        {
            await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        }
        await model.SaveAsync();

        Assert.False(model.IsDirty, model.StatusMessage);
        Assert.Equal(
            "Stored baseline" + Environment.NewLine + "Draft addition",
            (await store.LoadAsync("F123", "Sol", 42)).Notes
        );
        Assert.Equal(2, model.SelectedSystem!.Visit.Counts.Notes);
    }

    [AvaloniaFact]
    public async Task PendingNoteCountUsesTheCorrectedVisitAfterCommanderReturn()
    {
        JourneyDocument original = await SeedHistoricalJourneyAsync(1);
        var store = new JourneyStore(DataDirectory);
        JourneySystemVisit first = original.VisitedSystems[0] with { Counts = JourneyCounts.Empty };
        JourneySystemVisit redundant = first with { Arrived = first.Arrived.AddMinutes(1) };
        original = original with { VisitedSystems = [first, redundant] };
        await store.SaveAsync(original);
        string held = original.FilePath + ".held";
        var notes = new NoteWriteBoundary(
            new SystemNoteStore(DataDirectory),
            () =>
            {
                File.Move(original.FilePath, held);
                Directory.CreateDirectory(original.FilePath);
            }
        );
        JourneyWorkspaceViewModel model = CreateViewModel(noteStore: notes);
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        Assert.Equal(redundant.Arrived, model.SelectedSystem!.Visit.Arrived);
        model.SelectedSystemNotes = "Count survives correction";
        await model.SaveAsync();
        Assert.True(model.IsDirty);
        Directory.Delete(original.FilePath);
        File.Move(held, original.FilePath);
        await model.UpdateContextAsync("F456", "Other", true, "Sol", 42);
        await store.SaveAsync(original with { VisitedSystems = [first] });
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        await model.SaveAsync();

        Assert.False(model.IsDirty, model.StatusMessage);
        Assert.Equal(first.Arrived, model.SelectedSystem.Visit.Arrived);
        Assert.Equal(1, model.SelectedSystem.Visit.Counts.Notes);
        Assert.Equal("Count survives correction", (await notes.LoadAsync("F123", "Sol", 42)).Notes);
    }

    private sealed class PausedNoteStore(SystemNoteStore store, bool pauseWrite) : ISystemNoteStore
    {
        private bool paused;
        private bool pauseRead;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void PauseNextRead() => pauseRead = true;

        public async Task<SystemNoteLoadResult> LoadAsync(
            string frontierId,
            string systemName,
            long systemAddress,
            CancellationToken cancellationToken = default
        )
        {
            if ((!pauseWrite || pauseRead) && !paused)
            {
                paused = true;
                Entered.SetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await store.LoadAsync(frontierId, systemName, systemAddress, cancellationToken);
        }

        public async Task<string> SaveAsync(
            SystemNoteContext context,
            string? notes,
            CancellationToken cancellationToken = default
        )
        {
            if (pauseWrite && !paused)
            {
                paused = true;
                Entered.SetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await store.SaveAsync(context, notes, cancellationToken);
        }
    }
}
