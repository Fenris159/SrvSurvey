using System.Net;
using System.Text.Json.Nodes;
using SrvSurvey.Core.EdAstro;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.EdAstro;

public sealed class EdAstroPublisherTests
{
    /// <summary>Verifies consent replay legacy attribution and pause prevent collection.</summary>
    [Theory]
    [InlineData(false, true, true, "Cmdr", false)]
    [InlineData(true, false, true, "Cmdr", false)]
    [InlineData(true, true, false, "Cmdr", false)]
    [InlineData(true, true, true, null, false)]
    [InlineData(true, true, true, "Cmdr", true)]
    public async Task ConsentReplayLegacyAttributionAndPausePreventCollection(
        bool enabled,
        bool live,
        bool game,
        string? commander,
        bool pause
    )
    {
        using var fixture = new Fixture();
        fixture.Publisher.SetEnabled(enabled);
        fixture.Publisher.SetSuspended(pause);
        fixture.Publisher.Apply(Request([Event()], commander) with { AllowPublishing = live, IsLiveGame = game });
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Equal(0, fixture.Publisher.PendingCount);
        Assert.Empty(fixture.Wire);
    }

    /// <summary>Verifies wire envelope matches ed discovery with disclosed public fields and no credentials.</summary>
    [Fact]
    public async Task WireEnvelopeMatchesEdDiscoveryWithDisclosedPublicFieldsAndNoCredentials()
    {
        using var fixture = new Fixture();
        fixture.Publisher.SetEnabled(true);
        fixture.Publisher.Apply(
            Request([
                Event(
                    "CarrierStats",
                    "\"CarrierID\":123,\"Callsign\":\"ABC-123\",\"Name\":\"Explorer\",\"FuelLevel\":300,\"Finance\":{\"Balance\":999},\"EDDExtra\":true,\"Commander\":\"Private\",\"FID\":\"F123\""
                ),
                Event("Docked", "\"StationName\":\"Port\",\"StarSystem\":\"Sol\",\"Credits\":123"),
                Event(),
                Event("CodexEntry"),
                Event("CarrierJumpRequest"),
                Event("Loadout"),
            ])
        );
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Single(fixture.Wire);
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Single(fixture.Wire);
        fixture.Advance(10);
        await fixture.Publisher.ProcessPendingAsync();
        Wire request = fixture.Wire[1];
        JsonArray payload = JsonNode.Parse(request.Json!)!.AsArray();
        Assert.Equal("SrvSurvey-XP", payload[0]!["appName"]!.GetValue<string>());
        Assert.Equal("2.1.3.0", payload[0]!["appVersion"]!.GetValue<string>());
        Assert.True(payload[0]!["odyssey"]!.GetValue<bool>());
        Assert.Equal(6, payload.Count);
        Assert.Equal(123, payload[1]!["CarrierID"]!.GetValue<int>());
        Assert.Null(payload[1]!["Finance"]);
        Assert.DoesNotContain("Private", request.Json!, StringComparison.Ordinal);
        Assert.DoesNotContain("EDDExtra", request.Json!, StringComparison.Ordinal);
        Assert.DoesNotContain("Credits", request.Json!, StringComparison.Ordinal);
        Assert.Null(request.Authorization);
        Assert.Equal("SrvSurvey-XP/2.1.3.0", request.Agent);
        Assert.Equal("https://edastro.com/api/journal", request.Uri);
        Assert.Empty(fixture.Logs);
        Assert.Equal(0, fixture.Publisher.PendingCount);
    }

    /// <summary>Verifies large live reads are deduplicated and split into paced batches.</summary>
    [Fact]
    public async Task LargeLiveReadsAreDeduplicatedAndSplitIntoPacedBatches()
    {
        using var fixture = new Fixture();
        JournalEventEnvelope[] events = Enumerable.Range(0, 201).Select(i => Event(fields: $"\"Body\":{i}")).ToArray();
        fixture.Enable(events);
        fixture.Publisher.Apply(Request(events));
        Assert.Equal(201, fixture.Publisher.PendingCount);
        await fixture.Send();
        Assert.Equal(201, JsonNode.Parse(fixture.Wire[1].Json!)!.AsArray().Count);
        Assert.Equal(1, fixture.Publisher.PendingCount);
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Equal(2, fixture.Wire.Count);
        fixture.Advance(10);
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Equal(2, JsonNode.Parse(fixture.Wire[2].Json!)!.AsArray().Count);
        Assert.Equal(0, fixture.Publisher.PendingCount);
        fixture.Advance(900);
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Contains("201", Assert.Single(fixture.Logs), StringComparison.Ordinal);
    }

    /// <summary>Verifies captured sessions and expansion flags never mix.</summary>
    [Fact]
    public async Task CapturedSessionsAndExpansionFlagsNeverMix()
    {
        using var fixture = new Fixture();
        fixture.Enable([Event()]);
        fixture.Publisher.Apply(Request([Event(fields: "\"Body\":2")], "Other") with { IsOdyssey = false });
        await fixture.Send();
        Assert.Equal(1, fixture.Publisher.PendingCount);
        Assert.True(JsonNode.Parse(fixture.Wire[1].Json!)![0]!["odyssey"]!.GetValue<bool>());
        fixture.Advance(10);
        await fixture.Publisher.ProcessPendingAsync();
        Assert.False(JsonNode.Parse(fixture.Wire[2].Json!)![0]!["odyssey"]!.GetValue<bool>());
    }

    /// <summary>Verifies foreign commander boundary and untimed events are skipped.</summary>
    [Fact]
    public void ForeignCommanderBoundaryAndUntimedEventsAreSkipped()
    {
        using var fixture = new Fixture();
        fixture.Enable([Event() with { Timestamp = null }, Event("LoadGame", "\"Commander\":\"Different\""), Event()]);
        Assert.Equal(0, fixture.Publisher.PendingCount);
    }

    /// <summary>Verifies empty or unreviewed provider list never enables all events.</summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("[\"LoadGame\",\"Loadout\",\"NewUnreviewedEvent\"]")]
    public async Task EmptyOrUnreviewedProviderListNeverEnablesAllEvents(string list)
    {
        using var fixture = new Fixture();
        fixture.Responses.Enqueue(Response(list));
        fixture.Enable([Event()]);
        await fixture.Send();
        Assert.Single(fixture.Wire);
        Assert.Equal(0, fixture.Publisher.PendingCount);
    }

    /// <summary>Verifies invalid provider list fails closed and retries with backoff.</summary>
    [Theory]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("{}")]
    public async Task InvalidProviderListFailsClosedAndRetriesWithBackoff(string list)
    {
        using var fixture = new Fixture();
        fixture.Responses.Enqueue(Response(list));
        fixture.Enable([Event()]);
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Equal(1, fixture.Publisher.PendingCount);
        fixture.Advance(29);
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Single(fixture.Wire);
        fixture.Advance(1);
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Equal(HttpMethod.Get, fixture.Wire[1].Method);
        fixture.Advance(10);
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Equal(HttpMethod.Post, fixture.Wire[2].Method);
    }

    /// <summary>Verifies server quota and retry after headers override local pacing.</summary>
    [Fact]
    public async Task ServerQuotaAndRetryAfterHeadersOverrideLocalPacing()
    {
        using var fixture = new Fixture();
        fixture.Responses.Enqueue(Response(Fixture.Accepted));
        HttpResponseMessage busy = Response("busy", HttpStatusCode.TooManyRequests);
        busy.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
        busy.Headers.Add("RateLimit-Remaining", "0");
        busy.Headers.Add("RateLimit-Reset", "300");
        fixture.Responses.Enqueue(busy);
        fixture.Enable([Event()]);
        await fixture.Send();
        fixture.Advance(299);
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Equal(2, fixture.Wire.Count);
        fixture.Advance(1);
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Equal(0, fixture.Publisher.PendingCount);
    }

    /// <summary>Verifies rejected application identity is retained and does not cause rapid retries.</summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "{}")]
    [InlineData(HttpStatusCode.Unauthorized, "{}")]
    [InlineData(HttpStatusCode.OK, "{\"status\":403}")]
    public async Task RejectedApplicationIdentityIsRetainedAndDoesNotCauseRapidRetries(
        HttpStatusCode status,
        string body
    )
    {
        using var fixture = new Fixture();
        fixture.Responses.Enqueue(Response(Fixture.Accepted));
        fixture.Responses.Enqueue(Response(body, status));
        fixture.Enable([Event()]);
        await fixture.Send();
        Assert.Equal(1, fixture.Publisher.PendingCount);
        fixture.Advance(899);
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Equal(2, fixture.Wire.Count);
        fixture.Advance(1);
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Equal(0, fixture.Publisher.PendingCount);
        Assert.Single(fixture.Logs);
    }

    /// <summary>Verifies api results distinguish accepted and unwanted batches.</summary>
    [Theory]
    [InlineData("{\"status\":\"200\"}", 1)]
    [InlineData("{\"status\":401}", 0)]
    public async Task ApiResultsDistinguishAcceptedAndUnwantedBatches(string body, int logCount)
    {
        using var fixture = new Fixture();
        fixture.Responses.Enqueue(Response(Fixture.Accepted));
        fixture.Responses.Enqueue(Response(body));
        fixture.Enable([Event()]);
        await fixture.Send();
        Assert.Equal(0, fixture.Publisher.PendingCount);
        fixture.Publisher.Dispose();
        Assert.Equal(logCount, fixture.Logs.Count);
    }

    /// <summary>Verifies malformed upload response remains retryable.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"status\":\"invalid\"}")]
    [InlineData("{\"status\":true}")]
    [InlineData("{\"status\":null}")]
    [InlineData("{\"status\":999999999999}")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task MalformedUploadResponseRemainsRetryable(string body)
    {
        using var fixture = new Fixture();
        fixture.Responses.Enqueue(Response(Fixture.Accepted));
        fixture.Responses.Enqueue(Response(body));
        fixture.Enable([Event()]);
        await fixture.Send();
        Assert.Equal(1, fixture.Publisher.PendingCount);
        fixture.Advance(30);
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Equal(0, fixture.Publisher.PendingCount);
    }

    /// <summary>Verifies network failure and repeated server errors produce consolidated diagnostics.</summary>
    [Fact]
    public async Task NetworkFailureAndRepeatedServerErrorsProduceConsolidatedDiagnostics()
    {
        using var fixture = new Fixture();
        fixture.Enable([Event()]);
        fixture.Handler.Failure = new HttpRequestException("private request details");
        await fixture.Publisher.ProcessPendingAsync();
        fixture.Advance(30);
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Single(fixture.Logs);
        Assert.DoesNotContain("private", fixture.Logs[0], StringComparison.Ordinal);
        fixture.Handler.Failure = null;
        fixture.Advance(60);
        await fixture.Send();
        Assert.Equal(0, fixture.Publisher.PendingCount);
    }

    /// <summary>Verifies consent revocation cancels active delivery while pause preserves queued data.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsentRevocationCancelsActiveDeliveryWhilePausePreservesQueuedData(bool pause)
    {
        using var fixture = new Fixture();
        fixture.Enable([Event()]);
        await fixture.Publisher.ProcessPendingAsync();
        fixture.Advance(10);
        fixture.Handler.Block = true;
        Task sending = fixture.Publisher.ProcessPendingAsync();
        await fixture.Handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (pause)
        {
            fixture.Publisher.SetSuspended(true);
        }
        else
        {
            fixture.Publisher.SetEnabled(false);
        }
        await sending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(pause ? 1 : 0, fixture.Publisher.PendingCount);
        fixture.Handler.Block = false;
        if (pause)
        {
            fixture.Publisher.SetSuspended(false);
        }
        else
        {
            fixture.Enable([Event(fields: "\"Body\":99")]);
        }
        fixture.Advance(10);
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Equal(0, fixture.Publisher.PendingCount);
    }

    /// <summary>Verifies bounded queue expires without uploading stale reports.</summary>
    [Fact]
    public async Task BoundedQueueExpiresWithoutUploadingStaleReports()
    {
        using var fixture = new Fixture();
        fixture.Enable(Enumerable.Range(0, 4100).Select(i => Event(fields: $"\"Body\":{i}")).ToArray());
        Assert.Equal(4096, fixture.Publisher.PendingCount);
        Assert.Single(fixture.Logs);
        fixture.Advance(86401);
        await fixture.Publisher.ProcessPendingAsync();
        Assert.Equal(0, fixture.Publisher.PendingCount);
        Assert.Empty(fixture.Wire);
    }

    /// <summary>Verifies automatic worker stops without uploading after opt out.</summary>
    [Fact]
    public async Task AutomaticWorkerStopsWithoutUploadingAfterOptOut()
    {
        using var handler = new Handler();
        using var client = new HttpClient(handler);
        using var publisher = new EdAstroPublisher("2.1.3.0", client, trafficBudget: new EdAstroTrafficBudget());
        publisher.SetEnabled(true);
        publisher.Apply(Request([Event()]));
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        publisher.SetEnabled(false);
        Assert.Equal(0, publisher.PendingCount);
    }

    /// <summary>Unexpected adapter failures are consolidated and do not terminate the automatic delivery worker.</summary>
    [Fact]
    public async Task AutomaticWorkerRecoversAfterUnexpectedAdapterFailure()
    {
        using var handler = new Handler { Failure = new ArgumentException("simulated adapter failure") };
        using var client = new HttpClient(handler);
        var problem = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long clockTicks = DateTimeOffset.UtcNow.UtcTicks;
        handler.OnRequest = () =>
        {
            if (handler.Wire.Count == 2)
            {
                recovered.TrySetResult();
            }
        };
        using var publisher = new EdAstroPublisher(
            "2.1.3.0",
            client,
            log: _ => problem.TrySetResult(),
            utcNow: () => new DateTimeOffset(Interlocked.Read(ref clockTicks), TimeSpan.Zero),
            trafficBudget: new EdAstroTrafficBudget()
        );
        publisher.SetEnabled(true);
        publisher.Apply(Request([Event()]));
        await problem.Task.WaitAsync(TimeSpan.FromSeconds(30));
        handler.Failure = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!recovered.Task.IsCompleted)
        {
            Interlocked.Add(ref clockTicks, TimeSpan.FromSeconds(30).Ticks);
            await Task.Delay(25, deadline.Token);
        }
        await recovered.Task;
        Assert.Equal(2, handler.Wire.Count);
        Assert.All(handler.Wire, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    /// <summary>Disposal releases owned delivery resources and later processing remains inert.</summary>
    [Fact]
    public async Task DisposedPublisherDoesNotAcceptOrProcessNewData()
    {
        var publisher = new EdAstroPublisher("2.1.3.0", automaticProcessing: false);
        publisher.Dispose();
        publisher.SetEnabled(true);
        publisher.SetSuspended(true);
        publisher.Apply(Request([Event()]));
        await publisher.ProcessPendingAsync();
        Assert.Equal(0, publisher.PendingCount);
    }

    /// <summary>Captures a live Odyssey session for the supplied journal test evidence.</summary>
    private static EdAstroApplyRequest Request(
        IReadOnlyList<JournalEventEnvelope> events,
        string? commander = "Cmdr"
    ) => new(events, commander, "Journal.01.log", true, true, true);

    /// <summary>Builds timestamped journal evidence for the test scenario.</summary>
    private static JournalEventEnvelope Event(
        string name = "ScanOrganic",
        string fields = "\"Species\":\"Bacterium\",\"SystemAddress\":10477373803"
    )
    {
        Assert.True(
            JournalEventEnvelope.TryParse(
                $"{{\"timestamp\":\"2026-10-12T06:00:00Z\",\"event\":\"{name}\",{fields}}}",
                out JournalEventEnvelope? entry,
                out _
            )
        );
        return entry!;
    }

    /// <summary>Creates the controlled provider response used by delivery tests.</summary>
    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body) };

    private sealed record Wire(HttpMethod Method, string Uri, string? Json, string? Authorization, string Agent);

    private sealed class Fixture : IDisposable
    {
        internal const string Accepted =
            "[\"CarrierStats\",\"CarrierJumpRequest\",\"Docked\",\"ScanOrganic\",\"CodexEntry\",\"LoadGame\"]";
        private DateTimeOffset now = DateTimeOffset.Parse(
            "2026-10-12T06:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture
        );
        private readonly HttpClient client;
        internal Handler Handler { get; } = new();
        internal Queue<HttpResponseMessage> Responses => Handler.Responses;
        internal List<Wire> Wire => Handler.Wire;
        internal List<string> Logs { get; } = [];
        internal EdAstroPublisher Publisher { get; }

        /// <summary>Creates isolated test state without using external application data.</summary>
        internal Fixture()
        {
            client = new HttpClient(Handler);
            Publisher = new EdAstroPublisher(
                "2.1.3.0",
                client,
                Logs.Add,
                () => now,
                automaticProcessing: false,
                trafficBudget: new EdAstroTrafficBudget()
            );
        }

        /// <summary>Advances the deterministic clock to the next delivery checkpoint.</summary>
        internal void Advance(int seconds) => now += TimeSpan.FromSeconds(seconds);

        /// <summary>Enables test consent and applies the supplied live journal evidence.</summary>
        internal void Enable(IReadOnlyList<JournalEventEnvelope> events)
        {
            Publisher.SetEnabled(true);
            Publisher.Apply(Request(events));
        }

        /// <summary>Processes policy discovery and the first paced journal batch.</summary>
        internal async Task Send()
        {
            await Publisher.ProcessPendingAsync();
            Advance(10);
            await Publisher.ProcessPendingAsync();
        }

        /// <summary>Releases the isolated test resources after assertions complete.</summary>
        public void Dispose()
        {
            Publisher.Dispose();
            client.Dispose();
        }
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal Queue<HttpResponseMessage> Responses { get; } = new();
        internal List<Wire> Wire { get; } = [];
        internal Exception? Failure { get; set; }
        internal Action? OnRequest { get; set; }
        internal bool Block { get; set; }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Records the request and returns the controlled response for this test.</summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Wire.Add(
                new Wire(
                    request.Method,
                    request.RequestUri!.AbsoluteUri,
                    request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                    request.Headers.Authorization?.ToString(),
                    request.Headers.UserAgent.ToString()
                )
            );
            OnRequest?.Invoke();
            if (Failure is not null)
            {
                throw Failure;
            }
            if (Block)
            {
                Started.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            Started.TrySetResult();
            if (Responses.TryDequeue(out HttpResponseMessage? response))
            {
                return response;
            }
            string body = request.Method == HttpMethod.Get ? Fixture.Accepted : "{\"status\":200}";
            return Response(body);
        }
    }
}
