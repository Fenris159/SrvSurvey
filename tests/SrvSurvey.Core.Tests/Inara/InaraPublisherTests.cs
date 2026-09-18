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
        IsOdyssey: true
    );

    [Fact]
    public async Task BootstrapSeedsStateWithoutUploadingHistory()
    {
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));
        var cargo = new CargoSnapshot(
            DateTimeOffset.Parse("2026-07-28T12:00:01Z", global::System.Globalization.CultureInfo.InvariantCulture),
            "Cargo",
            "Ship",
            7,
            [new CargoItem("tea", null, 7, 0)]
        );

        InaraPublicationResult bootstrap = await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:00Z",
                          "event": "LoadGame",
                          "Commander": "Test Commander",
                          "FID": "F123456",
                          "Credits": 1000,
                          "Loan": 25
                        }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:01Z",
                          "event": "Cargo",
                          "Vessel": "Ship",
                          "Count": 7
                        }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:30Z",
                          "event": "Statistics",
                          "Multicrew": {
                            "Multicrew_Time_Total": 14483,
                            "Multicrew_Credits_Total": 0
                          }
                        }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:01:00Z",
                          "event": "FSDJump",
                          "StarSystem": "Sirius",
                          "StarPos": [6.25, -1.25, -5.75]
                        }
                        """
                    ),
                ],
                cargo,
                allowPublishing: false,
                allowSharedData: true
            )
        );
        Assert.Equal(0, bootstrap.QueuedEventCount);

        InaraPublicationResult live = await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:02:00Z",
                          "event": "Music",
                          "MusicTrack": "Exploration"
                        }
                        """
                    ),
                ],
                cargo,
                allowPublishing: true,
                allowSharedData: true
            )
        );

        Assert.Contains("getCommanderProfile", live.QueuedEventNames);
        Assert.Contains("setCommanderInventoryCargo", live.QueuedEventNames);
        Assert.Contains("setCommanderCredits", live.QueuedEventNames);
        Assert.DoesNotContain(
            live.QueuedEventNames,
            name => name.StartsWith("addCommanderTravel", StringComparison.Ordinal)
        );
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task MultiboxModeDoesNotUseSharedCargoSidecar()
    {
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(new InaraResponseHandler()));
        var cargo = new CargoSnapshot(
            DateTimeOffset.Parse("2026-07-28T12:00:01Z", global::System.Globalization.CultureInfo.InvariantCulture),
            "Cargo",
            "Ship",
            7,
            [new CargoItem("tea", null, 7, 0)]
        );

        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:01Z",
                          "event": "Cargo",
                          "Vessel": "Ship",
                          "Count": 7
                        }
                        """
                    ),
                ],
                cargo,
                allowPublishing: false,
                allowSharedData: false
            )
        );
        InaraPublicationResult live = await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:02:00Z",
                          "event": "Music",
                          "MusicTrack": "Exploration"
                        }
                        """
                    ),
                ],
                cargo,
                allowPublishing: true,
                allowSharedData: false
            )
        );

        Assert.DoesNotContain("setCommanderInventoryCargo", live.QueuedEventNames);
    }

    [Fact]
    public async Task ShutdownFlushUsesPersonalKeyAndValidationFlag()
    {
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));

        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:00Z",
                          "event": "LoadGame",
                          "Credits": 1000,
                          "Loan": 0
                        }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:01:00Z",
                          "event": "Shutdown"
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            )
        );
        InaraPublicationResult result = await publisher.FlushAsync();

        Assert.True(result.AcceptedEventCount > 0);
        Assert.Equal(0, result.PendingEventCount);
        JObject payload = Assert.IsType<JObject>(handler.LastPayload);
        JObject header = Assert.IsType<JObject>(payload["header"]);
        Assert.Equal("SrvSurvey", header.Value<string>("appName"));
        Assert.Equal("personal-key", header.Value<string>("APIkey"));
        Assert.True(header.Value<bool>("isBeingDeveloped"));
        Assert.Null(header["applicationAccessToken"]);
    }

    [Fact]
    public async Task TransientFailureRetainsBatchForRetry()
    {
        var handler = new InaraResponseHandler(HttpStatusCode.ServiceUnavailable);
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));

        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:00Z",
                          "event": "LoadGame",
                          "Credits": 1000
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            )
        );
        InaraPublicationResult deferred = await publisher.FlushAsync();

        Assert.True(deferred.PendingEventCount > 0);
        Assert.Contains(deferred.Warnings, warning => warning.Contains("retained", StringComparison.OrdinalIgnoreCase));

        handler.StatusCode = HttpStatusCode.OK;
        InaraPublicationResult retried = await publisher.FlushAsync();
        Assert.True(retried.AcceptedEventCount > 0);
        Assert.Equal(0, retried.PendingEventCount);
    }

    [Fact]
    public async Task OversizedResponseRetainsBatchWithoutParsingIt()
    {
        var handler = new InaraResponseHandler { ReturnOversizedResponse = true };
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));

        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:00Z",
                          "event": "LoadGame",
                          "Credits": 1000
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            )
        );
        InaraPublicationResult deferred = await publisher.FlushAsync();

        Assert.True(deferred.PendingEventCount > 0);
        Assert.Contains(
            deferred.Warnings,
            warning => warning.Contains(nameof(InvalidDataException), StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task MalformedEventResponseRetainsTheCompleteBatch()
    {
        var handler = new InaraResponseHandler { ReturnMalformedEventResponse = true };
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));

        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:00Z",
                          "event": "LoadGame",
                          "Credits": 1000
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            )
        );
        InaraPublicationResult deferred = await publisher.FlushAsync();

        Assert.True(deferred.PendingEventCount > 0);
        Assert.Contains(
            deferred.Warnings,
            warning => warning.Contains(nameof(InvalidDataException), StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task StopWaitsForAnActiveUploadAndFlushesGracefully()
    {
        var handler = new BlockingInaraHandler();
        var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));

        Task<InaraPublicationResult> applyTask = publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:00Z",
                          "event": "LoadGame",
                          "Credits": 1000
                        }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:01:00Z",
                          "event": "Shutdown"
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            )
        );

        await applyTask.WaitAsync(TimeSpan.FromSeconds(1));
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Task<InaraPublicationResult> stopTask = publisher.StopAsync();
        Assert.False(stopTask.IsCompleted);

        handler.ReleaseResponse();
        InaraPublicationResult stopped = await stopTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(stopped.AcceptedEventCount > 0);
        Assert.False(handler.RequestCancelled.Task.IsCompleted);
        int requestCount = handler.RequestCount;
        Task<InaraPublicationResult> repeatedStop = publisher.StopAsync();
        Assert.Same(stopTask, repeatedStop);
        await repeatedStop;
        publisher.Dispose();
        Assert.Equal(requestCount, handler.RequestCount);
    }

    [Fact]
    public async Task CancellingAStopWaiterDoesNotCancelSharedShutdown()
    {
        var handler = new BlockingInaraHandler();
        var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));
        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:00Z",
                          "event": "LoadGame",
                          "Credits": 1000
                        }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:01:00Z",
                          "event": "Shutdown"
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            )
        );
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        using var cancellation = new CancellationTokenSource();
        Task<InaraPublicationResult> cancelledWait = publisher.StopAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWait);
        Assert.False(handler.RequestCancelled.Task.IsCompleted);

        handler.ReleaseResponse();
        InaraPublicationResult stopped = await publisher.StopAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(stopped.AcceptedEventCount > 0);
        publisher.Dispose();
    }

    [Fact]
    public async Task DisposeCompletesWithoutPumpingTheCallersSynchronizationContext()
    {
        var handler = new BlockingInaraHandler();
        using var httpClient = new HttpClient(handler);
        var publisher = new InaraPublisher("2.0.95.0", httpClient);
        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:00Z",
                          "event": "LoadGame",
                          "Credits": 1000
                        }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:01:00Z",
                          "event": "Shutdown"
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            )
        );
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var disposeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            disposeStarted.TrySetResult();
            try
            {
                publisher.Dispose();
                completed.TrySetResult(null);
            }
            catch (Exception exception)
            {
                completed.TrySetResult(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Inara UI-context disposal test",
        };

        thread.Start();
        await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(completed.Task.IsCompleted);
        handler.ReleaseResponse();
        Exception? exception = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(exception);
        Assert.True(handler.ResponseCompleted);
    }

    [Fact]
    public async Task ClearingKeyCancelsAnActiveUploadAndDiscardsItsBatch()
    {
        var handler = new BlockingInaraHandler();
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));
        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:00Z",
                          "event": "LoadGame",
                          "Credits": 1000
                        }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:01:00Z",
                          "event": "Shutdown"
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            )
        );
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        InaraPublicationOptions disabledOptions = Options with { ApiKey = null };
        InaraPublicationResult optOut = await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:01:01Z",
                          "event": "Music",
                          "MusicTrack": "MainMenu"
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true,
                disabledOptions
            )
        );
        await handler.RequestCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        InaraPublicationResult cancelled = await publisher.FlushAsync();

        Assert.Equal(0, optOut.PendingEventCount);
        Assert.Equal(0, cancelled.AcceptedEventCount);
        Assert.Equal(0, cancelled.PendingEventCount);
        Assert.Contains(
            optOut.Warnings.Concat(cancelled.Warnings),
            warning => warning.Contains("cancelled", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task BatchUsesContextAtEachJournalEvent()
    {
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));

        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:00Z",
                          "event": "LoadGame",
                          "Commander": "Test Commander",
                          "FID": "F123456",
                          "Credits": 1000
                        }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:01Z",
                          "event": "Location",
                          "StarSystem": "Sol",
                          "Docked": true,
                          "StationName": "Galileo"
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: false,
                allowSharedData: true
            )
        );
        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:01:00Z",
                          "event": "MissionAccepted",
                          "MissionID": 42,
                          "Name": "Mission_Delivery",
                          "Faction": "Pilots Federation"
                        }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:01:01Z",
                          "event": "Undocked",
                          "StationName": "Galileo"
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            ) with
            {
                StationName = null,
            }
        );

        await publisher.FlushAsync();

        JObject mission = Assert.Single(
            handler.LastPayload!["events"]!.OfType<JObject>(),
            item => item.Value<string>("eventName") == "addCommanderMission"
        );
        JObject data = Assert.IsType<JObject>(mission["eventData"]);
        Assert.Equal("Sol", data.Value<string>("starsystemNameOrigin"));
        Assert.Equal("Galileo", data.Value<string>("stationNameOrigin"));
    }

    [Fact]
    public async Task CommanderMismatchFailsClosed()
    {
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(new InaraResponseHandler()));

        InaraPublicationResult result = await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:00Z",
                          "event": "LoadGame",
                          "Commander": "Different Commander",
                          "FID": "F999999",
                          "Credits": 1000
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            )
        );

        Assert.Equal(0, result.QueuedEventCount);
        Assert.Equal(0, result.PendingEventCount);
    }

    [Fact]
    public async Task SessionSwitchUsesOnlyTheMatchingCommanderAndApiKey()
    {
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));
        InaraPublicationOptions firstOptions = Options with
        {
            ApiKey = "first-key",
            CommanderName = "First Commander",
            FrontierId = "F111",
        };
        InaraPublicationOptions secondOptions = Options with
        {
            ApiKey = "second-key",
            CommanderName = "Second Commander",
            FrontierId = "F222",
        };

        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:00Z",
                          "event": "LoadGame",
                          "Commander": "First Commander",
                          "FID": "F111",
                          "Credits": 1000
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true,
                firstOptions
            ) with
            {
                JournalPath = "Journal.First.log",
            }
        );

        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T13:00:00Z",
                          "event": "LoadGame",
                          "Commander": "Second Commander",
                          "FID": "F222",
                          "Credits": 2000
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true,
                secondOptions
            ) with
            {
                JournalPath = "Journal.Second.log",
            }
        );
        await publisher.FlushAsync();

        Assert.Equal(2, handler.Payloads.Count);
        JObject[] headers = handler.Payloads.Select(payload => Assert.IsType<JObject>(payload["header"])).ToArray();
        Assert.Collection(
            headers,
            first =>
            {
                Assert.Equal("First Commander", first.Value<string>("commanderName"));
                Assert.Equal("F111", first.Value<string>("commanderFrontierID"));
                Assert.Equal("first-key", first.Value<string>("APIkey"));
            },
            second =>
            {
                Assert.Equal("Second Commander", second.Value<string>("commanderName"));
                Assert.Equal("F222", second.Value<string>("commanderFrontierID"));
                Assert.Equal("second-key", second.Value<string>("APIkey"));
            }
        );
    }

    [Fact]
    public async Task MulticrewJournalEventsCannotReplaceCommanderContext()
    {
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));
        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:00Z",
                          "event": "LoadGame",
                          "Commander": "Test Commander",
                          "FID": "F123456",
                          "Ship": "CobraMkIII",
                          "ShipID": 42,
                          "Credits": 1000
                        }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:01Z",
                          "event": "Location",
                          "StarSystem": "Sol",
                          "Docked": true,
                          "StationName": "Galileo"
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: false,
                allowSharedData: true
            )
        );

        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        { "timestamp": "2026-07-28T12:01:00Z", "event": "JoinACrew" }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:01:01Z",
                          "event": "Loadout",
                          "Ship": "Anaconda",
                          "ShipID": 99
                        }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:01:02Z",
                          "event": "FSDJump",
                          "StarSystem": "Sirius",
                          "StarPos": [6.25, -1.25, -5.75]
                        }
                        """
                    ),
                    Event(
                        """
                        { "timestamp": "2026-07-28T12:01:03Z", "event": "QuitACrew" }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:01:04Z",
                          "event": "MissionAccepted",
                          "MissionID": 42,
                          "Name": "Mission_Delivery"
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            )
        );
        await publisher.FlushAsync();

        JObject ship = Assert.Single(
            handler.LastPayload!["events"]!.OfType<JObject>(),
            item => item.Value<string>("eventName") == "setCommanderShip"
        );
        Assert.Equal(42, ship["eventData"]!.Value<int>("shipGameID"));
        JObject mission = Assert.Single(
            handler.LastPayload["events"]!.OfType<JObject>(),
            item => item.Value<string>("eventName") == "addCommanderMission"
        );
        Assert.Equal("Sol", mission["eventData"]!.Value<string>("starsystemNameOrigin"));
        Assert.DoesNotContain(
            handler.LastPayload["events"]!.OfType<JObject>(),
            item => item["eventData"]?.Value<int?>("shipGameID") == 99
        );
    }

    [Fact]
    public async Task OneFlushSendsOnlyOneBoundedRequest()
    {
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));
        JournalEventEnvelope[] events = Enumerable
            .Range(0, 140)
            .Select(index =>
                Event(
                    $$"""
                    {
                      "timestamp": "2026-07-28T12:00:00Z",
                      "event": "Friends",
                      "Status": "Added",
                      "Name": "Friend {{index}}"
                    }
                    """
                )
            )
            .ToArray();

        await publisher.ApplyAsync(CreateUpdate(events, cargo: null, allowPublishing: true, allowSharedData: true));
        InaraPublicationResult first = await publisher.FlushAsync();
        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:01Z",
                          "event": "Music",
                          "MusicTrack": "Exploration"
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            )
        );

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(128, handler.LastPayload!["events"]!.Count());
        Assert.True(first.PendingEventCount > 0);
    }

    [Fact]
    public async Task OversizedBatchIsSplitAndRemainderRetained()
    {
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));
        string largeName = new string('x', 600_000);

        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        $$"""
                        {
                          "timestamp": "2026-07-28T12:00:00Z",
                          "event": "Friends",
                          "Status": "Added",
                          "Name": "a{{largeName}}"
                        }
                        """
                    ),
                    Event(
                        $$"""
                        {
                          "timestamp": "2026-07-28T12:00:01Z",
                          "event": "Friends",
                          "Status": "Added",
                          "Name": "b{{largeName}}"
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            )
        );
        InaraPublicationResult result = await publisher.FlushAsync();

        Assert.Equal(1, handler.RequestCount);
        Assert.True(result.AcceptedEventCount > 0);
        Assert.True(result.PendingEventCount > 0);
        Assert.True(Encoding.UTF8.GetByteCount(handler.LastPayload!.ToString()) < 1024 * 1024);
    }

    [Fact]
    public async Task EventLevelTransientStatusIsRetriedAndRedirectIsRejected()
    {
        var handler = new InaraResponseHandler
        {
            EventStatusSelector = index =>
                index switch
                {
                    0 => 200,
                    1 => 429,
                    _ => 400,
                },
        };
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));
        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:00Z",
                          "event": "LoadGame",
                          "Credits": 1000
                        }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:01Z",
                          "event": "Friends",
                          "Status": "Added",
                          "Name": "Third Result"
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            )
        );

        InaraPublicationResult result = await publisher.FlushAsync();

        Assert.Equal(1, result.AcceptedEventCount);
        Assert.Equal(1, result.PendingEventCount);
        Assert.Contains(result.Warnings, warning => warning.Contains("deferred"));
        Assert.Contains(result.Warnings, warning => warning.Contains("rejected"));

        var redirectHandler = new InaraResponseHandler { HeaderEventStatus = 302 };
        using var redirectPublisher = new InaraPublisher("2.0.95.0", new HttpClient(redirectHandler));
        await redirectPublisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:00Z",
                          "event": "LoadGame",
                          "Credits": 1000
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            )
        );
        InaraPublicationResult redirected = await redirectPublisher.FlushAsync();
        Assert.Equal(0, redirected.AcceptedEventCount);
        Assert.Equal(0, redirected.PendingEventCount);
        Assert.Contains(redirected.Warnings, warning => warning.Contains("API status 302"));
    }

    [Fact]
    public async Task RetryAfterExtendsAutomaticRetryWindow()
    {
        var time = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-07-28T12:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture)
        );
        var handler = new InaraResponseHandler(HttpStatusCode.TooManyRequests) { RetryAfter = TimeSpan.FromMinutes(2) };
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler), time);
        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:00:00Z",
                          "event": "LoadGame",
                          "Credits": 1000
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            )
        );
        await publisher.FlushAsync();
        handler.StatusCode = HttpStatusCode.OK;

        time.Advance(TimeSpan.FromMinutes(1));
        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:01:00Z",
                          "event": "Music"
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            )
        );
        Assert.Equal(1, handler.RequestCount);

        time.Advance(TimeSpan.FromMinutes(1));
        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-07-28T12:02:00Z",
                          "event": "Music"
                        }
                        """
                    ),
                ],
                cargo: null,
                allowPublishing: true,
                allowSharedData: true
            )
        );
        await WaitForAsync(() => handler.RequestCount == 2);
    }

    [Fact]
    public async Task MixedEventStormSendsAtMostTwoPostsPerRollingMinuteAndKeepsOverflow()
    {
        var time = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-07-28T12:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture)
        );
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler), time);

        await ApplyLiveAsync(publisher, LoadGameJson(), FsdJumpJson("Sirius"));
        InaraPublicationResult first = await publisher.FlushAsync();
        await ApplyLiveAsync(publisher, CargoJson(), FsdJumpJson("Sol"));
        InaraPublicationResult second = await publisher.FlushAsync();
        await ApplyLiveAsync(publisher, CargoJson(), FsdJumpJson("Achenar"), ShutdownJson());

        Assert.True(first.AcceptedEventCount > 0);
        Assert.True(second.AcceptedEventCount > 0);
        Assert.Equal(2, handler.RequestCount);

        using var flushCancel = new CancellationTokenSource();
        Task<InaraPublicationResult> waitingFlush = publisher.FlushAsync(flushCancel.Token);
        await Task.Delay(50);
        Assert.False(waitingFlush.IsCompleted);
        await flushCancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitingFlush);

        time.Advance(InaraPublisher.RateLimitWindow);
        InaraPublicationResult later = await publisher.FlushAsync();

        Assert.Equal(3, handler.RequestCount);
        Assert.True(later.AcceptedEventCount > 0);
        Assert.Contains(
            handler.LastPayload!["events"]!,
            eventToken => eventToken.Value<string>("eventName") == "addCommanderTravelFSDJump"
        );
    }

    [Fact]
    public async Task HeaderRateLimitFourHundredRequeuesInsteadOfDroppingTheBatch()
    {
        var handler = new InaraResponseHandler
        {
            HeaderEventStatus = 400,
            HeaderEventStatusText = "API key temporarily revoked because of rate limiting (1 hour).",
        };
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));
        await ApplyLiveAsync(publisher, LoadGameJson());
        InaraPublicationResult deferred = await publisher.FlushAsync();

        Assert.Equal(0, deferred.AcceptedEventCount);
        Assert.True(deferred.PendingEventCount > 0);
        Assert.Contains(
            deferred.Warnings,
            warning =>
                warning.Contains("deferred", StringComparison.OrdinalIgnoreCase)
                && warning.Contains("rate limiting", StringComparison.OrdinalIgnoreCase)
        );

        handler.HeaderEventStatus = 200;
        handler.HeaderEventStatusText = null;
        InaraPublicationResult retried = await publisher.FlushAsync();
        Assert.True(retried.AcceptedEventCount > 0);
        Assert.Equal(0, retried.PendingEventCount);
    }

    [Fact]
    public async Task InventoryOnlyCargoWaitsForIdleInterval()
    {
        var time = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-07-28T12:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture)
        );
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler), time);

        await ApplyLiveAsync(publisher, LoadGameJson());
        await publisher.FlushAsync();
        Assert.Equal(1, handler.RequestCount);

        await ApplyLiveAsync(publisher, CargoJson());
        Assert.Equal(1, handler.RequestCount);

        time.Advance(InaraPublisher.InventoryIdleInterval - TimeSpan.FromSeconds(1));
        await ApplyLiveAsync(publisher, CargoJson());
        Assert.Equal(1, handler.RequestCount);

        time.Advance(TimeSpan.FromSeconds(2));
        await ApplyLiveAsync(publisher, CargoJson());
        await WaitForAsync(() => handler.Payloads.Count == 2);
        Assert.Contains(
            handler.LastPayload!["events"]!,
            eventToken => eventToken.Value<string>("eventName") == "setCommanderInventoryCargo"
        );
    }

    [Fact]
    public async Task TravelEventDoesNotWaitForInventoryIdleInterval()
    {
        var time = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-07-28T12:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture)
        );
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler), time);

        await ApplyLiveAsync(publisher, LoadGameJson());
        await publisher.FlushAsync();
        await ApplyLiveAsync(publisher, CargoJson());
        Assert.Equal(1, handler.RequestCount);

        await ApplyLiveAsync(publisher, FsdJumpJson("Sol"));
        await WaitForAsync(() => handler.Payloads.Count == 2);
        Assert.Contains(
            handler.LastPayload!["events"]!,
            eventToken => eventToken.Value<string>("eventName") == "addCommanderTravelFSDJump"
        );
        Assert.Contains(
            handler.LastPayload["events"]!,
            eventToken => eventToken.Value<string>("eventName") == "setCommanderInventoryCargo"
        );
    }

    [Fact]
    public async Task TravelEventAdvancesAnOutdatedInventoryDeadline()
    {
        var time = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-07-28T12:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture)
        );
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler), time);

        await ApplyLiveAsync(publisher, LoadGameJson());
        await publisher.FlushAsync();
        await ApplyLiveAsync(publisher, CargoJson());
        Assert.Equal(1, handler.RequestCount);

        time.Advance(TimeSpan.FromMinutes(1));
        await ApplyLiveAsync(publisher, FsdJumpJson("Sol"));
        Assert.Equal(1, handler.RequestCount);

        time.Advance(InaraPublisher.SendInterval);
        await ApplyLiveAsync(
            publisher,
            """
            {
              "timestamp": "2026-07-28T12:01:35Z",
              "event": "Music"
            }
            """
        );
        await WaitForAsync(() => handler.Payloads.Count == 2);
        Assert.Contains(
            handler.LastPayload!["events"]!,
            eventToken => eventToken.Value<string>("eventName") == "addCommanderTravelFSDJump"
        );
    }

    [Fact]
    public async Task SuccessfulUploadsAreLoggedInFifteenMinuteAggregates()
    {
        var time = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-07-28T12:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture)
        );
        var handler = new InaraResponseHandler();
        var logs = new List<string>();
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler), time, logs.Add);

        await ApplyLiveAsync(publisher, LoadGameJson(), FsdJumpJson("Sirius"));
        InaraPublicationResult first = await publisher.FlushAsync();
        Assert.Empty(logs);

        time.Advance(TimeSpan.FromMinutes(15));
        await ApplyLiveAsync(publisher, FsdJumpJson("Sol"));
        await publisher.FlushAsync();

        string eventLabel = first.AcceptedEventCount == 1 ? "Inara event" : "Inara events";
        Assert.Equal(
            [$"Inara uploaded {first.AcceptedEventCount:N0} {eventLabel} in the previous 15-minute activity window."],
            logs
        );
    }

    [Fact]
    public async Task AcceptedEventLogRecordsTravelWithoutApiKeys()
    {
        string logDirectory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-inara-{Guid.NewGuid():N}");
        Directory.CreateDirectory(logDirectory);
        try
        {
            var handler = new InaraResponseHandler();
            var accepted = new InaraAcceptedEventLog(Path.Combine(logDirectory, "inara-accepted.txt"));
            using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler), acceptedEventLog: accepted);
            await ApplyLiveAsync(publisher, LoadGameJson(), FsdJumpJson("Sirius"));
            await publisher.FlushAsync();

            IReadOnlyList<string> lines = accepted.ReadLines();
            Assert.Contains(lines, line => line.Contains("addCommanderTravelFSDJump", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.Contains("Sirius", StringComparison.Ordinal));
            Assert.All(lines, line => Assert.DoesNotContain("personal-key", line, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(logDirectory, true);
        }
    }

    [Fact]
    public async Task PostsCommanderWritesToTheDocumentedInaraEndpoint()
    {
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));
        await ApplyLiveAsync(publisher, LoadGameJson());
        await publisher.FlushAsync();

        Assert.Equal(new Uri(InaraPublisher.Endpoint), handler.LastRequestUri);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        JObject header = Assert.IsType<JObject>(handler.LastPayload!["header"]);
        Assert.Equal("SrvSurvey", header.Value<string>("appName"));
        Assert.Equal("personal-key", header.Value<string>("APIkey"));
        Assert.Equal("Test Commander", header.Value<string>("commanderName"));
        Assert.NotEmpty(Assert.IsType<JArray>(handler.LastPayload["events"]));
    }

    [Fact]
    public async Task ThrowingLogCallbackDoesNotFailOrDropAnAcceptedUpload()
    {
        var time = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-07-28T12:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture)
        );
        var handler = new InaraResponseHandler();
        using var publisher = new InaraPublisher(
            "2.0.95.0",
            new HttpClient(handler),
            time,
            _ => throw new InvalidOperationException("log boom")
        );

        await ApplyLiveAsync(publisher, LoadGameJson());
        InaraPublicationResult first = await publisher.FlushAsync();
        Assert.True(first.AcceptedEventCount > 0);
        Assert.Equal(0, first.PendingEventCount);

        time.Advance(TimeSpan.FromMinutes(15));
        await ApplyLiveAsync(publisher, FsdJumpJson("Sol"));
        InaraPublicationResult second = await publisher.FlushAsync();
        Assert.True(second.AcceptedEventCount > 0);
        Assert.Equal(0, second.PendingEventCount);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task UnwritableAcceptedEventLogDoesNotFailOrDropAnAcceptedUpload()
    {
        string occupied = Path.Combine(Path.GetTempPath(), $"SrvSurvey-inara-occupied-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(occupied, "not-a-directory");
        try
        {
            var handler = new InaraResponseHandler();
            var accepted = new InaraAcceptedEventLog(Path.Combine(occupied, "inara-accepted.txt"));
            using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler), acceptedEventLog: accepted);

            await ApplyLiveAsync(publisher, LoadGameJson(), FsdJumpJson("Sirius"));
            InaraPublicationResult result = await publisher.FlushAsync();

            Assert.True(result.AcceptedEventCount > 0);
            Assert.Equal(0, result.PendingEventCount);
            Assert.Equal(1, handler.RequestCount);
            Assert.Empty(accepted.ReadLines());
        }
        finally
        {
            File.Delete(occupied);
        }
    }

    [Fact]
    public async Task HttpFourHundredWithoutBodyDefersTheBatchForRetry()
    {
        var handler = new InaraResponseHandler(HttpStatusCode.BadRequest);
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));
        await ApplyLiveAsync(publisher, LoadGameJson());
        InaraPublicationResult deferred = await publisher.FlushAsync();

        Assert.Equal(0, deferred.AcceptedEventCount);
        Assert.True(deferred.PendingEventCount > 0);
        Assert.Contains(deferred.Warnings, warning => warning.Contains("deferred", StringComparison.OrdinalIgnoreCase));
        Assert.All(
            deferred.Warnings,
            warning => Assert.DoesNotContain("personal-key", warning, StringComparison.Ordinal)
        );

        handler.StatusCode = HttpStatusCode.OK;
        InaraPublicationResult retried = await publisher.FlushAsync();
        Assert.True(retried.AcceptedEventCount > 0);
        Assert.Equal(0, retried.PendingEventCount);
    }

    [Fact]
    public async Task HeaderFourHundredInvalidApiKeyDropsTheBatchInsteadOfRetryingForever()
    {
        var handler = new InaraResponseHandler { HeaderEventStatus = 400, HeaderEventStatusText = "Invalid API key." };
        using var publisher = new InaraPublisher("2.0.95.0", new HttpClient(handler));
        await ApplyLiveAsync(publisher, LoadGameJson());
        InaraPublicationResult rejected = await publisher.FlushAsync();

        Assert.Equal(0, rejected.AcceptedEventCount);
        Assert.Equal(0, rejected.PendingEventCount);
        Assert.Contains(rejected.Warnings, warning => warning.Contains("rejected", StringComparison.OrdinalIgnoreCase));
        Assert.All(
            rejected.Warnings,
            warning => Assert.DoesNotContain("personal-key", warning, StringComparison.Ordinal)
        );

        handler.HeaderEventStatus = 200;
        handler.HeaderEventStatusText = null;
        InaraPublicationResult empty = await publisher.FlushAsync();
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(0, empty.AcceptedEventCount);
        Assert.Equal(0, empty.PendingEventCount);
    }

    private static async Task ApplyLiveAsync(InaraPublisher publisher, params string[] jsonEvents)
    {
        await publisher.ApplyAsync(
            CreateUpdate(jsonEvents.Select(Event).ToArray(), cargo: null, allowPublishing: true, allowSharedData: true)
        );
    }

    private static string LoadGameJson() =>
        """
            {
              "timestamp": "2026-07-28T12:00:00Z",
              "event": "LoadGame",
              "Credits": 1000,
              "Loan": 0
            }
            """;

    private static string FsdJumpJson(string system) =>
        $$"""
            {
              "timestamp": "2026-07-28T12:00:30Z",
              "event": "FSDJump",
              "StarSystem": "{{system}}",
              "StarPos": [6.25, -1.25, -5.75],
              "JumpDist": 8.6
            }
            """;

    private static string CargoJson() =>
        """
            {
              "timestamp": "2026-07-28T12:00:31Z",
              "event": "Cargo",
              "Vessel": "Ship",
              "Inventory": [{ "Name": "tea", "Count": 2 }]
            }
            """;

    private static string ShutdownJson() =>
        """
            { "timestamp": "2026-07-28T12:00:32Z", "event": "Shutdown" }
            """;

    private static InaraPublicationUpdate CreateUpdate(
        IReadOnlyList<JournalEventEnvelope> events,
        CargoSnapshot? cargo,
        bool allowPublishing,
        bool allowSharedData,
        InaraPublicationOptions? options = null
    )
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
            options ?? Options
        );
    }

    private static JournalEventEnvelope Event(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? parsed, out string? error), error);
        return Assert.IsType<JournalEventEnvelope>(parsed);
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        while (!predicate() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(predicate(), "The expected asynchronous operation did not complete.");
    }

    private sealed class InaraResponseHandler(HttpStatusCode? initialStatus = null) : HttpMessageHandler
    {
        private int requestCount;

        public HttpStatusCode StatusCode { get; set; } = initialStatus ?? HttpStatusCode.OK;

        public int RequestCount => Volatile.Read(ref requestCount);

        public JToken? LastPayload { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public HttpMethod? LastMethod { get; private set; }

        public List<JToken> Payloads { get; } = [];

        public bool ReturnOversizedResponse { get; init; }

        public bool ReturnMalformedEventResponse { get; init; }

        public int HeaderEventStatus { get; set; } = 200;

        public string? HeaderEventStatusText { get; set; }

        public Func<int, int> EventStatusSelector { get; init; } = _ => 200;

        public TimeSpan? RetryAfter { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref requestCount);
            LastRequestUri = request.RequestUri;
            LastMethod = request.Method;
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            LastPayload = JToken.Parse(body);
            Payloads.Add(LastPayload);
            if ((int)StatusCode is < 200 or > 299)
            {
                var failed = new HttpResponseMessage(StatusCode);
                if (RetryAfter is { } retryAfter)
                {
                    failed.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter);
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

            int eventCount = LastPayload["events"]?.Count() ?? 0;
            JArray responseEvents = ReturnMalformedEventResponse
                ? new JArray(Enumerable.Range(0, eventCount).Select(_ => JValue.CreateString("malformed")))
                : new JArray(
                    Enumerable
                        .Range(0, eventCount)
                        .Select(index => new JObject { ["eventStatus"] = EventStatusSelector(index) })
                );
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(
                    new JObject
                    {
                        ["header"] = new JObject
                        {
                            ["eventStatus"] = HeaderEventStatus,
                            ["eventStatusText"] = HeaderEventStatusText,
                        },
                        ["events"] = responseEvents,
                    }.ToString(),
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }

    private sealed class BlockingInaraHandler : HttpMessageHandler
    {
        private int requestCount;
        private readonly TaskCompletionSource releaseResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RequestCount => Volatile.Read(ref requestCount);

        public TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource RequestCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ResponseCompleted { get; private set; }

        public void ReleaseResponse()
        {
            releaseResponse.TrySetResult();
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref requestCount);
            RequestStarted.TrySetResult();
            try
            {
                string body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var payload = JObject.Parse(body);
                await releaseResponse.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                ResponseCompleted = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        new JObject
                        {
                            ["header"] = new JObject { ["eventStatus"] = 200 },
                            ["events"] = new JArray(
                                payload["events"]!.Select(_ => new JObject { ["eventStatus"] = 200 })
                            ),
                        }.ToString(),
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            catch (OperationCanceledException)
            {
                RequestCancelled.TrySetResult();
                throw;
            }
        }
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) { }
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset utcNow;
        private readonly List<VirtualTimer> timers = [];
        private readonly Lock sync = new();

        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            this.utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (sync)
            {
                return utcNow;
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new VirtualTimer(this, callback, state);
            lock (sync)
            {
                timers.Add(timer);
            }

            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan duration)
        {
            var due = new List<Action>();
            lock (sync)
            {
                utcNow += duration;
                foreach (VirtualTimer timer in timers.ToArray())
                {
                    timer.CollectDue(utcNow, due);
                }
            }

            foreach (Action fire in due)
            {
                fire();
            }
        }

        private sealed class VirtualTimer(MutableTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset? dueAt;
            private TimeSpan period = Timeout.InfiniteTimeSpan;
            private bool disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Action? immediate = null;
                lock (owner.sync)
                {
                    if (disposed)
                    {
                        return false;
                    }

                    this.period = period;
                    if (dueTime == Timeout.InfiniteTimeSpan)
                    {
                        dueAt = null;
                        return true;
                    }

                    if (dueTime <= TimeSpan.Zero)
                    {
                        dueAt = null;
                        immediate = () => callback(state);
                    }
                    else
                    {
                        dueAt = owner.utcNow + dueTime;
                    }
                }

                immediate?.Invoke();
                return true;
            }

            public void CollectDue(DateTimeOffset now, List<Action> due)
            {
                if (disposed || dueAt is not { } scheduled || scheduled > now)
                {
                    return;
                }

                due.Add(() => callback(state));
                if (period > TimeSpan.Zero && period != Timeout.InfiniteTimeSpan)
                {
                    dueAt = now + period;
                }
                else
                {
                    dueAt = null;
                }
            }

            public void Dispose()
            {
                lock (owner.sync)
                {
                    disposed = true;
                    dueAt = null;
                    owner.timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
