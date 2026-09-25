using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class ArdentRouteTests
{
    [Fact]
    public void CarriersStayInTheResultsUntilTheSearchExcludesThem()
    {
        string included = ArdentRoutes.NearbyCommodity("Sol", "Platinum", "imports", 1, 2, "100", false);
        string excluded = ArdentRoutes.GalaxyCommodity("Platinum", "imports", 1, 2, true);

        Assert.DoesNotContain("fleetCarriers", included, StringComparison.Ordinal);
        Assert.EndsWith("&fleetCarriers=false", excluded, StringComparison.Ordinal);
    }
}
