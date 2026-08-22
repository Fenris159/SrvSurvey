using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Inara;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Inara;

public sealed class InaraPublisherTests
{
    private static readonly InaraPublicationOptions Options = new(
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
                      "timestamp": "2026-07-28T12:00:30Z",
                      "event": "Statistics",
                      "Multicrew": {
                        "Multicrew_Time_Total": 14483,
                        "Multicrew_Credits_Total": 0
                      }
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
    public async Task ShutdownFlushUsesPersonalKeyAndValidationFlag()
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
        var result = await publisher.FlushAsync();

        Assert.True(result.AcceptedEventCount > 0);
        Assert.Equal(0, result.PendingEventCount);
        var payload = Assert.IsType<JObject>(handler.LastPayload);
        var header = Assert.IsType<JObject>(payload["header"]);
        Assert.Equal("SrvSurvey", header.Value<string>("appName"));
        Assert.Equal("personal-key", header.Value<string>("APIkey"));
        Assert.True(header.Value<bool>("isBeingDeveloped"));
        Assert.Null(header["applicationAccessToken"]);
    }

    [Fact]
    public async Task TransientFailureRetainsBatchForRetry()
    {
        var handler = new InaraResponseHandler(
            HttpStatusCode.ServiceUnavailable);
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
            ],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true));
        var deferred = await publisher.FlushAsync();

        Assert.True(deferred.PendingEventCount > 0);
        Assert.Contains(
            deferred.Warnings,
            warning => warning.Contains(
                "retained",
                StringComparison.OrdinalIgnoreCase));

        handler.StatusCode = HttpStatusCode.OK;
        var retried = await publisher.FlushAsync();
        Assert.True(retried.AcceptedEventCount > 0);
        Assert.Equal(0, retried.PendingEventCount);
    }

    [Fact]
    public async Task OversizedResponseRetainsBatchWithoutParsingIt()
    {
        var handler = new InaraResponseHandler
        {
            ReturnOversizedResponse = true,
        };
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
            ],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true));
        var deferred = await publisher.FlushAsync();

        Assert.True(deferred.PendingEventCount > 0);
        Assert.Contains(
            deferred.Warnings,
            warning => warning.Contains(
                nameof(InvalidDataException),
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task MalformedEventResponseRetainsTheCompleteBatch()
    {
        var handler = new InaraResponseHandler
        {
            ReturnMalformedEventResponse = true,
        };
        using var publisher = new InaraPublisher(
            "2.0.95.0",
            new HttpClient(handler));

        await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-07-28T12:00:00Z",
                  "event": "LoadGame",
                  "Credits": 1000
                }
                """)],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true));
        var deferred = await publisher.FlushAsync();

        Assert.True(deferred.PendingEventCount > 0);
        Assert.Contains(
            deferred.Warnings,
            warning => warning.Contains(
                nameof(InvalidDataException),
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task StopWaitsForAnActiveUploadAndFlushesGracefully()
    {
        var handler = new BlockingInaraHandler();
        var publisher = new InaraPublisher(
            "2.0.95.0",
            new HttpClient(handler));

        var applyTask = publisher.ApplyAsync(CreateUpdate(
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

        await applyTask.WaitAsync(TimeSpan.FromSeconds(1));
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var stopTask = publisher.StopAsync();
        Assert.False(stopTask.IsCompleted);

        handler.ReleaseResponse();
        var stopped = await stopTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(stopped.AcceptedEventCount > 0);
        Assert.False(handler.RequestCancelled.Task.IsCompleted);
        var requestCount = handler.RequestCount;
        var repeatedStop = publisher.StopAsync();
        Assert.Same(stopTask, repeatedStop);
        await repeatedStop;
        publisher.Dispose();
        Assert.Equal(requestCount, handler.RequestCount);
    }

    [Fact]
    public async Task CancellingAStopWaiterDoesNotCancelSharedShutdown()
    {
        var handler = new BlockingInaraHandler();
        var publisher = new InaraPublisher(
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
            allowSharedData: true));
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        using var cancellation = new CancellationTokenSource();
        var cancelledWait = publisher.StopAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelledWait);
        Assert.False(handler.RequestCancelled.Task.IsCompleted);

        handler.ReleaseResponse();
        var stopped = await publisher.StopAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(stopped.AcceptedEventCount > 0);
        publisher.Dispose();
    }

    [Fact]
    public async Task ClearingKeyCancelsAnActiveUploadAndDiscardsItsBatch()
    {
        var handler = new BlockingInaraHandler();
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
            allowSharedData: true));
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var disabledOptions = Options with { ApiKey = null };
        var optOut = await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-07-28T12:01:01Z",
                  "event": "Music",
                  "MusicTrack": "MainMenu"
                }
                """)],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true,
            disabledOptions));
        await handler.RequestCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var cancelled = await publisher.FlushAsync();

        Assert.Equal(0, optOut.PendingEventCount);
        Assert.Equal(0, cancelled.AcceptedEventCount);
        Assert.Equal(0, cancelled.PendingEventCount);
        Assert.Contains(
            optOut.Warnings.Concat(cancelled.Warnings),
            warning => warning.Contains(
                "cancelled",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BatchUsesContextAtEachJournalEvent()
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
                      "Commander": "Test Commander",
                      "FID": "F123456",
                      "Credits": 1000
                    }
                    """),
                Event("""
                    {
                      "timestamp": "2026-07-28T12:00:01Z",
                      "event": "Location",
                      "StarSystem": "Sol",
                      "Docked": true,
                      "StationName": "Galileo"
                    }
                    """),
            ],
            cargo: null,
            allowPublishing: false,
            allowSharedData: true));
        await publisher.ApplyAsync(CreateUpdate(
            [
                Event("""
                    {
                      "timestamp": "2026-07-28T12:01:00Z",
                      "event": "MissionAccepted",
                      "MissionID": 42,
                      "Name": "Mission_Delivery",
                      "Faction": "Pilots Federation"
                    }
                    """),
                Event("""
                    {
                      "timestamp": "2026-07-28T12:01:01Z",
                      "event": "Undocked",
                      "StationName": "Galileo"
                    }
                    """),
            ],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true) with
        { StationName = null });

        await publisher.FlushAsync();

        var mission = Assert.Single(
            handler.LastPayload!["events"]!.OfType<JObject>(),
            item => item.Value<string>("eventName") == "addCommanderMission");
        var data = Assert.IsType<JObject>(mission["eventData"]);
        Assert.Equal("Sol", data.Value<string>("starsystemNameOrigin"));
        Assert.Equal("Galileo", data.Value<string>("stationNameOrigin"));
    }

    [Fact]
    public async Task CommanderMismatchFailsClosed()
    {
        using var publisher = new InaraPublisher(
            "2.0.95.0",
            new HttpClient(new InaraResponseHandler()));

        var result = await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-07-28T12:00:00Z",
                  "event": "LoadGame",
                  "Commander": "Different Commander",
                  "FID": "F999999",
                  "Credits": 1000
                }
                """)],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true));

        Assert.Equal(0, result.QueuedEventCount);
        Assert.Equal(0, result.PendingEventCount);
    }

    [Fact]
    public async Task SessionSwitchUsesOnlyTheMatchingCommanderAndApiKey()
    {
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher(
            "2.0.95.0",
            new HttpClient(handler));
        var firstOptions = Options with
        {
            ApiKey = "first-key",
            CommanderName = "First Commander",
            FrontierId = "F111",
        };
        var secondOptions = Options with
        {
            ApiKey = "second-key",
            CommanderName = "Second Commander",
            FrontierId = "F222",
        };

        await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-07-28T12:00:00Z",
                  "event": "LoadGame",
                  "Commander": "First Commander",
                  "FID": "F111",
                  "Credits": 1000
                }
                """)],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true,
            firstOptions) with
        { JournalPath = "Journal.First.log" });

        await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-07-28T13:00:00Z",
                  "event": "LoadGame",
                  "Commander": "Second Commander",
                  "FID": "F222",
                  "Credits": 2000
                }
                """)],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true,
            secondOptions) with
        { JournalPath = "Journal.Second.log" });
        await publisher.FlushAsync();

        Assert.Equal(2, handler.Payloads.Count);
        var headers = handler.Payloads
            .Select(payload => Assert.IsType<JObject>(payload["header"]))
            .ToArray();
        Assert.Collection(
            headers,
            first =>
            {
                Assert.Equal(
                    "First Commander",
                    first.Value<string>("commanderName"));
                Assert.Equal("F111", first.Value<string>("commanderFrontierID"));
                Assert.Equal("first-key", first.Value<string>("APIkey"));
            },
            second =>
            {
                Assert.Equal(
                    "Second Commander",
                    second.Value<string>("commanderName"));
                Assert.Equal("F222", second.Value<string>("commanderFrontierID"));
                Assert.Equal("second-key", second.Value<string>("APIkey"));
            });
    }

    [Fact]
    public async Task MulticrewJournalEventsCannotReplaceCommanderContext()
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
                      "Commander": "Test Commander",
                      "FID": "F123456",
                      "Ship": "CobraMkIII",
                      "ShipID": 42,
                      "Credits": 1000
                    }
                    """),
                Event("""
                    {
                      "timestamp": "2026-07-28T12:00:01Z",
                      "event": "Location",
                      "StarSystem": "Sol",
                      "Docked": true,
                      "StationName": "Galileo"
                    }
                    """),
            ],
            cargo: null,
            allowPublishing: false,
            allowSharedData: true));

        await publisher.ApplyAsync(CreateUpdate(
            [
                Event("""
                    { "timestamp": "2026-07-28T12:01:00Z", "event": "JoinACrew" }
                    """),
                Event("""
                    {
                      "timestamp": "2026-07-28T12:01:01Z",
                      "event": "Loadout",
                      "Ship": "Anaconda",
                      "ShipID": 99
                    }
                    """),
                Event("""
                    {
                      "timestamp": "2026-07-28T12:01:02Z",
                      "event": "FSDJump",
                      "StarSystem": "Sirius",
                      "StarPos": [6.25, -1.25, -5.75]
                    }
                    """),
                Event("""
                    { "timestamp": "2026-07-28T12:01:03Z", "event": "QuitACrew" }
                    """),
                Event("""
                    {
                      "timestamp": "2026-07-28T12:01:04Z",
                      "event": "MissionAccepted",
                      "MissionID": 42,
                      "Name": "Mission_Delivery"
                    }
                    """),
            ],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true));
        await publisher.FlushAsync();

        var ship = Assert.Single(
            handler.LastPayload!["events"]!.OfType<JObject>(),
            item => item.Value<string>("eventName") == "setCommanderShip");
        Assert.Equal(42, ship["eventData"]!.Value<int>("shipGameID"));
        var mission = Assert.Single(
            handler.LastPayload["events"]!.OfType<JObject>(),
            item => item.Value<string>("eventName") == "addCommanderMission");
        Assert.Equal(
            "Sol",
            mission["eventData"]!.Value<string>("starsystemNameOrigin"));
        Assert.DoesNotContain(
            handler.LastPayload["events"]!.OfType<JObject>(),
            item => item["eventData"]?.Value<int?>("shipGameID") == 99);
    }

    [Fact]
    public async Task OneFlushSendsOnlyOneBoundedRequest()
    {
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher(
            "2.0.95.0",
            new HttpClient(handler));
        var events = Enumerable.Range(0, 140)
            .Select(index => Event($$"""
                {
                  "timestamp": "2026-07-28T12:00:00Z",
                  "event": "Friends",
                  "Status": "Added",
                  "Name": "Friend {{index}}"
                }
                """))
            .ToArray();

        await publisher.ApplyAsync(CreateUpdate(
            events,
            cargo: null,
            allowPublishing: true,
            allowSharedData: true));
        var first = await publisher.FlushAsync();
        await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-07-28T12:00:01Z",
                  "event": "Music",
                  "MusicTrack": "Exploration"
                }
                """)],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true));

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(128, handler.LastPayload!["events"]!.Count());
        Assert.True(first.PendingEventCount > 0);
    }

    [Fact]
    public async Task OversizedBatchIsSplitAndRemainderRetained()
    {
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher(
            "2.0.95.0",
            new HttpClient(handler));
        var largeName = new string('x', 600_000);

        await publisher.ApplyAsync(CreateUpdate(
            [
                Event($$"""
                    {
                      "timestamp": "2026-07-28T12:00:00Z",
                      "event": "Friends",
                      "Status": "Added",
                      "Name": "a{{largeName}}"
                    }
                    """),
                Event($$"""
                    {
                      "timestamp": "2026-07-28T12:00:01Z",
                      "event": "Friends",
                      "Status": "Added",
                      "Name": "b{{largeName}}"
                    }
                    """),
            ],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true));
        var result = await publisher.FlushAsync();

        Assert.Equal(1, handler.RequestCount);
        Assert.True(result.AcceptedEventCount > 0);
        Assert.True(result.PendingEventCount > 0);
        Assert.True(
            Encoding.UTF8.GetByteCount(handler.LastPayload!.ToString())
                < 1024 * 1024);
    }

    [Fact]
    public async Task EventLevelTransientStatusIsRetriedAndRedirectIsRejected()
    {
        var handler = new InaraResponseHandler
        {
            EventStatusSelector = index => index switch
            {
                0 => 200,
                1 => 429,
                _ => 400,
            },
        };
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
                      "timestamp": "2026-07-28T12:00:01Z",
                      "event": "Friends",
                      "Status": "Added",
                      "Name": "Third Result"
                    }
                    """),
            ],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true));

        var result = await publisher.FlushAsync();

        Assert.Equal(1, result.AcceptedEventCount);
        Assert.Equal(1, result.PendingEventCount);
        Assert.Contains(result.Warnings, warning => warning.Contains("deferred"));
        Assert.Contains(result.Warnings, warning => warning.Contains("rejected"));

        var redirectHandler = new InaraResponseHandler
        {
            HeaderEventStatus = 302,
        };
        using var redirectPublisher = new InaraPublisher(
            "2.0.95.0",
            new HttpClient(redirectHandler));
        await redirectPublisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-07-28T12:00:00Z",
                  "event": "LoadGame",
                  "Credits": 1000
                }
                """)],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true));
        var redirected = await redirectPublisher.FlushAsync();
        Assert.Equal(0, redirected.AcceptedEventCount);
        Assert.Equal(0, redirected.PendingEventCount);
        Assert.Contains(
            redirected.Warnings,
            warning => warning.Contains("API status 302"));
    }

    [Fact]
    public async Task RetryAfterExtendsAutomaticRetryWindow()
    {
        var time = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-07-28T12:00:00Z"));
        var handler = new InaraResponseHandler(HttpStatusCode.TooManyRequests)
        {
            RetryAfter = TimeSpan.FromMinutes(2),
        };
        using var publisher = new InaraPublisher(
            "2.0.95.0",
            new HttpClient(handler),
            time);
        await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-07-28T12:00:00Z",
                  "event": "LoadGame",
                  "Credits": 1000
                }
                """)],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true));
        await publisher.FlushAsync();
        handler.StatusCode = HttpStatusCode.OK;

        time.Advance(TimeSpan.FromMinutes(1));
        await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-07-28T12:01:00Z",
                  "event": "Music"
                }
                """)],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true));
        Assert.Equal(1, handler.RequestCount);

        time.Advance(TimeSpan.FromMinutes(1));
        await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-07-28T12:02:00Z",
                  "event": "Music"
                }
                """)],
            cargo: null,
            allowPublishing: true,
            allowSharedData: true));
        await WaitForAsync(() => handler.RequestCount == 2);
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

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        while (!predicate() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(predicate(), "The expected asynchronous operation did not complete.");
    }

    private sealed class InaraResponseHandler(HttpStatusCode? initialStatus = null)
        : HttpMessageHandler
    {
        private int requestCount;

        public HttpStatusCode StatusCode { get; set; } =
            initialStatus ?? HttpStatusCode.OK;

        public int RequestCount => Volatile.Read(ref requestCount);

        public JToken? LastPayload { get; private set; }

        public List<JToken> Payloads { get; } = [];

        public bool ReturnOversizedResponse { get; init; }

        public bool ReturnMalformedEventResponse { get; init; }

        public int HeaderEventStatus { get; init; } = 200;

        public Func<int, int> EventStatusSelector { get; init; } = _ => 200;

        public TimeSpan? RetryAfter { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            var body = await request.Content!
                .ReadAsStringAsync(cancellationToken);
            LastPayload = JToken.Parse(body);
            Payloads.Add(LastPayload);
            if ((int)StatusCode is < 200 or > 299)
            {
                var failed = new HttpResponseMessage(StatusCode);
                if (RetryAfter is { } retryAfter)
                {
                    failed.Headers.RetryAfter =
                        new System.Net.Http.Headers.RetryConditionHeaderValue(
                            retryAfter);
                }

                return failed;
            }

            if (ReturnOversizedResponse)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[1024 * 1024 + 1]),
                };
            }

            var eventCount = LastPayload["events"]?.Count() ?? 0;
            var responseEvents = ReturnMalformedEventResponse
                ? new JArray(Enumerable.Range(0, eventCount).Select(
                    _ => JValue.CreateString("malformed")))
                : new JArray(Enumerable
                    .Range(0, eventCount)
                    .Select(index => new JObject
                    {
                        ["eventStatus"] = EventStatusSelector(index),
                    }));
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(
                    new JObject
                    {
                        ["header"] = new JObject
                        {
                            ["eventStatus"] = HeaderEventStatus,
                        },
                        ["events"] = responseEvents,
                    }.ToString(),
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }

    private sealed class BlockingInaraHandler : HttpMessageHandler
    {
        private int requestCount;
        private readonly TaskCompletionSource releaseResponse = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int RequestCount => Volatile.Read(ref requestCount);

        public TaskCompletionSource RequestStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource RequestCancelled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseResponse()
        {
            releaseResponse.TrySetResult();
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            RequestStarted.TrySetResult();
            try
            {
                var body = await request.Content!
                    .ReadAsStringAsync(cancellationToken);
                var payload = JObject.Parse(body);
                await releaseResponse.Task.WaitAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        new JObject
                        {
                            ["header"] = new JObject
                            {
                                ["eventStatus"] = 200,
                            },
                            ["events"] = new JArray(payload["events"]!
                                .Select(_ => new JObject
                                {
                                    ["eventStatus"] = 200,
                                })),
                        }.ToString(),
                        Encoding.UTF8,
                        "application/json"),
                };
            }
            catch (OperationCanceledException)
            {
                RequestCancelled.TrySetResult();
                throw;
            }
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration)
        {
            utcNow += duration;
        }
    }
}
