using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class MiningSystemImportsTests
{
    private static readonly MiningMarketQuery Query = new("Wille", "Any", false, SystemOnly: true);

    [Fact]
    public async Task ArdentImportsForTheWantedMaterialsAreUsedWithoutSpansh()
    {
        var provider = new StubMiningSearchProvider
        {
            Imports = (system, _) =>
                [
                    StubMiningSearchProvider.Quote(system, "Port", "Monazite", 300_000),
                    StubMiningSearchProvider.Quote(system, "Port", "Gold", 50_000),
                ],
        };

        (MiningMarketResult[] quotes, bool usedFallback) = await MiningSystemImports.FindAsync(
            provider,
            "Wille",
            "Sell",
            Query,
            Filter(["monazite"]),
            CancellationToken.None
        );

        Assert.Equal("Monazite", Assert.Single(quotes).Commodity);
        Assert.False(usedFallback);
        Assert.DoesNotContain(provider.Calls, call => call.StartsWith("spansh", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnavailableArdentImportsFallBackToFilteredSpanshCommodities()
    {
        DateTimeOffset stale = DateTimeOffset.UtcNow - TimeSpan.FromDays(10);
        var provider = new StubMiningSearchProvider
        {
            Imports = (_, _) => throw new HttpRequestException("offline"),
            SpanshCommodities = (_, system) =>
                [
                    StubMiningSearchProvider.Quote(system, "Kept", "Monazite", 300_000),
                    StubMiningSearchProvider.Quote(system, "Other commodity", "Gold", 300_000),
                    StubMiningSearchProvider.Quote(system, "Low demand", "Monazite", 300_000, demand: 5),
                    StubMiningSearchProvider.Quote(system, "High demand", "Monazite", 300_000, demand: 9_000),
                    StubMiningSearchProvider.Quote(system, "Stale", "Monazite", 300_000) with
                    {
                        Updated = stale,
                    },
                    StubMiningSearchProvider.Quote(system, "Small pad", "Monazite", 300_000) with
                    {
                        LargePad = false,
                    },
                ],
        };

        (MiningMarketResult[] quotes, bool usedFallback) = await MiningSystemImports.FindAsync(
            provider,
            "Wille",
            "Sell",
            Query,
            Filter(["Monazite"], minimumDemand: 10, maximumDemand: 5_000, maximumAge: TimeSpan.FromDays(2), pad: "L"),
            CancellationToken.None
        );

        Assert.Equal("Kept", Assert.Single(quotes).Station);
        Assert.True(usedFallback);
        Assert.Contains("spansh Wille Sell", provider.Calls);
    }

    [Fact]
    public async Task QuotedSaleFallbackRejectsUnpricedSpanshRowsAndUnlimitedFiltersKeepTheRest()
    {
        var provider = new StubMiningSearchProvider
        {
            SpanshCommodities = (_, system) =>
                [
                    StubMiningSearchProvider.Quote(system, "Priced", "Monazite", 300_000),
                    StubMiningSearchProvider.Quote(system, "Unpriced", "Monazite", 0),
                    StubMiningSearchProvider.Quote(system, "No demand", "Monazite", 300_000, demand: 0),
                ],
        };

        (MiningMarketResult[] quoted, _) = await MiningSystemImports.FindAsync(
            provider,
            "Wille",
            "Sell",
            Query,
            Filter(["Monazite"], maximumDemand: 0) with
            {
                RequireQuotedSale = true,
            },
            CancellationToken.None
        );
        (MiningMarketResult[] all, _) = await MiningSystemImports.FindAsync(
            provider,
            "Wille",
            "Sell",
            Query,
            Filter(["Monazite"], maximumDemand: 0),
            CancellationToken.None
        );

        Assert.Equal("Priced", Assert.Single(quoted).Station);
        Assert.Equal(3, all.Length);
    }

    [Fact]
    public void PadSizesMatchTheQuotedLandingPad()
    {
        MiningMarketResult large = StubMiningSearchProvider.Quote("Sell", "Large", "Monazite", 1);
        MiningMarketResult medium = large with { LargePad = false, QuotedPad = "Medium" };
        MiningMarketResult small = large with { LargePad = false, QuotedPad = "Small" };

        Assert.True(MiningImportFilter.MatchesPad(large, "L"));
        Assert.False(MiningImportFilter.MatchesPad(medium, "L"));
        Assert.True(MiningImportFilter.MatchesPad(medium, "M"));
        Assert.False(MiningImportFilter.MatchesPad(small, "M"));
        Assert.True(MiningImportFilter.MatchesPad(small, "S"));
        Assert.False(MiningImportFilter.MatchesPad(large, "S"));
        Assert.True(MiningImportFilter.MatchesPad(small, "Any"));
    }

    private static MiningImportFilter Filter(
        IReadOnlyList<string> materials,
        long minimumDemand = 0,
        long maximumDemand = 90_000,
        TimeSpan? maximumAge = null,
        string pad = "Any"
    ) => new(materials, minimumDemand, maximumDemand, maximumAge, pad);
}
