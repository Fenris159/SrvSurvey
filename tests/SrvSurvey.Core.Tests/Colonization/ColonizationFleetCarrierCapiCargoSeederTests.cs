using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Frontier;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationFleetCarrierCapiCargoSeederTests
{
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
