using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MiningSearchViewModelTests
{
    [Fact]
    public void PowerplayPickerOnlyOffersRingCommoditiesThatTheSearchCanPrice()
    {
        Assert.All(
            MiningSearchViewModel.PowerplayMinerals.Skip(2),
            commodity => Assert.True(PlanetaryMiningPlan.IsEdpmCommodity(commodity), commodity)
        );
        Assert.Contains("Void Opals", MiningSearchViewModel.PowerplayMinerals);
        Assert.DoesNotContain("Tritium", MiningSearchViewModel.PowerplayMinerals);
    }

    [Fact]
    public void PlanetaryMiningIsExclusiveAndSwitchesToSurfaceHuntMaterials()
    {
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        );

        model.MiningTypeChips.Add(PlanetaryMiningPlan.MiningType);

        Assert.Equal(PlanetaryMiningPlan.MiningType, Assert.Single(model.MiningTypeChips.Selected));
        Assert.False(model.UsesRingFilters);
        Assert.Contains("Diamond", PlanetaryMiningPlan.Materials);
        model.MineralChips.Add("Diamond");
        Assert.Contains("Diamond", model.MineralChips.Selected);
        model.MineralChips.Add("Void Opal");
        Assert.DoesNotContain("Void Opal", model.MineralChips.Selected);

        model.MiningTypeChips.Add("Core");
        Assert.Equal("Core", Assert.Single(model.MiningTypeChips.Selected));
        Assert.True(model.UsesRingFilters);
        Assert.DoesNotContain("Diamond", model.MineralChips.Selected);
    }

    [Fact]
    public async Task PlanetaryPowerplayUsesSurfaceHuntVolcanismWithoutReserveOrLandmarks()
    {
        using var handler = new PlanetaryFilterHandler();
        using var http = new HttpClient(handler);
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(http),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Timbalderis",
            Reserve = "Pristine",
            PowerState = "Stronghold",
        };
        model.MiningTypeChips.Add(PlanetaryMiningPlan.MiningType);
        model.MineralChips.Add("Periclase Dunite");

        await model.SearchSystemsAsync();

        Assert.NotNull(handler.BodyFilters);
        using var request = System.Text.Json.JsonDocument.Parse(handler.BodyFilters);
        System.Text.Json.JsonElement filters = request.RootElement.GetProperty("filters");
        Assert.True(filters.GetProperty("is_landable").GetProperty("value").GetBoolean());
        Assert.Equal(
            PlanetaryMiningPlan.MetallicMagmaTypes,
            filters
                .GetProperty("volcanism_type")
                .GetProperty("value")
                .EnumerateArray()
                .Select(value => value.GetString())
        );
        Assert.False(filters.TryGetProperty("reserve_level", out _));
        Assert.False(filters.TryGetProperty("system_power_state", out _));
        Assert.False(filters.TryGetProperty("system_controlling_power", out _));
        Assert.False(filters.TryGetProperty("landmarks", out _));
        Assert.Equal(1, handler.RequestedBodyRadius);
    }

    [Fact]
    public async Task PlanetaryPowerplayIncludesEligibleBodiesAfterTheFirstSpanshPage()
    {
        using var handler = new PlanetaryFilterHandler { PagedBodies = true };
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(new HttpClient(handler)),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Sol",
            ResultLimit = 2,
        };
        model.MiningTypeChips.Add(PlanetaryMiningPlan.MiningType);
        model.MineralChips.Add("Monazite");

        await model.SearchSystemsAsync();

        Assert.Equal(2, handler.BodyPages);
        Assert.Contains(
            Assert.Single(Assert.Single(model.PlanetarySearch.Rows).Systems).Bodies,
            body => body.Details.StartsWith("101:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task PlanetaryPowerplayChoosesSellSystemsByGoalBeforeSearchingBodies()
    {
        using var handler = new PlanetaryFilterHandler { MultipleSellSystems = true };
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(new HttpClient(handler)),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Timbalderis",
            PledgedPower = "Aisling Duval",
            Objective = "Reinforce",
        };
        model.MiningTypeChips.Add(PlanetaryMiningPlan.MiningType);
        model.MineralChips.Add("Monazite");

        await model.SearchSystemsAsync();

        Assert.Equal("Own Sell", Assert.Single(model.PlanetarySearch.Rows).Target);
        Assert.Equal(["Own Sell"], handler.BodyReferences);
        Assert.All(
            Assert.Single(model.PlanetarySearch.Rows).Systems,
            system => Assert.Equal("Own Sell", system.System)
        );
        Assert.Equal(["Own Sell"], handler.RequestedBodySystems);
        Assert.Equal(1, handler.RequestedBodyRadius);
        Assert.Empty(model.MeritRows);

        model.Objective = "Undermine";
        handler.BodyReferences.Clear();
        await model.SearchSystemsAsync();

        Assert.Equal("Other Sell", Assert.Single(model.PlanetarySearch.Rows).Target);
        Assert.Equal(["Other Sell"], handler.BodyReferences);
        Assert.All(
            Assert.Single(model.PlanetarySearch.Rows).Systems,
            system => Assert.Equal("Other Sell", system.System)
        );
        Assert.Equal(["Other Sell"], handler.RequestedBodySystems);
        Assert.Equal(1, handler.RequestedBodyRadius);
    }

    [Fact]
    public async Task PlanetaryAcquireOnlyOffersUnownedSellSystemsInsideSupporterReach()
    {
        using var handler = new PlanetaryFilterHandler { AcquireSellSystems = true };
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(new HttpClient(handler)),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Timbalderis",
            PledgedPower = "Aisling Duval",
            Objective = "Acquire",
        };
        model.MiningTypeChips.Add(PlanetaryMiningPlan.MiningType);
        model.MineralChips.Add("Monazite");

        await model.SearchSystemsAsync();

        Assert.Equal("Near Target", Assert.Single(model.PlanetarySearch.Rows).Target);
        Assert.Equal(["Near Target"], handler.BodyReferences);
        Assert.All(
            Assert.Single(model.PlanetarySearch.Rows).Systems,
            system => Assert.Equal("Supporter", system.System)
        );
        Assert.Equal(["Supporter"], handler.RequestedBodySystems);
        Assert.Equal(30, handler.RequestedBodyRadius);
        Assert.Equal([("Supporter", 20d)], handler.AcquisitionTargetQueries);
    }

    [Fact]
    public async Task PlanetaryAcquireUsesThirtyLyStrongholdSourceWhenFortifiedIsTooFar()
    {
        using var handler = new PlanetaryFilterHandler { AcquireSellSystems = true, StrongholdReach = true };
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(new HttpClient(handler)),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Timbalderis",
            PledgedPower = "Aisling Duval",
            Objective = "Acquire",
        };
        model.MiningTypeChips.Add(PlanetaryMiningPlan.MiningType);
        model.MineralChips.Add("Monazite");

        await model.SearchSystemsAsync();

        SurfaceSellRowViewModel row = Assert.Single(model.PlanetarySearch.Rows);
        Assert.Equal("Near Target", row.Target);
        Assert.Equal("Stronghold Source", Assert.Single(row.Systems).System);
        Assert.Equal(["Stronghold Source"], handler.RequestedBodySystems);
        Assert.Equal([("Stronghold Source", 30d)], handler.AcquisitionTargetQueries);
    }

    private sealed class PlanetaryFilterHandler : HttpMessageHandler
    {
        public string? BodyFilters { get; private set; }
        public bool PagedBodies { get; init; }
        public bool MultipleSellSystems { get; init; }
        public bool AcquireSellSystems { get; init; }
        public bool StrongholdReach { get; init; }
        public int BodyPages { get; private set; }
        public List<string> BodyReferences { get; } = [];
        public string[] RequestedBodySystems { get; private set; } = [];
        public double RequestedBodyRadius { get; private set; }
        public List<(string Reference, double Radius)> AcquisitionTargetQueries { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.RequestUri?.AbsolutePath == "/api/systems/search")
            {
                if (AcquireSellSystems)
                {
                    using var search = System.Text.Json.JsonDocument.Parse(
                        await request.Content!.ReadAsStringAsync(cancellationToken)
                    );
                    System.Text.Json.JsonElement filters = search.RootElement.GetProperty("filters");
                    if (filters.TryGetProperty("name", out _))
                    {
                        return Json(
                            StrongholdReach
                                ? """{"results":[{"name":"Near Target","power_state":"Unoccupied","x":25,"y":0,"z":0}]}"""
                                : """{"results":[{"name":"Near Target","power_state":"Unoccupied","x":18,"y":0,"z":0},{"name":"Far Target","power_state":"Unoccupied","x":31,"y":0,"z":0}]}"""
                        );
                    }

                    if (filters.GetProperty("power_state").GetProperty("value")[0].GetString() == "Unoccupied")
                    {
                        AcquisitionTargetQueries.Add(
                            (
                                search.RootElement.GetProperty("reference_system").GetString() ?? "",
                                filters.GetProperty("distance").GetProperty("max").GetDouble()
                            )
                        );
                        return Json(
                            StrongholdReach
                                ? """{"results":[{"name":"Near Target","power_state":"Unoccupied","x":25,"y":0,"z":0}]}"""
                                : """{"results":[{"name":"Near Target","power_state":"Unoccupied","x":18,"y":0,"z":0},{"name":"Far Target","power_state":"Unoccupied","x":31,"y":0,"z":0}]}"""
                        );
                    }

                    if (
                        StrongholdReach
                        && filters.GetProperty("power_state").GetProperty("value")[0].GetString() == "Stronghold"
                    )
                    {
                        return Json(
                            """{"results":[{"name":"Stronghold Source","controlling_power":"Aisling Duval","power_state":"Stronghold","x":50,"y":0,"z":0}]}"""
                        );
                    }

                    return Json(
                        filters.GetProperty("power_state").GetProperty("value")[0].GetString() == "Fortified"
                            ? """{"results":[{"name":"Supporter","controlling_power":"Aisling Duval","power_state":"Fortified","x":0,"y":0,"z":0}]}"""
                            : """{"results":[]}"""
                    );
                }

                return Json(
                    MultipleSellSystems
                        ? """{"results":[{"name":"Own Sell","controlling_power":"Aisling Duval","power_state":"Fortified"},{"name":"Other Sell","controlling_power":"Jerome Archer","power_state":"Fortified"}]}"""
                        : """{"results":[{"name":"Sell System","power_state":"Stronghold"}]}"""
                );
            }

            if (request.RequestUri?.AbsolutePath.Contains("/nearby/imports", StringComparison.Ordinal) == true)
            {
                string updated = DateTimeOffset.UtcNow.ToString("O");
                if (AcquireSellSystems)
                {
                    return Json(
                        StrongholdReach
                            ? $$"""[{"systemName":"Near Target","stationName":"Near Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":400000,"demand":1000,"stock":0,"updatedAt":"{{updated}}","distance":25,"distanceToArrival":200,"marketId":1,"commodityName":"Monazite"}]"""
                            : $$"""[{"systemName":"Near Target","stationName":"Near Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":400000,"demand":1000,"stock":0,"updatedAt":"{{updated}}","distance":18,"distanceToArrival":200,"marketId":1,"commodityName":"Monazite"},{"systemName":"Far Target","stationName":"Far Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":900000,"demand":1000,"stock":0,"updatedAt":"{{updated}}","distance":31,"distanceToArrival":100,"marketId":2,"commodityName":"Monazite"}]"""
                    );
                }

                if (MultipleSellSystems)
                {
                    return Json(
                        $$"""[{"systemName":"Own Sell","stationName":"Own Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":400000,"demand":1000,"stock":0,"updatedAt":"{{updated}}","distance":12,"distanceToArrival":200,"marketId":1,"commodityName":"Monazite"},{"systemName":"Other Sell","stationName":"Other Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":900000,"demand":1000,"stock":0,"updatedAt":"{{updated}}","distance":15,"distanceToArrival":100,"marketId":2,"commodityName":"Monazite"}]"""
                    );
                }
                return Json(
                    $$"""[{"systemName":"Sell System","stationName":"Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":500000,"demand":1000,"stock":0,"updatedAt":"{{updated}}","distance":12,"distanceToArrival":200,"marketId":1,"commodityName":"{{(PagedBodies ? "Monazite" : "Periclase Dunite")}}"}]"""
                );
            }

            if (request.RequestUri?.AbsolutePath == "/api/bodies/search")
            {
                BodyFilters = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var query = System.Text.Json.JsonDocument.Parse(BodyFilters);
                string reference = query.RootElement.GetProperty("reference_system").GetString() ?? "";
                BodyReferences.Add(reference);
                RequestedBodySystems = query
                    .RootElement.GetProperty("filters")
                    .GetProperty("system_name")
                    .GetProperty("value")
                    .EnumerateArray()
                    .Select(value => value.GetString() ?? "")
                    .ToArray();
                RequestedBodyRadius = query
                    .RootElement.GetProperty("filters")
                    .GetProperty("distance")
                    .GetProperty("max")
                    .GetDouble();
                if (PagedBodies)
                {
                    int page = query.RootElement.GetProperty("page").GetInt32();
                    BodyPages++;
                    object[] results =
                        page == 0
                            ? Enumerable
                                .Range(0, 100)
                                .Select(index =>
                                    (object)
                                        new
                                        {
                                            name = $"First {index}",
                                            system_name = "First",
                                            subtype = "Rocky body",
                                            volcanism_type = "Minor Metallic Magma",
                                            distance = 1,
                                            parents = new[]
                                            {
                                                new
                                                {
                                                    id64 = 42,
                                                    type = "Star",
                                                    subtype = "White Dwarf (DB) Star",
                                                },
                                            },
                                        }
                                )
                                .ToArray()
                            :
                            [
                                new
                                {
                                    name = "Sell System 101",
                                    system_name = "Sell System",
                                    subtype = "Rocky body",
                                    volcanism_type = "Minor Metallic Magma",
                                    distance = 2,
                                },
                            ];
                    return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { results })),
                    };
                }

                if (AcquireSellSystems)
                {
                    return Json(
                        StrongholdReach
                            ? """{"results":[{"name":"Stronghold Source 1","system_name":"Stronghold Source","subtype":"Rocky body","distance":25},{"name":"Supporter 1","system_name":"Supporter","subtype":"Rocky body","distance":25},{"name":"Near Target 1","system_name":"Near Target","subtype":"Rocky body","distance":0}]}"""
                            : """{"results":[{"name":"Supporter 1","system_name":"Supporter","subtype":"Rocky body","distance":18},{"name":"Near Target 1","system_name":"Near Target","subtype":"Rocky body","distance":0},{"name":"Miner 1","system_name":"Miner","subtype":"Rocky body","distance":2}]}"""
                    );
                }

                if (MultipleSellSystems)
                {
                    return Json(
                        $$"""{"results":[{"name":"{{reference}} 1","system_name":"{{reference}}","subtype":"Rocky body","distance":0},{"name":"Miner 1","system_name":"Miner","subtype":"Rocky body","distance":2}]}"""
                    );
                }

                return Json(
                    """{"results":[{"name":"Sell System 1","system_name":"Sell System","subtype":"Rocky body","volcanism_type":"Minor Metallic Magma","distance":0,"parents":[{"id64":42,"type":"Star","subtype":"White Dwarf (DB) Star"}]}]}"""
                );
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/commodities", StringComparison.Ordinal) == true)
            {
                return Json("[]");
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri?.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal) == true
                        ? "{\"results\":[]}"
                        : "[]"
                ),
            };
        }

        private static HttpResponseMessage Json(string value) =>
            new(System.Net.HttpStatusCode.OK) { Content = new StringContent(value) };
    }

    [Fact]
    public void EachSearchTableRetainsIndependentSortState()
    {
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        );

        model.Destination = 0;
        model.SortCommand.Execute("System");
        Assert.Equal("↑", model.SortIndicators["System"]);
        model.Destination = 1;
        Assert.Equal(string.Empty, model.SortIndicators["System"]);
        model.SortCommand.Execute("Price");
        Assert.Equal("↑", model.SortIndicators["Price"]);
        model.Destination = 0;
        Assert.Equal("↑", model.SortIndicators["System"]);
    }

    [Fact]
    public async Task PlainBookmarkDoesNotEraseKnownOverlapOrResAnnotations()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var bookmarks = new BookmarksViewModel(directory);
            bookmarks.AddMiningLocation(new GalacticBookmark { System = "Review Test", Body = "Review Test A Ring" });
            var ring = new MiningRing
            {
                System = "Review Test",
                Body = "Review Test A Ring",
                Position = new GalacticCoordinate(0, 0, 0),
                Hotspots = new() { ["Platinum"] = 2 },
                Overlaps = "Platinum x2",
                ResourceExtractionSites = "High",
            };
            using var model = new MiningSearchViewModel(
                new MiningSearchClient(),
                bookmarks,
                _ => { },
                () => [ring],
                new Resolver()
            )
            {
                Source = "Local",
                Reference = ring.System,
                Radius = 1,
                OnlyOverlaps = true,
                OnlyRes = true,
            };
            await model.SearchRingsAsync();
            Assert.Contains(
                model.Rings,
                r => r.System == ring.System && r.Overlaps == ring.Overlaps && r.ResourceExtractionSites == "High"
            );
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void SearchPreferencesRoundTripAndResetForAnotherCommander()
    {
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        );
        model.Reference = "Sol";
        model.Radius = 240;
        model.OnlyRes = true;
        model.TraderType = "Encoded";
        model.Reserve = "Pristine";
        model.MinimumDemand = 500;
        model.MaximumDemand = 5000;
        model.ResultLimit = 42;
        model.PlatinumMode = "Overlaps";
        model.OpposingPower = "Jerome Archer";
        model.PledgedPower = "Aisling Duval";
        model.Objective = "Acquire";
        MiningCommanderData restored = MiningStore.Parse(
            MiningStore.Export(
                new MiningCommanderData { Settings = new MiningPreferences { SearchOptions = model.SaveOptions() } }
            )
        );
        model.LoadOptions(new());
        Assert.Equal("", model.Reference);
        Assert.Equal(100, model.Radius);
        Assert.False(model.OnlyRes);
        Assert.Equal("All systems", model.Objective);
        Assert.Equal("Any", model.PledgedPower);
        model.LoadOptions(restored.Settings.SearchOptions);
        Assert.Equal("Sol", model.Reference);
        Assert.Equal(240, model.Radius);
        Assert.True(model.OnlyRes);
        Assert.Equal("Encoded", model.TraderType);
        Assert.Equal("Pristine", model.Reserve);
        Assert.Equal(500, model.MinimumDemand);
        Assert.Equal(5000, model.MaximumDemand);
        Assert.Equal(42, model.ResultLimit);
        Assert.Equal("Overlaps", model.PlatinumMode);
        Assert.Equal("Jerome Archer", model.OpposingPower);
    }

    [Fact]
    public async Task PlatinumSpotsRanksUsefulLocalRingsAndCanShowAllPlatinumRings()
    {
        const string system = "Platinum Spots Test";
        MiningRing[] rings =
        [
            Spot("Mapped A Ring", 1, resourceExtractionSites: "High"),
            Spot("Overlap B Ring", 1, overlaps: "Platinum x2"),
            Spot("Double C Ring", 2),
            Spot("Plain D Ring", 1),
            Spot("Wrong E Ring", 2) with
            {
                RingType = "Icy",
            },
        ];
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => rings,
            new Resolver()
        )
        {
            Reference = system,
            Source = "Local",
            Radius = 1,
            ResultLimit = 10,
        };

        await model.SearchPlatinumAsync();

        Assert.Equal(3, model.PlatinumSpots.Count);
        Assert.Equal("Mapped A Ring", model.PlatinumSpots[0].Body);
        Assert.DoesNotContain(model.PlatinumSpots, spot => spot.Body == "Plain D Ring");
        model.PlatinumMode = "All platinum";
        await model.SearchPlatinumAsync();
        Assert.Equal(4, model.PlatinumSpots.Count);
        Assert.Contains(model.PlatinumSpots, spot => spot.Body == "Plain D Ring");
        model.SelectedPlatinumSpot = model.PlatinumSpots[0];
        model.UseSelectedPlatinumSpot();
        Assert.Equal(system, model.Reference);
        model.BookmarkSelectedPlatinumSpot();
        model.SelectedPlatinumSpot = null;
        model.UseSelectedPlatinumSpot();
        model.BookmarkSelectedPlatinumSpot();

        static MiningRing Spot(string body, int hotspots, string overlaps = "", string resourceExtractionSites = "") =>
            new()
            {
                System = system,
                Body = body,
                RingType = "Metallic",
                Reserve = "Pristine",
                Hotspots = new() { ["Platinum"] = hotspots },
                Overlaps = overlaps,
                ResourceExtractionSites = resourceExtractionSites,
            };
    }

    [Fact]
    public async Task OversizedTraderResponseShowsInlineFailureAndAllowsRetry()
    {
        using var http = new HttpClient(new OversizedTraderHandler());
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(http),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Wille",
        };
        Exception? error = await Record.ExceptionAsync(() => model.SearchTradersAsync());
        Assert.Null(error);
        Assert.False(model.IsBusy);
        Assert.Contains("Request failed. Try again.", model.Status);
        Assert.Empty(model.Markets);
        await model.SearchTradersAsync();
        Assert.Contains("material traders", model.Status);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task SelectedSystemAndRingCarryLocationIntoSellingWorkflow()
    {
        using var handler = new WorkflowHandler();
        using var http = new HttpClient(handler);
        using var model = new MiningSearchViewModel(
            new(http),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Sol",
            Mineral = "Platinum",
            Source = "Spansh",
            Destination = 3,
            SelectedSystem = new("Wille", 2, "", "", "", "", "", "", "", 0),
        };
        await model.FindSelectedSystemRingsAsync();
        Assert.Equal(0, model.Destination);
        Assert.True(model.SystemOnly);
        Assert.Equal("Wille", model.Reference);
        model.SelectedRing = Assert.Single(model.Rings);
        await model.FindSellingStationsAsync();
        Assert.Equal(1, model.Destination);
        Assert.False(model.Buying);
        Assert.Equal("Sell mined cargo", model.TradeMode);
        Assert.False(model.SystemOnly);
        Assert.Equal(2, model.Markets.Count);
        await model.SearchTradersAsync();
        Assert.Equal(2, model.Markets.Count); // Trader results cannot replace commodity prices.
        Assert.Single(model.Traders);
    }

    [Fact]
    public async Task PowerplayObjectivesUseAnyOwnershipThenRequireTheNamedPledge()
    {
        using var handler = new WorkflowHandler();
        using var http = new HttpClient(handler);
        using var model = new MiningSearchViewModel(
            new(http),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Wille",
            Objective = "Reinforce",
        };
        await model.SearchSystemsAsync();
        Assert.Equal("Any", model.PledgedPower);
        Assert.DoesNotContain("Choose your pledged Power", model.Status);
        Assert.Contains(model.Systems, system => system.System == "Own");
        Assert.Contains(model.Systems, system => system.System == "Other");
        Assert.Contains(model.Systems, system => system.System == "Open");
        model.PledgedPower = "Aisling Duval";
        await model.SearchSystemsAsync();
        Assert.Equal("Own", Assert.Single(model.Systems).System);
        model.Objective = "Undermine";
        await model.SearchSystemsAsync();
        Assert.Equal("Other", Assert.Single(model.Systems).System);
        model.OpposingPower = "Aisling Duval";
        await model.SearchSystemsAsync();
        Assert.Empty(model.Systems);
        model.OpposingPower = "Jerome Archer";
        await model.SearchSystemsAsync();
        Assert.Equal("Other", Assert.Single(model.Systems).System);
        model.Objective = "Acquire";
        await model.SearchSystemsAsync();
        Assert.Empty(model.Systems);
        Assert.Contains("Fortified or Stronghold", model.Status);
    }

    [Fact]
    public async Task AcquisitionKeepsSellingDestinationWhenMiningRingIsInAnotherSystem()
    {
        using var http = new HttpClient(new WorkflowHandler());
        using var model = new MiningSearchViewModel(
            new(http),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            PledgedPower = "Archon Delaine",
            Objective = "Acquire",
            SelectedSystem = new("Acquisition target", 1, "", "", "", "", "", "", "Unoccupied", 0),
        };
        model.UseSelectedSystem();
        model.SystemOnly = false;
        model.SelectedRing = new MiningRing { System = "Mining origin", Body = "Mining origin A Ring" };
        await model.FindSellingStationsAsync();
        Assert.Equal("Acquisition target", model.Reference);
        Assert.True(model.SystemOnly);
        Assert.Contains("Mining: Mining origin", model.PlanningContext);
        Assert.Contains("Acquire destination: Acquisition target", model.PlanningContext);
        model.ClearPlan();
        Assert.Empty(model.PlanningContext);
    }

    [Fact]
    public void AnyPowerLocksTheGoalOnReinforceAndOpposingPowerOnAny()
    {
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        );
        model.PledgedPower = "Archon Delaine";
        model.Objective = "Acquire";
        model.OpposingPower = "Yuri Grom";
        Assert.True(model.CanChoosePowerGoal);

        model.PledgedPower = "Any";

        Assert.False(model.CanChoosePowerGoal);
        Assert.Equal("Reinforce", model.Objective);
        Assert.Equal("Any", model.OpposingPower);
        model.Objective = "Acquire";
        Assert.Equal("Reinforce", model.Objective);
    }

    [Fact]
    public void OpposingNoneIsOnlyAvailableWhileReinforcing()
    {
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        );

        Assert.Equal("Default", Assert.Single(model.MineralChips.Selected));
        model.MineralChips.Add("Platinum");
        Assert.Equal("Platinum", Assert.Single(model.MineralChips.Selected));
        model.MineralChips.Add("Any");
        Assert.Equal("Any", Assert.Single(model.MineralChips.Selected));

        model.PledgedPower = "Aisling Duval";
        model.Objective = "Reinforce";
        Assert.Contains("None", model.OpposingChoices);
        Assert.Contains("Multiple", model.OpposingChoices);
        model.OpposingPower = "None";
        model.Objective = "Undermine";

        Assert.Equal("Any", model.OpposingPower);
        Assert.DoesNotContain("None", model.OpposingChoices);
        Assert.Contains("Two", model.OpposingChoices);
    }

    [Fact]
    public async Task ReinforceSearchFallsBackWhenArdentFailsAndKeepsTheDiagnosticTogether()
    {
        var logs = new List<string>();
        using var http = new HttpClient(new ArdentFailureHandler());
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(http),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Wille",
            PledgedPower = "Aisling Duval",
            Objective = "Reinforce",
            Radius = 50,
        };
        model.UseDiagnosticLog(logs.Add);
        model.OpposingPower = "One";
        await model.SearchSystemsAsync();

        Assert.Equal("Own", Assert.Single(model.Systems).System);
        Assert.Equal("Own", Assert.Single(model.MeritRows).Name);
        Assert.Contains("Spansh fallback", model.Status);
        Assert.Contains(logs, line => line.Contains("failed", StringComparison.OrdinalIgnoreCase));
        model.DistanceSortCommand.Execute(null);
        Assert.Equal("Farthest first", model.DistanceSortLabel);
        model.ResetPowerplay();
        Assert.Equal("Reinforce", model.Objective);
        Assert.Equal("Any", model.OpposingPower);
    }

    [Fact]
    public async Task RingSearchFallsBackOnlyForTheCommodityMissingFromArdent()
    {
        using var handler = new ArdentFailureHandler { PartialArdent = true };
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(new HttpClient(handler)),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Wille",
            Radius = 50,
        };
        model.MineralChips.Add("Gold");
        model.MineralChips.Add("Platinum");

        await model.SearchSystemsAsync();

        Assert.Equal("Own", Assert.Single(model.MeritRows).Name);
        Assert.Contains("Ardent/Spansh fallback", model.Status);
        Assert.Contains("Platinum", handler.FallbackFilter);
        Assert.DoesNotContain("Gold", handler.FallbackFilter);
    }

    private sealed class ArdentFailureHandler : HttpMessageHandler
    {
        public bool PartialArdent { get; init; }
        public string? FallbackFilter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("/api/systems/search", StringComparison.Ordinal))
            {
                return await Json(
                    """{"results":[{"name":"Own","distance":12,"x":1,"y":2,"z":3,"controlling_power":"Aisling Duval","power_state":"Fortified","power_conflict_progress":[{"power":"Jerome Archer","progress":0.1}]}]}"""
                );
            }

            if (path.Contains("/api/bodies/search", StringComparison.Ordinal))
            {
                return await Json(
                    """{"results":[{"system_name":"Own","name":"Own A","rings":[{"name":"Own A Ring","type":"Metallic","signals":[{"name":"Platinum","count":2}]}]}]}"""
                );
            }

            if (path.Contains("/api/stations/search", StringComparison.Ordinal))
            {
                string filter = await request.Content!.ReadAsStringAsync(cancellationToken);
                if (filter.Contains("buying_commodities", StringComparison.Ordinal))
                {
                    FallbackFilter = filter;
                }

                return await Json(
                    $$"""{"results":[{"system_name":"Own","name":"Market","type":"Orbis Starport","distance_to_arrival":5,"market_updated_at":"{{DateTimeOffset.UtcNow:O}}","large_pads":1,"market":[{"commodity":"Platinum","sell_price":200000,"demand":1000}]}]}"""
                );
            }

            if (PartialArdent && path.Contains("/commodity/name/gold/nearby/imports", StringComparison.Ordinal))
            {
                return await Json(
                    $$"""[{"systemName":"Own","stationName":"Market","stationType":"Orbis Starport","maxLandingPadSize":3,"sellPrice":250000,"demand":1000,"updatedAt":"{{DateTimeOffset.UtcNow:O}}","distance":12,"commodityName":"Gold"}]"""
                );
            }

            if (PartialArdent && path.Contains("/commodity/name/platinum/nearby/imports", StringComparison.Ordinal))
            {
                return await Json("[]");
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
        }

        private static Task<HttpResponseMessage> Json(string payload) =>
            Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(payload) }
            );
    }

    [Fact]
    public async Task AcquireKeepsUnownedSystemsInsideFortifiedOrStrongholdRange()
    {
        using var http = new HttpClient(new AcquisitionRangeHandler());
        using var model = new MiningSearchViewModel(
            new(http),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Sol",
            PledgedPower = "Archon Delaine",
            Objective = "Acquire",
            Radius = 100,
        };

        await model.SearchSystemsAsync();

        MiningSystemResult claim = Assert.Single(model.Systems);
        Assert.Equal("Claim", claim.System);
        Assert.Equal("Unoccupied", claim.PowerState);
        AcquireResultRowViewModel row = Assert.Single(model.AcquireRows);
        Assert.Equal("Claim", row.Target);
        Assert.Equal("Anchor", Assert.Single(row.Miners).Name);
        Assert.Contains("20 ly", model.Status);
    }

    [Fact]
    public async Task AcquireReadsPastAFullBubblePageBeforeRulingOutTargets()
    {
        using var handler = new AcquisitionRangeHandler { PagedBubble = true };
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(new HttpClient(handler)),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Sol",
            PledgedPower = "Archon Delaine",
            Objective = "Acquire",
            Radius = 100,
        };

        await model.SearchSystemsAsync();

        Assert.Equal(2, handler.BubblePages);
        Assert.Equal("Claim", Assert.Single(model.Systems).System);
    }

    [Fact]
    public async Task ExpansionUsesLocalObservationsAndIsOnlyAnAcquisitionCandidate()
    {
        var cache = new MiningCommunityCache();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        cache.Apply(
            $$$"""{"$schemaRef":"https://eddn.edcd.io/schemas/journal/1","message":{"timestamp":"{{{now:O}}}","event":"Location","StarSystem":"Wille","StarPos":[0,0,0],"ControllingPower":"Aisling Duval","PowerplayState":"Expansion"}}""",
            now
        );
        using var http = new HttpClient(new WorkflowHandler());
        using var model = new MiningSearchViewModel(
            new(http),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver(),
            cache
        )
        {
            Reference = "Wille",
            PowerState = "Expansion",
            PledgedPower = "Aisling Duval",
            Objective = "Reinforce",
        };
        await model.SearchSystemsAsync();
        Assert.Empty(model.Systems);
        model.Objective = "Undermine";
        model.PledgedPower = "Jerome Archer";
        await model.SearchSystemsAsync();
        Assert.Empty(model.Systems);
        model.Objective = "Acquire";
        await model.SearchSystemsAsync();
        Assert.Empty(model.Systems);
        Assert.Contains("Fortified or Stronghold", model.Status);
        model.Security = "High";
        await model.SearchSystemsAsync();
        Assert.Empty(model.Systems); // The journal cannot certify security.
    }

    private sealed class WorkflowHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.Contains("material-trader", StringComparison.Ordinal))
            {
                return Task.FromResult(
                    new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") }
                );
            }

            string payload = path switch
            {
                "/api/bodies/search" =>
                    """{"results":[{"system_name":"Wille","rings":[{"name":"Wille A Ring","type":"Metallic","signals":[{"name":"Platinum","count":2}]}]}]}""",
                "/api/systems/search" =>
                    """{"results":[{"name":"Own","controlling_power":"Aisling Duval","power_state":"Fortified"},{"name":"Other","controlling_power":"Jerome Archer","power_state":"Exploited"},{"name":"Unknown"},{"name":"Open","power_state":"Unoccupied"}]}""",
                "/api/stations/search" => """{"results":[{"system_name":"Wille","name":"Trader"}]}""",
                _ =>
                    $$"""[{"systemName":"Wille","stationName":"Market","sellPrice":200000,"demand":1000,"updatedAt":"{{DateTimeOffset.UtcNow:O}}"},{"systemName":"Elsewhere","stationName":"Other market","sellPrice":250000,"demand":1000,"updatedAt":"{{DateTimeOffset.UtcNow:O}}"}]""",
            };
            return Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(payload) }
            );
        }
    }

    private sealed class AcquisitionRangeHandler : HttpMessageHandler
    {
        public bool PagedBubble { get; init; }
        public int BubblePages { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.Content is null)
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("[]") };
            }

            using var body = System.Text.Json.JsonDocument.Parse(
                await request.Content.ReadAsStringAsync(cancellationToken)
            );
            string reference = body.RootElement.GetProperty("reference_system").GetString() ?? "";
            int page = body.RootElement.GetProperty("page").GetInt32();
            string state = body
                .RootElement.GetProperty("filters")
                .TryGetProperty("power_state", out System.Text.Json.JsonElement powerState)
                ? powerState.GetProperty("value")[0].GetString() ?? ""
                : "";
            if (PagedBubble && reference == "Anchor" && request.RequestUri?.AbsolutePath == "/api/systems/search")
            {
                BubblePages++;
                object[] results =
                    page == 0
                        ? Enumerable
                            .Range(0, 100)
                            .Select(index =>
                                (object)
                                    new
                                    {
                                        name = $"Owned {index}",
                                        distance = 1,
                                        x = 1,
                                        y = 0,
                                        z = 0,
                                        controlling_power = "Yuri Grom",
                                        power_state = "Exploited",
                                    }
                            )
                            .ToArray()
                        :
                        [
                            new
                            {
                                name = "Claim",
                                distance = 10,
                                x = 10,
                                y = 0,
                                z = 0,
                                power_state = "Unoccupied",
                            },
                        ];
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { results })),
                };
            }

            string payload = (reference, state) switch
            {
                (_, "Fortified") =>
                    """{"results":[{"name":"Anchor","distance":0,"x":0,"y":0,"z":0,"controlling_power":"Archon Delaine","power_state":"Fortified"}]}""",
                (_, "Stronghold") => """{"results":[]}""",
                ("Anchor", _) =>
                    """{"results":[{"name":"Claim","distance":10,"x":10,"y":0,"z":0,"power_state":"Unoccupied"},{"name":"Owned","distance":5,"x":5,"y":0,"z":0,"controlling_power":"Yuri Grom","power_state":"Exploited"}]}""",
                _ => """{"results":[]}""",
            };
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(payload) };
        }
    }

    [Fact]
    public async Task CommanderResetDuringDeferredCancellationCannotStartReplacementSearch()
    {
        using var handler = new DeferredCancellationHandler();
        using var http = new HttpClient(handler);
        using var model = new MiningSearchViewModel(
            new(http),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Wille",
        };
        Task first = model.SearchTradersAsync();
        Task replacement = model.SearchTradersAsync();
        try
        {
            await handler.CancellationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            model.LoadOptions(new() { Reference = "Sol" });
            handler.Release.Set();
            await Task.WhenAll(first, replacement);
            Assert.Equal(1, handler.Calls);
            Assert.Empty(model.Traders);
            Assert.False(model.IsBusy);
        }
        finally
        {
            handler.Release.Set();
        }
    }

    private sealed class DeferredCancellationHandler : HttpMessageHandler
    {
        public ManualResetEventSlim Release { get; } = new();
        public TaskCompletionSource CancellationEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            if (Calls > 1)
            {
                return new(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"results\":[]}") };
            }

            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                CancellationEntered.TrySetResult();
                Release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None);
            });
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Canceled request must not complete.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Release.Set();
                Release.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class OversizedTraderHandler : HttpMessageHandler
    {
        private int calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            HttpContent content =
                ++calls <= 2
                    ? (HttpContent)new ByteArrayContent(new byte[8 * 1024 * 1024 + 1])
                    : new StringContent("[]");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class Resolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }
}
