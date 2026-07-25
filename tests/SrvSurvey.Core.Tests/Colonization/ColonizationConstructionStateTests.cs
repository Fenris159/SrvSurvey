using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationConstructionStateTests
{
    [Fact]
    public void TracksSquadronBankMusicContext()
    {
        var state = new ColonizationConstructionState();
        Assert.True(state.Apply(Event(
            "Docked",
            """
            "MarketID":42,"SystemAddress":7,"StarSystem":"Test",
            "StationName":"Squadron carrier","StationServices":["squadronBank"]
            """)));
        Assert.True(state.Apply(Event(
            "Music",
            "\"MusicTrack\":\"Squadrons\"")));

        Assert.True(state.CreateSnapshot().IsSquadronBankOpen);

        Assert.True(state.Apply(Event(
            "Music",
            "\"MusicTrack\":\"DockingComputer\"")));
        Assert.False(state.CreateSnapshot().IsSquadronBankOpen);
    }

    [Fact]
    public void TracksConstructionDockAndDepotRequirements()
    {
        var state = new ColonizationConstructionState();

        Assert.True(state.Apply(Event(
            "Docked",
            """
            "MarketID":3951663874,
            "SystemAddress":1180210008826,
            "StarSystem":"Test Sector AB-C d1",
            "StationName":"Planetary Construction Site: Far Reach",
            "StationFaction":{"Name":"Test Faction"},
            "StationServices":["dock","colonisationcontribution"]
            """)));
        Assert.True(state.Apply(Event(
            "ColonisationConstructionDepot",
            """
            "MarketID":3951663874,
            "ConstructionProgress":0.25,
            "ConstructionComplete":false,
            "ConstructionFailed":false,
            "ResourcesRequired":[
              {"Name":"$Steel_name;","Name_Localised":"Steel","RequiredAmount":100,"ProvidedAmount":25,"Payment":5057},
              {"Name":"$Water_name;","Name_Localised":"Water","RequiredAmount":40,"ProvidedAmount":10,"Payment":662}
            ]
            """)));

        var snapshot = state.CreateSnapshot();
        Assert.NotNull(snapshot.CurrentDock);
        Assert.True(snapshot.CurrentDock.IsConstructionSite);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-24T12:00:00Z"),
            snapshot.CurrentDock.Timestamp);
        Assert.Equal("Far Reach", snapshot.CurrentDock.DefaultProjectName);
        Assert.NotNull(snapshot.CurrentDepot);
        Assert.Equal(140, snapshot.CurrentDepot.TotalRequired);
        Assert.Equal(35, snapshot.CurrentDepot.TotalProvided);
        Assert.Equal(105, snapshot.CurrentDepot.TotalRemaining);
        Assert.Equal(0.25, snapshot.CurrentDepot.ReportedProgress);
        Assert.Equal(0.25, snapshot.CurrentDepot.CalculatedProgress);
    }

    [Fact]
    public void ContributionsRequireMatchingConstructionDock()
    {
        var state = new ColonizationConstructionState();
        state.Apply(Event(
            "Docked",
            """
            "MarketID":10,"SystemAddress":20,"StarSystem":"Test",
            "StationName":"Ordinary Station","StationServices":["dock"]
            """));

        Assert.False(state.Apply(Contribution(marketId: 10)));
        Assert.Null(state.LastContribution);

        state.Apply(Event(
            "Docked",
            """
            "MarketID":11,"SystemAddress":20,"StarSystem":"Test",
            "StationName":"Orbital Construction Site: Hope",
            "StationServices":["colonisationcontribution"]
            """));
        Assert.False(state.Apply(Contribution(marketId: 10)));
        Assert.True(state.Apply(Contribution(marketId: 11)));
        Assert.Equal(3, state.LastContribution?.TotalAmount);
        Assert.Equal(3, state.LastContribution?.Commodities["water"]);
    }

    [Fact]
    public void DockingAndUndockingClearStaleDepotState()
    {
        var state = new ColonizationConstructionState();
        state.Apply(Event(
            "ColonisationConstructionDepot",
            """
            "MarketID":1,"ResourcesRequired":[]
            """));

        Assert.True(state.Apply(Event(
            "Docked",
            """
            "MarketID":2,"SystemAddress":3,"StarSystem":"Test",
            "StationName":"Station","StationServices":[]
            """)));
        Assert.Null(state.CurrentDepot);
        Assert.True(state.Apply(Event("Undocked", string.Empty)));
        Assert.Null(state.CurrentDock);
    }

    [Fact]
    public void TracksClaimAndBeaconDeployment()
    {
        var state = new ColonizationConstructionState();

        Assert.True(state.Apply(Event(
            "ColonisationSystemClaim",
            """
            "StarSystem":"North America Sector PI-T c3-4",
            "SystemAddress":1180210008826
            """)));
        Assert.True(state.Apply(Event(
            "ColonisationBeaconDeployed",
            string.Empty)));

        Assert.Equal(
            "North America Sector PI-T c3-4",
            state.LastClaim?.SystemName);
        Assert.NotNull(state.LastBeaconDeployment);
    }

    [Fact]
    public void TracksCurrentShipCargoCapacityFromLoadout()
    {
        var state = new ColonizationConstructionState();

        Assert.True(state.Apply(Event(
            "Loadout",
            """
            "Ship":"typex","CargoCapacity":384
            """)));
        Assert.False(state.Apply(Event(
            "Loadout",
            """
            "Ship":"typex","CargoCapacity":384
            """)));

        Assert.Equal(384, state.ShipCargoCapacity);
        Assert.Equal(384, state.CreateSnapshot().ShipCargoCapacity);
    }

    [Fact]
    public void ReplayedEquivalentEventsDoNotAdvanceVersion()
    {
        var state = new ColonizationConstructionState();
        var docked = Event(
            "Docked",
            """
            "MarketID":10,"SystemAddress":20,"StarSystem":"Test",
            "StationName":"Orbital Construction Site: Hope",
            "StationServices":["dock","colonisationcontribution"]
            """);
        var depot = Event(
            "ColonisationConstructionDepot",
            """
            "MarketID":10,"ConstructionProgress":0.5,
            "ResourcesRequired":[{"Name":"$steel_name;","RequiredAmount":10,"ProvidedAmount":5}]
            """);

        Assert.True(state.Apply(docked));
        Assert.False(state.Apply(docked));
        Assert.True(state.Apply(depot));
        Assert.False(state.Apply(depot));
        Assert.Equal(2, state.Version);
    }

    [Theory]
    [InlineData("$Water_name;", "water")]
    [InlineData("$WATER_NAME;", "water")]
    [InlineData("steel", "steel")]
    [InlineData("", "")]
    public void NormalizesLegacyCommodityNames(string value, string expected)
    {
        Assert.Equal(
            expected,
            ColonizationConstructionState.NormalizeCommodityName(value));
    }

    [Theory]
    [InlineData("$EXT_PANEL_ColonisationShip; Test", "Primary port")]
    [InlineData("System Colonisation Ship", "Primary port")]
    [InlineData("Planetary Construction Site: Far Reach", "Far Reach")]
    [InlineData("Orbital Construction Site: High Hope", "High Hope")]
    public void PreservesLegacyDefaultProjectNames(
        string stationName,
        string expected)
    {
        var dock = new ColonizationDockingSnapshot(
            1,
            2,
            "Test",
            stationName,
            null,
            ["colonisationcontribution"]);

        Assert.Equal(expected, dock.DefaultProjectName);
    }

    private static JournalEventEnvelope Contribution(long marketId)
    {
        return Event(
            "ColonisationContribution",
            $$"""
            "MarketID":{{marketId}},
            "Contributions":[
              {"Name":"$Water_name;","Amount":1},
              {"Name":"$water_name;","Amount":2}
            ]
            """);
    }

    private static JournalEventEnvelope Event(
        string eventName,
        string properties)
    {
        var comma = string.IsNullOrWhiteSpace(properties) ? string.Empty : ",";
        var json = $$"""
            {"timestamp":"2026-07-24T12:00:00Z","event":"{{eventName}}"{{comma}}{{properties}}}
            """;
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var result, out var error),
            error);
        return result!;
    }
}
