using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class NearestSystemsViewModelTests
{
    [Fact]
    public async Task OneMissingColorRemainsVisibleWhenTheSpeciesLabelIsShown()
    {
        var client = new StubNearestSystemsClient();
        using var model = new NearestSystemsViewModel(client, new StubSystemResolver([]));
        model.UpdateContext("Sol", new(0, 0, 0), "Cmdr");
        var query = new CodexSearchQuery("Stratum Tectonicas", [new("Stratum Tectonicas - Lime", "Biology")], true);
        await model.SearchCodexAsync(query);
        Assert.Equal("Selected entries: Lime", model.CriteriaSummary);
        await model.SearchAsync();
        Assert.Same(query, client.Query);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextChangesCancelTheSearchAndReplaceTheSearchingStatus(bool commanderChanged)
    {
        var gate = new TaskCompletionSource<NearestSystemsSearchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var client = new StubNearestSystemsClient { Search = (_, _) => gate.Task };
        using var model = new NearestSystemsViewModel(client, new StubSystemResolver([]));
        model.UpdateContext("Sol", new(0, 0, 0), "Cmdr");
        model.CodexName = "Bell Mollusc";
        Task pending = model.SearchAsync();
        model.UpdateContext(commanderChanged ? "Sol" : "Other", new(1, 2, 3), commanderChanged ? "Other Cmdr" : "Cmdr");
        string status = model.StatusMessage;
        gate.SetResult(new([], null));
        await pending;
        Assert.False(model.IsSearching);
        Assert.Contains("context changed", status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(status, model.StatusMessage);
    }

    [Fact]
    public async Task CanonnOnlySelectionCannotOpenAnUnrelatedSavedSpanshSearch()
    {
        var client = new StubNearestSystemsClient
        {
            CanonnResult = new(
                [
                    new("Mapped", 1, "Mapped feature", new(0, 0, 0), 42, NearestSystemSource.Spansh)
                    {
                        SpanshSearchReference = "matched-search",
                    },
                    new("Fallback", 2, "Other feature", new(1, 2, 3), null, NearestSystemSource.Canonn),
                ],
                "matched-search"
            ),
        };
        using var model = new NearestSystemsViewModel(client, new StubSystemResolver([]));
        var links = new List<Uri>();
        model.SetPlatformServices(
            null,
            uri =>
            {
                links.Add(uri);
                return Task.FromResult(true);
            }
        );
        model.UpdateContext("Sol", new(0, 0, 0), "Cmdr");
        model.CodexName = "Cloud";
        await model.SearchAsync();
        Assert.True(model.HasSpanshSearchReference);
        model.SelectedResult = model.Results[1];
        Assert.False(model.HasSpanshSearchReference);
        Assert.False(model.OpenSpanshSearchCommand.CanExecute(null));
        await model.OpenOriginalSpanshSearchAsync();
        Assert.Empty(links);
        model.SelectedResult = null;
        Assert.True(model.HasSpanshSearchReference);
        await model.OpenOriginalSpanshSearchAsync();
        Assert.Contains("matched-search", Assert.Single(links).AbsoluteUri);
    }

    [Fact]
    public async Task ChangingModesClearsThePreviousCoverageAndReferenceSummary()
    {
        var client = new StubNearestSystemsClient
        {
            CanonnResult = new([], null) { CoverageNote = "No supported location index for this entry" },
        };
        using var model = new NearestSystemsViewModel(client, new StubSystemResolver([]));
        model.UpdateContext("Sol", new(0, 0, 0), "Cmdr");
        model.CodexName = "Thargoid Interceptor Cyclops";
        await model.SearchAsync();
        Assert.NotEmpty(model.CoverageNote);
        Assert.NotEmpty(model.ResultOrigin);
        model.SelectedMode = model.Modes[1];
        Assert.Empty(model.CoverageNote);
        Assert.Empty(model.ResultOrigin);
        Assert.Empty(model.Results);
    }

    [Fact]
    public async Task DirectLandmarkStreamFailuresAreReportedWithoutEscapingTheCommand()
    {
        var client = new StubNearestSystemsClient
        {
            LandmarkSearch = _ =>
                Task.FromException<NearestSystemsSearchResult>(
                    new HttpIOException(HttpRequestError.ResponseEnded, "Truncated response")
                ),
        };
        using var model = new NearestSystemsViewModel(client, new StubSystemResolver([]));
        model.UpdateContext("Sol", new(0, 0, 0), "Cmdr");
        model.SelectedMode = model.Modes[1];
        model.LandmarkSubtypes = "Albens Bell Mollusc";
        await model.SearchAsync();
        Assert.Contains("failed", model.StatusMessage);
        Assert.Contains("Truncated response", model.StatusMessage);
        Assert.False(model.IsSearching);
    }
}
