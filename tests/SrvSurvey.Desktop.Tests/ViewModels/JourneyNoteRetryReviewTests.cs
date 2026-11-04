using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class JourneyWorkspaceViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ACommittedNoteCountCanFinishAfterAnExternalEditWithoutOverwritingIt(bool furtherLocalEdit)
    {
        JourneyDocument document = await SeedHistoricalJourneyAsync(1);
        document = document with
        {
            VisitedSystems = [document.VisitedSystems[0] with { Counts = JourneyCounts.Empty }],
        };
        var journeys = new JourneyStore(DataDirectory);
        await journeys.SaveAsync(document);
        var store = new SystemNoteStore(DataDirectory);
        bool block = true;
        string held = document.FilePath + ".held";
        var boundary = new NoteWriteBoundary(
            store,
            () =>
            {
                if (block)
                {
                    block = false;
                    File.Move(document.FilePath, held);
                    Directory.CreateDirectory(document.FilePath);
                }
            }
        );
        JourneyWorkspaceViewModel model = CreateViewModel(noteStore: boundary);
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        model.SelectedSystemNotes = "Committed first edit";
        await model.SaveAsync();
        Assert.Contains("not fully saved", model.StatusMessage, StringComparison.Ordinal);
        Directory.Delete(document.FilePath);
        File.Move(held, document.FilePath);
        await store.SaveAsync(new("F123", "Drew", "Sol", 42, new(0, 0, 0)), "External edit");
        if (furtherLocalEdit)
        {
            model.SelectedSystemNotes = "Unsaved later edit";
        }

        await model.SaveAsync();

        JourneyDocument saved = (await journeys.LoadAsync("F123", document.FileName)).Journey!;
        Assert.Equal(1, Assert.Single(saved.VisitedSystems).Counts.Notes);
        Assert.Equal("External edit", (await store.LoadAsync("F123", "Sol", 42)).Notes);
        Assert.Equal(furtherLocalEdit, model.IsDirty);
        if (furtherLocalEdit)
        {
            Assert.Contains("stored system notes changed", model.StatusMessage, StringComparison.Ordinal);
            Assert.True(model.DiscardCommand.CanExecute(null));
            await model.DiscardAsync();
        }
        await model.RefreshAsync();
        Assert.False(model.IsDirty);
        Assert.Equal("External edit", model.SelectedSystemNotes);
        await model.SaveAsync();
        Assert.Equal(
            1,
            Assert.Single((await journeys.LoadAsync("F123", document.FileName)).Journey!.VisitedSystems).Counts.Notes
        );
    }
}
