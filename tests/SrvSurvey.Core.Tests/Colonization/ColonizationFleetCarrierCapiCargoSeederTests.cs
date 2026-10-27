using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Frontier;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationFleetCarrierCapiCargoSeederTests
{
    [Fact]
    public void ParsedCapiCargoUsesJournalIdentitiesInsteadOfLocalizedDisplayNames()
    {
        const string profile = """{"commander":{"name":"Test Cmdr"}}""";
        const string carrierJson = """
            {"name":{"callsign":"ABC-123"},"market":{"id":42},"cargo":[
              {"commodity":"fruitandvegetables","locName":"Fruit and Vegetables","qty":145},
              {"commodity":"liquidoxygen","locName":"Sauerstoff","qty":4000},
              {"commodity":"liquidoxygen","locName":"Sauerstoff","qty":976},
              {"commodity":"ancientrelic","locName":"Guardian Relic","qty":2},
              {"locName":"Water","qty":3}
            ]}
            """;
        FrontierCarrierSnapshot carrier = FrontierCapiSnapshotParser
            .Parse(profile, carrierJson, DateTimeOffset.UnixEpoch)
            .Carrier!;
        FrontierInventorySnapshot oxygen = Assert.Single(carrier.Cargo, item => item.CommodityKey == "liquidoxygen");
        Assert.Equal("Sauerstoff", oxygen.Name);
        Assert.Equal(4976, oxygen.Quantity);
        IReadOnlyDictionary<string, int> totals = ColonizationFleetCarrierCapiCargoSeeder.CreateCargoTotals(carrier);
        Assert.Equal(4, totals.Count);
        Assert.Equal(145, totals["fruitandvegetables"]);
        Assert.Equal(4976, totals["liquidoxygen"]);
        Assert.Equal(2, totals["ancientrelic"]);
        Assert.Equal(3, totals["water"]);
        var local = new ColonizationLocalCarrierCargo();
        local.Observe(carrier, DateTimeOffset.UnixEpoch);
        var linked = new ColonizationFleetCarrier { MarketId = 42, Name = "ABC-123" };
        Assert.Equal(4976, local.GetQuantity(linked, "liquid oxygen"));
        Assert.Equal(3, local.GetQuantity(linked, "water"));
    }

    [Fact]
    public void DisplayOnlySnapshotsResolveKnownNamesToJournalIdentities()
    {
        FrontierCarrierSnapshot carrier = Carrier(
            "ABC-123",
            42,
            [new FrontierInventorySnapshot("Cargo", "Fruit and Vegetables", 145, 0)]
        );
        Assert.Equal(145, ColonizationFleetCarrierCapiCargoSeeder.CreateCargoTotals(carrier)["fruitandvegetables"]);
    }

    [Fact]
    public void AcceptsFirstUndockedSnapshotThenIgnoresSessionRepeats()
    {
        var seeded = new HashSet<long>();
        var fetchedAt = DateTimeOffset.Parse(
            "2026-09-18T12:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );

        (bool firstAccepted, string firstReason) = ColonizationFleetCarrierCapiCargoSeeder.ShouldAcceptSnapshot(
            42,
            isDocked: false,
            seeded,
            fetchedAt,
            existingLinkedCargo: null
        );
        Assert.True(firstAccepted);
        Assert.Equal("server_cargo_missing", firstReason);
        seeded.Add(42);

        (bool secondAccepted, string secondReason) = ColonizationFleetCarrierCapiCargoSeeder.ShouldAcceptSnapshot(
            42,
            isDocked: false,
            seeded,
            fetchedAt.AddMinutes(15),
            new Dictionary<string, int> { ["steel"] = 10 }
        );
        Assert.False(secondAccepted);
        Assert.Equal("already_received", secondReason);
    }

    [Fact]
    public void RejectsDockedAndMissingTimestamp()
    {
        var seeded = new HashSet<long>();
        (bool dockedAccepted, string dockedReason) = ColonizationFleetCarrierCapiCargoSeeder.ShouldAcceptSnapshot(
            42,
            isDocked: true,
            seeded,
            DateTimeOffset.UnixEpoch,
            null
        );
        Assert.False(dockedAccepted);
        Assert.Equal("player_docked", dockedReason);

        (bool missingAccepted, string missingReason) = ColonizationFleetCarrierCapiCargoSeeder.ShouldAcceptSnapshot(
            42,
            isDocked: false,
            seeded,
            null,
            null
        );
        Assert.False(missingAccepted);
        Assert.Equal("missing_capi_timestamp", missingReason);
    }

    [Fact]
    public void BuildsTotalsAndDetectsManifestDifferences()
    {
        FrontierCarrierSnapshot carrier = Carrier(
            "ABC-123",
            marketId: 42,
            [
                new FrontierInventorySnapshot("Commodity", "Steel", 12, 0),
                new FrontierInventorySnapshot("Commodity", "Water", 3, 0),
            ]
        );

        IReadOnlyDictionary<string, int> totals = ColonizationFleetCarrierCapiCargoSeeder.CreateCargoTotals(carrier);
        Assert.Equal(12, totals["steel"]);
        Assert.Equal(3, totals["water"]);
        Assert.True(
            ColonizationFleetCarrierCapiCargoSeeder.ManifestsDiffer(
                totals,
                new Dictionary<string, int> { ["steel"] = 11 }
            )
        );
        Assert.False(ColonizationFleetCarrierCapiCargoSeeder.ManifestsDiffer(totals, totals));
    }

    [Fact]
    public void ResolvesLinkedMarketIdFromCarrierMarketOrCallsign()
    {
        ColonizationFleetCarrier[] linked =
        [
            new ColonizationFleetCarrier
            {
                MarketId = 99,
                Name = "SQD-001",
                Cargo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            },
        ];

        Assert.Equal(
            99,
            ColonizationFleetCarrierCapiCargoSeeder.ResolveLinkedMarketId(Carrier("SQD-001", 99, []), linked)
        );
        Assert.Equal(
            99,
            ColonizationFleetCarrierCapiCargoSeeder.ResolveLinkedMarketId(
                Carrier("SQD-001", marketId: null, []),
                linked
            )
        );
        Assert.Null(
            ColonizationFleetCarrierCapiCargoSeeder.ResolveLinkedMarketId(Carrier("OTHER", marketId: null, []), linked)
        );
    }

    private static FrontierCarrierSnapshot Carrier(
        string callsign,
        long? marketId,
        IReadOnlyList<FrontierInventorySnapshot> cargo
    )
    {
        FrontierMarketSnapshot? market = marketId is null
            ? null
            : new FrontierMarketSnapshot(
                marketId,
                callsign,
                "FleetCarrier",
                [],
                [],
                [],
                [],
                [],
                [],
                DateTimeOffset.UnixEpoch
            );

        return new FrontierCarrierSnapshot(
            callsign,
            callsign,
            "Sol",
            "NormalOperation",
            "All",
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            [],
            cargo,
            [],
            [],
            [],
            [],
            Market: market
        );
    }
}
