using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SurfaceMiningSearchViewModelTests
{
    private const string Material = "Diamond";

    [Fact]
    public async Task CompletedSearchAndDisplayPreferenceReturnWithoutProviderRequests()
    {
        string directory = Path.Combine(Path.GetTempPath(), "srv-mining-cache-" + Guid.NewGuid());
        try
        {
            var cache = new MiningSearchResultCache(directory);
            using var firstHandler = new SurfaceHandler();
            using (SurfaceMiningSearchViewModel first = Create(firstHandler))
            {
                first.ConfigureCache(cache);
                first.Reference = "Sol";
                first.Radius = 40;
                first.Materials.Add(Material);
                await first.SearchAsync();
                Assert.Single(first.Rows);
                first.HideIrrelevantMaterialTags = true;
            }

            using var secondHandler = new SurfaceHandler { Mode = "fail" };
            using SurfaceMiningSearchViewModel restored = Create(secondHandler);
            restored.ConfigureCache(cache);
            Assert.Single(restored.Rows);
            Assert.Equal("Sol", restored.Reference);
            Assert.True(restored.HideIrrelevantMaterialTags);
            Assert.Equal(0, secondHandler.Requests);

            restored.Radius = 30;
            Assert.Empty(restored.Rows);
            restored.Radius = 40;
            Assert.Single(restored.Rows);
            Assert.Equal(0, secondHandler.Requests);

            restored.UpdateCurrentLocation("Timbalderis");
            restored.Reset();
            Assert.Equal("Timbalderis", restored.Reference);
            Assert.Equal(100, restored.Radius);
            Assert.Equal(90_000, restored.MaximumDemand);
            Assert.Empty(restored.Materials.Selected);
            Assert.Empty(restored.Rows);
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
    public void IrrelevantTagsCanHideWithoutChangingStationMatchedTags()
    {
        var faded = new SurfaceBodyTag("DIA", false);
        var matched = new SurfaceBodyTag("MON", true);
        faded.SetHideIrrelevant(true);
        matched.SetHideIrrelevant(true);
        Assert.False(faded.IsVisible);
        Assert.True(matched.IsVisible);
        faded.SetHideIrrelevant(false);
        Assert.True(faded.IsVisible);
    }

    [Fact]
    public void AcquireClustersShareMiningSystemsWithoutSharingStationAvailability()
    {
        SurfaceSellRowViewModel first = ClusterSellRow(
            "First Sell",
            [ClusterMiningRow("Alpha", "IRI"), ClusterMiningRow("Terminus", "IRI")],
            [
                new AcquireQuoteViewModel("IRI", "400,000 CR", "100 Demand"),
                new AcquireQuoteViewModel("MON", "300,000 CR", "100 Demand", true),
            ]
        );
        SurfaceSellRowViewModel second = ClusterSellRow(
            "Second Sell",
            [ClusterMiningRow("Terminus", "MON"), ClusterMiningRow("Beta", "MON")],
            [new AcquireQuoteViewModel("MON", "350,000 CR", "100 Demand")]
        );
        SurfaceSellRowViewModel third = ClusterSellRow(
            "Separate Sell",
            [ClusterMiningRow("Gamma", "DIA")],
            [new AcquireQuoteViewModel("DIA", "250,000 CR", "100 Demand")]
        );

        IReadOnlyList<PowerplayAcquireClusterViewModel> clusters = PowerplayAcquireClusterViewModel.Group([
            first,
            second,
            third,
        ]);

        Assert.Equal(2, clusters.Count);
        PowerplayAcquireClusterViewModel shared = clusters[0];
        Assert.Equal(["First Sell", "Second Sell"], shared.SellNodes.Select(node => node.Row.Target));
        Assert.Equal(["Alpha", "Terminus", "Beta"], shared.MiningSystems.Select(node => node.System));
        PowerplayAcquireMiningNode terminus = Assert.Single(shared.MiningSystems, node => node.System == "Terminus");
        Assert.Equal(2, terminus.SellCount);
        Assert.True(Assert.Single(first.Stations[0].Quotes, quote => quote.Code == "MON").IsUnavailable);
        Assert.False(Assert.Single(terminus.Bodies[0].Tags, tag => tag.Code == "MON").MatchesStation);

        shared.SellNodes[1].SelectCommand.Execute(null);

        Assert.True(Assert.Single(terminus.Bodies[0].Tags, tag => tag.Code == "MON").MatchesStation);
        shared.SetHideIrrelevant(true);
        Assert.False(Assert.Single(terminus.Bodies[0].Tags, tag => tag.Code == "IRI").IsVisible);
        Assert.True(Assert.Single(terminus.Bodies[0].Tags, tag => tag.Code == "MON").IsVisible);
        Assert.True(Assert.Single(first.Stations[0].Quotes, quote => quote.Code == "MON").IsUnavailable);
        Assert.True(shared.SellNodes[1].IsSelected);
        Assert.True(clusters[1].IsSingle);
    }

    [Fact]
    public void AcquireClusterKeepsEverySellSystemLinkedWhenFiveMiningRowsCanCoverThem()
    {
        SurfaceSellRowViewModel[] sells = Enumerable
            .Range(0, 10)
            .Select(index =>
                ClusterSellRow(
                    $"Sell {index}",
                    Enumerable
                        .Range(Math.Max(0, index - 1), index is 0 or 9 ? 1 : 2)
                        .Select(edge => ClusterMiningRow($"Shared {edge}", "IRI"))
                        .ToArray(),
                    [new AcquireQuoteViewModel("IRI", "400,000 CR", "100 Demand")]
                )
            )
            .ToArray();

        PowerplayAcquireClusterViewModel cluster = Assert.Single(PowerplayAcquireClusterViewModel.Group(sells));

        Assert.Equal(5, cluster.VisibleMiningSystems.Count);
        Assert.All(
            sells,
            sell => Assert.Contains(cluster.VisibleMiningSystems, system => system.ConnectsTo(sell.Target))
        );
    }

    private static SurfaceMiningSystemRowViewModel ClusterMiningRow(string system, string code) =>
        new(system, 10, [new SurfaceBodyLine([code], "A 1: Rocky body", new HashSet<string>([code]))]);

    private static SurfaceSellRowViewModel ClusterSellRow(
        string system,
        IReadOnlyList<SurfaceMiningSystemRowViewModel> mining,
        IReadOnlyList<AcquireQuoteViewModel> quotes
    ) =>
        new(
            system,
            "10 ly",
            [new AcquireStationViewModel(system + " Port", "Large", "", "", quotes)],
            mining,
            10,
            400_000
        );

    [Fact]
    public void PowerplayCommodityBadgesUseMaterialColorsAcrossTheEdpmList()
    {
        Assert.Equal("#B87333", new MeritCommodityLineViewModel("COP", "", "").ColorHex);
        Assert.Equal("#C0C0C0", new MeritCommodityLineViewModel("SIL", "", "").ColorHex);
        Assert.Equal("#2E8B57", new MeritCommodityLineViewModel("MON", "", "").ColorHex);
        Assert.Equal("#7D5894", new MeritCommodityLineViewModel("MUS", "", "").ColorHex);
        Assert.All(
            PlanetaryMiningPlan.EdpmCommodityNames,
            commodity =>
            {
                string code = MiningCommodityCode.Abbreviate(commodity);
                Assert.NotEqual("#E6D59A", new MeritCommodityLineViewModel(code, "", "").ColorHex);
            }
        );
    }

    [Fact]
    public async Task EmptyReferenceAndUnknownMaterialDoNotSearch()
    {
        using var handler = new SurfaceHandler();
        using SurfaceMiningSearchViewModel model = Create(handler);
        Assert.Empty(model.Materials.Selected);
        Assert.False(model.ExcludeCarrierMarkets);
        Assert.DoesNotContain("Default", model.Materials.Suggestions);
        Assert.Equal(0, model.MinimumDemand);
        Assert.Equal(90_000, model.MaximumDemand);

        await model.SearchAsync();

        Assert.Equal("Choose a reference system and a surface material.", model.Status);
        Assert.False(model.HasRows);
        model.Reference = "Sol";
        model.Materials.Selected.Clear();
        model.Materials.Selected.Add("Nope");

        await model.SearchAsync();

        Assert.Equal("Choose a surface material.", model.Status);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task SearchGroupsBodiesUnderTheBestSellAndCanResort()
    {
        using var handler = new SurfaceHandler();
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "  Sol  ";
        model.Radius = 40;
        model.Materials.Add(Material);

        await model.SearchAsync();

        SurfaceSellRowViewModel row = Assert.Single(model.Rows);
        Assert.Equal("Sell System", row.Target);
        Assert.Equal("12 ly", row.Distance);
        Assert.Equal("Sell System", handler.BodyReference);
        Assert.Equal(["Alpha", "Beta", "Gamma"], row.Systems.Select(system => system.System).ToArray());
        Assert.Equal(["4.2 ly", "9.5 ly", ""], row.Systems.Select(system => system.Distance).ToArray());
        Assert.Equal("↑", model.DistanceSortIndicator);
        Assert.DoesNotContain("reserve", row.Systems[0].Bodies[0].Details);
        Assert.DoesNotContain("reserve", row.Systems[1].Bodies[0].Details);
        Assert.DoesNotContain(" ly", row.Systems[2].Bodies[0].Details);
        Assert.StartsWith("1: Rocky body", row.Systems[0].Bodies[0].Details);
        Assert.Equal(["DIA"], row.Systems[0].Bodies[0].Codes);
        AcquireStationViewModel station = Assert.Single(row.Stations);
        Assert.Equal("Gold Port", station.Name);
        Assert.Equal("DIA", Assert.Single(station.Quotes).Code);
        Assert.Contains("3 landable bodies for Diamond", model.Status);
        Assert.Contains("Ardent", model.Status);
        Assert.True(model.HasRows);
        Assert.Equal(0, handler.ImportRequests);

        model.DistanceSortCommand.Execute(null);

        Assert.Equal("Farthest first", model.DistanceSortLabel);
        Assert.Equal("↓", model.DistanceSortIndicator);
        Assert.Equal(
            ["Gamma", "Beta", "Alpha"],
            Assert.Single(model.Rows).Systems.Select(system => system.System).ToArray()
        );
    }

    [Fact]
    public async Task GroupedSearchKeepsTheBestCompositeStationGroup()
    {
        using var handler = new SurfaceHandler { Mode = "group-rank" };
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "Sol";
        model.Materials.Add(Material);
        model.GroupStationsBySystem = true;
        model.ResultLimit = 1;

        await model.SearchAsync();

        Assert.Equal("Steady Sell", Assert.Single(model.Rows).Target);
        Assert.Equal(3, Assert.Single(model.Rows).StationRanking.StationCount);
    }

    [Fact]
    public void CurrentSystemFillsAndRestoresTheReference()
    {
        using var handler = new SurfaceHandler();
        using SurfaceMiningSearchViewModel model = Create(handler);

        model.UpdateCurrentLocation("Timbalderis");
        Assert.Equal("Timbalderis", model.Reference);
        model.Reference = "Sol";
        model.UpdateCurrentLocation("Wille");
        Assert.Equal("Sol", model.Reference);
        model.Reference = "";
        Assert.Equal("Wille", model.Reference);
        model.UpdateCurrentLocation("Lave");
        Assert.Equal("Lave", model.Reference);
    }

    [Fact]
    public async Task DuplicateBodiesCollapseBySystemAndOnlyFiveSystemsShowInitially()
    {
        using var handler = new SurfaceHandler { Mode = "many-systems" };
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "Sol";
        model.Materials.Add(Material);

        await model.SearchAsync();

        SurfaceSellRowViewModel row = Assert.Single(model.Rows);
        Assert.Equal(7, row.Systems.Count);
        Assert.Equal(5, row.VisibleSystems.Count);
        Assert.True(row.HasAdditionalSystems);
        Assert.Equal("Show all Systems", row.ShowAllLabel);
        SurfaceMiningSystemRowViewModel first = row.VisibleSystems[0];
        Assert.Equal("Alpha", first.System);
        Assert.Equal(2, first.Bodies.Count);
        Assert.Single(first.VisibleBodies);
        Assert.Equal("▸", first.Chevron);

        first.ToggleCommand.Execute(null);
        Assert.Equal(2, first.VisibleBodies.Count);
        Assert.Equal("▾", first.Chevron);
        first.ToggleCommand.Execute(null);
        Assert.Single(first.VisibleBodies);

        row.ToggleAllCommand.Execute(null);
        Assert.Equal(7, row.VisibleSystems.Count);
        Assert.Equal("Show fewer Systems", row.ShowAllLabel);
        model.DistanceSortCommand.Execute(null);
        Assert.Equal("Eta", row.VisibleSystems[0].System);
        row.ToggleAllCommand.Execute(null);
        Assert.Equal(5, row.VisibleSystems.Count);
    }

    [Fact]
    public async Task AnyLabelsBodiesWithMaterialsMatchingTheirTypeAndGeology()
    {
        using var handler = new SurfaceHandler { Mode = "any" };
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "Sol";
        model.Materials.Add("Any");

        await model.SearchAsync();

        SurfaceSellRowViewModel row = Assert.Single(model.Rows);
        SurfaceBodyLine first = row.Systems[0].Bodies[0];
        Assert.Contains("DIA", first.Codes);
        Assert.Contains("MON", first.Codes);
        Assert.Contains("LTD", first.Codes);
        Assert.DoesNotContain("JAD", first.Codes);
        Assert.Contains(first.Tags, tag => tag.Code == "MON" && tag.IsExtraMaterial);
        Assert.Contains(first.Tags, tag => tag.Code == "DIA" && tag.MatchesStation && !tag.IsExtraMaterial);
        Assert.All(Assert.Single(row.Stations).Quotes, quote => Assert.True(quote.UseMaterialColor));
        Assert.StartsWith("1: Rocky body", first.Details);
        SurfaceBodyLine second = row.Systems[1].Bodies[0];
        Assert.DoesNotContain("MON", second.Codes);
        Assert.Contains("LTD", second.Codes);
    }

    [Fact]
    public async Task SellSystemsRankByAvailableCommodityAndMarkQuotesWithoutNearbyBodies()
    {
        using var handler = new SurfaceHandler { Mode = "ranked" };
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "Sol";
        model.ResultLimit = 2;
        model.Materials.Add("Any");

        await model.SearchAsync();

        Assert.Equal(2, model.Rows.Count);
        Assert.Equal(["Viable Sell", "High Sell"], model.Rows.Select(row => row.Target).ToArray());
        Assert.Equal([850_000L, 1_100_000L], model.Rows.Select(row => row.BestViablePrice).ToArray());
        AcquireQuoteViewModel[] highQuotes = Assert.Single(model.Rows[1].Stations).Quotes.ToArray();
        Assert.True(highQuotes.Single(quote => quote.Code == "PER").IsUnavailable);
        Assert.False(highQuotes.Single(quote => quote.Code == "GLD").IsUnavailable);
        Assert.All(
            model.Rows[1].Systems.SelectMany(system => system.Bodies),
            body => Assert.Contains(body.Tags, tag => tag.Code == "GLD" && tag.MatchesStation)
        );
        Assert.Contains(
            model.Rows[0].Systems.SelectMany(system => system.Bodies),
            body => body.Tags.Any(tag => tag.Code == "PER" && tag.MatchesStation)
        );
        Assert.Equal("#8BC34A", new SurfaceBodyTag("PER", true).ColorHex);
        Assert.Equal("#8BC34A", highQuotes.Single(quote => quote.Code == "PER").ColorHex);

        model.ResultLimit = 1;
        await model.SearchAsync();

        Assert.Equal("Viable Sell", Assert.Single(model.Rows).Target);
    }

    [Fact]
    public async Task EveryQuotedMaterialGetsAVisibleBodyEvenWhenItAppearsOnTheNextSpanshPage()
    {
        using var handler = new SurfaceHandler { Mode = "paged" };
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "Sol";
        model.Materials.Add("Any");

        await model.SearchAsync();

        SurfaceSellRowViewModel row = Assert.Single(model.Rows);
        Assert.Equal(2, handler.BodyPages);
        Assert.Equal(30, row.Systems.Sum(system => system.Bodies.Count));
        Assert.Contains(row.Systems.SelectMany(system => system.Bodies), body => body.Codes.Contains("PER"));
        Assert.All(Assert.Single(row.Stations).Quotes, quote => Assert.False(quote.IsUnavailable));
    }

    [Fact]
    public async Task LandingPadAndDemandFilterSellStationsBeforeBodySearch()
    {
        using var handler = new SurfaceHandler { Mode = "pad-demand" };
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "Sol";
        model.Materials.Add(Material);
        model.MinimumDemand = 900;
        model.MaximumDemand = 1_500;
        model.PadSize = "L";

        await model.SearchAsync();

        Assert.Equal("Large Port", Assert.Single(Assert.Single(model.Rows).Stations).Name);
        Assert.Equal("Large Sell", handler.BodyReference);

        model.PadSize = "M";
        await model.SearchAsync();

        Assert.Equal("Medium Port", Assert.Single(Assert.Single(model.Rows).Stations).Name);
        Assert.Equal("Medium Sell", handler.BodyReference);

        model.MaximumDemand = 500;
        await model.SearchAsync();

        Assert.Empty(model.Rows);
        Assert.Contains("No sell station matches", model.Status);
    }

    [Fact]
    public async Task MissingImportsAndAveragesStillKeepTheBestQuote()
    {
        using var handler = new SurfaceHandler { Mode = "imports-fail" };
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "Sol";
        model.Materials.Add(Material);
        model.Materials.Add("Gold");
        var notes = new List<string>();
        model.UseDiagnosticLog(notes.Add);

        await model.SearchAsync();

        Assert.Equal("DIA", Assert.Single(Assert.Single(Assert.Single(model.Rows).Stations).Quotes).Code);
        Assert.NotEmpty(notes);

        using SurfaceHandler averages = new() { Mode = "averages-bad" };
        using SurfaceMiningSearchViewModel priced = Create(averages);
        priced.Reference = "Sol";
        priced.Materials.Add(Material);
        await priced.SearchAsync();

        Assert.Contains("Request failed. Try again.", priced.Status);
        Assert.True(priced.HasRows);
    }

    [Fact]
    public async Task NoBodiesExplainTheEmptyResult()
    {
        using var handler = new SurfaceHandler { Mode = "no-bodies" };
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "Timbalderis";
        model.Materials.Add("Monazite");

        await model.SearchAsync();

        Assert.False(model.HasRows);
        Assert.Contains("No sell station has a matching surface mining body", model.Status);
    }

    [Fact]
    public async Task EmptyBodiesCheckEverySellSystem()
    {
        using var handler = new SurfaceHandler { Mode = "reserve-stress" };
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "Timbalderis";
        model.Materials.Add("Monazite");
        model.ResultLimit = 99;

        await model.SearchAsync();

        Assert.Equal(60, handler.BodyPages);
        Assert.Equal(0, handler.ImportRequests);
        Assert.Equal(5, model.ResultLimit);
        Assert.False(model.HasRows);
        Assert.Contains("No sell station has a matching surface mining body", model.Status);
    }

    [Fact]
    public async Task CompactArdentPericlaseNameMatchesTheSelectedSurfaceMaterial()
    {
        using var handler = new SurfaceHandler { Mode = "compact-periclase" };
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "Timbalderis";
        model.Radius = 214;
        model.MaximumDemand = 90_000;
        model.PadSize = "Any";
        model.Materials.Add("Periclase Dunite");

        await model.SearchAsync();

        SurfaceSellRowViewModel row = Assert.Single(model.Rows);
        Assert.Equal("Barnard's Star", row.Target);
        Assert.Equal("PER", Assert.Single(Assert.Single(row.Stations).Quotes).Code);
        Assert.Contains(Assert.Single(row.Systems).Bodies, body => body.Codes.Contains("PER"));
        Assert.Contains("/commodity/name/periclasedunite/nearby/imports", handler.MarketPath, StringComparison.Ordinal);
        Assert.Contains("maxDistance=214", handler.MarketQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SparseBodiesCanFindASellSystemBeyondTheOldPageBudget()
    {
        using var handler = new SurfaceHandler { Mode = "reserve-stress-positive" };
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "Timbalderis";
        model.Materials.Add("Monazite");
        model.ResultLimit = 1;

        await model.SearchAsync();

        Assert.Equal("Sell 41", Assert.Single(model.Rows).Target);
        Assert.Equal(41, handler.BodyPages);
        Assert.Equal(0, handler.ImportRequests);
    }

    [Fact]
    public async Task SellSystemEligibilityLooksUpCandidatesInBoundedBatches()
    {
        using var handler = new SurfaceHandler { Mode = "batched-eligibility" };
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "Timbalderis";
        model.Materials.Add("Monazite");
        var batches = new List<int>();
        model.EligibleSellSystemsAsync = (systems, _) =>
        {
            batches.Add(systems.Count);
            return Task.FromResult<IReadOnlySet<string>>(
                systems.Where(system => system == "Sell 60").ToHashSet(StringComparer.OrdinalIgnoreCase)
            );
        };

        await model.SearchAsync();

        Assert.Equal([50, 10], batches);
        Assert.Equal("Sell 60", Assert.Single(model.Rows).Target);
        Assert.Equal(1, handler.BodyPages);
    }

    [Fact]
    public async Task SurfaceSearchNeverSendsReserveFilterAtFiveHundredLightYears()
    {
        using var handler = new SurfaceHandler { Mode = "reserve-stress" };
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "Timbalderis";
        model.Radius = 500;
        model.Materials.Add("Monazite");

        await model.SearchAsync();

        Assert.Empty(model.Rows);
        Assert.Equal(60, handler.BodyPages);
        Assert.True(handler.FirstBodyRequestHadDistance);
        Assert.False(handler.FirstBodyRequestHadReserve);
        Assert.Equal(0, handler.ImportRequests);
        Assert.Contains("No sell station has a matching surface mining body", model.Status);
    }

    [Fact]
    public async Task ProviderFailureAndCancellationUpdateStatus()
    {
        using var handler = new SurfaceHandler { Mode = "fail" };
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "Sol";
        model.Materials.Add(Material);

        await model.SearchAsync();

        Assert.Equal("Request failed. Try again.", model.Status);
        Assert.False(model.HasRows);

        handler.Mode = "delay";
        Task search = model.SearchAsync();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        model.Cancel();
        await search;

        Assert.Equal("Search canceled.", model.Status);
        Assert.False(model.IsBusy);
        model.Dispose();
    }

    private static SurfaceMiningSearchViewModel Create(SurfaceHandler handler) =>
        new(new SrvSurvey.Core.Search.MiningSearchClient(new HttpClient(handler)));

    private sealed class SurfaceHandler : HttpMessageHandler
    {
        public string Mode { get; set; } = "ok";
        public int Requests { get; private set; }
        public string BodyReference { get; private set; } = "";
        public int BodyPages { get; private set; }
        public bool FirstBodyRequestHadDistance { get; private set; }
        public bool FirstBodyRequestHadReserve { get; private set; }
        public int ImportRequests { get; private set; }
        public string MarketPath { get; private set; } = "";
        public string MarketQuery { get; private set; } = "";
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests++;
            string path = request.RequestUri!.AbsolutePath;
            if (Mode == "delay")
            {
                Started.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (Mode == "fail" && path.Contains("bodies/search", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            if (path.Contains("bodies/search", StringComparison.Ordinal))
            {
                using var bodyRequest = System.Text.Json.JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                BodyReference = bodyRequest.RootElement.GetProperty("reference_system").GetString() ?? "";
                if (BodyPages == 0)
                {
                    FirstBodyRequestHadDistance = bodyRequest
                        .RootElement.GetProperty("filters")
                        .TryGetProperty("distance", out _);
                    FirstBodyRequestHadReserve = bodyRequest
                        .RootElement.GetProperty("filters")
                        .TryGetProperty("reserve_level", out _);
                }
                BodyPages++;
                if (Mode == "paged")
                {
                    int page = bodyRequest.RootElement.GetProperty("page").GetInt32();
                    if (page == 1)
                    {
                        return Json(
                            """{"results":[{"name":"Periclase Miner 1","system_name":"Periclase Miner","subtype":"Rocky body","distance":99,"volcanism_type":"Minor Metallic Magma","parents":[{"id64":42,"type":"Star","subtype":"White Dwarf (DA) Star"}]}]}"""
                        );
                    }

                    return Json(
                        JsonSerializer.Serialize(
                            new
                            {
                                results = Enumerable
                                    .Range(1, 100)
                                    .Select(index => new
                                    {
                                        name = $"Gold Miner {index}",
                                        system_name = "Gold Miner",
                                        subtype = "Rocky body",
                                        distance = index * 0.5,
                                    }),
                            }
                        )
                    );
                }
                if (
                    Mode == "reserve-stress-positive" && BodyReference == "Sell 41"
                    || Mode == "batched-eligibility" && BodyReference == "Sell 60"
                )
                {
                    return Json(
                        """{"results":[{"name":"Viable Somewhere 1","system_name":"Viable Somewhere","subtype":"Rocky body","reserve_level":"Pristine","distance":50}]}"""
                    );
                }

                if (Mode is "no-bodies" or "reserve-stress" or "reserve-stress-positive" or "batched-eligibility")
                {
                    return Json("""{"results":[]}""");
                }

                if (Mode is "ranked" or "paged")
                {
                    return Json(
                        BodyReference switch
                        {
                            "Viable Sell" =>
                                """{"results":[{"name":"Periclase Miner 1","system_name":"Periclase Miner","subtype":"Rocky body","distance":8,"volcanism_type":"Metallic Magma","parents":[{"id64":42,"type":"Star","subtype":"White Dwarf (DB) Star"}]}]}""",
                            "High Sell" =>
                                """{"results":[{"name":"Gold Miner 1","system_name":"Gold Miner","subtype":"Rocky body","distance":4}]}""",
                            _ =>
                                """{"results":[{"name":"Icy Miner 1","system_name":"Icy Miner","subtype":"Icy body","distance":3}]}""",
                        }
                    );
                }

                if (Mode == "many-systems")
                {
                    return Json(
                        """
                        {"results":[
                          {"name":"Alpha 1","system_name":"Alpha","subtype":"Rocky body","distance":1},
                          {"name":"Alpha 2","system_name":"Alpha","subtype":"Rocky body","distance":1},
                          {"name":"Beta 1","system_name":"Beta","subtype":"Rocky body","distance":2},
                          {"name":"Gamma 1","system_name":"Gamma","subtype":"Rocky body","distance":3},
                          {"name":"Delta 1","system_name":"Delta","subtype":"Rocky body","distance":4},
                          {"name":"Epsilon 1","system_name":"Epsilon","subtype":"Rocky body","distance":5},
                          {"name":"Zeta 1","system_name":"Zeta","subtype":"Rocky body","distance":6},
                          {"name":"Eta 1","system_name":"Eta","subtype":"Rocky body","distance":7}
                        ]}
                        """
                    );
                }

                if (Mode == "any")
                {
                    return Json(
                        """
                        {"results":[
                          {"name":"Alpha 1","system_name":"Alpha","subtype":"Rocky body","distance":1,"volcanism_type":"Minor Metallic Magma","landmarks":[{"subtype":"Iron Magma Lava Spout"}]},
                          {"name":"Beta 1","system_name":"Beta","subtype":"Icy body","distance":2}
                        ]}
                        """
                    );
                }

                if (Mode == "imports-fail")
                {
                    return Json(
                        """{"results":[{"name":"Alpha 1","system_name":"Alpha","subtype":"Metal-rich body","distance":4.2,"volcanism_type":"Minor Metallic Magma","landmarks":[{"subtype":"Iron Magma Lava Spout"}]}]}"""
                    );
                }

                if (Mode == "compact-periclase")
                {
                    return Json(
                        """{"results":[{"name":"Miner 1","system_name":"Miner","subtype":"Rocky body","distance":12,"volcanism_type":"Major Metallic Magma","parents":[{"id64":42,"type":"Star","subtype":"White Dwarf (DC) Star"}]}]}"""
                    );
                }

                if (Mode == "group-rank")
                {
                    return Json(
                        $$"""{"results":[{"name":"{{BodyReference}} 1","system_name":"{{BodyReference}}","subtype":"Rocky body","distance":1}]}"""
                    );
                }

                return Json(
                    """
                    {"results":[
                      {"name":"Alpha 1","system_name":"Alpha","subtype":"Rocky body","reserve_level":"Pristine","gravity":0.4,"distance_to_arrival":200,"distance":4.2},
                      {"name":"Beta 1","system_name":"Beta","subtype":"Icy body","reserve_level":"","gravity":0.1,"distance_to_arrival":50,"distance":9.5},
                      {"name":"Gamma 1","system_name":"Gamma","subtype":"Rocky body","gravity":1.2,"distance_to_arrival":10}
                    ]}
                    """
                );
            }

            if (path.Contains("/commodities/imports", StringComparison.Ordinal))
            {
                ImportRequests++;
                if (Mode == "ranked")
                {
                    return Json("[]");
                }
                if (Mode == "imports-fail")
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                if (Mode == "any")
                {
                    string updated = DateTimeOffset.UtcNow.ToString("O");
                    return Json(
                        Markets(extra: true)[..^1]
                            + ","
                            + $$"""{"systemName":"Sell System","stationName":"Gold Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":180000,"demand":1000,"stock":10,"updatedAt":"{{updated}}","distance":12.4,"distanceToArrival":150,"marketId":9,"commodityName":"Low Temperature Diamonds"}]"""
                    );
                }

                return Json(Markets(extra: true));
            }

            if (path.EndsWith("/commodities", StringComparison.Ordinal))
            {
                return Json(Mode == "averages-bad" ? "{}" : """[{"commodityName":"Diamond","avgSellPrice":100000}]""");
            }

            if (path.Contains("/nearby/imports", StringComparison.Ordinal))
            {
                MarketPath = path;
                MarketQuery = request.RequestUri.Query;
            }
            if (Mode == "compact-periclase")
            {
                string updated = DateTimeOffset.UtcNow.ToString("O");
                return Json(
                    $$"""[{"systemName":"Barnard's Star","stationName":"Boston Base","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":925152,"demand":3784,"stock":0,"updatedAt":"{{updated}}","distance":74,"distanceToArrival":100,"marketId":1,"commodityName":"periclasedunite"}]"""
                );
            }

            return Json(
                Mode switch
                {
                    "pad-demand" => MarketsWithPads(),
                    "ranked" => RankedMarkets(),
                    "paged" => CoverageMarkets(),
                    "no-bodies" => MonaziteMarket(),
                    "reserve-stress" => ManyMonaziteMarkets(),
                    "reserve-stress-positive" => ManyMonaziteMarkets(),
                    "batched-eligibility" => ManyMonaziteMarkets(),
                    "group-rank" => GroupRankingMarkets(),
                    _ => Markets(extra: false),
                }
            );
        }

        private static string ManyMonaziteMarkets()
        {
            string updated = DateTimeOffset.UtcNow.ToString("O");
            return JsonSerializer.Serialize(
                Enumerable
                    .Range(1, 60)
                    .Select(index => new
                    {
                        systemName = $"Sell {index}",
                        stationName = $"Port {index}",
                        stationType = "Coriolis",
                        maxLandingPadSize = 3,
                        sellPrice = 1_000_000 - index,
                        demand = 1_000,
                        stock = 0,
                        updatedAt = updated,
                        distance = (double)index,
                        distanceToArrival = 100,
                        marketId = index,
                        commodityName = "Monazite",
                    })
            );
        }

        private static string GroupRankingMarkets()
        {
            string updated = DateTimeOffset.UtcNow.ToString("O");
            return JsonSerializer.Serialize(
                new[]
                {
                    new
                    {
                        systemName = "Peak Sell",
                        stationName = "Peak",
                        sellPrice = 1000,
                        marketId = 1,
                    },
                    new
                    {
                        systemName = "Steady Sell",
                        stationName = "One",
                        sellPrice = 990,
                        marketId = 2,
                    },
                    new
                    {
                        systemName = "Steady Sell",
                        stationName = "Two",
                        sellPrice = 980,
                        marketId = 3,
                    },
                    new
                    {
                        systemName = "Steady Sell",
                        stationName = "Three",
                        sellPrice = 970,
                        marketId = 4,
                    },
                }.Select(item => new
                {
                    item.systemName,
                    item.stationName,
                    stationType = "Coriolis",
                    maxLandingPadSize = 3,
                    item.sellPrice,
                    demand = 1000,
                    updatedAt = updated,
                    distance = 10,
                    distanceToArrival = 100,
                    item.marketId,
                    commodityName = "Diamond",
                })
            );
        }

        private static string MonaziteMarket()
        {
            string updated = DateTimeOffset.UtcNow.ToString("O");
            return $$"""[{"systemName":"Sell System","stationName":"Gold Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":200000,"demand":1000,"stock":0,"updatedAt":"{{updated}}","distance":12.4,"distanceToArrival":150,"marketId":9,"commodityName":"Monazite"}]""";
        }

        private static string RankedMarkets()
        {
            string updated = DateTimeOffset.UtcNow.ToString("O");
            return JsonSerializer.Serialize(
                new[]
                {
                    new
                    {
                        systemName = "Unviable Sell",
                        stationName = "No Mine Port",
                        stationType = "Coriolis",
                        maxLandingPadSize = 3,
                        sellPrice = 1_200_000,
                        demand = 1_000,
                        stock = 0,
                        updatedAt = updated,
                        distance = 10.0,
                        distanceToArrival = 100,
                        marketId = 1,
                        commodityName = "Periclase Dunite",
                    },
                    new
                    {
                        systemName = "High Sell",
                        stationName = "High Port",
                        stationType = "Coriolis",
                        maxLandingPadSize = 3,
                        sellPrice = 1_000_000,
                        demand = 1_000,
                        stock = 0,
                        updatedAt = updated,
                        distance = 20.0,
                        distanceToArrival = 100,
                        marketId = 2,
                        commodityName = "Periclase Dunite",
                    },
                    new
                    {
                        systemName = "High Sell",
                        stationName = "High Port",
                        stationType = "Coriolis",
                        maxLandingPadSize = 3,
                        sellPrice = 1_100_000,
                        demand = 1_000,
                        stock = 0,
                        updatedAt = updated,
                        distance = 20.0,
                        distanceToArrival = 100,
                        marketId = 2,
                        commodityName = "Gold",
                    },
                    new
                    {
                        systemName = "Viable Sell",
                        stationName = "Viable Port",
                        stationType = "Coriolis",
                        maxLandingPadSize = 3,
                        sellPrice = 850_000,
                        demand = 1_000,
                        stock = 0,
                        updatedAt = updated,
                        distance = 30.0,
                        distanceToArrival = 100,
                        marketId = 3,
                        commodityName = "Periclase Dunite",
                    },
                }
            );
        }

        private static string CoverageMarkets()
        {
            string updated = DateTimeOffset.UtcNow.ToString("O");
            return JsonSerializer.Serialize(
                new[]
                {
                    new
                    {
                        systemName = "Sell System",
                        stationName = "Best Port",
                        stationType = "Coriolis",
                        maxLandingPadSize = 3,
                        sellPrice = 1_000_000,
                        demand = 1_000,
                        stock = 0,
                        updatedAt = updated,
                        distance = 10.0,
                        distanceToArrival = 100,
                        marketId = 1,
                        commodityName = "Periclase Dunite",
                    },
                    new
                    {
                        systemName = "Sell System",
                        stationName = "Best Port",
                        stationType = "Coriolis",
                        maxLandingPadSize = 3,
                        sellPrice = 400_000,
                        demand = 1_000,
                        stock = 0,
                        updatedAt = updated,
                        distance = 10.0,
                        distanceToArrival = 100,
                        marketId = 1,
                        commodityName = "Gold",
                    },
                }
            );
        }

        private static string MarketsWithPads()
        {
            string updated = DateTimeOffset.UtcNow.ToString("O");
            return "["
                + $$"""{"systemName":"Large Sell","stationName":"Large Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":200000,"demand":1000,"stock":10,"updatedAt":"{{updated}}","distance":12,"distanceToArrival":150,"marketId":9,"commodityName":"Diamond"}"""
                + ","
                + $$"""{"systemName":"Medium Sell","stationName":"Medium Port","stationType":"Outpost","maxLandingPadSize":2,"sellPrice":300000,"demand":1200,"stock":10,"updatedAt":"{{updated}}","distance":8,"distanceToArrival":40,"marketId":4,"commodityName":"Diamond"}"""
                + "]";
        }

        private static string Markets(bool extra)
        {
            string updated = DateTimeOffset.UtcNow.ToString("O");
            string quote =
                $$"""{"systemName":"Sell System","stationName":"Gold Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":200000,"demand":1000,"stock":10,"updatedAt":"{{updated}}","distance":12.4,"distanceToArrival":150,"marketId":9,"commodityName":"Diamond"}""";
            if (!extra)
            {
                return "["
                    + $$"""{"systemName":"Sell System","stationName":"Cheap Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":100000,"demand":800,"stock":10,"updatedAt":"{{updated}}","distance":8,"distanceToArrival":40,"marketId":4,"commodityName":"Diamond"}"""
                    + ","
                    + quote
                    + "]";
            }

            return "["
                + quote
                + $$""",{"systemName":"Sell System","stationName":"Gold Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":150000,"demand":40,"stock":10,"updatedAt":"{{updated}}","distance":12.4,"distanceToArrival":150,"marketId":9,"commodityName":"Diamond"}"""
                + $$""",{"systemName":"Sell System","stationName":"Other Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":300000,"demand":40,"stock":10,"updatedAt":"{{updated}}","distance":12.4,"distanceToArrival":20,"marketId":8,"commodityName":"Diamond"}"""
                + $$""",{"systemName":"Sell System","stationName":"Gold Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":500000,"demand":40,"stock":10,"updatedAt":"{{updated}}","distance":12.4,"distanceToArrival":150,"marketId":9,"commodityName":"Gold"}"""
                + "]";
        }

        private static HttpResponseMessage Json(string payload) =>
            new(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
    }
}
