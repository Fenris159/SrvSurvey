using SrvSurvey.Core.Routes;

namespace SrvSurvey.Core.Tests.Routes;

public sealed class SpanshRouteUrlParserTests
{
    private static readonly Guid RouteId = Guid.Parse(
        "74FA2952-2048-11F1-8302-B948FF6DF5C1");

    [Theory]
    [InlineData(
        "https://spansh.co.uk/riches/results/74FA2952-2048-11F1-8302-B948FF6DF5C1",
        SpanshRouteKind.Riches)]
    [InlineData(
        "https://spansh.co.uk/ammonia/results/74FA2952-2048-11F1-8302-B948FF6DF5C1",
        SpanshRouteKind.Riches)]
    [InlineData(
        "https://spansh.co.uk/earth/results/74FA2952-2048-11F1-8302-B948FF6DF5C1",
        SpanshRouteKind.Riches)]
    [InlineData(
        "https://spansh.co.uk/rocky-metal/results/74FA2952-2048-11F1-8302-B948FF6DF5C1",
        SpanshRouteKind.Riches)]
    [InlineData(
        "https://spansh.co.uk/exobiology/results/74FA2952-2048-11F1-8302-B948FF6DF5C1",
        SpanshRouteKind.Exobiology)]
    [InlineData(
        "https://spansh.co.uk/tourist/results/74FA2952-2048-11F1-8302-B948FF6DF5C1?source=Sol",
        SpanshRouteKind.Tourist)]
    [InlineData(
        "https://www.spansh.co.uk/plotter/results/74FA2952-2048-11F1-8302-B948FF6DF5C1",
        SpanshRouteKind.Neutron)]
    [InlineData(
        "https://spansh.co.uk/exact-plotter/results/74FA2952-2048-11F1-8302-B948FF6DF5C1",
        SpanshRouteKind.Galaxy)]
    [InlineData(
        "https://spansh.co.uk/fleet-carrier/results/74FA2952-2048-11F1-8302-B948FF6DF5C1",
        SpanshRouteKind.FleetCarrier)]
    [InlineData(
        "https://spansh.co.uk/colonisation/results/74FA2952-2048-11F1-8302-B948FF6DF5C1",
        SpanshRouteKind.Colonisation)]
    [InlineData(
        "https://spansh.co.uk/trade/results/74FA2952-2048-11F1-8302-B948FF6DF5C1",
        SpanshRouteKind.Trade)]
    public void ParseRecognizesAllSpanshRouteFamilies(
        string url,
        SpanshRouteKind expectedKind)
    {
        var parsed = SpanshRouteUrlParser.TryParse(url, out var route);

        Assert.True(parsed);
        Assert.NotNull(route);
        Assert.Equal(RouteId, route.JobId);
        Assert.Equal(expectedKind, route.Kind);
    }

    [Fact]
    public void TouristSearchUsesTheJobIdAfterResults()
    {
        var searchId = Guid.Parse("55C5C3EC-FEC5-48B8-A7BC-83435559521D");
        var parsed = SpanshRouteUrlParser.TryParse(
            $"https://spansh.co.uk/tourist-search/{searchId:D}/results/{RouteId:D}",
            out var route);

        Assert.True(parsed);
        Assert.Equal(
            new SpanshRouteReference(RouteId, SpanshRouteKind.Tourist),
            route);
    }

    [Fact]
    public void ParseAcceptsAJobIdWithoutAUrl()
    {
        var parsed = SpanshRouteUrlParser.TryParse(
            RouteId.ToString(),
            out var route);

        Assert.True(parsed);
        Assert.Equal(
            new SpanshRouteReference(RouteId, SpanshRouteKind.Generic),
            route);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/tourist/results/74FA2952-2048-11F1-8302-B948FF6DF5C1")]
    [InlineData("https://spansh.co.uk/tourist/results/not-a-guid")]
    [InlineData("https://spansh.co.uk/bodies/search/74FA2952-2048-11F1-8302-B948FF6DF5C1/1")]
    public void ParseRejectsInvalidOrNonSpanshUrls(string? value)
    {
        Assert.False(SpanshRouteUrlParser.TryParse(value, out var route));
        Assert.Null(route);
    }
}
