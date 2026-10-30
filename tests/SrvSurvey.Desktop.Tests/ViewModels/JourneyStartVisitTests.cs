using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class JourneyWorkspaceViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedArrivalStartsReplayAtThatVisitIncludingLaterReturns(bool earliest)
    {
        await WriteRepeatedVisitsAsync();
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        await model.StartNewJourneyAsync();
        model.NewJourneyName = "Chosen start";
        Assert.False(model.BeginJourneyCommand.CanExecute(null));
        await model.FindStartAsync();
        Assert.Equal(3, model.StartVisits.Count);
        Assert.Null(model.SelectedStartVisit);
        Assert.False(model.BeginJourneyCommand.CanExecute(null));
        await model.BeginJourneyAsync();
        Assert.False(model.HasActiveJourney);
        model.SelectedStartVisit = earliest ? model.StartVisits[^1] : model.StartVisits[0];
        Assert.EndsWith("UTC", model.SelectedStartVisit.Arrived, StringComparison.Ordinal);
        Assert.True(model.BeginJourneyCommand.CanExecute(null));
        DateTimeOffset start = model.SelectedStartVisit.Entry.Event.Timestamp!.Value;
        await model.BeginJourneyAsync();
        Assert.True(model.HasActiveJourney);
        Assert.Equal(start, model.VisitedSystems[^1].Visit.Arrived);
        Assert.Equal(earliest ? 5 : 1, model.VisitedSystems.Count);
        Assert.Equal(earliest ? 3 : 1, model.VisitedSystems.Sum(system => system.Visit.Counts.Screenshots));
    }

    [Fact]
    public async Task ChangingCurrentSystemOrCommanderAndCancelingClearsTheSelectedVisit()
    {
        await WriteRepeatedVisitsAsync();
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        await model.StartNewJourneyAsync();
        model.NewJourneyName = "Chosen start";
        await model.FindStartAsync();
        model.SelectedStartVisit = model.StartVisits[0];
        JourneyStartVisitViewModel stale = model.SelectedStartVisit;
        await model.UpdateContextAsync("F123", "Drew", true, "Achenar", 99);
        Assert.Empty(model.StartVisits);
        Assert.Null(model.SelectedStartVisit);
        model.SelectedStartVisit = stale;
        Assert.Null(model.SelectedStartVisit);
        Assert.False(model.BeginJourneyCommand.CanExecute(null));
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        await model.FindStartAsync();
        Assert.Equal(3, model.StartVisits.Count);
        model.CancelNewJourneyCommand.Execute(null);
        Assert.Empty(model.StartVisits);
        Assert.False(model.IsCreating);
        await model.StartNewJourneyAsync();
        await model.FindStartAsync();
        model.SelectedStartVisit = model.StartVisits[0];
        await model.UpdateContextAsync("F999", "Other", true, "Sol", 42);
        Assert.Empty(model.StartVisits);
        Assert.Null(model.SelectedStartVisit);
    }

    [Fact]
    public async Task EditingAPriorSystemSearchClearsItsArrivalsAndStartSelection()
    {
        await WriteRepeatedVisitsAsync();
        JourneyWorkspaceViewModel model = CreateViewModel([
            new StarSystemReference("Sol", 42, new GalacticCoordinate(0, 0, 0)),
            new StarSystemReference("Alpha", 43, new GalacticCoordinate(1, 0, 0)),
        ]);
        await model.UpdateContextAsync("F123", "Drew", true, "Alpha", 43);
        await model.StartNewJourneyAsync();
        model.NewJourneyName = "Earlier expedition";
        model.UsePriorStart = true;
        model.StartSystemQuery = "Sol";
        await model.SearchSystemsAsync();
        await model.FindStartAsync();
        Assert.Equal(3, model.StartVisits.Count);
        model.SelectedStartVisit = model.StartVisits[^1];
        Assert.True(model.BeginJourneyCommand.CanExecute(null));
        model.StartSystemQuery = "Alpha";
        Assert.Empty(model.StartVisits);
        Assert.Null(model.SelectedStartVisit);
        Assert.Null(model.SelectedStartSystem);
        Assert.False(model.BeginJourneyCommand.CanExecute(null));
        await model.SearchSystemsAsync();
        await model.FindStartAsync();
        Assert.Equal(2, model.StartVisits.Count);
        Assert.All(model.StartVisits, visit => Assert.Equal("Alpha", visit.Entry.System.Name));
    }

    [Fact]
    public async Task AChangedStartModeCancelsTheSearchWithoutRestoringItsResults()
    {
        await WriteRepeatedVisitsAsync();
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        await model.StartNewJourneyAsync();
        model.PropertyChanged += (_, args) =>
        {
            if (
                args.PropertyName == nameof(model.StartStatus)
                && model.StartStatus.StartsWith("Finding recorded", StringComparison.Ordinal)
            )
            {
                model.UseCurrentStart = false;
            }
        };
        await model.FindStartAsync();
        Assert.False(model.IsBusy);
        Assert.Empty(model.StartVisits);
        Assert.False(model.UseCurrentStart);
        Assert.Equal("Search for a previously visited system.", model.StartStatus);
    }

    private async Task WriteRepeatedVisitsAsync() =>
        await WriteJournalAsync(
            """
            {"timestamp":"2026-07-01T00:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-07-01T00:00:01Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2026-07-01T00:01:00Z","event":"Location","StarSystem":"Before","SystemAddress":1,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-01T00:02:00Z","event":"Screenshot"}
            {"timestamp":"2026-07-01T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-01T00:06:00Z","event":"Screenshot"}
            {"timestamp":"2026-07-01T00:07:00Z","event":"FSDJump","StarSystem":"Alpha","SystemAddress":43,"StarPos":[1,0,0]}
            {"timestamp":"2026-07-02T00:05:00Z","event":"CarrierJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-02T00:06:00Z","event":"Screenshot"}
            {"timestamp":"2026-07-02T00:07:00Z","event":"FSDJump","StarSystem":"Alpha","SystemAddress":43,"StarPos":[1,0,0]}
            {"timestamp":"2026-07-03T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-03T00:06:00Z","event":"Screenshot"}
            """
        );
}
