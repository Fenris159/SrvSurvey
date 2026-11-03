using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class JourneyWorkspaceViewModelTests
{
    [Fact]
    public async Task ClearingAnUnavailableActiveSelectionKeepsItsHistoryReadErrorVisible()
    {
        JourneyDocument journey = await SeedHistoricalJourneyAsync(1, complete: false);
        await File.WriteAllTextAsync(journey.FilePath, "{broken");
        await new CommanderProfileStore(DataDirectory).SaveActiveJourneyAsync("F123", "Drew", true, journey.FileName);
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        await model.ClearUnavailableJourneyAsync();
        Assert.False(model.HasUnavailableJourney);
        Assert.Contains("was cleared", model.StartStatus, StringComparison.Ordinal);
        Assert.Contains(Path.GetFileName(journey.FilePath), model.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("was cleared", model.StatusMessage, StringComparison.Ordinal);
        Assert.Equal("{broken", await File.ReadAllTextAsync(journey.FilePath));
    }

    [Fact]
    public async Task NewEditsToAReadableJourneyTakePriorityOverAnUnavailableRetainedDraft()
    {
        JourneyDocument missing = await SeedHistoricalJourneyAsync(1);
        JourneyDocument readable = await SeedHistoricalJourneyAsync(2);
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        model.SelectedJourney = model.Journeys.Single(item => item.Document.FileName == missing.FileName);
        model.JourneyDescription = "Older unavailable draft";
        await model.UpdateContextAsync("F456", "Other", true, "Sol", 42);
        File.Delete(missing.FilePath);
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        Assert.Equal(readable.FileName, model.SelectedJourney.Document.FileName);
        model.JourneyDescription = "Newer readable draft";
        await model.UpdateContextAsync("F456", "Other", true, "Sol", 42);
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        Assert.Equal(readable.FileName, model.SelectedJourney.Document.FileName);
        Assert.Equal("Newer readable draft", model.JourneyDescription);
        Assert.True(model.IsDirty);
    }

    [Fact]
    public async Task AReportCannotBeSavedOrPublishPickerStatusThroughAnotherCommander()
    {
        await SeedHistoricalJourneyAsync(1);
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        JourneyHtmlExport report = Assert.IsType<JourneyHtmlExport>(await model.CreateHtmlReportAsync());
        await model.UpdateContextAsync("F456", "Other", true, "Sol", 42);
        string status = model.StatusMessage;
        string destination = Path.Combine(temporaryDirectory, "wrong-commander-report");
        await model.SaveHtmlReportAsync(report, destination);
        model.SetExportStatus(report, "Old picker canceled");
        Assert.False(Directory.Exists(destination));
        Assert.Equal(status, model.StatusMessage);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task EditsEnteredDuringANoteLoadKeepTheStoredBaselineAndSelectedVisit()
    {
        JourneyDocument document = await SeedHistoricalJourneyAsync(1);
        JourneySystemVisit sol = document.VisitedSystems[0] with { Counts = JourneyCounts.Empty };
        JourneySystemVisit lave = sol with
        {
            StarSystem = new("Lave", 43, new(1, 2, 3)),
            Arrived = sol.Arrived.AddMinutes(1),
        };
        await new JourneyStore(DataDirectory).SaveAsync(document with { VisitedSystems = [sol, lave] });
        var notes = new SystemNoteStore(DataDirectory);
        await notes.SaveAsync(new("F123", "Drew", "Sol", 42, new(0, 0, 0)), "Stored Sol note");
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Lave", 43);
        bool entered = false;
        model.PropertyChanged += (_, args) =>
        {
            if (
                !entered
                && args.PropertyName == nameof(model.SelectedSystemNotes)
                && model.SelectedSystem?.Name == "Sol"
            )
            {
                entered = true;
                Assert.False(model.CanEditSystemNotes);
                model.SelectedSystemNotes = "New draft";
            }
        };
        model.SelectedSystem = model.VisitedSystems.Single(item => item.Name == "Sol");
        await model.SaveAsync();
        Assert.True(entered);
        Assert.True(model.CanEditSystemNotes);
        Assert.Equal("Stored Sol note" + Environment.NewLine + "New draft", model.SelectedSystemNotes);
        Assert.Equal(
            "Stored Sol note" + Environment.NewLine + "New draft",
            (await notes.LoadAsync("F123", "Sol", 42)).Notes
        );
        JourneyDocument saved = (await new JourneyStore(DataDirectory).LoadAsync("F123", document.FileName)).Journey!;
        Assert.Equal([1, 0], saved.VisitedSystems.Select(visit => visit.Counts.Notes));
    }

    [Fact]
    public async Task SavingNotesDoesNotOverwriteAStoredEditThatWasNeverLoaded()
    {
        await SeedHistoricalJourneyAsync(1);
        var notes = new SystemNoteStore(DataDirectory);
        var context = new SystemNoteContext("F123", "Drew", "Sol", 42, new(0, 0, 0));
        await notes.SaveAsync(context, "Initial");
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        model.SelectedSystemNotes = "Draft";
        await notes.SaveAsync(context, "External edit");
        await model.SaveAsync();
        Assert.Equal("External edit", (await notes.LoadAsync("F123", "Sol", 42)).Notes);
        Assert.Equal("Draft", model.SelectedSystemNotes);
        Assert.Contains("stored system notes changed", model.StatusMessage, StringComparison.Ordinal);
        Assert.True(model.IsDirty);
    }
}
