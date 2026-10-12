using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Exploration;

public sealed class EdAstroPoiClientTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-poi-test-" + Guid.NewGuid().ToString("N")
    );
    private const string CatalogueJson = """
        [{"id":43,"source":"GEC","name":"Great Annihilator","galMapSearch":"Great Annihilator","id64":"2587943",
          "coordinates":[3,4,0],"type":"Stellar Features","type2":"Historical","summary":"Two black holes",
          "descriptionMardown":"Details","rating":6.92,"avgStars":4.16,"votes":25,"rare":1}]
        """;

    /// <summary>Verifies download persists validated snapshot and restores without network.</summary>
    [Fact]
    public async Task DownloadPersistsValidatedSnapshotAndRestoresWithoutNetwork()
    {
        var handler = new Handler();
        using var http = new HttpClient(handler);
        using var client = new EdAstroPoiClient(directory, http, trafficBudget: new EdAstroTrafficBudget());
        ExplorationPoiLoadResult result = await client.GetAsync();
        Assert.False(result.IsCached);
        Assert.Null(result.Warning);
        Assert.True(File.Exists(Path.Combine(directory, "edastro-gec.json")));
        Assert.Equal("https://edastro.com/gec/json/all", Assert.Single(handler.Requests));
        ExplorationPoi poi = Assert.Single(result.Catalog.ForSystem("Great Annihilator", 2587943));
        Assert.Equal("GEC:43", poi.Identity);
        Assert.Equal(new GalacticCoordinate(3, 4, 0), poi.Position);
        Assert.Equal("Stellar Features · Historical", poi.CategoryText);
        Assert.Equal(6.92, poi.Rating);
        Assert.Equal(4.16, poi.AverageStars);
        Assert.Equal(25, poi.Votes);
        Assert.True(poi.IsRare);
        Assert.Equal("https://edastro.com/gec/view/43", poi.SourceUri!.AbsoluteUri);
        Assert.True((await client.GetAsync(true)).IsCached);
        using var restored = new EdAstroPoiClient(directory, http, trafficBudget: new EdAstroTrafficBudget());
        Assert.Single((await restored.GetAsync()).Catalog.Items);
        Assert.Single(handler.Requests);
    }

    /// <summary>Non-GEC catalogue identities never open an unrelated GEC attraction with the same numeric ID.</summary>
    [Fact]
    public void OtherProviderIdentityDoesNotProduceGecSourceLink()
    {
        using var json = System.Text.Json.JsonDocument.Parse(
            CatalogueJson.Replace("GEC", "GMP", StringComparison.Ordinal)
        );
        ExplorationPoi poi = Assert.Single(ExplorationPoiCatalog.Parse(json.RootElement).Items);
        Assert.Equal("GMP:43", poi.Identity);
        Assert.Null(poi.SourceUri);
    }

    /// <summary>Verifies bad refresh retains last good disk and memory snapshot.</summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("bad json")]
    public async Task BadRefreshRetainsLastGoodDiskAndMemorySnapshot(string replacement)
    {
        var clock = new Clock();
        var handler = new Handler();
        using var http = new HttpClient(handler);
        using var client = new EdAstroPoiClient(directory, http, clock, new EdAstroTrafficBudget());
        await client.GetAsync();
        string before = await File.ReadAllTextAsync(Path.Combine(directory, "edastro-gec.json"));
        clock.Now += TimeSpan.FromDays(8);
        handler.Payload = replacement;
        ExplorationPoiLoadResult result = await client.GetAsync();
        Assert.Single(result.Catalog.Items);
        Assert.NotNull(result.Warning);
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(directory, "edastro-gec.json")));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    /// <summary>Verifies corrupt cache is replaced by valid download.</summary>
    [Theory]
    [InlineData("bad")]
    [InlineData("{}")]
    [InlineData("{\"fetchedAt\":\"invalid\",\"data\":[]}")]
    [InlineData("{\"fetchedAt\":0,\"data\":[]}")]
    [InlineData("{\"fetchedAt\":\"2026-10-11T00:00:00Z\",\"data\":[]}")]
    public async Task CorruptCacheIsReplacedByValidDownload(string cache)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "edastro-gec.json"), cache);
        using var http = new HttpClient(new Handler());
        using var client = new EdAstroPoiClient(directory, http, trafficBudget: new EdAstroTrafficBudget());
        Assert.Single((await client.GetAsync()).Catalog.Items);
    }

    /// <summary>Verifies quota and retry headers prevent repeated requests.</summary>
    [Fact]
    public async Task QuotaAndRetryHeadersPreventRepeatedRequests()
    {
        var clock = new Clock();
        var handler = new Handler { Status = HttpStatusCode.TooManyRequests, QuotaExhausted = true };
        using var http = new HttpClient(handler);
        using var client = new EdAstroPoiClient(directory, http, clock, new EdAstroTrafficBudget());
        Assert.NotNull((await client.GetAsync()).Warning);
        clock.Now += TimeSpan.FromMinutes(2);
        await client.GetAsync(true);
        Assert.Single(handler.Requests);
        clock.Now += TimeSpan.FromMinutes(15);
        handler.Status = HttpStatusCode.OK;
        handler.QuotaExhausted = false;
        Assert.Single((await client.GetAsync()).Catalog.Items);
        Assert.Equal(2, handler.Requests.Count);
    }

    /// <summary>Verifies timeout and oversized responses fail safely and cancellation propagates.</summary>
    [Fact]
    public async Task TimeoutAndOversizedResponsesFailSafelyAndCancellationPropagates()
    {
        var handler = new Handler { Failure = new OperationCanceledException("timeout") };
        using var http = new HttpClient(handler);
        using var client = new EdAstroPoiClient(directory, http, trafficBudget: new EdAstroTrafficBudget());
        Assert.NotNull((await client.GetAsync()).Warning);
        using var other = new EdAstroPoiClient(directory, http, trafficBudget: new EdAstroTrafficBudget());
        handler.Failure = null;
        handler.Oversized = true;
        Assert.NotNull((await other.GetAsync()).Warning);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetAsync(cancellationToken: cancellation.Token)
        );
    }

    /// <summary>Verifies parser keeps source identities and every attraction in a system.</summary>
    [Fact]
    public void ParserKeepsSourceIdentitiesAndEveryAttractionInASystem()
    {
        using var json = JsonDocument.Parse(
            """
            [{"id":1,"source":"GEC","name":"One","galMapSearch":"Sol","id64":10,"type":"Stellar Features"},
             {"id":1,"source":"GMP","name":"Two","galMapSearch":"Sol","descriptionHtml":"<b>Scenery &amp; stars</b>","coordinates":["1","2","3"]},
             {"id":2,"name":"Legacy","galMapSearch":"Sol","type2":"Legacy"},
             {"id":3,"name":"Three","galMapSearch":"Sol","coordinates":[0,"invalid",2],"rating":"NaN","votes":-5},
             {"id":4,"name":"Four","galMapSearch":"Sol","coordinates":[1,2],"rare":true},
             {"id":1,"source":"GEC","name":"Duplicate","galMapSearch":"Sol"},
             4, {}, {"id":0}, {"id":9,"name":""}, {"id":10,"name":"Missing system"}]
            """
        );
        var catalog = ExplorationPoiCatalog.Parse(json.RootElement);
        Assert.Equal(5, catalog.Items.Count);
        Assert.Equal(4, catalog.ForSystem(" sol ", 10).Count);
        Assert.Equal(5, catalog.ForSystem("Sol", 10, true).Count);
        Assert.Equal(3, catalog.ForSystem("Sol", 99).Count);
        Assert.Empty(catalog.ForSystem("Elsewhere", null));
        ExplorationPoi second = catalog.Items.Single(p => p.Source == "GMP");
        Assert.Equal("Scenery & stars", second.Description);
        Assert.Equal(new GalacticCoordinate(1, 2, 3), second.Position);
        Assert.Null(catalog.Items.Single(p => p.Id == 3).Position);
        Assert.Null(catalog.Items.Single(p => p.Id == 3).Rating);
        Assert.Equal(0, catalog.Items.Single(p => p.Id == 3).Votes);
        Assert.Null(catalog.Items.Single(p => p.Id == 4).Position);
        Assert.True(catalog.Items.Single(p => p.Id == 4).IsRare);
        using var invalid = JsonDocument.Parse("{}");
        Assert.Throws<InvalidDataException>(() => ExplorationPoiCatalog.Parse(invalid.RootElement));
    }

    /// <summary>Verifies exhausted shared quota keeps catalogue reads offline.</summary>
    [Fact]
    public async Task ExhaustedSharedQuotaKeepsCatalogueReadsOffline()
    {
        using var http = new HttpClient(new Handler());
        var budget = new EdAstroTrafficBudget();
        var clock = new Clock();
        for (int i = 0; i < 100; i++)
        {
            Assert.True(budget.TryAcquire(clock.Now, out _));
        }
        using var client = new EdAstroPoiClient(directory, http, clock, budget);
        ExplorationPoiLoadResult result = await client.GetAsync();
        Assert.Empty(result.Catalog.Items);
        Assert.Contains("shared request quota", result.Warning!, StringComparison.Ordinal);
        clock.Now += TimeSpan.FromMinutes(15);
        Assert.NotEmpty((await client.GetAsync()).Catalog.Items);
    }

    /// <summary>Releases the isolated test resources after assertions complete.</summary>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);

        /// <summary>Provides get utc now for the isolated test fixture.</summary>
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public string Payload { get; set; } = CatalogueJson;
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public bool QuotaExhausted { get; set; }
        public bool Oversized { get; set; }
        public Exception? Failure { get; set; }

        /// <summary>Records the request and returns the controlled response for this test.</summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            if (Failure is not null)
            {
                return Task.FromException<HttpResponseMessage>(Failure);
            }

            var response = new HttpResponseMessage(Status) { Content = new StringContent(Payload) };
            if (Oversized)
            {
                response.Content.Headers.ContentLength = 20 * 1024 * 1024;
            }

            if (QuotaExhausted)
            {
                response.Headers.Add("RateLimit-Remaining", "0");
                response.Headers.Add("RateLimit-Reset", "900");
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            }
            return Task.FromResult(response);
        }
    }
}
