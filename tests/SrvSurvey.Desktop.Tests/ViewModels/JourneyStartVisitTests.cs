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
        model.UsePreviousStart = true;
        model.NewJourneyName = "Chosen start";
        Assert.False(model.BeginJourneyCommand.CanExecute(null));
        model.StartSystemQuery = "Sol";
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
    public async Task ExplicitSystemSearchSurvivesTravelButNotCommanderChangesOrCancellation()
    {
        await WriteRepeatedVisitsAsync();
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        await model.StartNewJourneyAsync();
        model.UsePreviousStart = true;
        model.NewJourneyName = "Chosen start";
        model.StartSystemQuery = "Sol";
        await model.FindStartAsync();
        model.SelectedStartVisit = model.StartVisits[0];
        JourneyStartVisitViewModel stale = model.SelectedStartVisit;
        await model.UpdateContextAsync("F123", "Drew", true, "Achenar", 99);
        Assert.Equal(3, model.StartVisits.Count);
        Assert.Same(stale, model.SelectedStartVisit);
        Assert.Equal("Sol", model.StartSystemQuery);
        model.StartSystemQuery = "Achenar";
        Assert.Empty(model.StartVisits);
        model.SelectedStartVisit = stale;
        Assert.Null(model.SelectedStartVisit);
        Assert.False(model.BeginJourneyCommand.CanExecute(null));
        model.StartSystemQuery = "Sol";
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        await model.FindStartAsync();
        Assert.Equal(3, model.StartVisits.Count);
        model.CancelNewJourneyCommand.Execute(null);
        Assert.Empty(model.StartVisits);
        Assert.False(model.IsCreating);
        await model.StartNewJourneyAsync();
        model.UsePreviousStart = true;
        model.StartSystemQuery = "Sol";
        await model.FindStartAsync();
        model.SelectedStartVisit = model.StartVisits[0];
        await model.UpdateContextAsync("F999", "Other", true, "Sol", 42);
        Assert.Empty(model.StartVisits);
        Assert.Null(model.SelectedStartVisit);
    }

    [Fact]
    public async Task SystemNameSearchReturnsVisitsDirectlyAndEditingClearsTheSelection()
    {
        await WriteRepeatedVisitsAsync();
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Alpha", 43);
        await model.StartNewJourneyAsync();
        model.UsePreviousStart = true;
        model.NewJourneyName = "Earlier expedition";
        model.StartSystemQuery = "Sol";
        await model.FindStartAsync();
        Assert.Equal(3, model.StartVisits.Count);
        model.SelectedStartVisit = model.StartVisits[^1];
        Assert.True(model.BeginJourneyCommand.CanExecute(null));
        model.StartSystemQuery = "Alpha";
        Assert.Empty(model.StartVisits);
        Assert.Null(model.SelectedStartVisit);
        Assert.False(model.BeginJourneyCommand.CanExecute(null));
        await model.FindStartAsync();
        Assert.Equal(2, model.StartVisits.Count);
        Assert.All(model.StartVisits, visit => Assert.Equal("Alpha", visit.Entry.System.Name));
    }

    [Fact]
    public async Task AutocompleteSelectionRestrictsMatchesToThatSystemAndHandlesAnUnvisitedSuggestion()
    {
        await WriteRepeatedVisitsAsync();
        await File.AppendAllTextAsync(
            Path.Combine(JournalDirectory, "Journal.2026-07-01T000000.01.log"),
            "\n"
                + """{"timestamp":"2026-07-04T00:05:00Z","event":"FSDJump","StarSystem":"Solati","SystemAddress":99,"StarPos":[2,0,0]}"""
        );
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Alpha", 43);
        await model.StartNewJourneyAsync();
        model.UsePreviousStart = true;
        Assert.Empty(model.StartSystemQuery);
        Assert.False(model.FindStartCommand.CanExecute(null));
        model.NewJourneyName = "Alpha";
        model.NewJourneyDescription = "Alpha";
        model.StartSystemQuery = " sOL ";
        await model.FindStartAsync();
        Assert.Equal(4, model.StartVisits.Count);
        await model.SelectStartingSystemAsync("Sol", 42);
        Assert.Equal(3, model.StartVisits.Count);
        Assert.All(model.StartVisits, visit => Assert.Equal("Sol", visit.SystemName));
        await model.SelectStartingSystemAsync("Unvisited", 777);
        Assert.Empty(model.StartVisits);
        Assert.Null(model.SelectedStartVisit);
        Assert.Contains("No previous visits match Unvisited", model.StartStatus, StringComparison.Ordinal);
        Assert.False(model.BeginJourneyCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentSystemStartsAutomaticallyFromTheLatestLocationWithoutSearching(bool locationOnly)
    {
        if (locationOnly)
        {
            await WriteJournalAsync(
                """
                {"timestamp":"2026-07-03T00:00:00Z","event":"Fileheader","Odyssey":true}
                {"timestamp":"2026-07-03T00:00:01Z","event":"Commander","FID":"F123"}
                {"timestamp":"2026-07-03T00:05:00Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0],"Docked":true}
                {"timestamp":"2026-07-03T00:06:00Z","event":"Screenshot"}
                """
            );
        }
        else
        {
            await WriteRepeatedVisitsAsync();
        }
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        await model.StartNewJourneyAsync();
        model.NewJourneyName = "Current expedition";
        model.VisitsOnOrAfter = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        Assert.True(model.UseCurrentStart);
        Assert.False(model.FindStartCommand.CanExecute(null));
        Assert.True(model.BeginJourneyCommand.CanExecute(null));
        Assert.Empty(model.StartVisits);
        await model.BeginJourneyAsync();
        Assert.True(model.HasActiveJourney);
        Assert.Equal("Sol", model.SelectedSystemName);
        Assert.Equal(3, Assert.Single(model.VisitedSystems).Visit.Arrived.Day);
        Assert.Equal(1, model.VisitedSystems[0].Visit.Counts.Screenshots);
    }

    [Fact]
    public async Task DateCutoffIsInclusiveAndChangingItClearsTheSelectedVisit()
    {
        await WriteRepeatedVisitsAsync();
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        await model.StartNewJourneyAsync();
        model.UsePreviousStart = true;
        model.NewJourneyName = "Earlier expedition";
        model.StartSystemQuery = "Sol";
        model.VisitsOnOrAfter = new DateTime(2026, 7, 2, 15, 30, 0, DateTimeKind.Unspecified);
        Assert.Equal(new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Unspecified), model.VisitsOnOrAfter);
        await model.FindStartAsync();
        Assert.Equal(2, model.StartVisits.Count);
        model.SelectedStartVisit = model.StartVisits[^1];
        Assert.Equal(2, model.SelectedStartVisit.Entry.Event.Timestamp!.Value.Day);
        model.VisitsOnOrAfter = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Unspecified);
        Assert.Empty(model.StartVisits);
        Assert.False(model.BeginJourneyCommand.CanExecute(null));
        await model.FindStartAsync();
        Assert.Equal(3, Assert.Single(model.StartVisits).Entry.Event.Timestamp!.Value.Day);
        model.VisitsOnOrAfter = null;
        await model.FindStartAsync();
        Assert.Equal(3, model.StartVisits.Count);
    }

    [Fact]
    public async Task CurrentSystemChangingDuringPreparationRequiresANewBegin()
    {
        await WriteRepeatedVisitsAsync();
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        await model.StartNewJourneyAsync();
        model.NewJourneyName = "Current expedition";
        Task<bool>? changed = null;
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.IsBusy) && model.IsBusy && changed is null)
            {
                changed = model.UpdateContextAsync("F123", "Drew", true, "Alpha", 43);
            }
        };
        await model.BeginJourneyAsync();
        Assert.NotNull(changed);
        await changed;
        Assert.False(model.HasActiveJourney);
        Assert.Contains("current system changed", model.StartStatus, StringComparison.Ordinal);
        Assert.Empty(model.Journeys);
    }

    [Fact]
    public async Task CurrentSystemRequiresAKnownLocationAndExplainsMissingJournalHistory()
    {
        await WriteRepeatedVisitsAsync();
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Unvisited", 777);
        await model.StartNewJourneyAsync();
        model.NewJourneyName = "Current expedition";
        await model.BeginJourneyAsync();
        Assert.False(model.HasActiveJourney);
        Assert.Contains("current location could not be found", model.StartStatus, StringComparison.Ordinal);
        await model.UpdateContextAsync("F123", "Drew", true, null, null);
        Assert.False(model.BeginJourneyCommand.CanExecute(null));
        await model.StartNewJourneyAsync();
        Assert.True(model.UsePreviousStart);
    }

    [Fact]
    public async Task AChangedSystemNameCancelsTheSearchWithoutRestoringItsResults()
    {
        await WriteRepeatedVisitsAsync();
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        await model.StartNewJourneyAsync();
        model.UsePreviousStart = true;
        model.PropertyChanged += (_, args) =>
        {
            if (
                args.PropertyName == nameof(model.StartStatus)
                && model.StartStatus.StartsWith("Finding your visits", StringComparison.Ordinal)
            )
            {
                model.StartSystemQuery = "Alpha";
            }
        };
        model.StartSystemQuery = "Sol";
        await model.FindStartAsync();
        Assert.False(model.IsBusy);
        Assert.Empty(model.StartVisits);
        Assert.Equal("Alpha", model.StartSystemQuery);
        Assert.Equal("Search for a system to see your previous visits.", model.StartStatus);
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
