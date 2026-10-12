using System.Text.Json;
using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ExplorationPoiViewModelTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-poi-vm-" + Guid.NewGuid().ToString("N")
    );

    /// <summary>Provides poi for the isolated test fixture.</summary>
    private static ExplorationPoi Poi(long id, string system = "Sol", string category = "Stellar Features") =>
        new(
            id,
            "GEC",
            "Attraction " + id,
            system,
            system == "Sol" ? 1 : 2,
            new GalacticCoordinate(id, 0, 0),
            category,
            "Historical",
            "Synopsis",
            "Description",
            id,
            id / 2d,
            4,
            id == 2
        );

    /// <summary>Verifies new poi opens the provider submission form and reports launcher failure.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task NewPoiOpensTheProviderSubmissionFormAndReportsLauncherFailure(bool success, bool throws)
    {
        using ExplorationPoiViewModel vm = Create(new FakeClient([]));
        Assert.False(vm.NewPoiCommand.CanExecute(null));
        Uri? opened = null;
        vm.UseActions(
            _ => Task.CompletedTask,
            uri =>
            {
                opened = uri;
                if (throws)
                {
                    throw new InvalidOperationException("launcher unavailable");
                }
                return Task.FromResult(success);
            }
        );
        Assert.True(vm.NewPoiCommand.CanExecute(null));
        vm.NewPoiCommand.Execute(null);
        await Task.Yield();
        Assert.Equal("https://edastro.com/gec/new", opened?.AbsoluteUri);
        if (!success)
        {
            Assert.Contains("Unable to open", vm.DataStatus, StringComparison.Ordinal);
        }
    }

    /// <summary>Verifies current system panel appears passively clears during jump and hides when unmatched.</summary>
    [Fact]
    public async Task CurrentSystemPanelAppearsPassivelyClearsDuringJumpAndHidesWhenUnmatched()
    {
        var client = new FakeClient([Poi(1), Poi(2), Poi(3), Poi(4, category: "Legacy")]);
        using ExplorationPoiViewModel vm = Create(client);
        Assert.False(vm.ShouldShow);
        vm.SetExternalEnabled(true);
        Update(vm, "Sol", 1);
        await vm.PendingLoad;
        Assert.True(vm.ShouldShow);
        Assert.Equal(3, vm.CurrentPois.Count);
        Assert.Equal(2, vm.OverlayPois.Count);
        Assert.True(vm.HasMorePois);
        Assert.Contains("1 additional", vm.MorePoisText);
        Update(vm, "Sol", 1, GuiFocus.GalaxyMap);
        Assert.False(vm.ShouldShow);
        Update(vm, "Sol", 1, GuiFocus.Fss);
        Assert.False(vm.ShouldShow);
        Update(vm, "Sol", 1, events: [Event("StartJump", "\"JumpType\":\"Hyperspace\"")]);
        Assert.False(vm.ShouldShow);
        Update(vm, "Other", 99, events: [Event("FSDJump")]);
        Assert.Empty(vm.CurrentPois);
        Assert.False(vm.ShouldShow);
        Update(vm, "Sol", 1);
        Assert.True(vm.ShouldShow);
        Update(vm, "Other", 99);
        Update(vm, "Sol", 1);
        Assert.True(vm.ShouldShow);
        Assert.Equal(1, client.Calls);
        vm.AutoShow = false;
        Assert.False(vm.ShouldShow);
        using ExplorationPoiViewModel restored = Create(client);
        Assert.False(restored.AutoShow);
    }

    /// <summary>Verifies nearby filters and actions save distinct pois without replacing notes.</summary>
    [Fact]
    public async Task NearbyFiltersAndActionsSaveDistinctPoisWithoutReplacingNotes()
    {
        var client = new FakeClient([Poi(1), Poi(2), Poi(3, "Alpha"), Poi(4, category: "Legacy")]);
        var bookmarks = new BookmarkCatalog(directory);
        using ExplorationPoiViewModel vm = Create(client, bookmarks);
        string? copied = null;
        Uri? launched = null;
        vm.UseActions(
            text =>
            {
                copied = text;
                return Task.CompletedTask;
            },
            uri =>
            {
                launched = uri;
                return Task.FromResult(true);
            }
        );
        vm.SetExternalEnabled(true);
        Update(vm, "Sol", 1);
        await vm.PendingLoad;
        Assert.Equal(2, vm.Items.Count);
        vm.Selected = vm.Items[0];
        vm.CopySystemCommand.Execute(null);
        vm.OpenSourceCommand.Execute(null);
        Assert.Equal("Sol", copied);
        Assert.Equal(vm.Selected.Poi.SourceUri, launched);
        vm.SavePoiCommand.Execute(null);
        GalacticBookmark first = Assert.Single(bookmarks.Items);
        bookmarks.Save(first with { Notes = "Personal notes" });
        vm.SavePoiCommand.Execute(null);
        Assert.Equal("Personal notes", Assert.Single(bookmarks.Items).Notes);
        vm.Selected = vm.Items[1];
        vm.SavePoiCommand.Execute(null);
        Assert.Equal(2, bookmarks.Items.Count);
        var imported = new BookmarkCatalog(Path.Combine(directory, "import"));
        imported.Import(bookmarks.Export());
        imported.Import(bookmarks.Export());
        Assert.Equal(2, imported.Items.Count);
        Assert.Equal(2, imported.Items.Select(b => b.ExternalPoiIdentity).Distinct().Count());
        vm.CurrentOnly = false;
        Assert.Equal(3, vm.Items.Count);
        vm.MaximumDistanceLy = 2;
        Assert.Equal(2, vm.Items.Count);
        vm.MinimumRating = 2;
        Assert.Single(vm.Items);
        vm.RareOnly = true;
        Assert.True(Assert.Single(vm.Items).Poi.IsRare);
        vm.AttractionQuery = "missing";
        Assert.Empty(vm.Items);
        Assert.Null(vm.Selected);
        vm.AttractionQuery = "Attraction";
        vm.Category = "Historical";
        Assert.Single(vm.Items);
        vm.Category = "missing";
        Assert.Empty(vm.Items);
        vm.Category = "All";
        vm.RareOnly = false;
        vm.MinimumRating = 0;
        vm.MaximumDistanceLy = 100;
        vm.IncludeLegacy = true;
        Assert.Equal(4, vm.Items.Count);
        Assert.Contains("Legacy", vm.Categories);
        Assert.Contains("attractions", vm.ResultCount);
        ExplorationPoiItemViewModel row = vm.Items[0];
        Assert.Equal("1.0 ly", row.DistanceText);
        Assert.Contains("/ 10", row.RatingText);
        Assert.Contains("votes", row.VotesText);
        Assert.Equal("Description", row.Description);
        Assert.Equal("Synopsis", row.Summary);
        vm.MinimumRating = double.NaN;
        vm.MaximumDistanceLy = -1;
        Assert.Equal(100, vm.MaximumDistanceLy);
        Assert.Equal(0, vm.MinimumRating);
        var unknown = new ExplorationPoiItemViewModel(
            Poi(9) with
            {
                Summary = "",
                Rating = null,
                AverageStars = null,
            },
            null
        );
        Assert.Equal("—", unknown.DistanceText);
        Assert.Equal("Unrated", unknown.RatingText);
        Assert.Empty(unknown.VotesText);
        Assert.Equal("Description", unknown.Summary);
    }

    /// <summary>Verifies external data opt out cancels pending request and rejects late results.</summary>
    [Fact]
    public async Task ExternalDataOptOutCancelsPendingRequestAndRejectsLateResults()
    {
        var client = new FakeClient([Poi(1)]) { Pending = new TaskCompletionSource<ExplorationPoiLoadResult>() };
        using ExplorationPoiViewModel vm = Create(client);
        Update(vm, "Sol", 1);
        await vm.LoadAsync();
        Assert.Equal(0, client.Calls);
        vm.SetExternalEnabled(true);
        Assert.Equal(1, client.Calls);
        vm.SetExternalEnabled(true);
        vm.SetExternalEnabled(false);
        client.Pending.SetResult(new ExplorationPoiLoadResult(new ExplorationPoiCatalog([Poi(1)]), null, false));
        await vm.PendingLoad;
        Assert.Empty(vm.Catalog.Items);
        Assert.False(vm.RefreshCommand.CanExecute(null));
        client.Pending = null;
        vm.SetExternalEnabled(true);
        await vm.PendingLoad;
        Assert.Single(vm.CurrentPois);
        Assert.NotEmpty(vm.DataStatus);
        Assert.True(vm.RefreshCommand.CanExecute(null));
        vm.RefreshCommand.Execute(null);
        Assert.Equal(3, client.Calls);
    }

    /// <summary>Verifies position editor uses the shared poi presentation and representative data.</summary>
    [Fact]
    public void PositionEditorUsesTheSharedPoiPresentationAndRepresentativeData()
    {
        Assert.True(OverlayRuntimePresentationFactory.IsSupported("PlotExplorationPois"));
        Assert.Equal(320, OverlayLayoutCatalog.GetRequired("PlotExplorationPois").PreviewSize.Width);
        using ExplorationPoiViewModel preview = Assert.IsType<ExplorationPoiViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly("PlotExplorationPois")
        );
        Assert.Equal("Great Annihilator", preview.CurrentSystem);
        Assert.Equal(2, preview.OverlayPois.Count);
        Assert.False(preview.HasDistinctOverlaySystem);
    }

    /// <summary>Creates isolated test state without using external application data.</summary>
    private ExplorationPoiViewModel Create(
        IExplorationPoiClient client,
        BookmarkCatalog? bookmarks = null,
        IStarSystemResolver? resolver = null
    ) => new(client, new ExplorationPoiSettingsStore(Path.Combine(directory, "ui.json")), bookmarks, resolver);

    /// <summary>Verifies system and attraction searches are exclusive and search their own fields.</summary>
    [Fact]
    public async Task SystemAndAttractionSearchesAreExclusiveAndSearchTheirOwnFields()
    {
        var client = new FakeClient([Poi(1), Poi(2, "Alpha")]);
        using ExplorationPoiViewModel vm = Create(client);
        Update(vm, "Sol", 1);
        vm.SetExternalEnabled(true);
        await vm.PendingLoad;
        vm.CurrentOnly = false;
        vm.SystemQuery = "Alpha";
        Assert.Equal("Alpha", Assert.Single(vm.Items).SystemName);
        Assert.False(vm.IsAttractionSearchEnabled);
        vm.AttractionQuery = "Attraction";
        Assert.Empty(vm.AttractionQuery);
        vm.SystemQuery = "Attraction";
        Assert.Empty(vm.Items);
        vm.SystemQuery = " ";
        Assert.True(vm.IsAttractionSearchEnabled);
        vm.AttractionQuery = "Attraction 1";
        Assert.Equal("Sol", Assert.Single(vm.Items).SystemName);
        Assert.False(vm.IsSystemSearchEnabled);
        vm.SystemQuery = "Sol";
        Assert.True(string.IsNullOrWhiteSpace(vm.SystemQuery));
        vm.AttractionQuery = "Synopsis";
        Assert.Empty(vm.Items);
        vm.AttractionQuery = "";
        Assert.True(vm.IsSystemSearchEnabled);
        Assert.Equal(2, vm.Items.Count);
    }

    /// <summary>Verifies reference defaults to current system and catalogue origin moves only browser distances.</summary>
    [Fact]
    public async Task ReferenceDefaultsToCurrentSystemAndCatalogueOriginMovesOnlyBrowserDistances()
    {
        var client = new FakeClient([Poi(1), Poi(2, "Alpha"), Poi(4, category: "Legacy")]);
        using ExplorationPoiViewModel vm = Create(client);
        Update(vm, "Sol", 1);
        vm.SetExternalEnabled(true);
        await vm.PendingLoad;
        Assert.Equal("Sol", vm.ReferenceSystem);
        Assert.Equal(1, Assert.Single(vm.Items).DistanceLy);
        vm.ReferenceSystem = " alpha ";
        Assert.Equal("Alpha", Assert.Single(vm.Items).SystemName);
        Assert.Equal(0, Assert.Single(vm.Items).DistanceLy);
        Assert.Equal(1, Assert.Single(vm.CurrentPois).DistanceLy);
        Assert.Contains("alpha", vm.ReferenceStatus);
        vm.CurrentOnly = false;
        vm.MaximumDistanceLy = .5;
        Assert.Equal("Alpha", Assert.Single(vm.Items).SystemName);
        Update(vm, "Other", 99);
        Assert.Equal(" alpha ", vm.ReferenceSystem);
        Assert.Single(vm.Items);
        vm.UseCurrentSystemCommand.Execute(null);
        Assert.Equal("Other", vm.ReferenceSystem);
        Update(vm, "Sol", 1);
        Assert.Equal("Sol", vm.ReferenceSystem);
        vm.CurrentOnly = true;
        vm.IncludeLegacy = true;
        Assert.Equal(2, vm.Items.Count);
        Assert.Single(vm.CurrentPois);
        Assert.Single(vm.OverlayPois);
        vm.ReferenceSystem = "";
        Assert.Empty(vm.Items);
        Assert.Contains("Enter a reference", vm.ReferenceStatus);
        vm.UseCurrentSystemCommand.Execute(null);
        Assert.Equal(2, vm.Items.Count);
    }

    /// <summary>Verifies arbitrary reference uses shared resolver and exact match rather than first suggestion.</summary>
    [Fact]
    public async Task ArbitraryReferenceUsesSharedResolverAndExactMatchRatherThanFirstSuggestion()
    {
        var resolver = new FakeResolver(
            (_, _) =>
                Task.FromResult<IReadOnlyList<StarSystemReference>>([
                    new("Beta Minor", 10, new(1000, 0, 0)),
                    new("Beta", 11, new(3, 0, 0)),
                ])
        );
        using ExplorationPoiViewModel vm = Create(new FakeClient([Poi(1), Poi(2)]), resolver: resolver);
        Update(vm, "Sol", 1);
        vm.SetExternalEnabled(true);
        await vm.PendingLoad;
        vm.CurrentOnly = false;
        vm.MaximumDistanceLy = 1;
        vm.ReferenceSystem = "Beta";
        Assert.Empty(vm.Items);
        Assert.Contains("Resolving", vm.ReferenceStatus);
        await vm.PendingReferenceLookup;
        Assert.Equal(2, Assert.Single(vm.Items).Poi.Id);
        Assert.Equal(1, Assert.Single(vm.Items).DistanceLy);
        Assert.Equal(["Beta"], resolver.Queries);
        Update(vm, "Elsewhere", 30);
        await vm.LoadAsync(true);
        Assert.Equal("Beta", vm.ReferenceSystem);
        Assert.Single(vm.Items);
        Assert.Single(resolver.Queries);
    }

    /// <summary>Verifies failed reference lookup clears distances instead of using previous origin.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("http")]
    [InlineData("json")]
    [InlineData("io")]
    public async Task FailedReferenceLookupClearsDistancesInsteadOfUsingPreviousOrigin(string failure)
    {
        var resolver = new FakeResolver(
            (_, _) =>
                failure switch
                {
                    "http" => throw new HttpRequestException("offline"),
                    "json" => throw new JsonException("invalid"),
                    "io" => throw new InvalidDataException("invalid"),
                    _ => Task.FromResult<IReadOnlyList<StarSystemReference>>([new("Other", 10, new(0, 0, 0))]),
                }
        );
        using ExplorationPoiViewModel vm = Create(new FakeClient([Poi(1)]), resolver: resolver);
        Update(vm, "Sol", 1);
        vm.SetExternalEnabled(true);
        await vm.PendingLoad;
        vm.CurrentOnly = false;
        Assert.Single(vm.Items);
        vm.ReferenceSystem = "Unknown";
        await vm.PendingReferenceLookup;
        Assert.Empty(vm.Items);
        Assert.Contains(failure == "missing" ? "No coordinates" : "could not be resolved", vm.ReferenceStatus);
        Assert.Single(vm.CurrentPois);
    }

    /// <summary>Verifies origin lookup debounces cancels and rejects late results after new origin or opt out.</summary>
    [Fact]
    public async Task OriginLookupDebouncesCancelsAndRejectsLateResultsAfterNewOriginOrOptOut()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<StarSystemReference>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = new FakeResolver(
            (_, _) =>
            {
                started.TrySetResult();
                return pending.Task;
            }
        );
        using ExplorationPoiViewModel vm = Create(new FakeClient([Poi(1)]), resolver: resolver);
        Update(vm, "Sol", 1);
        vm.ReferenceSystem = "Remote";
        Assert.Contains("Enable external data", vm.ReferenceStatus);
        Assert.Empty(resolver.Queries);
        vm.SetExternalEnabled(true);
        await vm.PendingLoad;
        vm.CurrentOnly = false;
        vm.ReferenceSystem = "Typing";
        Task superseded = vm.PendingReferenceLookup;
        vm.ReferenceSystem = "Remote";
        await superseded;
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task oldLookup = vm.PendingReferenceLookup;
        vm.UseCurrentSystemCommand.Execute(null);
        pending.SetResult([new("Remote", 10, new(1000, 0, 0))]);
        await oldLookup;
        Assert.Single(vm.Items);
        Assert.Contains("Distances from Sol", vm.ReferenceStatus);
        Assert.Equal(["Remote"], resolver.Queries);
        vm.ReferenceSystem = "New remote";
        Task disabled = vm.PendingReferenceLookup;
        vm.SetExternalEnabled(false);
        await disabled;
        Assert.Empty(vm.Items);
        Assert.Contains("Enable external data", vm.ReferenceStatus);
        Assert.Single(resolver.Queries);
    }

    private sealed class FakeResolver(Func<string, CancellationToken, Task<IReadOnlyList<StarSystemReference>>> search)
        : IStarSystemResolver
    {
        public List<string> Queries { get; } = [];

        /// <summary>Provides search async for the isolated test fixture.</summary>
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        )
        {
            Queries.Add(query);
            return search(query, cancellationToken);
        }
    }

    /// <summary>Verifies main window shares one catalogue with existing travel surfaces.</summary>
    [AvaloniaFact]
    public async Task MainWindowSharesOneCatalogueWithExistingTravelSurfaces()
    {
        var client = new FakeClient([Poi(1)]);
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            b => b.WithExplorationPoiClient(client)
        );
        main.SystemSurvey.UseExternalData = false;
        Update(main.ExplorationPois, "Sol", 1);
        Assert.Equal(0, client.Calls);
        main.SystemSurvey.UseExternalData = true;
        await main.ExplorationPois.PendingLoad;
        Assert.Single(main.ExplorationPois.CurrentPois);
        Assert.Equal(1, client.Calls);
        main.SystemSurvey.UseExternalBioData = false;
        Assert.Equal(1, client.Calls);
        main.SystemSurvey.UseExternalData = false;
        Assert.False(main.ExplorationPois.RefreshCommand.CanExecute(null));
    }

    /// <summary>Provides update for the isolated test fixture.</summary>
    private static void Update(
        ExplorationPoiViewModel vm,
        string system,
        long address,
        GuiFocus focus = GuiFocus.NoFocus,
        IReadOnlyList<JournalEventEnvelope>? events = null
    ) =>
        vm.UpdateContext(
            system,
            address,
            new GalacticCoordinate(0, 0, 0),
            new EliteStatus { GuiFocus = focus, Flags = StatusFlags.InMainShip },
            events ?? []
        );

    /// <summary>Builds timestamped journal evidence for the test scenario.</summary>
    private static JournalEventEnvelope Event(string name, string fields = "")
    {
        string json = "{\"event\":\"" + name + "\"" + (fields.Length > 0 ? "," + fields : "") + "}";
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out _));
        return entry!;
    }

    /// <summary>Releases the isolated test resources after assertions complete.</summary>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class FakeClient(IEnumerable<ExplorationPoi> pois) : IExplorationPoiClient
    {
        public int Calls { get; private set; }
        public TaskCompletionSource<ExplorationPoiLoadResult>? Pending { get; set; }

        /// <summary>Provides get async for the isolated test fixture.</summary>
        public Task<ExplorationPoiLoadResult> GetAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            return Pending?.Task
                ?? Task.FromResult(
                    new ExplorationPoiLoadResult(new ExplorationPoiCatalog(pois), DateTimeOffset.UtcNow, false)
                );
        }
    }
}
