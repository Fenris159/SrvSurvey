using System.Net;
using System.Text;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class RavenColonialCarrierManifestTests
{
    private const string CarrierJson =
        """{"marketId":"42","name":"ABC-123","displayName":"Team supplies","owner":"Other Cmdr","systemName":"Sol","access":"All","notorious":true,"cargo":{"steel":"100"}}""";

    [Theory]
    [InlineData("platinum", "Platinum", "Metals")]
    [InlineData("metaalloys", "Meta-Alloys", "Industrial Materials")]
    [InlineData("metalloys", "Meta-Alloys", "Industrial Materials")]
    [InlineData("ancientrelic", "Guardian Relic", "Salvage")]
    [InlineData("usscargoblackbox", "Black Box", "Salvage")]
    [InlineData("lavianbrandy", "Lavian Brandy", "Legal Drugs")]
    [InlineData("micro controllers", "Micro Controllers", "Technology")]
    public void KnownManifestGoodsUseTheirProperNamesAndCategories(string key, string name, string category)
    {
        var commodity = ColonizationManifestCommodity.ForKey(key);
        Assert.Equal(name, commodity.Name);
        Assert.Equal(category, commodity.Category);
    }

    [Fact]
    public void ColonyFilterUsesActualConstructionGoodsAndRecognizesLegacyNames()
    {
        Assert.True(ColonizationManifestCommodity.ForKey("steel").IsColonyCommodity);
        Assert.True(ColonizationManifestCommodity.ForKey("micro controllers").IsColonyCommodity);
        Assert.False(ColonizationManifestCommodity.ForKey("tritium").IsColonyCommodity);
        Assert.False(ColonizationManifestCommodity.ForKey("platinum").IsColonyCommodity);
        Assert.False(ColonizationManifestCommodity.ForKey("unknown").IsColonyCommodity);
        Assert.DoesNotContain(ColonizationManifestCommodity.All, item => item.Category == "?");
    }

    [Fact]
    public async Task ReadsAllLinkedCarriersIncludingOthersAndRetainsMetadata()
    {
        using var http = new HttpClient(
            new Handler(request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.EndsWith("/api/cmdr/Test%20Cmdr/fc/all", request.RequestUri!.AbsolutePath);
                return Json("[" + CarrierJson + "]");
            })
        );
        var client = new RavenColonialClient(http);
        ColonizationFleetCarrier carrier = Assert.Single(
            await client.GetLinkedFleetCarriersAsync(" Test Cmdr ", CancellationToken.None)
        );
        Assert.Equal("Other Cmdr", carrier.Owner);
        Assert.Equal("Sol", carrier.SystemName);
        Assert.Equal("All", carrier.Access);
        Assert.True(carrier.Notorious);
        Assert.Equal(100, carrier.Cargo["steel"]);
        Assert.Contains(ColonizationManifestCommodity.All, item => item.Key == "steel" && item.Category == "Metals");
        Assert.Equal("Other", ColonizationManifestCommodity.ForKey("unknown spaced key").Category);
        Assert.Equal("Steel", ColonizationManifestCommodity.ForKey("STEEL").Name);
    }

    /// <summary>
    /// Locks refresh and location request routes, required query identity, and initiating credentials to the API
    /// contract.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostsBodylessRefreshAndEncodedLocationWithTheInitiatingKey(bool location)
    {
        using var http = new HttpClient(
            new Handler(request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Null(request.Content);
                Assert.Equal("key", Assert.Single(request.Headers.GetValues("rcc-key")));
                Assert.EndsWith(
                    location ? "/api/fc/42/location/Alpha%20Centauri" : "/api/fc/42/refresh",
                    request.RequestUri!.AbsolutePath
                );
                Assert.Equal(location ? "?marketIdOrName=42" : string.Empty, request.RequestUri.Query);
                return Json(CarrierJson);
            })
        );
        var client = new RavenColonialClient(http);
        ColonizationFleetCarrier carrier = location
            ? await client.SetFleetCarrierLocationAsync(42, " Alpha Centauri ", " key ", CancellationToken.None)
            : await client.RefreshFleetCarrierAsync(42, " key ", CancellationToken.None);
        Assert.Equal(42, carrier.MarketId);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.RefreshFleetCarrierAsync(0, "key", CancellationToken.None)
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SetFleetCarrierLocationAsync(42, " ", "key", CancellationToken.None)
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.RefreshFleetCarrierAsync(42, " ", CancellationToken.None)
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetLinkedFleetCarriersAsync(" ", CancellationToken.None)
        );
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Denied")]
    [InlineData(HttpStatusCode.TooManyRequests, "Wait five minutes")]
    public async Task PreservesServerErrorsForEditorFeedback(HttpStatusCode status, string message)
    {
        using var http = new HttpClient(
            new Handler(_ => new HttpResponseMessage(status) { Content = new StringContent(message) })
        );
        var client = new RavenColonialClient(http);
        RavenColonialServiceException error = await Assert.ThrowsAsync<RavenColonialServiceException>(() =>
            client.RefreshFleetCarrierAsync(42, "key", CancellationToken.None)
        );
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public async Task PropagatesMalformedResponsesAndCancellation()
    {
        using var http = new HttpClient(new Handler(_ => Json("not json")));
        var client = new RavenColonialClient(http);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.RefreshFleetCarrierAsync(42, "key", CancellationToken.None)
        );
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.RefreshFleetCarrierAsync(42, "key", cancelled.Token)
        );
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
