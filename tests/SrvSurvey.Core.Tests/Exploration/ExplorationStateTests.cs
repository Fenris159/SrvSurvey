using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Exploration;

public sealed class ExplorationStateTests
{
    [Fact]
    public void ApplyTracksLegacyCountersAndRewards()
    {
        var state = new ExplorationState();
        state.Apply(Parse("""{"event":"Fileheader","Odyssey":true}"""));
        state.Apply(Parse("""{"event":"StartJump","JumpType":"Supercruise"}"""));
        state.Apply(Parse("""{"event":"StartJump","JumpType":"Hyperspace"}"""));
        state.Apply(Parse("""{"event":"FSDJump","JumpDist":12.345}"""));
        var scan = Parse("""{"event":"Scan","StarSystem":"Test","SystemAddress":1,"BodyID":4,"BodyName":"Test 4","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1.0,"WasDiscovered":false,"WasMapped":false}""");
        state.Apply(scan);
        state.Apply(scan);
        state.Apply(Parse("""{"event":"SAAScanComplete","SystemAddress":1,"BodyID":4,"ProbesUsed":6,"EfficiencyTarget":6}"""));
        var touchdown = Parse("""{"event":"Touchdown","SystemAddress":1,"BodyID":4,"OnPlanet":true}""");
        state.Apply(touchdown);
        state.Apply(touchdown);
        state.Apply(Parse("""{"event":"Touchdown","SystemAddress":1,"BodyID":5,"OnPlanet":false}"""));

        var snapshot = state.CreateSnapshot();
        Assert.Equal(1, snapshot.JumpCount);
        Assert.Equal(12.345, snapshot.DistanceTravelled, 3);
        Assert.Equal(1, snapshot.ScanCount);
        Assert.Equal(1, snapshot.DetailedSurfaceScanCount);
        Assert.Equal(1, snapshot.LandedBodyCount);
        Assert.Equal(449200 + 2700541, snapshot.EstimatedRewards);
        var trackedReward = Assert.Single(snapshot.EstimatedRewardsBySystem!);
        Assert.Equal("Test", trackedReward.Key);
        Assert.Equal(snapshot.EstimatedRewards, trackedReward.Value);
    }

    [Fact]
    public void SellExplorationDataRemovesOnlyMatchedSystemsOnce()
    {
        var state = new ExplorationState();
        state.Apply(Parse("""{"event":"Scan","StarSystem":"Alpha","SystemAddress":1,"BodyID":4,"PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1.0,"WasDiscovered":false,"WasMapped":false}"""));
        state.Apply(Parse("""{"event":"Scan","StarSystem":"Beta","SystemAddress":2,"BodyID":4,"PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1.0,"WasDiscovered":false,"WasMapped":false}"""));

        Assert.True(state.Apply(Parse("""{"event":"SellExplorationData","Systems":[" alpha "],"Discovered":["ALPHA","Unknown"],"TotalEarnings":123}""")));

        var afterSale = state.CreateSnapshot();
        Assert.Equal(449200, afterSale.EstimatedRewards);
        Assert.Equal(2, afterSale.ScanCount);
        var remaining = Assert.Single(afterSale.EstimatedRewardsBySystem!);
        Assert.Equal("Beta", remaining.Key);
        Assert.Equal(449200, remaining.Value);

        state.Apply(Parse("""{"event":"SellExplorationData","Systems":["Alpha"],"Discovered":["Alpha"]}"""));

        Assert.Equal(afterSale, state.CreateSnapshot());
    }

    [Fact]
    public void MultiSellExplorationDataKeepsUnattributedHistoricalRewards()
    {
        var state = new ExplorationState(new ExplorationSnapshot(
            1_000,
            0,
            0,
            0,
            0,
            0,
            new Dictionary<string, long>
            {
                ["Alpha"] = 400,
                ["Beta"] = 300,
            }));

        Assert.True(state.Apply(Parse("""{"event":"MultiSellExplorationData","Discovered":[{"SystemName":"ALPHA","NumBodies":2},{"SystemName":"Beta","NumBodies":1}] }""")));

        var snapshot = state.CreateSnapshot();
        Assert.Equal(300, snapshot.EstimatedRewards);
        Assert.Null(snapshot.EstimatedRewardsBySystem);
    }

    [Fact]
    public void SeedAndResetPreserveImportedTotalsWithoutBodyHistory()
    {
        var seed = new ExplorationSnapshot(1000, 42.5, 3, 4, 5, 6);
        var state = new ExplorationState(seed);

        state.Apply(Parse("""{"event":"StartJump","JumpType":"Hyperspace"}"""));
        Assert.Equal(4, state.JumpCount);

        state.Reset(seed);

        Assert.Equal(seed, state.CreateSnapshot());
    }

    [Fact]
    public void UnknownEventsRemainObservableToOtherReducers()
    {
        var state = new ExplorationState();

        Assert.False(state.Apply(Parse("""{"event":"FutureEvent","Value":42}""")));
        Assert.Equal(ExplorationSnapshot.Empty, state.CreateSnapshot());
    }

    private static JournalEventEnvelope Parse(string json)
    {
        var success = JournalEventEnvelope.TryParse(json, out var journalEvent, out var error);
        Assert.True(success, error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }
}
