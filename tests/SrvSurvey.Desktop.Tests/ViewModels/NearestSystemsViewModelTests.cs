using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class NearestSystemsViewModelTests
{
    [Fact]
    public async Task CanonnSearchUsesCurrentContextAndSupportsRowActions()
    {
        var client = new StubNearestSystemsClient
        {
            CanonnResult = new NearestSystemsSearchResult(
                [
                    new NearestSystemSearchRow(
                        "Test A",
                        2.5,
                        "Body A1: 2 signals",
                        new GalacticCoordinate(1, 2, 3),
                        null,
                        NearestSystemSource.Canonn
                    ),
                ],
                null
            ),
        };
        var viewModel = new NearestSystemsViewModel(client, new StubSystemResolver([]));
        string? copied = null;
        Uri? opened = null;
        viewModel.SetPlatformServices(
            text =>
            {
                copied = text;
                return Task.CompletedTask;
            },
            uri =>
            {
                opened = uri;
                return Task.FromResult(true);
            }
        );
        viewModel.UpdateContext("Reference", new GalacticCoordinate(10, 20, 30), "Cmdr Test", 10477373803);
        viewModel.CodexName = "Stratum";

        await viewModel.SearchAsync();
        await viewModel.CopySystemAsync();
        await viewModel.OpenCanonnAsync();

        Assert.Equal("Stratum", client.CodexName);
        Assert.Equal("Cmdr Test", client.CommanderName);
        Assert.Equal(new GalacticCoordinate(10, 20, 30), client.Reference);
        Assert.Equal("id64 10477373803", viewModel.ReferenceSystemAddressText);
        Assert.Equal("Test A", viewModel.SelectedResult?.SystemName);
        Assert.Equal("Test A", copied);
        Assert.Equal("https://signals.canonn.tech/?system=Test%20A", opened?.AbsoluteUri);
    }

    [Fact]
    public async Task VariantSearchParsesColorsAndOpensSpanshLinks()
    {
        var client = new StubNearestSystemsClient
        {
            VariantResult = new NearestSystemsSearchResult(
                [
                    new NearestSystemSearchRow(
                        "Test B",
                        5.5,
                        "Emerald - body: 3",
                        new GalacticCoordinate(4, 5, 6),
                        42,
                        NearestSystemSource.Spansh
                    ),
                ],
                "search-1"
            ),
        };
        var viewModel = new NearestSystemsViewModel(client, new StubSystemResolver([]));
        var opened = new List<Uri>();
        viewModel.SetPlatformServices(
            null,
            uri =>
            {
                opened.Add(uri);
                return Task.FromResult(true);
            }
        );
        viewModel.UpdateContext("Reference", new GalacticCoordinate(1, 2, 3), "Cmdr Test");
        viewModel.SelectedMode = viewModel.Modes[1];
        viewModel.LandmarkType = "Tussock";
        viewModel.LandmarkSubtypes = "Tussock Capillum";
        viewModel.LandmarkVariants = "emerald, teal; Emerald";

        await viewModel.SearchAsync();
        await viewModel.OpenSpanshAsync();
        await viewModel.OpenOriginalSpanshSearchAsync();

        Assert.Equal(["emerald", "teal"], client.LandmarkVariants);
        Assert.True(viewModel.HasSpanshSearchReference);
        Assert.Equal("https://spansh.co.uk/system/42", opened[0].AbsoluteUri);
        Assert.Equal("https://spansh.co.uk/bodies/search/search-1/1", opened[1].AbsoluteUri);
    }

    [Fact]
    public async Task CanonnResultResolvesAddressBeforeOpeningSpansh()
    {
        var client = new StubNearestSystemsClient
        {
            CanonnResult = new NearestSystemsSearchResult(
                [
                    new NearestSystemSearchRow(
                        "Test A",
                        2.5,
                        "Reported Bacterium location",
                        new GalacticCoordinate(1, 2, 3),
                        null,
                        NearestSystemSource.Canonn
                    ),
                ],
                null
            ),
        };
        var resolver = new StubSystemResolver([
            new StarSystemReference("Test A", 1234, new GalacticCoordinate(1, 2, 3)),
        ]);
        var viewModel = new NearestSystemsViewModel(client, resolver);
        Uri? opened = null;
        viewModel.SetPlatformServices(
            null,
            uri =>
            {
                opened = uri;
                return Task.FromResult(true);
            }
        );
        viewModel.UpdateContext("Reference", new GalacticCoordinate(1, 2, 3), null);
        viewModel.CodexName = "Bacterium";
        await viewModel.SearchAsync();

        await viewModel.OpenSpanshAsync();

        Assert.Equal("Test A", resolver.Query);
        Assert.Equal("https://spansh.co.uk/system/1234", opened?.AbsoluteUri);
    }

    [Fact]
    public async Task CodexRequestsSelectModePopulateInputsAndSearch()
    {
        var client = new StubNearestSystemsClient();
        var viewModel = new NearestSystemsViewModel(client, new StubSystemResolver([]));
        viewModel.UpdateContext("Reference", new GalacticCoordinate(10, 20, 30), "Cmdr Test");

        var catalog = new CodexSearchCatalog();
        await viewModel.SearchCodexAsync(catalog.Resolve("Stratum"));

        Assert.True(viewModel.IsCodexMode);
        Assert.Equal("Stratum", viewModel.CodexName);
        Assert.Equal("Stratum", client.CodexName);

        var missing = new CodexSearchQuery(
            "Tussock Capillum missing colors",
            [new("Tussock Capillum - Emerald", "Biology"), new("Tussock Capillum - Teal", "Biology")],
            true
        );
        await viewModel.SearchCodexAsync(missing);
        Assert.True(viewModel.IsCodexMode);
        Assert.Same(missing, client.Query);
        Assert.Equal(missing.Label, viewModel.CodexName);
    }

    [Fact]
    public async Task SearchRequiresCurrentCoordinates()
    {
        var viewModel = new NearestSystemsViewModel(new StubNearestSystemsClient(), new StubSystemResolver([]))
        {
            CodexName = "Stratum",
        };

        await viewModel.SearchAsync();

        Assert.Contains("coordinates", viewModel.StatusMessage);
        Assert.False(viewModel.SearchCommand.CanExecute(null));
    }

    [Fact]
    public void LandmarkSubtypeAloneIsSearchableWithoutATypeOrColor()
    {
        using var model = new NearestSystemsViewModel(new StubNearestSystemsClient(), new StubSystemResolver([]));
        model.UpdateContext("Reference", new GalacticCoordinate(0, 0, 0), "Cmdr");
        model.SelectedMode = model.Modes[1];
        model.LandmarkSubtypes = "Albens Bell Mollusc";

        Assert.True(model.SearchCommand.CanExecute(null));
        Assert.Contains("Mollusc", model.LandmarkTypes);
        Assert.Contains("Albens Bell Mollusc", model.LandmarkSubtypeOptions);
        Assert.Contains("Emerald", model.LandmarkVariantOptions);
    }

    [Theory]
    [InlineData("commander")]
    [InlineData("search")]
    [InlineData("dispose")]
    public async Task ObsoleteSearchCannotReplaceResultsAfterContextOrRequestChanges(string change)
    {
        var gate = new TaskCompletionSource<NearestSystemsSearchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var client = new StubNearestSystemsClient
        {
            Search = (query, _) =>
                query.Label == "First" ? gate.Task : Task.FromResult(new NearestSystemsSearchResult([], null)),
        };
        using var model = new NearestSystemsViewModel(client, new StubSystemResolver([]));
        model.UpdateContext("Reference", new GalacticCoordinate(0, 0, 0), "First Cmdr");
        model.CodexName = "First";
        Task first = model.SearchAsync();
        Assert.True(model.IsSearching);
        if (change == "commander")
        {
            model.UpdateContext("Reference", new GalacticCoordinate(0, 0, 0), "Second Cmdr");
        }
        else if (change == "search")
        {
            model.CodexName = "Second";
            await model.SearchAsync();
        }
        else
        {
            model.Dispose();
        }
        gate.SetResult(new([new("Obsolete", 1, "Old", new(0, 0, 0), 1, NearestSystemSource.Spansh)], null));
        await first;
        Assert.Empty(model.Results);
        Assert.False(model.IsSearching);
    }

    [Theory]
    [InlineData("http", "failed")]
    [InlineData("timeout", "timed out")]
    [InlineData("empty", "No published locations")]
    public async Task SearchDistinguishesFailuresTimeoutsAndNoPublishedMatches(string outcome, string expected)
    {
        var client = new StubNearestSystemsClient
        {
            Search = (_, _) =>
                outcome switch
                {
                    "http" => Task.FromException<NearestSystemsSearchResult>(new HttpRequestException("Unavailable")),
                    "timeout" => Task.FromException<NearestSystemsSearchResult>(new TaskCanceledException()),
                    _ => Task.FromResult(new NearestSystemsSearchResult([], null) { CoverageNote = "Limited index" }),
                },
        };
        using var model = new NearestSystemsViewModel(client, new StubSystemResolver([]));
        model.UpdateContext("Reference", new(0, 0, 0), "Cmdr");
        model.CodexName = "Bell Mollusc";
        await model.SearchAsync();
        Assert.Contains(expected, model.StatusMessage);
        Assert.False(model.IsSearching);
        if (outcome == "empty")
        {
            Assert.Contains("no distance cutoff", model.ResultOrigin);
            Assert.Equal("Limited index", model.CoverageNote);
        }
    }

    [Fact]
    public async Task OnlyColorIsInsufficientButTypeOrSubtypeDoesNotNeedColors()
    {
        using var model = new NearestSystemsViewModel(new StubNearestSystemsClient(), new StubSystemResolver([]));
        model.UpdateContext("Reference", new(0, 0, 0), "Cmdr");
        await model.SearchAsync();
        Assert.Contains("Choose a Codex entry", model.StatusMessage);
        model.SelectedMode = model.Modes[1];
        model.LandmarkVariants = "Emerald";
        await model.SearchAsync();
        Assert.Contains("type or subtype", model.StatusMessage);
        Assert.False(model.SearchCommand.CanExecute(null));
        model.LandmarkType = "Mollusc";
        Assert.True(model.SearchCommand.CanExecute(null));
        await model.SearchAsync();
        model.Dispose();
        Assert.False(model.SearchCommand.CanExecute(null));
        await model.SearchAsync();
    }

    [Fact]
    public async Task MissingEntrySelectionRemainsExplicitUntilEdited()
    {
        using var model = new NearestSystemsViewModel(new StubNearestSystemsClient(), new StubSystemResolver([]));
        model.UpdateContext("Reference", new(0, 0, 0), "Cmdr");
        await model.SearchCodexAsync(
            new("Bell Mollusc", [new("Albens Bell Mollusc", "Biology"), new("Blatteum Bell Mollusc", "Biology")], true)
        );
        Assert.Equal("Selected entries: Albens, Blatteum", model.CriteriaSummary);
        model.CodexName = "Stratum";
        Assert.Empty(model.CriteriaSummary);
        await model.SearchCodexAsync(
            new("Tussock Capillum", [new("Tussock Capillum - Emerald", "Biology"), new("Other", null)], true)
        );
        Assert.Equal("Selected entries: Emerald, Other", model.CriteriaSummary);
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("type")]
    [InlineData("subtype")]
    [InlineData("variant")]
    public async Task EditingCriteriaCancelsPendingSearchAndIgnoresLateResults(string field)
    {
        var gate = new TaskCompletionSource<NearestSystemsSearchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        CancellationToken captured = default;
        var client = new StubNearestSystemsClient
        {
            Search = (_, token) =>
            {
                captured = token;
                return gate.Task;
            },
        };
        using var model = new NearestSystemsViewModel(client, new StubSystemResolver([]));
        model.UpdateContext("Reference", new(0, 0, 0), "Cmdr");
        model.CodexName = "Bell Mollusc";
        Task search = model.SearchAsync();
        if (field == "mode")
        {
            model.SelectedMode = model.Modes[1];
        }
        else if (field == "type")
        {
            model.LandmarkType = "Mollusc";
        }
        else if (field == "subtype")
        {
            model.LandmarkSubtypes = "Albens Bell Mollusc";
        }
        else
        {
            model.LandmarkVariants = "Emerald";
        }
        Assert.True(captured.IsCancellationRequested);
        gate.SetResult(new([new("Old", 1, "Old", new(0, 0, 0), null, NearestSystemSource.Canonn)], null));
        await search;
        Assert.Empty(model.Results);
        Assert.False(model.IsSearching);
    }

    private sealed class StubNearestSystemsClient : INearestSystemsClient
    {
        public NearestSystemsSearchResult CanonnResult { get; init; } = new([], null);

        public NearestSystemsSearchResult VariantResult { get; init; } = new([], null);

        public GalacticCoordinate Reference { get; private set; }

        public string? CodexName { get; private set; }

        public string? CommanderName { get; private set; }

        public string? LandmarkType { get; private set; }

        public string? LandmarkSubtypes { get; private set; }

        public IReadOnlyList<string> LandmarkVariants { get; private set; } = [];

        public Task<NearestSystemsSearchResult> SearchCanonnAsync(
            GalacticCoordinate reference,
            string codexName,
            string commanderName,
            int limit = 20,
            CancellationToken cancellationToken = default
        )
        {
            Reference = reference;
            CodexName = codexName;
            CommanderName = commanderName;
            return Task.FromResult(CanonnResult);
        }

        public CodexSearchQuery? Query { get; private set; }
        public Func<CodexSearchQuery, CancellationToken, Task<NearestSystemsSearchResult>>? Search { get; init; }

        public Task<NearestSystemsSearchResult> SearchCodexAsync(
            GalacticCoordinate reference,
            CodexSearchQuery query,
            string commanderName,
            CancellationToken cancellationToken = default
        )
        {
            Reference = reference;
            CodexName = query.Label;
            CommanderName = commanderName;
            Query = query;
            return Search?.Invoke(query, cancellationToken) ?? Task.FromResult(CanonnResult);
        }

        public Task<NearestSystemsSearchResult> SearchLandmarksAsync(
            GalacticCoordinate reference,
            CodexLandmarkFilter filter,
            CancellationToken cancellationToken = default
        )
        {
            Reference = reference;
            LandmarkType = filter.Type;
            LandmarkSubtypes = string.Join(", ", filter.Subtypes);
            LandmarkVariants = filter.Variants;
            return Task.FromResult(VariantResult);
        }
    }

    private sealed class StubSystemResolver(IReadOnlyList<StarSystemReference> results) : IStarSystemResolver
    {
        public string? Query { get; private set; }

        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        )
        {
            Query = query;
            return Task.FromResult(results);
        }
    }
}
