using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Navigation;

public sealed class JumpInfoRoutePlannerTests
{
    [Fact]
    public void JournalFsdTargetTakesPrecedenceOverStatusDestination()
    {
        var status = new EliteStatus
        {
            Destination = new StatusDestination
            {
                Name = "Status target",
                System = 22,
                Body = 0,
            },
        };

        var target = JumpInfoRoutePlanner.SelectTarget(
            new JumpTarget("Journal target", 11, "N"),
            status);

        Assert.Equal(new JumpTarget("Journal target", 11, "N"), target);
    }

    [Theory]
    [InlineData(1, 42, "Planet target")]
    [InlineData(0, 0, "Missing address")]
    [InlineData(0, 42, null)]
    public void InvalidStatusDestinationIsNotUsed(
        int body,
        long systemAddress,
        string? name)
    {
        var status = new EliteStatus
        {
            Destination = new StatusDestination
            {
                Name = name,
                System = systemAddress,
                Body = body,
            },
        };

        Assert.Null(JumpInfoRoutePlanner.SelectTarget(null, status));
    }

    [Fact]
    public void NavRouteCalculatesProgressScoopabilityAndBoostedLegs()
    {
        var route = new NavRouteSnapshot(
            DateTimeOffset.UtcNow,
            "NavRoute",
            [
                Entry("Sol", 1, 0, "G"),
                Entry("Alpha", 2, 10, "K"),
                Entry("Neutron", 3, 45, "N"),
                Entry("Finish", 4, 55, "M"),
            ]);

        var plan = JumpInfoRoutePlanner.Create(
            new JumpInfoRoutePlannerRequest(
                new JumpTarget("Neutron", 3),
                null,
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0),
                route,
                null,
                MaximumJumpRange: 25));

        Assert.NotNull(plan);
        Assert.Equal(JumpInfoRouteSource.NavRoute, plan.Source);
        Assert.Equal("N", plan.Target.StarClass);
        Assert.Equal(2, plan.JumpNumber);
        Assert.Equal(3, plan.Legs.Count);
        Assert.Equal(55, plan.TotalDistanceLy);
        Assert.True(plan.Legs[0].IsScoopable);
        Assert.True(plan.Legs[1].RequiresBoost);
        Assert.True(plan.Legs[2].IsScoopable);
    }

    [Fact]
    public void ShortNavRouteFallsBackToActiveFollowedRoute()
    {
        var navRoute = new NavRouteSnapshot(
            DateTimeOffset.UtcNow,
            "NavRoute",
            [Entry("Sol", 1, 0, "G"), Entry("Alpha", 2, 10, "K")]);
        var followed = new FollowRouteDocument(
            "F123",
            "route.json",
            true,
            true,
            0,
            [
                Hop("Sol", 1, 0),
                Hop("Alpha", 2, 10),
                Hop("Jackson's Lighthouse", 3, 20, neutron: true),
            ]);

        var plan = JumpInfoRoutePlanner.Create(
            new JumpInfoRoutePlannerRequest(
                null,
                new EliteStatus
                {
                    Destination = new StatusDestination
                    {
                        Name = "Jackson's Lighthouse",
                        System = 3,
                        Body = 0,
                    },
                },
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0),
                navRoute,
                followed));

        Assert.NotNull(plan);
        Assert.Equal(JumpInfoRouteSource.FollowedRoute, plan.Source);
        Assert.Equal(2, plan.JumpNumber);
        Assert.Equal(2, plan.Legs.Count);
        Assert.True(plan.Legs[1].RequiresBoost);
        Assert.Equal("N", plan.Target.StarClass);
    }

    [Fact]
    public void TargetOutsideRouteCreatesDirectPlanWhenCoordinatesAreUnknown()
    {
        var plan = JumpInfoRoutePlanner.Create(
            new JumpInfoRoutePlannerRequest(
                new JumpTarget("Unlisted", 99, "A"),
                null,
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0),
                new NavRouteSnapshot(
                    DateTimeOffset.UtcNow,
                    "NavRoute",
                    [
                        Entry("Sol", 1, 0, "G"),
                        Entry("Alpha", 2, 10, "K"),
                        Entry("Beta", 3, 20, "M"),
                    ]),
                null));

        Assert.NotNull(plan);
        Assert.Equal(JumpInfoRouteSource.Direct, plan.Source);
        Assert.Empty(plan.Legs);
        Assert.Equal("Unlisted", plan.Target.Name);
    }

    private static NavRouteEntry Entry(
        string name,
        long address,
        double x,
        string starClass)
    {
        return new NavRouteEntry(
            name,
            address,
            new GalacticCoordinate(x, 0, 0),
            starClass);
    }

    private static FollowRouteHop Hop(
        string name,
        long address,
        double x,
        bool neutron = false)
    {
        return new FollowRouteHop(
            name,
            address,
            new GalacticCoordinate(x, 0, 0),
            null,
            false,
            neutron);
    }
}
