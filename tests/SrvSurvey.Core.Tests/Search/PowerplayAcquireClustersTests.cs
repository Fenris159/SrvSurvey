using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class PowerplayAcquireClustersTests
{
    private sealed record Sell(string Name, string[] Miners);

    [Fact]
    public void SellSystemsSharingAMiningSystemClusterInResultOrder()
    {
        Sell first = new("First", ["Alpha", "Terminus"]);
        Sell separate = new("Separate", ["Gamma"]);
        Sell second = new("Second", ["terminus", "Beta"]);
        Sell bridged = new("Bridged", ["Delta"]);
        Sell bridge = new("Bridge", ["Gamma", "Delta"]);

        IReadOnlyList<IReadOnlyList<Sell>> clusters = PowerplayAcquireClusters.Group(
            [first, separate, second, bridged, bridge],
            sell => sell.Miners
        );

        Assert.Equal(2, clusters.Count);
        Assert.Equal(["First", "Second"], clusters[0].Select(sell => sell.Name));
        Assert.Equal(["Separate", "Bridged", "Bridge"], clusters[1].Select(sell => sell.Name));
    }

    [Fact]
    public void SellSystemsWithoutSharedMinersStaySingle()
    {
        IReadOnlyList<IReadOnlyList<Sell>> clusters = PowerplayAcquireClusters.Group(
            [new Sell("One", ["A"]), new Sell("Two", []), new Sell("Three", ["B"])],
            sell => sell.Miners
        );

        Assert.Equal(["One", "Two", "Three"], clusters.Select(cluster => Assert.Single(cluster).Name));
        Assert.Empty(PowerplayAcquireClusters.Group(Array.Empty<Sell>(), sell => sell.Miners));
    }
}
