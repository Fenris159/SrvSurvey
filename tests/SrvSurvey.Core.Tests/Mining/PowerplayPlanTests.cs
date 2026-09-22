using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class PowerplayPlanTests
{
    [Fact]
    public void SettledStatesStayOnTheirSpanshIndex()
    {
        PowerplaySpanshQuery filter = PowerplayPlan.SpanshFilter(PowerplayPlan.Reinforce, "Fortified");

        Assert.Equal("Fortified", filter.IndexedState);
        Assert.Equal("", filter.RequiredState);
        Assert.False(PowerplayPlan.UsesLiveConflict(PowerplayPlan.Reinforce, "Fortified"));
    }

    [Fact]
    public void LiveStatesReadTheUnoccupiedIndexAndKeepTheRequestedStanding()
    {
        PowerplaySpanshQuery filter = PowerplayPlan.SpanshFilter("All systems", PowerplayStanding.Contested);

        Assert.Equal(PowerplayStanding.Unoccupied, filter.IndexedState);
        Assert.Equal(PowerplayStanding.Contested, filter.RequiredState);
        Assert.True(PowerplayPlan.UsesLiveConflict(PowerplayPlan.Reinforce, PowerplayStanding.Expansion));
    }

    [Fact]
    public void OpenAcquireReadsEveryInferredRowFromTheUnoccupiedIndex()
    {
        PowerplaySpanshQuery filter = PowerplayPlan.SpanshFilter(PowerplayPlan.Acquire, "");

        Assert.Equal(PowerplayStanding.Unoccupied, filter.IndexedState);
        Assert.Equal("", filter.RequiredState);
        Assert.Equal(
            PowerplayStanding.Contested,
            PowerplayPlan.SpanshFilter(PowerplayPlan.Acquire, PowerplayStanding.Contested).RequiredState
        );
    }

    [Fact]
    public void ObjectivesKeepUnknownOwnershipOutOfReinforceAndUndermine()
    {
        MiningSystemResult own = System("Own", "Aisling Duval", "Fortified");
        MiningSystemResult other = System("Other", "Jerome Archer", "Exploited");
        MiningSystemResult unknown = System("Unknown", "", "");
        MiningSystemResult expansion = System("Wille", "Aisling Duval", PowerplayStanding.Expansion);
        MiningSystemResult claim = System("Claim", "", PowerplayStanding.Unoccupied);

        Assert.True(PowerplayPlan.Matches(PowerplayPlan.Reinforce, own, "Aisling Duval", PowerplayPlan.AnyPower));
        Assert.False(PowerplayPlan.Matches(PowerplayPlan.Reinforce, other, "Aisling Duval", PowerplayPlan.AnyPower));
        Assert.False(PowerplayPlan.Matches(PowerplayPlan.Reinforce, unknown, "Aisling Duval", PowerplayPlan.AnyPower));
        Assert.False(
            PowerplayPlan.Matches(PowerplayPlan.Reinforce, expansion, "Aisling Duval", PowerplayPlan.AnyPower)
        );
        Assert.True(
            PowerplayPlan.Matches(PowerplayPlan.Reinforce, unknown, PowerplayPlan.NoPower, PowerplayPlan.AnyPower)
        );

        Assert.True(PowerplayPlan.Matches(PowerplayPlan.Undermine, other, "Aisling Duval", "Jerome Archer"));
        Assert.False(PowerplayPlan.Matches(PowerplayPlan.Undermine, own, "Aisling Duval", "Jerome Archer"));
        Assert.False(PowerplayPlan.Matches(PowerplayPlan.Undermine, other, "Aisling Duval", "Aisling Duval"));
        Assert.False(PowerplayPlan.Matches(PowerplayPlan.Undermine, expansion, "Jerome Archer", "Aisling Duval"));

        Assert.True(PowerplayPlan.Matches(PowerplayPlan.Acquire, claim, "Aisling Duval", PowerplayPlan.AnyPower));
        Assert.False(PowerplayPlan.Matches(PowerplayPlan.Acquire, own, "Aisling Duval", PowerplayPlan.AnyPower));
        Assert.True(PowerplayPlan.Matches("All systems", unknown, PowerplayPlan.AnyPower, PowerplayPlan.AnyPower));
    }

    [Fact]
    public void AcquisitionReachFollowsTheSupportingStrongholdOrFortifiedSystem()
    {
        Assert.Equal(PowerplayPlan.StrongholdReachLy, PowerplayPlan.AcquisitionReachLy("Stronghold"));
        Assert.Equal(PowerplayPlan.FortifiedReachLy, PowerplayPlan.AcquisitionReachLy("Fortified"));

        MiningSystemResult claim = System("Claim", "", PowerplayStanding.Unoccupied);
        MiningSystemResult owned = System("Owned", "Yuri Grom", "Exploited");
        MiningSystemResult expansion = System("Edge", "", PowerplayStanding.Expansion);

        Assert.True(PowerplayPlan.IsAcquisitionTarget(claim, ""));
        Assert.False(PowerplayPlan.IsAcquisitionTarget(owned, ""));
        Assert.True(PowerplayPlan.IsAcquisitionTarget(expansion, PowerplayStanding.Expansion));
        Assert.False(PowerplayPlan.IsAcquisitionTarget(claim, PowerplayStanding.Contested));
    }

    [Fact]
    public void OppositionCountsUseConflictProgressAndKeepNoneForReinforce()
    {
        MiningSystemResult quiet = System("Quiet", "Aisling Duval", "Fortified");
        MiningSystemResult one = System(
            "One",
            "Aisling Duval",
            "Fortified",
            [new PowerplayProgress("Jerome Archer", 0.1)]
        );
        MiningSystemResult two = System(
            "Two",
            "Aisling Duval",
            "Fortified",
            [new PowerplayProgress("Jerome Archer", 0.1), new PowerplayProgress("Yuri Grom", 0.4)]
        );
        MiningSystemResult ownProgress = System(
            "Own",
            "A. Lavigny-Duval",
            "Fortified",
            [new PowerplayProgress("A. Lavigny-Duval", 0.9), new PowerplayProgress("Edmund Mahon", 0.2)]
        );

        Assert.Equal(0, PowerplayPlan.OpposingPowerCount(quiet.Conflict, "Aisling Duval"));
        Assert.Equal(1, PowerplayPlan.OpposingPowerCount(one.Conflict, "Aisling Duval"));
        Assert.Equal(2, PowerplayPlan.OpposingPowerCount(two.Conflict, "Aisling Duval"));
        Assert.Equal(1, PowerplayPlan.OpposingPowerCount(ownProgress.Conflict, "Arissa Lavigny-Duval"));
        Assert.True(
            PowerplayPlan.MatchesOppositionCount(PowerplayPlan.Reinforce, PowerplayPlan.NoPower, quiet, "Aisling Duval")
        );
        Assert.False(
            PowerplayPlan.MatchesOppositionCount(PowerplayPlan.Undermine, PowerplayPlan.NoPower, quiet, "Aisling Duval")
        );
        Assert.True(
            PowerplayPlan.MatchesOppositionCount(
                PowerplayPlan.Undermine,
                PowerplayPlan.OneOpposition,
                one,
                "Nakato Kaine"
            )
        );
        Assert.True(
            PowerplayPlan.MatchesOppositionCount(
                PowerplayPlan.Undermine,
                PowerplayPlan.TwoOpposition,
                two,
                "Nakato Kaine"
            )
        );
        Assert.True(
            PowerplayPlan.MatchesOppositionCount(
                PowerplayPlan.Undermine,
                PowerplayPlan.MultipleOpposition,
                two,
                "Nakato Kaine"
            )
        );
        Assert.False(
            PowerplayPlan.MatchesOppositionCount(
                PowerplayPlan.Undermine,
                PowerplayPlan.MultipleOpposition,
                one,
                "Nakato Kaine"
            )
        );
    }

    [Fact]
    public void TravelDistanceUsesTheReferencePosition()
    {
        var origin = new GalacticCoordinate(0, 0, 0);
        var target = new GalacticCoordinate(3, 4, 0);

        Assert.Equal(5, PowerplayPlan.TravelDistance(origin, target));
        Assert.Null(PowerplayPlan.TravelDistance(null, target));
    }

    private static MiningSystemResult System(
        string name,
        string power,
        string state,
        IReadOnlyList<PowerplayProgress>? conflict = null
    ) => new(name, 1, "", "", "", "", "", power, state, 0) { Conflict = conflict ?? [] };
}
