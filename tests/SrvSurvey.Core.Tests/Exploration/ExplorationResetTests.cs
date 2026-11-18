using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Exploration;

public sealed class ExplorationResetTests
{
    [Fact]
    public void ResettingTheOdometerPreservesRewardsBodyTotalsAndDuplicateProtection()
    {
        ExplorationState state = Survey();

        state.ResetOdometer();
        Assert.Equal(3_149_741, state.EstimatedRewards);
        Assert.Equal(1, state.ScanCount);
        Assert.Equal(1, state.DetailedSurfaceScanCount);
        Assert.Equal(1, state.LandedBodyCount);
        state.Apply(Parse(Scan));
        state.Apply(Parse(Map));
        state.Apply(Parse(Landing));

        ExplorationSnapshot snapshot = state.CreateSnapshot();
        Assert.Equal(0, snapshot.JumpCount);
        Assert.Equal(0, snapshot.DistanceTravelled);
        Assert.Equal(3_149_741, snapshot.EstimatedRewards);
        Assert.Equal(1, snapshot.ScanCount);
        Assert.Equal(1, snapshot.DetailedSurfaceScanCount);
        Assert.Equal(1, snapshot.LandedBodyCount);
        Assert.Equal(3_149_741, Assert.Single(snapshot.EstimatedRewardsBySystem!).Value);
    }

    [Fact]
    public void ResettingTheSurveyValueStartsFreshBodyTotalsAndKeepsTheOdometer()
    {
        ExplorationState state = Survey();

        state.ResetSurveyValue();

        ExplorationSnapshot snapshot = state.CreateSnapshot();
        Assert.Equal(1, snapshot.JumpCount);
        Assert.Equal(12.345, snapshot.DistanceTravelled, 3);
        Assert.Equal(0, snapshot.EstimatedRewards);
        Assert.Equal(0, snapshot.ScanCount);
        Assert.Equal(0, snapshot.DetailedSurfaceScanCount);
        Assert.Equal(0, snapshot.LandedBodyCount);
        Assert.Null(snapshot.EstimatedRewardsBySystem);
        state.Apply(Parse(Scan));
        state.Apply(Parse(Landing));
        Assert.Equal(449_200, state.EstimatedRewards);
        Assert.Equal(1, state.ScanCount);
        Assert.Equal(0, state.DetailedSurfaceScanCount);
        Assert.Equal(1, state.LandedBodyCount);
        Assert.Equal(1, state.JumpCount);
    }

    [Fact]
    public void MappingAfterAValueResetUsesTheEarlierScanAndCountsOnlyTheNewMapping()
    {
        ExplorationState state = Survey();
        state.ResetSurveyValue();
        state.Apply(Parse(Map));
        state.Apply(Parse(Map));
        Assert.Equal(2_700_541, state.EstimatedRewards);
        Assert.Equal(0, state.ScanCount);
        Assert.Equal(1, state.DetailedSurfaceScanCount);
        Assert.Equal(0, state.LandedBodyCount);
        Assert.Equal(1, state.JumpCount);
        Assert.Equal(12.345, state.DistanceTravelled, 3);
    }

    private const string Scan =
        """{"event":"Scan","StarSystem":"Test","SystemAddress":1,"BodyID":4,"PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1.0,"WasDiscovered":false,"WasMapped":false}""";
    private const string Map =
        """{"event":"SAAScanComplete","SystemAddress":1,"BodyID":4,"ProbesUsed":6,"EfficiencyTarget":6}""";
    private const string Landing = """{"event":"Touchdown","SystemAddress":1,"BodyID":4,"OnPlanet":true}""";

    private static ExplorationState Survey()
    {
        var state = new ExplorationState();
        state.Apply(Parse("""{"event":"StartJump","JumpType":"Hyperspace"}"""));
        state.Apply(Parse("""{"event":"FSDJump","JumpDist":12.345}"""));
        state.Apply(Parse(Scan));
        state.Apply(Parse(Map));
        state.Apply(Parse(Landing));
        return state;
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out string? error), error);
        return entry!;
    }
}
