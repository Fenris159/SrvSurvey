using SrvSurvey.Core.Routes;

namespace SrvSurvey.Core.Tests.Routes;

public sealed class SpanshRouteUrlParserTests
{
    private static readonly Guid RouteId = Guid.Parse(
        "74FA2952-2048-11F1-8302-B948FF6DF5C1");

    [Theory]
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
        "https://spansh.co.uk/bodies/search/74FA2952-2048-11F1-8302-B948FF6DF5C1/1",
        SpanshRouteKind.Generic)]
    public void ParseRecognizesLegacySpanshRouteFamilies(
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
    public void ParseRejectsInvalidOrNonSpanshUrls(string? value)
    {
        Assert.False(SpanshRouteUrlParser.TryParse(value, out var route));
        Assert.Null(route);
    }
}
