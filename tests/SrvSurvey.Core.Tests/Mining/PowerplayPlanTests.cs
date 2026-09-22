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

    private static MiningSystemResult System(string name, string power, string state) =>
        new(name, 1, "", "", "", "", "", power, state, 0);
}
