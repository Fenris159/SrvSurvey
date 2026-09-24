using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MeritSystemRowViewModelTests
{
    [Fact]
    public void StationPreviewPrioritizesHotspotsAndExpandsOnlyThatStation()
    {
        PowerplayMeritStation[] quotes =
        [
            new("Hub", "Coriolis", "Large", 1_000, 100, "", "Gold"),
            new("Hub", "Coriolis", "Large", 900, 100, "", "Silver"),
            new("Hub", "Coriolis", "Large", 800, 100, "", "Copper"),
            new("Hub", "Coriolis", "Large", 700, 100, "", "Palladium"),
            new("Hub", "Coriolis", "Large", 600, 100, "", "Platinum"),
            new("Hub", "Coriolis", "Large", 500, 100, "", "Monazite"),
            new("Hub", "Coriolis", "Large", 400, 100, "", "Alexandrite"),
            new("Hub", "Coriolis", "Large", 300, 100, "", "Painite"),
            new("Other", "Orbis", "Large", 350, 100, "", "Gold"),
        ];
        var system = new PowerplayMeritSystem(
            "Sol",
            1,
            "Aisling Duval",
            "Stronghold",
            "",
            1_000,
            [
                new PowerplayMeritRing(
                    "Sol 1 A Ring",
                    "Monazite",
                    SignalLines: ["Monazite: 1 Hotspot", "Alexandrite: 1 Hotspot"]
                ),
            ],
            quotes
        );

        var row = MeritSystemRowViewModel.From(system);
        MeritStationBlockViewModel hub = Assert.Single(row.StationBlocks);
        Assert.Equal("ALE", hub.OtherCommodities[0].Code);
        Assert.Equal(6, hub.OtherCommodities.Count);
        Assert.True(hub.CanToggleCommodities);
        hub.ToggleCommoditiesCommand.Execute(null);
        Assert.Equal(7, hub.OtherCommodities.Count);
        Assert.Equal("Show All", row.ExportSnapshot().StationBlocks[0].Restore().CommodityToggleLabel);
    }

    [Fact]
    public void PowerLinesUseSpanshConflictProgressInDescendingOrder()
    {
        IReadOnlyList<PowerplayPowerLineViewModel> lines = PowerplayPowerLineViewModel.From(
            ["Felicia Winters", "A. Lavigny-Duval", "Edmund Mahon"],
            [new PowerplayProgress("Edmund Mahon", 0.3), new PowerplayProgress("Arissa Lavigny-Duval", 1.0)]
        );

        Assert.Equal(
            ["A. Lavigny-Duval 100%", "Edmund Mahon 30%", "Felicia Winters —"],
            lines.Select(line => line.Display)
        );
        Assert.Equal("#7F00FF", lines[0].ColorHex);
    }

    [Fact]
    public void OwnedPowerUsesControlProgressAndBothMiningLayoutsOrderPowers()
    {
        var system = new PowerplayMeritSystem("Deciat", 85, "A. Lavigny-Duval", "Stronghold", "Boom", 0, [], [])
        {
            NearbyPowers = ["Zemina Torval", "A. Lavigny-Duval", "Edmund Mahon"],
            Conflict = [new PowerplayProgress("Edmund Mahon", 0.1)],
            ControlProgress = 0.835221,
        };

        var ring = MeritSystemRowViewModel.From(system);
        Assert.Equal(
            ["A. Lavigny-Duval 83.5%", "Edmund Mahon 10%", "Zemina Torval —"],
            ring.PowerLines.Select(line => line.Display)
        );

        var details = new SurfaceSellSystemDetails("Stronghold", "Boom", system.NearbyPowers)
        {
            ControllingPower = system.Power,
            ControlProgress = system.ControlProgress,
            Conflict = system.Conflict,
        };
        var planetary = new SurfaceSellRowViewModel(
            "Deciat",
            "85 ly",
            [],
            [],
            85,
            0,
            new SurfaceSellRowOptions([], details)
        );
        Assert.Equal(ring.PowerLines.Select(line => line.Display), planetary.PowerLines.Select(line => line.Display));
    }

    [Fact]
    public void UnreportedPowerProgressRemainsDistinctFromReportedZero()
    {
        IReadOnlyList<PowerplayPowerLineViewModel> lines = PowerplayPowerLineViewModel.From(
            ["Zemina Torval", "Felicia Winters"],
            [new PowerplayProgress("Felicia Winters", 0)]
        );

        Assert.Equal(["Felicia Winters 0%", "Zemina Torval —"], lines.Select(line => line.Display));
        IReadOnlyList<PowerplayPowerLineViewModel> extra = PowerplayPowerLineViewModel.From(
            ["Zemina Torval"],
            [new PowerplayProgress("Jerome Archer", 0.6)]
        );
        Assert.Equal(["Jerome Archer 60%", "Zemina Torval —"], extra.Select(line => line.Display));
    }

    [Fact]
    public void StationPadIsCompactAndSavedQuoteAgeIsRecomputed()
    {
        DateTimeOffset updated = DateTimeOffset.UtcNow.AddHours(-2);
        var system = new PowerplayMeritSystem(
            "Sol",
            2,
            "Aisling Duval",
            "Fortified",
            "",
            1_000,
            [],
            [
                new PowerplayMeritStation(
                    "Hub",
                    "Outpost",
                    "Small / medium pads",
                    1_000,
                    10,
                    "",
                    "Gold",
                    Updated: updated
                ),
            ]
        );

        MeritStationBlockViewModel station = Assert.Single(MeritSystemRowViewModel.From(system).StationBlocks);
        Assert.Equal("Hub (S/M)", station.Heading);
        Assert.Equal(updated, station.UpdatedAt);
        Assert.Contains("2h", station.Updated, StringComparison.Ordinal);

        MeritStationBlockViewModel restored = MeritStationBlockSnapshot.From(station).Restore();
        Assert.Equal(updated, restored.UpdatedAt);
        Assert.Contains("2h", restored.Updated, StringComparison.Ordinal);
    }

    [Fact]
    public void RowsUsePowerStateAndStationArtwork()
    {
        var row = MeritSystemRowViewModel.From(
            new PowerplayMeritSystem(
                "Paesia",
                12.3,
                "",
                "",
                "",
                250000,
                [
                    new PowerplayMeritRing("Paesia 2 A Ring", "Platinum"),
                    new PowerplayMeritRing("Paesia 2 B Ring", "Painite"),
                ],
                [
                    Station("A", "Coriolis Starport"),
                    Station("B", "Orbis Starport"),
                    Station("C", "Ocellus Starport"),
                    Station("D", "Asteroid base"),
                    Station("E", "Settlement"),
                    Station("F", "Planetary Outpost"),
                    Station("G", "Surface Port"),
                    Station("H", "Outpost"),
                ]
            )
        );

        Assert.Equal("No controlling Power", row.Power);
        Assert.Equal("Unknown", row.StateText);
        Assert.Equal("", row.StateIcon);
        Assert.Contains("ly", row.Distance, StringComparison.Ordinal);
        Assert.Equal("Planet", Assert.Single(row.Rings).Icon);
        row.ToggleSignals();
        Assert.Equal("Planet", row.Rings[0].Icon);
        Assert.Equal("Planet", row.Rings[1].Icon);
        Assert.Equal("Show less", row.SignalToggleLabel);
        Assert.Equal(
            ["Coriolis", "Orbis", "Ocellus", "Asteroid", "Settlement", "SurfacePort", "SurfacePort", "Outpost"],
            row.Stations.Select(station => station.Icon)
        );

        var known = MeritSystemRowViewModel.From(
            new PowerplayMeritSystem("Sol", null, "Archon Delaine", "Exploited", "", 1, [], [])
        );
        Assert.Equal("", known.Distance);
        Assert.Equal("Archon Delaine", known.Power);
        Assert.Equal("Exploited", known.StateText);

        var planet = MeritSystemRowViewModel.From(
            new PowerplayMeritSystem(
                "HR 5098",
                12,
                "Nakato Kaine",
                "Exploited",
                "",
                1,
                [
                    new PowerplayMeritRing("HR 5098 2", "2.53 g · 294.3 ls", true),
                    new PowerplayMeritRing("HR 5098 3", "1.10 g · 40 ls", true),
                ],
                []
            )
        );
        Assert.Equal("Planet", Assert.Single(planet.Rings).Icon);
        planet.ToggleSignals();
        Assert.Equal(["Planet", "Planet"], planet.Rings.Select(line => line.Icon));
    }

    [Fact]
    public void AcquireConnectorBranchesEveryMiningRow()
    {
        Assert.Equal("Single", AcquireConnector.ForIndex(0, 1));
        Assert.Equal("First", AcquireConnector.ForIndex(0, 3));
        Assert.Equal("Next", AcquireConnector.ForIndex(1, 3));
        Assert.Equal("Last", AcquireConnector.ForIndex(2, 3));
    }

    [Fact]
    public void ChipBoxRejectsUnknownNamesAndRestoresAny()
    {
        var chips = new MiningChipBoxViewModel("Mineral / metal", ["Any", "Platinum", "Osmium"], "Any");
        chips.Query = "plat";
        Assert.Contains("Platinum", chips.Suggestions);
        chips.Query = "plat";
        chips.Add("Missing");
        Assert.Equal("Any", Assert.Single(chips.Selected));
        chips.Add("Platinum");
        chips.Add("Platinum");
        chips.Add("Any");
        Assert.Equal("Any", Assert.Single(chips.Selected));
        chips.Remove("Any");
        Assert.Equal("Any", Assert.Single(chips.Selected));
    }

    [Fact]
    public void ChipBoxPromptsMatchTheEntryAndListEveryRemainingChoice()
    {
        string[] minerals =
        [
            "Default",
            "Any",
            "Platinum",
            "Painite",
            "Osmium",
            "Gold",
            "Silver",
            "Indite",
            "Jadeite",
            "Monazite",
            "Lithium Hydroxide",
        ];
        var mineralsBox = new MiningChipBoxViewModel("Mineral / metal", minerals, "Default", ["Default", "Any"]);
        var miningTypes = new MiningChipBoxViewModel("Mining type", ["All", "Core", "Laser"], "All");
        var states = new MiningChipBoxViewModel("System state", ["Any", "Boom"], "Any");

        Assert.Equal("LHY", mineralsBox.ChipLabel("Lithium Hydroxide"));
        Assert.Equal("Default", mineralsBox.ChipLabel("Default"));
        Assert.Equal("All", miningTypes.ChipLabel("All"));
        Assert.Equal("Boom", states.ChipLabel("Boom"));
        mineralsBox.Query = "lhy";
        Assert.Contains(mineralsBox.Suggestions, choice => choice == "Lithium Hydroxide");
        mineralsBox.Add("Lithium Hydroxide");
        Assert.Equal("LHY", mineralsBox.Tags[^1].Label);
        Assert.Equal("Type to add minerals/metals...", mineralsBox.Prompt);
        Assert.Equal("Type to add mining types...", miningTypes.Prompt);
        Assert.Equal("Type to add system states...", states.Prompt);
        Assert.Equal(minerals.Length - 1, mineralsBox.Suggestions.Count);
        mineralsBox.Query = "ite";
        Assert.Equal(["Painite", "Indite", "Jadeite", "Monazite"], mineralsBox.Suggestions);
    }

    [Fact]
    public void ShowAllSignalsRevealsLowerPricedCommoditiesAndEveryRing()
    {
        var row = MeritSystemRowViewModel.From(
            new PowerplayMeritSystem(
                "Sol",
                10,
                "Archon Delaine",
                "Exploited",
                "",
                200,
                [
                    new PowerplayMeritRing("A Ring", "Platinum ×2 · Metallic"),
                    new PowerplayMeritRing("B Ring", "Monazite ×1 · Rocky"),
                ],
                [
                    new PowerplayMeritStation("Hub", "Coriolis Starport", "Large", 200, 10, "", "Platinum"),
                    new PowerplayMeritStation("Hub", "Coriolis Starport", "Large", 50, 4, "", "Monazite"),
                ]
            )
        );

        Assert.Equal("Platinum", Assert.Single(row.StationBlocks).Commodity);
        Assert.Equal("MON", Assert.Single(Assert.Single(row.StationBlocks).OtherCommodities).Code);
        Assert.Contains("Platinum", Assert.Single(row.Rings).Text, StringComparison.Ordinal);
        row.ToggleSignals();
        Assert.Equal(2, row.Stations.Count);
        Assert.Equal(2, row.Rings.Count);
        Assert.Equal("Show less", row.SignalToggleLabel);
    }

    [Fact]
    public void StationHeadlineFollowsTheRingHotspotWhenAnotherCommodityPaysMore()
    {
        var row = MeritSystemRowViewModel.From(
            new PowerplayMeritSystem(
                "Sosong",
                5.1,
                "",
                "Stronghold",
                "",
                411_452,
                [
                    new PowerplayMeritRing(
                        "ABC 1 A Ring",
                        "Musgravite ×2",
                        SignalLines: ["Musgravite: 2 Hotspots", "Bromellite: 1 Hotspot"],
                        Reserve: "Major",
                        RingType: "Rocky"
                    ),
                ],
                [
                    new PowerplayMeritStation("Potter Terminal", "Orbis Starport", "L", 900_000, 1, "", "Diamond"),
                    new PowerplayMeritStation(
                        "Potter Terminal",
                        "Orbis Starport",
                        "L",
                        463_527,
                        150_826,
                        "",
                        "periclasedunite"
                    ),
                    new PowerplayMeritStation("Potter Terminal", "Orbis Starport", "L", 743_334, 6, "", "Monazite"),
                    new PowerplayMeritStation("Potter Terminal", "Orbis Starport", "L", 411_452, 17, "", "Musgravite"),
                ]
            )
        );

        MeritStationBlockViewModel station = Assert.Single(row.StationBlocks);
        Assert.Equal("Musgravite", station.Commodity);
        Assert.Contains("411,452", station.Price, StringComparison.Ordinal);
        Assert.DoesNotContain(station.OtherCommodities, line => line.Code == "DIA");
        Assert.DoesNotContain(station.OtherCommodities, line => line.Code == "PER");
        Assert.Contains(station.OtherCommodities, line => line.Code == "MON");
        MeritLineViewModel primary = Assert.Single(row.Rings);
        Assert.Equal("Planet", primary.Icon);
        Assert.Equal("RingRocky", primary.RingTypeIcon);
        Assert.Equal("ReserveMajor", primary.ReserveIcon);
        row.ToggleSignals();
        Assert.Equal("", row.Rings[1].Icon);
        Assert.True(row.Rings[1].ShowSpacer);
    }

    private static PowerplayMeritStation Station(string name, string type) => new(name, type, "L", 1, 1, name);
}
