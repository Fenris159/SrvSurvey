using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Edsm;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Edsm;

public sealed class EdsmPublisherTests
{
    private static readonly EdsmPublicationOptions Options = new(
        ApiKey: "personal-key",
        EdsmCommanderName: "EDSM Commander",
        ActiveCommanderName: "Game Commander",
        FrontierId: "F123456",
        GameVersion: "4.1.0.100",
        GameBuild: "r300000/r0",
        IsOdyssey: true);

    [Fact]
    public async Task StatisticsMulticrewObjectDoesNotTriggerCrewMode()
    {
        var handler = new EdsmResponseHandler();
        using var publisher = CreatePublisher(handler);

        var result = await publisher.ApplyAsync(CreateUpdate(
            [
                Event("""
                    {
                      "timestamp": "2026-08-25T12:00:00Z",
                      "event": "Statistics",
                      "Multicrew": {
                        "Multicrew_Time_Total": 1
                      }
                    }
                    """),
                Event("""
                    {
                      "timestamp": "2026-08-25T12:00:01Z",
                      "event": "FSDJump",
                      "StarSystem": "Sol"
                    }
                    """),
            ]));

        Assert.Equal(["Statistics", "FSDJump"], result.QueuedEventNames);
    }

    [Fact]
    public async Task BootstrapSeedsContextAndLiveBatchUsesRequiredFormFields()
    {
        var handler = new EdsmResponseHandler();
        using var publisher = CreatePublisher(handler);

        var bootstrap = await publisher.ApplyAsync(CreateUpdate(
            [
                Event("""
                    {
                      "timestamp": "2026-08-25T12:00:00Z",
                      "event": "Location",
                      "StarSystem": "Sol",
                      "SystemAddress": 10477373803,
                      "StarPos": [0.0, 0.0, 0.0],
                      "Docked": true,
                      "StationName": "Galileo",
                      "MarketID": 128666762
                    }
                    """),
                Event("""
                    {
                      "timestamp": "2026-08-25T12:00:01Z",
                      "event": "Loadout",
                      "ShipID": 42
                    }
                    """),
            ],
            allowPublishing: false));
        Assert.Equal(0, bootstrap.QueuedEventCount);

        var live = await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-08-25T12:01:00Z",
                  "event": "CollectCargo",
                  "Type": "tea",
                  "Stolen": 0
                }
                """)]));
        Assert.Equal(1, live.QueuedEventCount);

        var sent = await publisher.FlushAsync();

        Assert.Equal(1, sent.AcceptedEventCount);
        Assert.Equal(1, handler.PostCount);
        Assert.Equal("EDSM Commander", handler.LastForm["commanderName"]);
        Assert.Equal("personal-key", handler.LastForm["apiKey"]);
        Assert.Equal("SrvSurvey", handler.LastForm["fromSoftware"]);
        Assert.Equal("2.1.3.0", handler.LastForm["fromSoftwareVersion"]);
        Assert.Equal("4.1.0.100", handler.LastForm["fromGameVersion"]);
        Assert.Equal("r300000/r0", handler.LastForm["fromGameBuild"]);
        var message = JArray.Parse(handler.LastForm["message"]);
        var uploaded = Assert.IsType<JObject>(Assert.Single(message));
        Assert.Equal("CollectCargo", uploaded.Value<string>("event"));
        Assert.Equal("Sol", uploaded.Value<string>("_systemName"));
        Assert.Equal(10477373803, uploaded.Value<long>("_systemAddress"));
        Assert.Equal("Galileo", uploaded.Value<string>("_stationName"));
        Assert.Equal(128666762, uploaded.Value<long>("_marketId"));
        Assert.Equal(42, uploaded.Value<long>("_shipId"));
        Assert.Equal([0.0, 0.0, 0.0], uploaded["_systemCoordinates"]!.Values<double>());
    }

    [Fact]
    public async Task InvalidDiscardListFailsClosedAndUsesBoundedRetryCadence()
    {
        var handler = new EdsmResponseHandler
        {
            DiscardedEvents = [],
        };
        using var publisher = CreatePublisher(handler);
        await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-08-25T12:00:00Z",
                  "event": "FSDJump",
                  "StarSystem": "Sol"
                }
                """)]));

        var first = await publisher.FlushAsync();
        var second = await publisher.FlushAsync();

        Assert.Equal(0, handler.PostCount);
        Assert.Equal(1, handler.GetCount);
        Assert.Equal(1, first.PendingEventCount);
        Assert.Contains(
            first.Warnings,
            warning => warning.Contains("discarded-event list", StringComparison.Ordinal));
        Assert.Equal(1, second.PendingEventCount);
    }

    [Fact]
    public async Task CurrentDiscardListSilentlyFiltersUnsupportedEventsBeforePost()
    {
        var handler = new EdsmResponseHandler
        {
            DiscardedEvents = ["SendText", "Screenshot"],
        };
        using var publisher = CreatePublisher(handler);

        await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-08-25T12:00:00Z",
                  "event": "SendText",
                  "To": "local",
                  "Message": "private text"
                }
                """)]));
        var result = await publisher.FlushAsync();

        Assert.Equal(0, result.AcceptedEventCount);
        Assert.Equal(0, result.PendingEventCount);
        Assert.Equal(0, handler.PostCount);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task SuccessfulUploadsAreSummarizedOncePerFifteenMinuteWindow()
    {
        var handler = new EdsmResponseHandler();
        var time = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-08-25T12:00:00Z"));
        var logs = new List<string>();
        using var publisher = new EdsmPublisher(
            "2.1.3.0",
            new HttpClient(handler),
            time,
            logs.Add);

        await publisher.ApplyAsync(CreateUpdate(
            [
                Event("""
                    {
                      "timestamp": "2026-08-25T12:00:00Z",
                      "event": "FSDJump",
                      "StarSystem": "Sol"
                    }
                    """),
                Event("""
                    {
                      "timestamp": "2026-08-25T12:00:01Z",
                      "event": "FSDJump",
                      "StarSystem": "Sirius"
                    }
                    """),
            ]));
        await publisher.FlushAsync();

        Assert.Empty(logs);

        time.Advance(TimeSpan.FromMinutes(15));
        await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-08-25T12:15:00Z",
                  "event": "FSDJump",
                  "StarSystem": "Achenar"
                }
                """)]));
        await publisher.FlushAsync();

        Assert.Equal(
            ["EDSM uploaded 2 journal events in the previous 15-minute activity window."],
            logs);
    }

    [Fact]
    public async Task MissingCredentialsAndNonLiveSessionsNeverLoadOrUpload()
    {
        var handler = new EdsmResponseHandler();
        using var publisher = CreatePublisher(handler);
        var journalEvent = Event("""
            {
              "timestamp": "2026-08-25T12:00:00Z",
              "event": "FSDJump",
              "StarSystem": "Sol"
            }
            """);

        await publisher.ApplyAsync(CreateUpdate(
            [journalEvent],
            options: Options with { ApiKey = null }));
        await publisher.ApplyAsync(CreateUpdate(
            [journalEvent],
            options: Options with
            {
                GameVersion = "3.8.0.0",
                IsOdyssey = false,
            }));
        await publisher.ApplyAsync(CreateUpdate(
            [journalEvent],
            options: Options with { GameVersion = "4.1.0 beta" }));

        Assert.Equal(0, handler.GetCount);
        Assert.Equal(0, handler.PostCount);
    }

    [Theory]
    [InlineData("QuitACrew")]
    [InlineData("EndCrewSession")]
    [InlineData("CrewMemberQuits")]
    public async Task MulticrewEventsAreSuppressedUntilCrewSessionEnds(
        string crewEndEvent)
    {
        var handler = new EdsmResponseHandler
        {
            DiscardedEvents = ["JoinACrew"],
        };
        using var publisher = CreatePublisher(handler);

        var result = await publisher.ApplyAsync(CreateUpdate(
            [
                Event("""
                    {
                      "timestamp": "2026-08-25T12:00:00Z",
                      "event": "JoinACrew"
                    }
                    """),
                Event("""
                    {
                      "timestamp": "2026-08-25T12:00:01Z",
                      "event": "FSDJump",
                      "StarSystem": "Sirius"
                    }
                    """),
                Event($$"""
                    {
                      "timestamp": "2026-08-25T12:00:02Z",
                      "event": "{{crewEndEvent}}"
                    }
                    """),
                Event("""
                    {
                      "timestamp": "2026-08-25T12:00:03Z",
                      "event": "FSDJump",
                      "StarSystem": "Vega"
                    }
                    """),
            ]));
        var sent = await publisher.FlushAsync();

        Assert.Equal([crewEndEvent, "FSDJump"], result.QueuedEventNames);
        Assert.Equal(2, sent.AcceptedEventCount);
    }

    [Fact]
    public async Task TransientFailureRetainsBatchAndRetriesWithoutImmediateLoop()
    {
        var handler = new EdsmResponseHandler
        {
            PostStatusCode = HttpStatusCode.ServiceUnavailable,
        };
        using var publisher = CreatePublisher(handler);
        await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-08-25T12:00:00Z",
                  "event": "FSDJump",
                  "StarSystem": "Sol"
                }
                """)]));

        var deferred = await publisher.FlushAsync();

        Assert.Equal(1, deferred.PendingEventCount);
        Assert.Equal(1, handler.PostCount);
        Assert.Contains(
            deferred.Warnings,
            warning => warning.Contains("retained in memory", StringComparison.Ordinal));

        handler.PostStatusCode = HttpStatusCode.OK;
        var retried = await publisher.FlushAsync();
        Assert.Equal(1, retried.AcceptedEventCount);
        Assert.Equal(2, handler.PostCount);
    }

    [Fact]
    public async Task FatalCredentialResponsePausesUntilCredentialsChange()
    {
        var handler = new EdsmResponseHandler
        {
            TopStatus = 203,
            TopMessage = "Commander name/API Key not found",
        };
        using var publisher = CreatePublisher(handler);
        await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-08-25T12:00:00Z",
                  "event": "FSDJump",
                  "StarSystem": "Sol"
                }
                """)]));
        var rejected = await publisher.FlushAsync();
        Assert.Contains(
            rejected.Warnings,
            warning => warning.Contains("203", StringComparison.Ordinal));

        await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-08-25T12:01:00Z",
                  "event": "FSDJump",
                  "StarSystem": "Sirius"
                }
                """)]));
        await publisher.FlushAsync();
        Assert.Equal(1, handler.PostCount);

        handler.TopStatus = 100;
        handler.TopMessage = "OK";
        await publisher.ApplyAsync(CreateUpdate(
            [Event("""
                {
                  "timestamp": "2026-08-25T12:02:00Z",
                  "event": "FSDJump",
                  "StarSystem": "Achenar"
                }
                """)],
            options: Options with { ApiKey = "replacement-key" }));
        var accepted = await publisher.FlushAsync();
        Assert.Equal(1, accepted.AcceptedEventCount);
        Assert.Equal(2, handler.PostCount);
    }

    [Fact]
    public async Task ApiStatus402RetriesOnlyUnknownCatalogEvent()
    {
        var handler = new EdsmResponseHandler
        {
            EventStatusSelector = index => index == 0 ? 100 : 402,
        };
        using var publisher = CreatePublisher(handler);
        await publisher.ApplyAsync(CreateUpdate(
            [
                Event("""
                    {
                      "timestamp": "2026-08-25T12:00:00Z",
                      "event": "FSDJump",
                      "StarSystem": "Sol"
                    }
                    """),
                Event("""
                    {
                      "timestamp": "2026-08-25T12:00:01Z",
                      "event": "CollectCargo",
                      "Type": "future_item"
                    }
                    """),
            ]));

        var partial = await publisher.FlushAsync();
        Assert.Equal(1, partial.AcceptedEventCount);
        Assert.Equal(1, partial.PendingEventCount);

        handler.EventStatusSelector = _ => 100;
        var retried = await publisher.FlushAsync();
        Assert.Equal(1, retried.AcceptedEventCount);
        var retriedBatch = JArray.Parse(handler.Forms[1]["message"]);
        Assert.Equal("CollectCargo", Assert.Single(retriedBatch).Value<string>("event"));
    }

    private static EdsmPublisher CreatePublisher(EdsmResponseHandler handler)
    {
        return new EdsmPublisher("2.1.3.0", new HttpClient(handler));
    }

    private static EdsmPublicationUpdate CreateUpdate(
        IReadOnlyList<JournalEventEnvelope> events,
        bool allowPublishing = true,
        EdsmPublicationOptions? options = null)
    {
        return new EdsmPublicationUpdate(
            events,
            JournalPath: "Journal.2026-08-25T120000.01.log",
            allowPublishing,
            options ?? Options);
    }

    private static JournalEventEnvelope Event(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var parsed, out var error),
            error);
        return Assert.IsType<JournalEventEnvelope>(parsed);
    }

    private sealed class EdsmResponseHandler : HttpMessageHandler
    {
        private int getCount;
        private int postCount;

        public string[] DiscardedEvents { get; set; } = ["SendText"];

        public HttpStatusCode PostStatusCode { get; set; } = HttpStatusCode.OK;

        public int TopStatus { get; set; } = 100;

        public string TopMessage { get; set; } = "OK";

        public Func<int, int> EventStatusSelector { get; set; } = _ => 100;

        public int GetCount => Volatile.Read(ref getCount);

        public int PostCount => Volatile.Read(ref postCount);

        public Dictionary<string, string> LastForm => Forms[^1];

        public List<Dictionary<string, string>> Forms { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                Interlocked.Increment(ref getCount);
                return JsonResponse(new JArray(DiscardedEvents));
            }

            Interlocked.Increment(ref postCount);
            var encoded = await request.Content!
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            var form = ParseForm(encoded);
            Forms.Add(form);
            if (PostStatusCode != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(PostStatusCode);
            }

            var eventCount = JArray.Parse(form["message"]).Count;
            return JsonResponse(new JObject
            {
                ["msgnum"] = TopStatus,
                ["msg"] = TopMessage,
                ["events"] = new JArray(Enumerable.Range(0, eventCount).Select(
                    index => new JObject
                    {
                        ["msgnum"] = EventStatusSelector(index),
                        ["msg"] = EventStatusSelector(index) == 100
                            ? "OK"
                            : "Item unknown",
                    })),
            });
        }

        private static HttpResponseMessage JsonResponse(JToken body)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    body.ToString(Formatting.None),
                    Encoding.UTF8,
                    "application/json"),
            };
        }

        private static Dictionary<string, string> ParseForm(string content)
        {
            return content.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Split('=', 2))
                .ToDictionary(
                    item => Decode(item[0]),
                    item => Decode(item.Length > 1 ? item[1] : string.Empty),
                    StringComparer.Ordinal);
        }

        private static string Decode(string value)
        {
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan elapsed)
        {
            utcNow += elapsed;
        }
    }
}
