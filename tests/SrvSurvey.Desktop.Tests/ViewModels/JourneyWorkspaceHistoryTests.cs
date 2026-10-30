using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class JourneyWorkspaceViewModelTests
{
    [Fact]
    public async Task ArchiveRestoreAndConfirmedDeletePersistWithoutRemovingSystemNotes()
    {
        await SeedHistoricalJourneyAsync(1);
        JourneyDocument second = await SeedHistoricalJourneyAsync(2);
        var notes = new SystemNoteStore(DataDirectory);
        await notes.SaveAsync(new("F123", "Drew", "Sol", 42, new(0, 0, 0)), "A saved field note");
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        Assert.Equal(2, model.VisibleJourneys.Count);
        Assert.True(model.CanExportReport);
        JourneyHtmlExport report = Assert.IsType<JourneyHtmlExport>(await model.CreateHtmlReportAsync());
        Assert.Contains("A saved field note", report.Html, StringComparison.Ordinal);
        Assert.EndsWith(".html", report.FileName, StringComparison.Ordinal);
        await model.ArchiveSelectedAsync();
        Assert.Single(model.VisibleJourneys);
        Assert.Contains(model.Journeys, item => item.Document.IsArchived);
        Assert.Contains("Archived", model.StatusMessage, StringComparison.Ordinal);
        model.ShowArchived = true;
        Assert.True(Assert.Single(model.VisibleJourneys).Document.IsArchived);
        Assert.Equal("Restore journey", model.ArchiveButtonText);
        Assert.True(model.CanExportReport);
        await model.ArchiveSelectedAsync();
        Assert.Empty(model.VisibleJourneys);
        Assert.False(model.HasSelectedJourney);
        model.ShowArchived = false;
        Assert.Equal(2, model.VisibleJourneys.Count);
        Assert.Equal(second.FileName, model.SelectedJourney!.Document.FileName);
        await model.DeleteSelectedAsync();
        Assert.True(File.Exists(second.FilePath));
        model.RequestDeleteCommand.Execute(null);
        Assert.True(model.IsDeletePending);
        model.CancelDeleteCommand.Execute(null);
        Assert.False(model.IsDeletePending);
        model.RequestDeleteCommand.Execute(null);
        await model.DeleteSelectedAsync();
        Assert.False(model.IsDeletePending);
        Assert.Single(model.Journeys);
        Assert.False(File.Exists(second.FilePath));
        Assert.Equal("A saved field note", (await notes.LoadAsync("F123", "Sol", 42)).Notes);
        JourneyWorkspaceViewModel restarted = CreateViewModel();
        await restarted.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        Assert.Single(restarted.VisibleJourneys);
    }

    [Fact]
    public async Task ArchiveIsHiddenAfterRestartAndActiveJourneysCannotBeManagedOrExported()
    {
        JourneyDocument historical = await SeedHistoricalJourneyAsync(1);
        await new JourneyStore(DataDirectory).SetArchivedAsync("F123", historical.FileName, true);
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        Assert.Empty(model.VisibleJourneys);
        Assert.False(model.HasSelectedJourney);
        Assert.Contains("Archive (1)", model.ArchiveLabel, StringComparison.Ordinal);
        model.ShowArchived = true;
        Assert.True(model.HasSelectedJourney);
        await model.ArchiveSelectedAsync();
        model.ShowArchived = false;
        JourneyDocument active = await SeedHistoricalJourneyAsync(2, false);
        await model.RefreshAsync();
        model.SelectedJourney = model.Journeys.Single(item => item.Document.FileName == active.FileName);
        Assert.False(model.IsCompletedJourney);
        Assert.False(model.ArchiveCommand.CanExecute(null));
        Assert.False(model.RequestDeleteCommand.CanExecute(null));
        Assert.Null(await model.CreateHtmlReportAsync());
        await model.ArchiveSelectedAsync();
        await model.DeleteSelectedAsync();
        Assert.True(File.Exists(active.FilePath));
    }

    [Fact]
    public async Task DirtyJourneyEditsBlockSwitchingRefreshingAndHistoryActions()
    {
        await SeedHistoricalJourneyAsync(1);
        await SeedHistoricalJourneyAsync(2);
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        JourneyListItemViewModel original = model.SelectedJourney!;
        model.JourneyName = "Unsaved expedition name";
        model.ShowArchived = true;
        Assert.False(model.ShowArchived);
        model.SelectedJourney = model.Journeys.First(item => item.Document.FileName != original.Document.FileName);
        Assert.Equal(original, model.SelectedJourney);
        await model.RefreshAsync();
        Assert.Equal("Unsaved expedition name", model.JourneyName);
        Assert.False(model.ArchiveCommand.CanExecute(null));
        Assert.False(model.RequestDeleteCommand.CanExecute(null));
        Assert.False(model.CanExportReport);
        Assert.Null(await model.CreateHtmlReportAsync());
        await model.DiscardAsync();
        Assert.False(model.IsDirty);
        Assert.True(model.CanExportReport);
    }

    [Fact]
    public async Task FailedArchiveAndDeleteKeepTheCatalogAndReportTheFailure()
    {
        JourneyDocument document = await SeedHistoricalJourneyAsync(1);
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        File.Delete(document.FilePath);
        await model.ArchiveSelectedAsync();
        Assert.Contains("could not be moved", model.StatusMessage, StringComparison.Ordinal);
        Assert.Single(model.VisibleJourneys);
        model.RequestDeleteCommand.Execute(null);
        await model.DeleteSelectedAsync();
        Assert.Contains("could not be deleted", model.StatusMessage, StringComparison.Ordinal);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task LiveUpdatesKeepTheSelectedHistoricalJourneyAndDisconnectClearsTheReadout()
    {
        JourneyDocument historical = await SeedHistoricalJourneyAsync(1);
        await WriteJournalAsync(
            """
            {"timestamp":"2026-07-01T00:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-07-01T00:00:01Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2026-07-01T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            """
        );
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        await model.StartNewJourneyAsync();
        model.NewJourneyName = "Live expedition";
        await model.BeginJourneyAsync();
        model.SelectedJourney = model.Journeys.Single(item => item.Document.FileName == historical.FileName);
        model.JourneyDescription = "Keep this draft";
        await model.ApplyJournalEventsAsync([Parse("""{"timestamp":"2026-07-01T00:06:00Z","event":"Screenshot"}""")]);
        Assert.Equal(historical.FileName, model.SelectedJourney.Document.FileName);
        Assert.Equal("Keep this draft", model.JourneyDescription);
        Assert.True(model.IsDirty);
        await model.UpdateContextAsync(null, null, true, null, null);
        Assert.False(model.HasProfile);
        Assert.False(model.HasActiveJourney);
        Assert.False(model.HasSelectedJourney);
        Assert.Empty(model.Journeys);
        Assert.Equal("No active journey", model.ActiveJourneyName);
    }

    [Fact]
    public async Task InvalidSavedNotesAbortTheReportAndCompletedVisitsDoNotClaimToBeCurrent()
    {
        await SeedHistoricalJourneyAsync(1);
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        Assert.Contains("Journey ended here", model.SelectedSystemDetails, StringComparison.Ordinal);
        Assert.DoesNotContain("Currently in this system", model.SelectedSystemDetails, StringComparison.Ordinal);
        var notes = new SystemNoteStore(DataDirectory);
        string path = await notes.SaveAsync(new("F123", "Drew", "Sol", 42, new(0, 0, 0)), "Saved notes");
        await File.WriteAllTextAsync(path, "{broken");
        Assert.Null(await model.CreateHtmlReportAsync());
        Assert.Contains("report could not be created", model.StatusMessage, StringComparison.Ordinal);
        Assert.False(model.IsBusy);
    }

    private async Task<JourneyDocument> SeedHistoricalJourneyAsync(int minute, bool complete = true)
    {
        var store = new JourneyStore(DataDirectory);
        DateTimeOffset at = DateTimeOffset.UnixEpoch.AddMinutes(minute);
        JourneyDocument document = await store.CreateAsync(
            new("F123", "Drew", $"Expedition {minute}", "Recorded field log", "Journal.seed.log", at)
        );
        JourneySystemVisit visit = new(
            new("Sol", 42, new(0, 0, 0)),
            at,
            null,
            new(5, 2, 1, 1, 1, 5, 1, 1, 3_000_000_000, 1_000_000, 1),
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
        document = document with { EndTime = complete ? at.AddHours(1) : null, VisitedSystems = [visit] };
        await store.SaveAsync(document);
        return document;
    }
}
