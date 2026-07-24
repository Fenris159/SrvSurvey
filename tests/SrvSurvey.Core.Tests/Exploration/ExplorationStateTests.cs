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
        var scan = Parse("""{"event":"Scan","SystemAddress":1,"BodyID":4,"BodyName":"Test 4","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1.0,"WasDiscovered":false,"WasMapped":false}""");
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
