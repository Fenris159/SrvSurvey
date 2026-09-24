using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MiningSearchViewModelTests
{
    [Fact]
    public void PowerplaySearchRowsReturnFromDiskWithoutAProviderRequest()
    {
        string directory = Path.Combine(Path.GetTempPath(), "srv-powerplay-cache-" + Guid.NewGuid());
        try
        {
            var cache = new MiningSearchResultCache(directory);
            var snapshot = new PowerplaySearchSnapshot(
                new MiningSearchPreferences { Reference = "Sol", PledgedPower = "Archon Delaine" },
                "Undermine",
                ["All"],
                ["Default"],
                ["Any"],
                "1 saved location",
                [
                    new MeritSystemSnapshot(
                        "Achenar",
                        "10.0 ly",
                        10,
                        "Stronghold",
                        "Stronghold",
                        "Archon Delaine",
                        [new PowerplayPowerLineViewModel("Archon Delaine", 0.3)],
                        [],
                        [],
                        [],
                        [],
                        [
                            new MeritStationBlockSnapshot(
                                "Coriolis",
                                "Port (L)",
                                "Monazite",
                                "Price: 100 CR",
                                "Demand: 10",
                                "",
                                "",
                                Enumerable
                                    .Range(1, 7)
                                    .Select(index => new MeritCommodityLineViewModel("MON", index + " CR", "10 Demand"))
                                    .ToArray()
                            ),
                        ],
                        ""
                    ),
                ],
                [],
                null
            )
            {
                PledgedPower = "Felicia Winters",
            };
            using (
                var keyModel = new MiningSearchViewModel(
                    new MiningSearchClient(),
                    new BookmarksViewModel(directory),
                    _ => { },
                    () => [],
                    new Resolver()
                )
            )
            {
                keyModel.LoadOptions(snapshot.Options);
                keyModel.PledgedPower = snapshot.PledgedPower;
                keyModel.Objective = snapshot.Objective;
                cache.Save("powerplay", keyModel.PowerplayCacheKey(), snapshot);
            }

            using var model = new MiningSearchViewModel(
                new MiningSearchClient(),
                new BookmarksViewModel(directory),
                _ => { },
                () => [],
                new Resolver()
            );
            model.ConfigureResultCache(cache);
            model.LoadOptions(new MiningSearchPreferences { Reference = "Other" });
            model.RestoreLastCompletedPowerplaySearch();
            model.UpdateCurrentLocation("Timbalderis");
            model.PreparePowerplay();

            Assert.Equal("Sol", model.Reference);
            Assert.Equal("Undermine", model.Objective);
            Assert.Equal("Felicia Winters", model.PledgedPower);
            Assert.Equal("Achenar", Assert.Single(model.MeritRows).Name);
            Assert.Equal("Archon Delaine 30%", Assert.Single(Assert.Single(model.MeritRows).PowerLines).Display);
            MeritStationBlockViewModel station = Assert.Single(Assert.Single(model.MeritRows).StationBlocks);
            Assert.Equal(6, station.OtherCommodities.Count);
            station.ToggleCommoditiesCommand.Execute(null);
            Assert.Equal(7, station.OtherCommodities.Count);
            Assert.Equal("1 saved location", model.Status);
            model.Radius = 101;
            Assert.Empty(model.MeritRows);
            model.Radius = 100;
            Assert.Equal("Achenar", Assert.Single(model.MeritRows).Name);

            model.PledgedPower = "Any";
            cache.Save(
                "powerplay",
                model.PowerplayCacheKey(),
                snapshot with
                {
                    PledgedPower = "Any",
                    Objective = "Reinforce",
                }
            );
            model.RestoreLastCompletedPowerplaySearch();
            string restoredKey = model.PowerplayCacheKey();
            model.NoteDetectedPower("Arissa Lavigny-Duval");
            Assert.Equal("Any", model.PledgedPower);
            Assert.Equal(restoredKey, model.PowerplayCacheKey());
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
    public void CachedRingAcquireRowsRestoreAndSortByDistanceOrStationScore()
    {
        string directory = Path.Combine(Path.GetTempPath(), "srv-ring-acquire-cache-" + Guid.NewGuid());
        try
        {
            var cache = new MiningSearchResultCache(directory);
            var options = new MiningSearchPreferences { Reference = "Sol", PledgedPower = "Archon Delaine" };
            AcquireResultSnapshot Row(string target, double distance, long score) =>
                new(
                    target,
                    "Unoccupied",
                    distance + " ly",
                    [
                        new MeritStationBlockSnapshot(
                            "Station",
                            target + " Port (L)",
                            "Monazite",
                            "Price: 500 CR",
                            "Demand: 10",
                            "",
                            "",
                            []
                        ),
                    ],
                    [
                        new AcquireMinerSnapshot(
                            target + " Mine",
                            [
                                new MeritLineViewModel(
                                    "Planet",
                                    "A Ring: Monazite",
                                    "A Ring",
                                    "RingRocky",
                                    "",
                                    "Monazite"
                                ),
                            ],
                            "Fortified",
                            [],
                            "Single"
                        ),
                    ]
                )
                {
                    DistanceLy = distance,
                    StationScores = [score],
                };
            var snapshot = new PowerplaySearchSnapshot(
                options,
                "Acquire",
                ["All"],
                ["Any"],
                ["Any"],
                "2 saved targets",
                [],
                [Row("Near", 10, 500), Row("Far", 30, 800)],
                null
            )
            {
                PledgedPower = "Archon Delaine",
            };
            using var model = new MiningSearchViewModel(
                new MiningSearchClient(),
                new BookmarksViewModel(directory),
                _ => { },
                () => [],
                new Resolver()
            );
            model.LoadOptions(options);
            model.PledgedPower = "Archon Delaine";
            model.Objective = "Acquire";
            cache.Save("powerplay", model.PowerplayCacheKey(), snapshot);
            model.ConfigureResultCache(new MiningSearchResultCache(directory));

            Assert.Equal("Near", model.AcquireRows[0].Target);
            Assert.Equal(2, model.RingAcquireClusters.Count);
            model.AcquireBestStationSortCommand.Execute(null);
            Assert.Equal("Far", model.AcquireRows[0].Target);
            model.AcquireBestStationSortCommand.Execute(null);
            Assert.Equal("Near", model.AcquireRows[0].Target);
            model.AcquireDistanceSortCommand.Execute(null);
            Assert.Equal("Far", model.AcquireRows[0].Target);
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
        Assert.Equal("Aisling Duval 42%", Assert.Single(model.PlanetarySearch.Rows).PowerLines[0].Display);
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
        Assert.Equal("Jerome Archer 67%", Assert.Single(model.PlanetarySearch.Rows).PowerLines[0].Display);
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
        Assert.Equal("18 ly", Assert.Single(model.PlanetarySearch.Rows).Distance);
        Assert.Equal("↑", model.PlanetarySearch.SellDistanceSortIndicator);
        Assert.Equal(["Near Target"], handler.BodyReferences);
        Assert.All(
            Assert.Single(model.PlanetarySearch.Rows).Systems,
            system => Assert.Equal("Supporter", system.System)
        );
        Assert.Equal(["Supporter"], handler.RequestedBodySystems);
        Assert.Equal(30, handler.RequestedBodyRadius);
        Assert.Contains(("Supporter", 20d), handler.AcquisitionTargetQueries);
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
        Assert.Contains(("Stronghold Source", 30d), handler.AcquisitionTargetQueries);
    }

    [Fact]
    public async Task PlanetaryAcquireContinuesPastUnpricedTargetAroundDistantSupporters()
    {
        using var handler = new DistantPlanetaryAcquireHandler();
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(new HttpClient(handler)),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Timbalderis",
            Radius = 1,
            PledgedPower = "Aisling Duval",
            Objective = "Acquire",
            ResultLimit = 2,
        };
        model.MiningTypeChips.Add(PlanetaryMiningPlan.MiningType);
        model.MineralChips.Add("Monazite");

        await model.SearchSystemsAsync();

        Assert.Equal("Good Target", Assert.Single(model.PlanetarySearch.Rows).Target);
        Assert.Equal("810 ly", Assert.Single(model.PlanetarySearch.Rows).Distance);
        Assert.Equal(["Empty Target", "Good Target", "Third Target"], handler.ImportSystems);
        Assert.Equal(1, handler.BodyReferences.Count(system => system == "Good Target"));
        Assert.False(handler.SawGalaxyMarket);
        Assert.True(handler.SawGlobalSupporterQuery);
        Assert.True(handler.Events.IndexOf("Bubble Fortress B") < handler.Events.IndexOf("Import Empty Target"));
    }

    [Fact]
    public async Task PlanetaryAcquireUsesSpanshShortNameForArissaWithoutAReferenceRadius()
    {
        using var handler = new DistantPlanetaryAcquireHandler { UseArissaAlias = true };
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(new HttpClient(handler)),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Timbalderis",
            Radius = 1,
            PledgedPower = "Arissa Lavigny-Duval",
            Objective = "Acquire",
            ResultLimit = 1,
        };
        model.MiningTypeChips.Add(PlanetaryMiningPlan.MiningType);
        model.MineralChips.Add("Monazite");

        await model.SearchSystemsAsync();

        Assert.Equal("Good Target", Assert.Single(model.PlanetarySearch.Rows).Target);
        Assert.True(handler.SawGlobalSupporterQuery);
    }

    [Fact]
    public async Task PlanetaryAcquireAnySearchesEverySurfaceMaterialInsteadOfTheRingMineral()
    {
        using var handler = new DistantPlanetaryAcquireHandler { IncludeMonaziteVolcanism = true };
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
            Mineral = "Platinum",
            ResultLimit = 1,
        };
        model.MiningTypeChips.Add(PlanetaryMiningPlan.MiningType);
        model.MineralChips.Add("Any");

        await model.SearchSystemsAsync();

        Assert.True(model.PlanetarySearch.Rows.Count > 0, model.Status);
        Assert.Equal("Good Target", Assert.Single(model.PlanetarySearch.Rows).Target);
        Assert.Contains("Any surface material", model.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanetaryAcquireKeepsFoundRowsWhenASecondBodySearchFails()
    {
        using var handler = new DistantPlanetaryAcquireHandler { FailLaterBodySearch = true };
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
            ResultLimit = 2,
        };
        model.MiningTypeChips.Add(PlanetaryMiningPlan.MiningType);
        model.MineralChips.Add("Monazite");

        await model.SearchSystemsAsync();

        Assert.Equal("Good Target", Assert.Single(model.PlanetarySearch.Rows).Target);
        Assert.Contains("partial results", model.Status, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class DistantPlanetaryAcquireHandler : HttpMessageHandler
    {
        public bool UseArissaAlias { get; init; }
        public bool FailLaterBodySearch { get; init; }
        public bool IncludeMonaziteVolcanism { get; init; }
        public List<string> ImportSystems { get; } = [];
        public List<string> Events { get; } = [];
        public List<string> BodyReferences { get; } = [];
        public bool SawGalaxyMarket { get; private set; }
        public bool SawGlobalSupporterQuery { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/systems/search")
            {
                using var body = System.Text.Json.JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                System.Text.Json.JsonElement filters = body.RootElement.GetProperty("filters");
                string reference = body.RootElement.GetProperty("reference_system").GetString() ?? "";
                if (filters.TryGetProperty("name", out _))
                {
                    return Json(
                        reference == "Timbalderis"
                            ? """{"results":[{"name":"Empty Target","distance":710,"power_state":"Unoccupied"},{"name":"Good Target","distance":810,"power_state":"Unoccupied"},{"name":"Third Target","distance":910,"power_state":"Unoccupied"}]}"""
                            : """{"results":[]}"""
                    );
                }

                string state = filters.TryGetProperty("power_state", out System.Text.Json.JsonElement power)
                    ? power.GetProperty("value")[0].GetString() ?? ""
                    : "";
                if (filters.TryGetProperty("controlling_power", out _) && state == "Fortified")
                {
                    SawGlobalSupporterQuery = !filters.TryGetProperty("distance", out _);
                    string filteredPower =
                        filters.GetProperty("controlling_power").GetProperty("value")[0].GetString() ?? "";
                    if (UseArissaAlias && filteredPower != "A. Lavigny-Duval")
                    {
                        return Json("""{"results":[]}""");
                    }

                    string response =
                        """{"results":[{"name":"Fortress A","distance":700,"x":700,"y":0,"z":0,"controlling_power":"Aisling Duval","power_state":"Fortified"},{"name":"Fortress B","distance":800,"x":800,"y":0,"z":0,"controlling_power":"Aisling Duval","power_state":"Fortified"},{"name":"Fortress C","distance":900,"x":900,"y":0,"z":0,"controlling_power":"Aisling Duval","power_state":"Fortified"}]}""";
                    return Json(
                        UseArissaAlias
                            ? response.Replace("Aisling Duval", "A. Lavigny-Duval", StringComparison.Ordinal)
                            : response
                    );
                }

                if (state == "Unoccupied")
                {
                    Events.Add("Bubble " + reference);
                    return Json(
                        reference switch
                        {
                            "Fortress A" =>
                                """{"results":[{"name":"Empty Target","distance":10,"x":710,"y":0,"z":0,"power_state":"Unoccupied"}]}""",
                            "Fortress B" =>
                                """{"results":[{"name":"Good Target","distance":10,"x":810,"y":0,"z":0,"power_state":"Unoccupied"}]}""",
                            _ =>
                                """{"results":[{"name":"Third Target","distance":10,"x":910,"y":0,"z":0,"power_state":"Unoccupied"}]}""",
                        }
                    );
                }

                if (reference is "Empty Target" or "Good Target")
                {
                    return Json(
                        """{"results":[{"name":"Supporter","distance":10,"controlling_power":"Aisling Duval","power_state":"Fortified"}]}"""
                    );
                }

                return Json("""{"results":[]}""");
            }

            if (path == "/api/bodies/search")
            {
                using var body = System.Text.Json.JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                string reference = body.RootElement.GetProperty("reference_system").GetString() ?? "";
                BodyReferences.Add(reference);
                if (FailLaterBodySearch && reference == "Third Target")
                {
                    return new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway);
                }

                return Json(
                    IncludeMonaziteVolcanism
                        ? """{"results":[{"name":"Fortress B 1","system_name":"Fortress B","subtype":"Rocky body","volcanism_type":"Metallic Magma","distance":10}]}"""
                        : """{"results":[{"name":"Fortress B 1","system_name":"Fortress B","subtype":"Rocky body","distance":10}]}"""
                );
            }

            if (path.EndsWith("/commodities/imports", StringComparison.Ordinal))
            {
                string system = path switch
                {
                    var name when name.Contains("Good%20Target", StringComparison.Ordinal) => "Good Target",
                    var name when name.Contains("Third%20Target", StringComparison.Ordinal) => "Third Target",
                    _ => "Empty Target",
                };
                ImportSystems.Add(system);
                Events.Add("Import " + system);
                return Json(
                    system is "Good Target" or "Third Target"
                        ? $$"""[{"systemName":"{{system}}","stationName":"Good Port","stationType":"Coriolis","maxLandingPadSize":3,"commodityName":"Monazite","sellPrice":400000,"demand":1000,"updatedAt":"{{DateTimeOffset.UtcNow:O}}"}]"""
                        : "[]"
                );
            }

            if (path.StartsWith("/v2/commodity/name/", StringComparison.Ordinal))
            {
                SawGalaxyMarket = true;
            }

            return Json(path.StartsWith("/api/", StringComparison.Ordinal) ? """{"results":[]}""" : "[]");
        }

        private static HttpResponseMessage Json(string value) =>
            new(System.Net.HttpStatusCode.OK) { Content = new StringContent(value) };
    }

    [Fact]
    public async Task PlanetaryPowerplayRanksViableStationsInsideOneSellSystem()
    {
        using var handler = new PlanetaryFilterHandler { MultipleStations = true, IncludeCarrier = true };
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(new HttpClient(handler)),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Sol",
        };
        model.MiningTypeChips.Add(PlanetaryMiningPlan.MiningType);
        model.MineralChips.Add("Periclase Dunite");

        await model.SearchSystemsAsync();

        SurfaceSellRowViewModel row = Assert.Single(model.PlanetarySearch.Rows);
        Assert.True(model.PlanetarySearch.ExcludeCarrierMarkets);
        Assert.Equal(["High Port", "Middle Port", "Low Port"], row.Stations.Select(station => station.Name));
        Assert.Equal("High Port", Assert.Single(row.VisibleStations).Name);
        Assert.Equal(550_000, row.BestViablePrice);
        Assert.Equal(3, row.StationRanking.StationCount);
        row.ToggleStationsCommand.Execute(null);
        Assert.Equal(3, row.VisibleStations.Count);
        row.ToggleStationsCommand.Execute(null);
        Assert.Single(row.VisibleStations);
    }

    [Fact]
    public void PlanetaryMiningReplacesDefaultMineralWithAnyAndBlocksDefaultSelection()
    {
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        );
        Assert.Equal([MiningMaterialSelection.Default], model.MineralChips.Selected);

        model.MiningTypeChips.Add(PlanetaryMiningPlan.MiningType);

        Assert.Equal([MiningMaterialSelection.Any], model.MineralChips.Selected);
        Assert.DoesNotContain(MiningMaterialSelection.Default, model.MineralChips.Suggestions);
        model.MineralChips.Add(MiningMaterialSelection.Default);
        Assert.Equal([MiningMaterialSelection.Any], model.MineralChips.Selected);
        model.MiningTypeChips.Add("All");
        Assert.Equal([MiningMaterialSelection.Any], model.MineralChips.Selected);
        Assert.Contains(MiningMaterialSelection.Default, model.MineralChips.Suggestions);
    }

    [Fact]
    public void AcquireDisablesRadiusAndCapsResultsAtTen()
    {
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        );
        model.ResultLimit = 100;
        Assert.Equal(30, model.ResultLimit);
        model.PledgedPower = "Aisling Duval";
        model.MiningTypeChips.Add(PlanetaryMiningPlan.MiningType);
        model.Objective = "Acquire";
        Assert.False(model.CanChooseDistance);
        Assert.True(model.IsPlanetaryAcquire);
        Assert.Equal(10, model.MaximumResultLimit);
        Assert.Equal(10, model.ResultLimit);
        model.ResultLimit = 100;
        Assert.Equal(10, model.ResultLimit);
        model.Objective = "Reinforce";
        Assert.True(model.CanChooseDistance);
        Assert.Equal(30, model.MaximumResultLimit);
        model.Objective = "Acquire";
        model.PledgedPower = "Any";
        Assert.Equal("Reinforce", model.Objective);
        Assert.True(model.IsPlanetaryCombined);
    }

    private sealed class PlanetaryFilterHandler : HttpMessageHandler
    {
        public string? BodyFilters { get; private set; }
        public bool PagedBodies { get; init; }
        public bool MultipleSellSystems { get; init; }
        public bool AcquireSellSystems { get; init; }
        public bool StrongholdReach { get; init; }
        public bool MultipleStations { get; init; }
        public bool IncludeCarrier { get; init; }
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
                                ? """{"results":[{"name":"Near Target","distance":25,"power_state":"Unoccupied","x":25,"y":0,"z":0}]}"""
                                : """{"results":[{"name":"Near Target","distance":18,"power_state":"Unoccupied","x":18,"y":0,"z":0},{"name":"Far Target","distance":31,"power_state":"Unoccupied","x":31,"y":0,"z":0}]}"""
                        );
                    }

                    if (!filters.TryGetProperty("power_state", out _))
                    {
                        string target = search.RootElement.GetProperty("reference_system").GetString() ?? "";
                        AcquisitionTargetQueries.Add(
                            (target, filters.GetProperty("distance").GetProperty("max").GetDouble())
                        );
                        if (target != "Near Target")
                        {
                            return Json("""{"results":[]}""");
                        }

                        return Json(
                            StrongholdReach
                                ? """{"results":[{"name":"Stronghold Source","distance":25,"controlling_power":"Aisling Duval","power_state":"Stronghold","x":50,"y":0,"z":0}]}"""
                                : """{"results":[{"name":"Supporter","distance":18,"controlling_power":"Aisling Duval","power_state":"Fortified","x":0,"y":0,"z":0}]}"""
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
                        ? """{"results":[{"name":"Own Sell","controlling_power":"Aisling Duval","power":["Felicia Winters","Aisling Duval"],"power_state":"Fortified","power_state_control_progress":0.42},{"name":"Other Sell","controlling_power":"Jerome Archer","power":["Felicia Winters","Jerome Archer"],"power_state":"Fortified","power_state_control_progress":0.67}]}"""
                        : """{"results":[{"name":"Sell System","power_state":"Stronghold"}]}"""
                );
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/imports", StringComparison.Ordinal) == true)
            {
                string updated = DateTimeOffset.UtcNow.ToString("O");
                if (AcquireSellSystems)
                {
                    return Json(
                        StrongholdReach
                            ? $$"""[{"systemName":"Near Target","stationName":"Near Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":400000,"demand":1000,"stock":0,"updatedAt":"{{updated}}","distance":999,"distanceToArrival":200,"marketId":1,"commodityName":"Monazite"}]"""
                            : $$"""[{"systemName":"Near Target","stationName":"Near Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":400000,"demand":1000,"stock":0,"updatedAt":"{{updated}}","distance":999,"distanceToArrival":200,"marketId":1,"commodityName":"Monazite"},{"systemName":"Far Target","stationName":"Far Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":900000,"demand":1000,"stock":0,"updatedAt":"{{updated}}","distance":999,"distanceToArrival":100,"marketId":2,"commodityName":"Monazite"}]"""
                    );
                }

                if (MultipleSellSystems)
                {
                    return Json(
                        $$"""[{"systemName":"Own Sell","stationName":"Own Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":400000,"demand":1000,"stock":0,"updatedAt":"{{updated}}","distance":12,"distanceToArrival":200,"marketId":1,"commodityName":"Monazite"},{"systemName":"Other Sell","stationName":"Other Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":900000,"demand":1000,"stock":0,"updatedAt":"{{updated}}","distance":15,"distanceToArrival":100,"marketId":2,"commodityName":"Monazite"}]"""
                    );
                }
                if (MultipleStations)
                {
                    return Json(
                        $$"""[{"systemName":"Sell System","stationName":"Low Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":400000,"demand":1000,"stock":0,"updatedAt":"{{updated}}","distance":12,"distanceToArrival":100,"marketId":1,"commodityName":"Periclase Dunite"},{"systemName":"Sell System","stationName":"High Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":550000,"demand":1000,"stock":0,"updatedAt":"{{updated}}","distance":12,"distanceToArrival":200,"marketId":2,"commodityName":"Periclase Dunite"},{"systemName":"Sell System","stationName":"Middle Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":450000,"demand":1000,"stock":0,"updatedAt":"{{updated}}","distance":12,"distanceToArrival":300,"marketId":3,"commodityName":"Periclase Dunite"}{{(IncludeCarrier ? $$""",{"systemName":"Sell System","stationName":"B5W-6VH","stationType":"Fleet Carrier","maxLandingPadSize":3,"sellPrice":900000,"demand":1000,"stock":0,"updatedAt":"{{updated}}","distance":12,"distanceToArrival":10,"marketId":4,"commodityName":"Periclase Dunite"}""" : "")}}]"""
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
                                .Range(0, 500)
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
                                    distance = 0,
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
        Assert.Equal(10, model.ResultLimit);
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
        using var http = new HttpClient(new AcquisitionRangeHandler { HasSpanshMarket = true });
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
            Mineral = "Any",
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
    public async Task AcquireDoesNotPresentEmptyStationAndRingCells()
    {
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(new HttpClient(new AcquisitionRangeHandler())),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Sol",
            PledgedPower = "Archon Delaine",
            Objective = "Acquire",
            Mineral = "Any",
        };

        await model.SearchSystemsAsync();

        Assert.Empty(model.AcquireRows);
    }

    [Fact]
    public async Task AcquireUsesSpanshStationFallbackAfterEmptyArdentImports()
    {
        using var handler = new AcquisitionRangeHandler { HasSpanshMarket = true };
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
            Mineral = "Any",
        };

        await model.SearchSystemsAsync();

        AcquireResultRowViewModel row = Assert.Single(model.AcquireRows);
        Assert.Equal("Fallback Port (L)", Assert.Single(row.StationBlocks).Heading);
        Assert.Equal("Monazite", Assert.Single(row.StationBlocks).Commodity);
        Assert.Equal("RingRocky", Assert.Single(Assert.Single(row.Miners).RingLines).RingTypeIcon);
        Assert.True(handler.SpanshMarketRequested);
    }

    [Fact]
    public async Task AcquireUsesTheNextCandidateWhenTheNearestHasNoSellStation()
    {
        using var handler = new AcquisitionRangeHandler { HasSpanshMarket = true, SecondTargetHasMarket = true };
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
            Mineral = "Any",
            ResultLimit = 1,
        };

        await model.SearchSystemsAsync();

        AcquireResultRowViewModel row = Assert.Single(model.AcquireRows);
        Assert.Equal("Claim 2", row.Target);
        Assert.NotEmpty(row.StationBlocks);
        Assert.NotEmpty(Assert.Single(row.Miners).RingLines);
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
    public async Task AcquireFindsSupportersBeyondTheConfiguredDistance()
    {
        using var handler = new AcquisitionRangeHandler { DistantSupporter = true };
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
            Radius = 1,
        };

        await model.SearchSystemsAsync();

        Assert.True(handler.SawGlobalSupporterQuery);
        Assert.True(handler.SawGlobalBodyQuery);
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
        public bool HasSpanshMarket { get; init; }
        public bool SecondTargetHasMarket { get; init; }
        public bool SpanshMarketRequested { get; private set; }
        public bool PagedBubble { get; init; }
        public bool DistantSupporter { get; init; }
        public bool SawGlobalSupporterQuery { get; private set; }
        public bool SawGlobalBodyQuery { get; private set; }
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
            if (request.RequestUri?.AbsolutePath == "/api/bodies/search")
            {
                SawGlobalBodyQuery = !body.RootElement.GetProperty("filters").TryGetProperty("distance", out _);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        HasSpanshMarket
                            ? """{"results":[{"system_name":"Anchor","name":"Anchor 1","rings":[{"name":"Anchor 1 A Ring","type":"Rocky","reserve_level":"Pristine","signals":[{"name":"Monazite","count":2}]}]}]}"""
                            : """{"results":[]}"""
                    ),
                };
            }
            if (request.RequestUri?.AbsolutePath == "/api/stations/search" && HasSpanshMarket)
            {
                SpanshMarketRequested = true;
                string marketSystem = SecondTargetHasMarket ? "Claim 2" : "Claim";
                string market =
                    $$"""{"results":[{"system_name":"{{marketSystem}}","name":"Fallback Port","type":"Coriolis Starport","large_pads":1,"market_updated_at":"{{DateTimeOffset.UtcNow:O}}","market":[{"commodity":"Monazite","sell_price":600000,"demand":1000}]}]}""";
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(market) };
            }

            string reference = body.RootElement.GetProperty("reference_system").GetString() ?? "";
            int page = body.RootElement.GetProperty("page").GetInt32();
            string state = body
                .RootElement.GetProperty("filters")
                .TryGetProperty("power_state", out System.Text.Json.JsonElement powerState)
                ? powerState.GetProperty("value")[0].GetString() ?? ""
                : "";
            if (state == "Fortified")
            {
                SawGlobalSupporterQuery = !body.RootElement.GetProperty("filters").TryGetProperty("distance", out _);
            }
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
                (_, "Fortified") when DistantSupporter =>
                    """{"results":[{"name":"Anchor","distance":700,"x":700,"y":0,"z":0,"controlling_power":"Archon Delaine","power_state":"Fortified"}]}""",
                (_, "Fortified") =>
                    """{"results":[{"name":"Anchor","distance":0,"x":0,"y":0,"z":0,"controlling_power":"Archon Delaine","power_state":"Fortified"}]}""",
                (_, "Stronghold") => """{"results":[]}""",
                ("Anchor", _) when DistantSupporter =>
                    """{"results":[{"name":"Claim","distance":10,"x":710,"y":0,"z":0,"power_state":"Unoccupied"}]}""",
                ("Anchor", _) when SecondTargetHasMarket =>
                    """{"results":[{"name":"Claim","distance":10,"x":10,"y":0,"z":0,"power_state":"Unoccupied"},{"name":"Claim 2","distance":11,"x":11,"y":0,"z":0,"power_state":"Unoccupied"}]}""",
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

    [Fact]
    public void DistanceWarningEscalatesAndAcquireDoesNotWarnAboutAnUnusedRadius()
    {
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        );
        Assert.False(model.HasDistanceWarning);
        foreach (int radius in new[] { 101, 200, 300, 400, 500 })
        {
            model.Radius = radius;
            Assert.Contains(
                radius == 101 ? "100" : radius.ToString(System.Globalization.CultureInfo.InvariantCulture),
                model.DistanceWarning
            );
        }

        model.PledgedPower = "Aisling Duval";
        model.Objective = "Acquire";
        Assert.False(model.HasDistanceWarning);
    }

    [Fact]
    public async Task PlanetaryMarketCategorySearchesEverySelectedCommodityAndKeepsBothStationQuotes()
    {
        using var handler = new MultiCommodityMarketHandler();
        using var model = new MiningSearchViewModel(
            new MiningSearchClient(new HttpClient(handler)),
            new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            _ => { },
            () => [],
            new Resolver()
        )
        {
            Reference = "Timbalderis",
            MarketPadSize = "L",
            MarketMinimumVolume = 100,
            MarketMaximumVolume = 1_000,
            MarketMaximumAgeDays = 7,
            MaximumDemand = 1,
        };
        model.MarketCategoryChips.Add("Planetary Mining");
        Assert.Contains("Periclase Dunite", model.MarketCommodityChips.Suggestions);
        model.MarketCommodityChips.Remove("Platinum");
        model.MarketCommodityChips.Add("Periclase Dunite");
        model.MarketCommodityChips.Add("Monazite");

        await model.SearchMarketsAsync();

        Assert.Equal(2, model.Markets.Count);
        Assert.Equal(["Periclase Dunite", "Monazite"], model.Markets.Select(market => market.Commodity).ToArray());
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, uri => Assert.Contains("minVolume=100", uri.Query));
        Assert.All(handler.Requests, uri => Assert.Contains("maxDaysAgo=7", uri.Query));
        Assert.Equal(["Periclase Dunite", "Monazite"], model.SaveOptions().MarketCommodities);
    }

    private sealed class MultiCommodityMarketHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Uri uri = request.RequestUri!;
            Requests.Add(uri);
            bool periclase = uri.AbsolutePath.Contains("periclasedunite", StringComparison.Ordinal);
            string commodity = periclase ? "periclasedunite" : "monazite";
            int price = periclase ? 500_000 : 400_000;
            string payload =
                $$"""[{"systemName":"Timbalderis","stationName":"Shared Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":{{price}},"demand":500,"updatedAt":"{{DateTimeOffset.UtcNow:O}}","distance":0,"commodityName":"{{commodity}}"}]""";
            return Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(payload) }
            );
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
