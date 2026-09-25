using System.Globalization;
using System.Net;
using System.Text.Json;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class MiningProviderResponseCacheTests
{
    [Fact]
    public void ResponseSurvivesReopeningUntilItsMaximumAge()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mining-provider-cache-" + Guid.NewGuid().ToString("N"));
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var cache = new MiningProviderResponseCache(directory, clock);
            using var response = JsonDocument.Parse("""{"price":123}""");
            cache.Save("station/Sol", response);

            using JsonDocument? reopened = new MiningProviderResponseCache(directory, clock).Load(
                "station/Sol",
                TimeSpan.FromMinutes(10)
            );
            Assert.Equal(123, reopened?.RootElement.GetProperty("price").GetInt32());

            clock.Advance(TimeSpan.FromMinutes(10));
            Assert.Null(cache.Load("station/Sol", TimeSpan.FromMinutes(10)));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ArdentUsesTheSameSavedResponseAcrossInstances()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ardent-provider-cache-" + Guid.NewGuid().ToString("N"));
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        using var handler = new CountingHandler();
        using var http = new HttpClient(handler);
        try
        {
            const string route = "system/name/Sol/commodities/imports?maxDaysAgo=2";
            var first = new ArdentApi(http, cache: new MiningProviderResponseCache(directory, clock));
            using JsonDocument initial = await first.GetAsync(route, 1024, "test", TimeSpan.FromMinutes(10));
            var second = new ArdentApi(http, cache: new MiningProviderResponseCache(directory, clock));
            using JsonDocument reused = await second.GetAsync(route, 1024, "test", TimeSpan.FromMinutes(10));
            Assert.Equal(1, handler.RequestCount);
            Assert.Equal(initial.RootElement.GetRawText(), reused.RootElement.GetRawText());

            clock.Advance(TimeSpan.FromMinutes(11));
            using JsonDocument refreshed = await second.GetAsync(route, 1024, "test", TimeSpan.FromMinutes(10));
            Assert.Equal(2, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task EmptyArdentResponseIsReusedBrieflyAcrossInstances()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ardent-empty-cache-" + Guid.NewGuid().ToString("N"));
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        using var handler = new EmptyHandler();
        using var http = new HttpClient(handler);
        try
        {
            const string route = "system/name/Sol/commodities/imports?maxDaysAgo=2";
            var first = new ArdentApi(http, cache: new MiningProviderResponseCache(directory, clock));
            using JsonDocument initial = await first.GetAsync(route, 1024, "test", TimeSpan.FromMinutes(10));
            Assert.Equal(0, initial.RootElement.GetArrayLength());

            var second = new ArdentApi(http, cache: new MiningProviderResponseCache(directory, clock));
            using JsonDocument reused = await second.GetAsync(route, 1024, "test", TimeSpan.FromMinutes(10));
            Assert.Equal(1, handler.RequestCount);

            clock.Advance(TimeSpan.FromMinutes(2));
            using JsonDocument refreshed = await second.GetAsync(route, 1024, "test", TimeSpan.FromMinutes(10));
            Assert.Equal(2, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task SystemImportsReuseStationDataAndApplyNewDemandLocally()
    {
        string directory = Path.Combine(Path.GetTempPath(), "station-import-cache-" + Guid.NewGuid().ToString("N"));
        using var handler = new StationImportsHandler();
        using var http = new HttpClient(handler);
        try
        {
            var first = new MiningSearchClient(http, providerResponseCache: new MiningProviderResponseCache(directory));
            IReadOnlyList<MiningMarketResult> broad = await first.FindSystemImportsAsync(
                "Sol",
                new MiningMarketQuery("Sol", "Any", false, MinimumDemand: 0, MaximumAge: TimeSpan.FromDays(2))
            );
            Assert.Equal(2, broad.Count);

            var reopened = new MiningSearchClient(
                http,
                providerResponseCache: new MiningProviderResponseCache(directory)
            );
            IReadOnlyList<MiningMarketResult> filtered = await reopened.FindSystemImportsAsync(
                "Sol",
                new MiningMarketQuery("Sol", "Any", false, MinimumDemand: 200, MaximumAge: TimeSpan.FromDays(2))
            );
            Assert.Single(filtered);
            Assert.Equal("High Demand", filtered[0].Station);
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("2026-09-24T06:59:59Z", "2026-09-17T07:00:00Z")]
    [InlineData("2026-09-24T07:00:00Z", "2026-09-24T07:00:00Z")]
    [InlineData("2026-09-25T12:00:00Z", "2026-09-24T07:00:00Z")]
    public void PowerplayGeometryChangesCycleKeyAtTheWeeklyBoundary(string input, string expected)
    {
        Assert.Equal(
            DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture),
            MiningSearchClient.PowerplayCycleStart(DateTimeOffset.Parse(input, CultureInfo.InvariantCulture))
        );
    }

    [Fact]
    public async Task AcquisitionSupporterCoordinatesSurviveRestartButProgressDoesNot()
    {
        string directory = Path.Combine(Path.GetTempPath(), "acquire-provider-cache-" + Guid.NewGuid().ToString("N"));
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        using var handler = new SupporterHandler();
        using var http = new HttpClient(handler);
        try
        {
            var first = new MiningSearchClient(
                http,
                clock,
                providerResponseCache: new MiningProviderResponseCache(directory, clock)
            );
            IReadOnlyList<MiningSystemResult> original = await first.FindAcquireSupportersAsync(
                "Timbalderis",
                "Arissa Lavigny-Duval",
                "Fortified"
            );
            Assert.Equal(1, handler.RequestCount);
            Assert.Equal(1, Assert.Single(original).Position?.X);
            Assert.Empty(original[0].Conflict);

            var reopened = new MiningSearchClient(
                http,
                clock,
                providerResponseCache: new MiningProviderResponseCache(directory, clock)
            );
            IReadOnlyList<MiningSystemResult> saved = await reopened.FindAcquireSupportersAsync(
                "Timbalderis",
                "Arissa Lavigny-Duval",
                "Fortified"
            );
            Assert.Single(saved);
            Assert.Equal(1, handler.RequestCount);

            clock.Advance(TimeSpan.FromDays(7));
            await reopened.FindAcquireSupportersAsync("Timbalderis", "Arissa Lavigny-Duval", "Fortified");
            Assert.Equal(2, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AcquisitionCandidateCoordinatesSurviveRestartWithoutKeepingProgress()
    {
        string directory = Path.Combine(Path.GetTempPath(), "acquire-candidate-cache-" + Guid.NewGuid().ToString("N"));
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        using var handler = new CandidateHandler();
        using var http = new HttpClient(handler);
        var query = new MiningSystemQuery("Deciat", 20, Objective: PowerplayPlan.Acquire);
        try
        {
            var first = new MiningSearchClient(
                http,
                clock,
                providerResponseCache: new MiningProviderResponseCache(directory, clock)
            );
            MiningSystemPage initial = await first.FindAcquireCandidatePageAsync(query);
            Assert.Single(initial.Systems);
            Assert.False(initial.FromCache);
            Assert.NotEmpty(initial.Systems[0].Conflict);

            var reopened = new MiningSearchClient(
                http,
                clock,
                providerResponseCache: new MiningProviderResponseCache(directory, clock)
            );
            MiningSystemPage saved = await reopened.FindAcquireCandidatePageAsync(query);
            Assert.Single(saved.Systems);
            Assert.True(saved.FromCache);
            Assert.Equal(1, saved.Systems[0].Position?.X);
            Assert.Equal(PowerplayStanding.Unoccupied, saved.Systems[0].PowerState);
            Assert.Empty(saved.Systems[0].Conflict);
            Assert.Equal(1, handler.RequestCount);

            clock.Advance(TimeSpan.FromDays(7));
            await reopened.FindAcquireCandidatePageAsync(query);
            Assert.Equal(2, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task IncompatibleAcquisitionCacheFallsBackToSpansh()
    {
        string directory = Path.Combine(Path.GetTempPath(), "acquire-invalid-cache-" + Guid.NewGuid().ToString("N"));
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        using var handler = new SupporterHandler();
        using var http = new HttpClient(handler);
        try
        {
            var cache = new MiningProviderResponseCache(directory, clock);
            DateTimeOffset cycle = MiningSearchClient.PowerplayCycleStart(clock.GetUtcNow());
            string key = $"spansh-acquire-supporters:v1:{cycle:O}:settled:Timbalderis:A. Lavigny-Duval:Fortified";
            var client = new MiningSearchClient(http, clock, providerResponseCache: cache);
            Assert.Single(await client.FindAcquireSupportersAsync("Timbalderis", "Arissa Lavigny-Duval", "Fortified"));
            using JsonDocument? populated = cache.Load(key, TimeSpan.FromDays(7));
            Assert.NotNull(populated);
            int requestsBeforeFallback = handler.RequestCount;
            using var incompatible = JsonDocument.Parse("""{"old":"schema"}""");
            cache.Save(key, incompatible);

            IReadOnlyList<MiningSystemResult> systems = await client.FindAcquireSupportersAsync(
                "Timbalderis",
                "Arissa Lavigny-Duval",
                "Fortified"
            );
            Assert.Single(systems);
            Assert.Equal(requestsBeforeFallback + 1, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AcquisitionGeometryUsesShortCacheDuringTickSettling()
    {
        string directory = Path.Combine(Path.GetTempPath(), "acquire-tick-cache-" + Guid.NewGuid().ToString("N"));
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 24, 7, 10, 0, TimeSpan.Zero));
        using var handler = new SupporterHandler();
        using var http = new HttpClient(handler);
        try
        {
            var client = new MiningSearchClient(
                http,
                clock,
                providerResponseCache: new MiningProviderResponseCache(directory, clock)
            );
            await client.FindAcquireSupportersAsync("Timbalderis", "Arissa Lavigny-Duval", "Fortified");
            await client.FindAcquireSupportersAsync("Timbalderis", "Arissa Lavigny-Duval", "Fortified");
            Assert.Equal(1, handler.RequestCount);

            clock.Advance(TimeSpan.FromMinutes(6));
            await client.FindAcquireSupportersAsync("Timbalderis", "Arissa Lavigny-Duval", "Fortified");
            Assert.Equal(2, handler.RequestCount);

            clock.Advance(TimeSpan.FromMinutes(50));
            await client.FindAcquireSupportersAsync("Timbalderis", "Arissa Lavigny-Duval", "Fortified");
            Assert.Equal(3, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestCount++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""[{"price":123}]""") }
            );
        }
    }

    private sealed class EmptyHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }

    private sealed class SupporterHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestCount++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"results":[{"name":"Deciat","distance":85,"x":1,"y":2,"z":3,"controlling_power":"A. Lavigny-Duval","power_state":"Fortified","power_conflict_progress":[{"name":"A. Lavigny-Duval","progress":0.8}]}]}"""
                    ),
                }
            );
        }
    }

    private sealed class CandidateHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestCount++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"results":[{"name":"Open Target","distance":5,"x":1,"y":2,"z":3,"power_state":"Unoccupied","power_conflict_progress":[{"name":"A. Lavigny-Duval","progress":0.8}]}]}"""
                    ),
                }
            );
        }
    }

    private sealed class StationImportsHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestCount++;
            string updated = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""[{"systemName":"Sol","stationName":"Low Demand","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":1000,"demand":100,"updatedAt":"{{updated}}","commodityName":"Gold"},{"systemName":"Sol","stationName":"High Demand","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":900,"demand":500,"updatedAt":"{{updated}}","commodityName":"Gold"}]"""
                    ),
                }
            );
        }
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current += duration;
    }
}
