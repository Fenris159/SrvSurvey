using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Inara;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Inara;

public sealed class InaraPublisherTests
{
    private static readonly InaraPublicationOptions Options = new(
        Enabled: true,
        DeveloperTestMode: false,
        ApiKey: "personal-key",
        CommanderName: "Test Commander",
        FrontierId: "F123456",
        GameVersion: "4.1.0.100",
        IsOdyssey: true);

    [Fact]
    public async Task BootstrapSeedsStateWithoutUploadingHistory()
    {
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher(
            "2.0.95.0",
            new HttpClient(handler));
        var cargo = new CargoSnapshot(
            DateTimeOffset.Parse("2026-07-28T12:00:01Z"),
            "Cargo",
            "Ship",
            7,
            [new CargoItem("tea", null, 7, 0)]);

        var bootstrap = await publisher.ApplyAsync(CreateUpdate(
            [
                Event("""
                    {
                      "timestamp": "2026-07-28T12:00:00Z",
                      "event": "LoadGame",
                      "Commander": "Test Commander",
                      "FID": "F123456",
                      "Credits": 1000,
                      "Loan": 25
                    }
                    """),
                Event("""
                    {
                      "timestamp": "2026-07-28T12:00:01Z",
                      "event": "Cargo",
                      "Vessel": "Ship",
                      "Count": 7
                    }
                    """),
                Event("""
                    {
                      "timestamp": "2026-07-28T12:01:00Z",
                      "event": "FSDJump",
                      "StarSystem": "Sirius",
                      "StarPos": [6.25, -1.25, -5.75]
                    }
                    """),
            ],
            cargo,
            allowPublishing: false,
            allowSharedData: true));
        Assert.Equal(0, bootstrap.QueuedEventCount);

        var live = await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-07-28T12:02:00Z",
                  "event": "Music",
                  "MusicTrack": "Exploration"
                }
                """)],
            cargo,
            allowPublishing: true,
            allowSharedData: true));

        Assert.Contains("getCommanderProfile", live.QueuedEventNames);
        Assert.Contains("setCommanderInventoryCargo", live.QueuedEventNames);
        Assert.Contains("setCommanderCredits", live.QueuedEventNames);
        Assert.DoesNotContain(
            live.QueuedEventNames,
            name => name.StartsWith(
                "addCommanderTravel",
                StringComparison.Ordinal));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task MultiboxModeDoesNotUseSharedCargoSidecar()
    {
        using var publisher = new InaraPublisher(
            "2.0.95.0",
            new HttpClient(new InaraResponseHandler()));
        var cargo = new CargoSnapshot(
            DateTimeOffset.Parse("2026-07-28T12:00:01Z"),
            "Cargo",
            "Ship",
            7,
            [new CargoItem("tea", null, 7, 0)]);

        await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-07-28T12:00:01Z",
                  "event": "Cargo",
                  "Vessel": "Ship",
                  "Count": 7
                }
                """)],
            cargo,
            allowPublishing: false,
            allowSharedData: false));
        var live = await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-07-28T12:02:00Z",
                  "event": "Music",
                  "MusicTrack": "Exploration"
                }
                """)],
            cargo,
            allowPublishing: true,
            allowSharedData: false));

        Assert.DoesNotContain(
            "setCommanderInventoryCargo",
            live.QueuedEventNames);
    }

    [Fact]
    public async Task ShutdownFlushUsesPersonalKeyAndProductionFlag()
    {
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher(
            "2.0.95.0",
            new HttpClient(handler));

        var result = await publisher.ApplyAsync(CreateUpdate(
            [
                Event("""
                    {
                      "timestamp": "2026-07-28T12:00:00Z",
                      "event": "LoadGame",
                      "Credits": 1000,
                      "Loan": 0
                    }
                    """),
                Event("""
                    {
                      "timestamp": "2026-07-28T12:01:00Z",
                      "event": "Shutdown"
                    }
                    """),
            ],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true));

        Assert.True(result.AcceptedEventCount > 0);
        Assert.Equal(0, result.PendingEventCount);
        var payload = Assert.IsType<JObject>(handler.LastPayload);
        var header = Assert.IsType<JObject>(payload["header"]);
        Assert.Equal("SrvSurvey", header.Value<string>("appName"));
        Assert.Equal("personal-key", header.Value<string>("APIkey"));
        Assert.False(header.Value<bool>("isBeingDeveloped"));
        Assert.Null(header["applicationAccessToken"]);
    }

    [Fact]
    public async Task DeveloperTestModeFlowsToPayloadOnlyWhenSelected()
    {
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher(
            "2.0.95.0",
            new HttpClient(handler));

        await publisher.ApplyAsync(CreateUpdate(
            [
                Event("""
                    {
                      "timestamp": "2026-07-28T12:00:00Z",
                      "event": "LoadGame",
                      "Credits": 1000
                    }
                    """),
                Event("""
                    {
                      "timestamp": "2026-07-28T12:01:00Z",
                      "event": "Shutdown"
                    }
                    """),
            ],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true,
            Options with { DeveloperTestMode = true }));

        var payload = Assert.IsType<JObject>(handler.LastPayload);
        var header = Assert.IsType<JObject>(payload["header"]);
        Assert.True(header.Value<bool>("isBeingDeveloped"));
    }

    [Fact]
    public async Task TransientFailureRetainsBatchForRetry()
    {
        var handler = new InaraResponseHandler(
            HttpStatusCode.ServiceUnavailable);
        using var publisher = new InaraPublisher(
            "2.0.95.0",
            new HttpClient(handler));

        var deferred = await publisher.ApplyAsync(CreateUpdate(
            [
                Event("""
                    {
                      "timestamp": "2026-07-28T12:00:00Z",
                      "event": "LoadGame",
                      "Credits": 1000
                    }
                    """),
                Event("""
                    {
                      "timestamp": "2026-07-28T12:01:00Z",
                      "event": "Shutdown"
                    }
                    """),
            ],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true));

        Assert.True(deferred.PendingEventCount > 0);
        Assert.Contains(
            deferred.Warnings,
            warning => warning.Contains(
                "retained",
                StringComparison.OrdinalIgnoreCase));

        handler.StatusCode = HttpStatusCode.OK;
        var retried = await publisher.FlushAsync(Options);
        Assert.True(retried.AcceptedEventCount > 0);
        Assert.Equal(0, retried.PendingEventCount);
    }

    private static InaraPublicationUpdate CreateUpdate(
        IReadOnlyList<JournalEventEnvelope> events,
        CargoSnapshot? cargo,
        bool allowPublishing,
        bool allowSharedData,
        InaraPublicationOptions? options = null)
    {
        return new InaraPublicationUpdate(
            events,
            Status: null,
            cargo,
            JournalPath: null,
            allowPublishing,
            allowSharedData,
            SystemName: "Sol",
            StationName: "Galileo",
            BodyName: "Earth",
            ShipType: "CobraMkIII",
            ShipId: 42,
            ShipName: "Surveyor",
            ShipIdent: "SRV-42",
            options ?? Options);
    }

    private static JournalEventEnvelope Event(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var parsed, out var error),
            error);
        return Assert.IsType<JournalEventEnvelope>(parsed);
    }

    private sealed class InaraResponseHandler(HttpStatusCode? initialStatus = null)
        : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } =
            initialStatus ?? HttpStatusCode.OK;

        public int RequestCount { get; private set; }

        public JToken? LastPayload { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var body = await request.Content!
                .ReadAsStringAsync(cancellationToken);
            LastPayload = JToken.Parse(body);
            if (StatusCode != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(StatusCode);
            }

            var eventCount = LastPayload["events"]?.Count() ?? 0;
            var responseEvents = new JArray(Enumerable
                .Range(0, eventCount)
                .Select(_ => new JObject { ["eventStatus"] = 200 }));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    new JObject
                    {
                        ["header"] = new JObject
                        {
                            ["eventStatus"] = 200,
                        },
                        ["events"] = responseEvents,
                    }.ToString(),
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }
}
