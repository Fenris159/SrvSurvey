using SrvSurvey.Core.Frontier;

namespace SrvSurvey.Core.Tests.Frontier;

public sealed class FrontierCapiSnapshotParserTests
{
    [Fact]
    public void ParsesCommanderFleetRanksAndCarrierDetails()
    {
        var fetchedAt = DateTimeOffset.Parse("2026-07-29T12:00:00Z");

        var snapshot = FrontierCapiSnapshotParser.Parse(
            ProfileJson,
            CarrierJson,
            fetchedAt);

        Assert.Equal("Fenris", snapshot.CommanderName);
        Assert.Equal(1_234_567_890, snapshot.Credits);
        Assert.Equal(10_000, snapshot.Debt);
        Assert.Equal("Shinrarta Dezhra", snapshot.LastSystem);
        Assert.Equal("Jameson Memorial", snapshot.LastStation);
        Assert.Equal(2, snapshot.Ships.Count);
        Assert.Equal("Surveyor", snapshot.CurrentShip!.Name);
        Assert.Equal("Cobra Mk III", snapshot.CurrentShip.Type);
        Assert.Equal(42_000_000, snapshot.FleetValue);
        Assert.Equal("Elite", snapshot.Ranks.Single(rank => rank.Key == "explore").Name);
        Assert.Contains("Horizons", snapshot.Capabilities);
        Assert.Equal(
            100,
            Assert.Single(snapshot.CommanderReputation!).Score);

        var carrier = Assert.IsType<FrontierCarrierSnapshot>(snapshot.Carrier);
        Assert.Equal("RAV-001", carrier.Callsign);
        Assert.Equal("Raven's Rest", carrier.Name);
        Assert.Equal("Colonia", carrier.System);
        Assert.Equal(900, carrier.Tritium);
        Assert.Equal(1_450, carrier.CapacityUsed);
        Assert.Equal(23_550, carrier.CapacityFree);
        Assert.Equal(25, carrier.Cargo.Single(item => item.Name == "Tritium").Quantity);
        Assert.Equal(2, carrier.Locker.Single(item => item.Name == "Power Regulator").Quantity);
        Assert.Equal(100, Assert.Single(carrier.SellOrders).Quantity);
        Assert.Equal(150, Assert.Single(carrier.BuyOrders).Remaining);
        Assert.Contains("Refuel", carrier.Services);
        Assert.Equal(fetchedAt, snapshot.FetchedAt);
    }

    [Fact]
    public void PreservesCommanderMetadataWithoutManufacturingCarrierOwnership()
    {
        const string profile =
            """
            {
              "commander":{"name":"Drew","rank":{}},
              "ships":[],
              "reputation":{"alliance":"85.5","federation":50}
            }
            """;
        const string carrierEnvelope =
            """
            {
              "reputation":[
                {"majorFaction":"federation","score":75},
                {"majorFaction":"independent","score":100}
              ],
              "futureAccountMetadata":{"available":true}
            }
            """;

        var snapshot = FrontierCapiSnapshotParser.Parse(
            profile,
            carrierEnvelope,
            DateTimeOffset.UnixEpoch);

        Assert.Null(snapshot.Carrier);
        Assert.Equal(3, snapshot.CommanderReputation!.Count);
        Assert.Equal(
            75,
            snapshot.CommanderReputation.Single(item =>
                item.Faction == "Federation").Score);
        Assert.Contains(snapshot.CarrierEndpointData!, point =>
            point.Path == "fleetcarrier.futureAccountMetadata.available"
                && point.Value == "Yes");
    }

    [Fact]
    public void SupportsIdKeyedShipAndOrderObjects()
    {
        const string profile =
            """
            {
              "commander":{"name":"Drew","currentShipId":7,"rank":{}},
              "ships":{"7":{"id":7,"name":"sidewinder","value":{"total":32000}}},
              "lastSystem":{"name":"Sol"}
            }
            """;
        const string carrier =
            """
            {
              "name":{"callsign":"ABC-123"},
              "orders":{"onfootmicroresources":{"sales":{"9":{"name":"healthmonitor","locName":"Health Monitor","stock":3,"price":4200}}}}
            }
            """;

        var snapshot = FrontierCapiSnapshotParser.Parse(
            profile,
            carrier,
            DateTimeOffset.UnixEpoch);

        Assert.Equal("Sidewinder", Assert.Single(snapshot.Ships).Type);
        var sale = Assert.Single(snapshot.Carrier!.SellOrders);
        Assert.Equal("Health Monitor", sale.Name);
        Assert.Equal("Microresource", sale.Category);
    }

    [Fact]
    public void ParsesDetailedShipLocationAndPreservesEveryProfileScalar()
    {
        const string profile =
            """
            {
              "commander":{"id":88,"name":"Drew","currentShipId":7,"rank":{}},
              "lastSystem":{"id":12,"systemaddress":34,"name":"Sol","allegiance":"federation"},
              "lastStarport":{"id":56,"name":"Galileo","faction":"federation","services":{"shipyard":"ok"}},
              "ship":{
                "id":7,"name":"sidewinder","shipName":"Pathfinder","alive":true,
                "cockpitBreached":false,"oxygenRemaining":421.5,
                "value":{"total":32000,"hull":10000,"modules":20000,"cargo":2000},
                "health":{"hull":950000,"shield":0.8,"integrity":0.9,"paintwork":62060,"shieldup":true},
                "starsystem":{"name":"Sol","id":12,"systemaddress":34},
                "station":{"name":"Galileo","id":56},
                "modules":{"mainengines":{"module":{"id":101,"name":"int_engine_size2_class1","value":9000,"health":0.99,"on":true,"priority":1},"engineer":{"engineerName":"Felicity Farseer","recipeName":"engine_tuned","recipeLevel":3},"specialModifications":{"effect":"drag drives"}}},
                "launchBays":{"fighter1":{"name":"empire_fighter","loadoutName":"Aegis F","rebuilds":5}}
              },
              "customFutureField":{"nested":1234}
            }
            """;

        var snapshot = FrontierCapiSnapshotParser.Parse(
            profile,
            null,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(88, snapshot.CommanderId);
        Assert.Equal("Federation", snapshot.LastSystemDetails!.Allegiance);
        Assert.Contains("Shipyard", snapshot.LastStationDetails!.Services);
        var ship = Assert.IsType<FrontierShipSnapshot>(snapshot.CurrentShip);
        Assert.Equal(95, ship.HullHealth);
        Assert.Equal(6.206, ship.Paintwork);
        Assert.Equal(34, ship.SystemAddress);
        var module = Assert.Single(ship.Modules!);
        Assert.Equal("Felicity Farseer", module.Engineer);
        Assert.Equal("int_engine_size2_class1", module.InternalName);
        Assert.Equal(5, Assert.Single(ship.LaunchBays!).Rebuilds);
        Assert.Contains(
            snapshot.ProfileData!,
            point => point.Path == "profile.customFutureField.nested"
                && point.Value == "1234");
    }

    [Fact]
    public void ParsesMarketShipyardAndAllEndpointScalars()
    {
        const string market =
            """
            {
              "id":128,"name":"Jameson Memorial","outpostType":"starport",
              "imported":["gold"],"exported":["tea"],"prohibited":["slaves"],
              "services":{"commodities":"ok","blackmarket":"unavailable"},
              "economies":{"hightech":{"id":4,"name":"High Tech","proportion":0.75}},
              "commodities":[{"id":42,"categoryName":"metals","locName":"Gold","buyPrice":100,"sellPrice":90,"meanPrice":95,"demandBracket":2,"stockBracket":3,"stock":50,"demand":200,"statusFlags":["Rare"]}],
              "futureMarketValue":true
            }
            """;
        const string shipyard =
            """
            {
              "id":128,"name":"Jameson Memorial","outpostType":"starport",
              "services":{"shipyard":"ok","outfitting":"ok"},
              "modules":[{"id":4,"category":"utility","locName":"Heat Sink Launcher","cost":3500,"stock":7,"sku":"ELITE_HORIZONS_V_PLANETARY_LANDINGS"}],
              "ships":{"shipyard_list":{"1":{"id":1,"name":"sidewinder","basevalue":32000,"stock":-1,"sku":""}}},
              "futureShipyardValue":"available"
            }
            """;
        var fetchedAt = DateTimeOffset.Parse("2026-07-29T12:00:00Z");

        var parsedMarket = FrontierCapiSnapshotParser.ParseMarket(market, fetchedAt);
        var parsedShipyard = FrontierCapiSnapshotParser.ParseShipyard(shipyard, fetchedAt);

        Assert.Equal("Starport", parsedMarket.OutpostType);
        Assert.Equal("Gold", Assert.Single(parsedMarket.Commodities).Name);
        Assert.Equal(0.75, Assert.Single(parsedMarket.Economies).Proportion);
        Assert.Contains(parsedMarket.DataPoints!, point =>
            point.Path == "market.futureMarketValue" && point.Value == "Yes");
        Assert.Equal("Heat Sink Launcher", Assert.Single(parsedShipyard.Modules).Name);
        Assert.Equal("Sidewinder", Assert.Single(parsedShipyard.Ships).Name);
        Assert.Contains(parsedShipyard.DataPoints!, point =>
            point.Path == "shipyard.futureShipyardValue"
                && point.Value == "available");
    }

    [Fact]
    public void ParsesCommunityGoalProgressDescriptionAndCommanderStanding()
    {
        const string communityGoals =
            """
            {
              "active":[{
                "CGID":321,"Title":"Deliver medicines","Description":"Support the relief effort.",
                "Objective":"Deliver Basic Medicines","Reward":"Global rewards",
                "SystemName":"Sol","MarketName":"Galileo","Expiry":"2026-08-01T12:00:00Z",
                "CurrentTotal":5000,"TargetTotal":10000,"PlayerContribution":250,
                "NumContributors":40,"TierReached":"Tier 2","PlayerPercentileBand":25,
                "Bonus":1500000,"TopRankSize":10,"PlayerInTopRank":false,
                "futureGoalField":"retained"
              }]
            }
            """;

        var goal = Assert.Single(
            FrontierCapiSnapshotParser.ParseCommunityGoals(communityGoals));

        Assert.Equal(321, goal.Id);
        Assert.Equal("Support the relief effort.", goal.Description);
        Assert.Equal(5_000, goal.CurrentTotal);
        Assert.Equal(10_000, goal.TargetTotal);
        Assert.Equal(250, goal.PlayerContribution);
        Assert.Equal(25, goal.PlayerPercentile);
        Assert.Contains(goal.DataPoints!, point =>
            point.Path == "goal.futureGoalField" && point.Value == "retained");
    }

    [Fact]
    public void RejectsProfileWithoutCommanderIdentity()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            FrontierCapiSnapshotParser.Parse(
                "{\"commander\":{}}",
                null,
                DateTimeOffset.UnixEpoch));

        Assert.Contains("commander name", exception.Message);
    }

    private const string ProfileJson =
        """
        {
          "commander": {
            "name": "Fenris",
            "credits": 1234567890,
            "debt": 10000,
            "docked": true,
            "alive": true,
            "currentShipId": 7,
            "rank": {"combat":3,"trade":7,"explore":8,"exobiologist":6},
            "capabilities": {"horizons":true,"cobraMkIV":false}
          },
          "lastSystem": {"name":"Shinrarta Dezhra"},
          "lastStarport": {"name":"Jameson Memorial"},
          "ship": {
            "id":7,
            "name":"cobramkiii",
            "shipName":"Surveyor",
            "value":{"total":12000000},
            "health":{"hull":0.95,"shield":1.0},
            "starsystem":{"name":"Shinrarta Dezhra"},
            "station":{"name":"Jameson Memorial"}
          },
          "ships": [
            {"id":7,"name":"cobramkiii","shipName":"Surveyor","value":{"total":12000000},"starsystem":{"name":"Shinrarta Dezhra"},"station":{"name":"Jameson Memorial"}},
            {"id":9,"name":"anaconda","shipName":"Long View","value":{"total":30000000},"starsystem":{"name":"Colonia"},"station":{"name":"Jaques Station"}}
          ]
        }
        """;

    private const string CarrierJson =
        """
        {
          "name": {
            "callsign":"RAV-001",
            "filteredVanityName":"526176656e27732052657374"
          },
          "currentStarSystem":"Colonia",
          "state":"normalOperation",
          "dockingAccess":"squadronfriends",
          "fuel":900,
          "capacity": {
            "shipPacks":500,
            "cargoForSale":200,
            "cargoNotForSale":750,
            "freeSpace":23550,
            "microresourceCapacityTotal":1000
          },
          "finance": {
            "bankBalance":5000000000,
            "bankReservedBalance":20000000,
            "maintenance":18500000
          },
          "marketFinances": {
            "cargoTotalValue":70000000,
            "allTimeProfit":123000000,
            "balanceAllocForPurchaseOrders":45000000
          },
          "cargo": [
            {"commodity":"tritium","locName":"Tritium","qty":20,"value":1000000},
            {"commodity":"tritium","locName":"Tritium","qty":5,"value":250000}
          ],
          "orders": {
            "commodities": {
              "sales":[{"name":"Gold","stock":100,"price":50000}],
              "purchases":[{"name":"Silver","total":200,"outstanding":150,"price":40000}]
            }
          },
          "carrierLocker": {
            "assets":[{"name":"powerregulator","locName":"Power Regulator","quantity":2}]
          },
          "servicesCrew": {"refuel":{"status":"ok"},"repair":{"status":"ok"}}
          ,"reputation": [
            {"majorFaction":"federation","score":100}
          ]
        }
        """;
}
