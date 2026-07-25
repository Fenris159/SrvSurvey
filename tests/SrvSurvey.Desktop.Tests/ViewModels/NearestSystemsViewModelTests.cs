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
                    NearestSystemSource.Canonn),
            ],
            null),
        };
        var viewModel = new NearestSystemsViewModel(
            client,
            new StubSystemResolver([]));
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
            });
        viewModel.UpdateContext(
            "Reference",
            new GalacticCoordinate(10, 20, 30),
            "Cmdr Test");
        viewModel.BiologicalSignal = "Stratum";

        await viewModel.SearchAsync();
        await viewModel.CopySystemAsync();
        await viewModel.OpenCanonnAsync();

        Assert.Equal("Stratum", client.CanonnSignal);
        Assert.Equal("Cmdr Test", client.CommanderName);
        Assert.Equal(new GalacticCoordinate(10, 20, 30), client.Reference);
        Assert.Equal("Test A", viewModel.SelectedResult?.SystemName);
        Assert.Equal("Test A", copied);
        Assert.Equal(
            "https://signals.canonn.tech/?system=Test%20A",
            opened?.AbsoluteUri);
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
                    NearestSystemSource.Spansh),
            ],
            "search-1"),
        };
        var viewModel = new NearestSystemsViewModel(
            client,
            new StubSystemResolver([]));
        var opened = new List<Uri>();
        viewModel.SetPlatformServices(
            null,
            uri =>
            {
                opened.Add(uri);
                return Task.FromResult(true);
            });
        viewModel.UpdateContext(
            "Reference",
            new GalacticCoordinate(1, 2, 3),
            "Cmdr Test");
        viewModel.SelectedMode = viewModel.Modes[1];
        viewModel.Genus = "Tussock";
        viewModel.Species = "Tussock Capillum";
        viewModel.VariantColors = "emerald, teal; Emerald";

        await viewModel.SearchAsync();
        await viewModel.OpenSpanshAsync();
        await viewModel.OpenOriginalSpanshSearchAsync();

        Assert.Equal(["emerald", "teal"], client.VariantColors);
        Assert.True(viewModel.HasSpanshSearchReference);
        Assert.Equal(
            "https://spansh.co.uk/system/42",
            opened[0].AbsoluteUri);
        Assert.Equal(
            "https://spansh.co.uk/bodies/search/search-1/1",
            opened[1].AbsoluteUri);
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
                    "No bio signals in system",
                    new GalacticCoordinate(1, 2, 3),
                    null,
                    NearestSystemSource.Canonn),
            ],
            null),
        };
        var resolver = new StubSystemResolver(
        [
            new StarSystemReference(
                "Test A",
                1234,
                new GalacticCoordinate(1, 2, 3)),
        ]);
        var viewModel = new NearestSystemsViewModel(client, resolver);
        Uri? opened = null;
        viewModel.SetPlatformServices(
            null,
            uri =>
            {
                opened = uri;
                return Task.FromResult(true);
            });
        viewModel.UpdateContext(
            "Reference",
            new GalacticCoordinate(1, 2, 3),
            null);
        viewModel.BiologicalSignal = "Bacterium";
        await viewModel.SearchAsync();

        await viewModel.OpenSpanshAsync();

        Assert.Equal("Test A", resolver.Query);
        Assert.Equal(
            "https://spansh.co.uk/system/1234",
            opened?.AbsoluteUri);
    }

    [Fact]
    public async Task CodexRequestsSelectModePopulateInputsAndSearch()
    {
        var client = new StubNearestSystemsClient();
        var viewModel = new NearestSystemsViewModel(
            client,
            new StubSystemResolver([]));
        viewModel.UpdateContext(
            "Reference",
            new GalacticCoordinate(10, 20, 30),
            "Cmdr Test");

        await viewModel.SearchCodexSignalAsync("  Stratum  ");

        Assert.True(viewModel.IsCanonnMode);
        Assert.Equal("Stratum", viewModel.BiologicalSignal);
        Assert.Equal("Stratum", client.CanonnSignal);

        await viewModel.SearchCodexVariantsAsync(
            "  Tussock  ",
            "  Tussock Capillum  ",
            [" emerald ", "", "teal"]);

        Assert.True(viewModel.IsVariantMode);
        Assert.Equal("Tussock", viewModel.Genus);
        Assert.Equal("Tussock Capillum", viewModel.Species);
        Assert.Equal(["emerald", "teal"], client.VariantColors);
        Assert.Equal("Tussock", client.Genus);
        Assert.Equal("Tussock Capillum", client.Species);
    }

    [Fact]
    public async Task SearchRequiresCurrentCoordinates()
    {
        var viewModel = new NearestSystemsViewModel(
            new StubNearestSystemsClient(),
            new StubSystemResolver([]))
        {
            BiologicalSignal = "Stratum",
        };

        await viewModel.SearchAsync();

        Assert.Contains("coordinates", viewModel.StatusMessage);
        Assert.False(viewModel.SearchCommand.CanExecute(null));
    }

    private sealed class StubNearestSystemsClient : INearestSystemsClient
    {
        public NearestSystemsSearchResult CanonnResult { get; init; } =
            new([], null);

        public NearestSystemsSearchResult VariantResult { get; init; } =
            new([], null);

        public GalacticCoordinate Reference { get; private set; }

        public string? CanonnSignal { get; private set; }

        public string? CommanderName { get; private set; }

        public string? Genus { get; private set; }

        public string? Species { get; private set; }

        public IReadOnlyList<string> VariantColors { get; private set; } = [];

        public Task<NearestSystemsSearchResult> SearchCanonnAsync(
            GalacticCoordinate reference,
            string biologicalSignal,
            string commanderName,
            int limit = 5,
            CancellationToken cancellationToken = default)
        {
            Reference = reference;
            CanonnSignal = biologicalSignal;
            CommanderName = commanderName;
            return Task.FromResult(CanonnResult);
        }

        public Task<NearestSystemsSearchResult> SearchMissingVariantsAsync(
            GalacticCoordinate reference,
            string genus,
            string species,
            IReadOnlyList<string> variantColors,
            CancellationToken cancellationToken = default)
        {
            Reference = reference;
            Genus = genus;
            Species = species;
            VariantColors = variantColors;
            return Task.FromResult(VariantResult);
        }
    }

    private sealed class StubSystemResolver(
        IReadOnlyList<StarSystemReference> results) : IStarSystemResolver
    {
        public string? Query { get; private set; }

        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            Query = query;
            return Task.FromResult(results);
        }
    }
}
