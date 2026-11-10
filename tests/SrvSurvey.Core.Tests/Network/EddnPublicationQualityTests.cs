using System.IO.Compression;
using System.Net;
using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Tests.Network;

[Trait("Category", "JournalUploadScenario")]
public sealed class EddnPublicationQualityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "srv-eddn-quality-" + Guid.NewGuid().ToString("N"));
    private readonly List<JObject> payloads = [];
    private readonly DateTimeOffset now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ContentDuplicatesStaySuppressedAcrossRestartWithoutLosingNewObservations()
    {
        const string scan =
            """{"timestamp":"2026-11-10T12:00:10Z","event":"SAASignalsFound","SystemAddress":123,"BodyID":4,"BodyName":"Test A 1","Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}]}""";
        using (EddnPublisher first = Create())
        {
            await Bootstrap(first);
            await first.ApplyAsync(Request(true, [Event(scan, "source-a"), Event(scan, "source-b")]));
            await first.ProcessPendingAsync();
            Assert.Single(payloads);
        }
        using (EddnPublisher reopened = Create())
        {
            await Bootstrap(reopened);
            await reopened.ApplyAsync(Request(true, [Event(scan, "source-c")]));
            await reopened.ProcessPendingAsync();
            Assert.Single(payloads);
            await reopened.ApplyAsync(
                Request(true, [Event(scan.Replace("12:00:10", "12:00:11", StringComparison.Ordinal), "source-d")])
            );
            await reopened.ProcessPendingAsync();
            Assert.Equal(2, payloads.Count);
        }
        using EddnPublisher other = Create();
        await Bootstrap(other, "Other Cmdr");
        await other.ApplyAsync(Request(true, [Event(scan, "source-e")], "Other Cmdr"));
        await other.ProcessPendingAsync();
        Assert.Equal(
            ["Test Cmdr", "Test Cmdr", "Other Cmdr"],
            payloads.Select(payload => payload["header"]!.Value<string>("uploaderID"))
        );
    }

    [Fact]
    public async Task PermanentRejectionDoesNotBlockTheSameContentFromANewSource()
    {
        const string scan =
            """{"timestamp":"2026-11-10T12:00:10Z","event":"SAASignalsFound","SystemAddress":123,"BodyID":4,"BodyName":"Test A 1","Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}]}""";
        using EddnPublisher publisher = Create(rejectFirst: true);
        await Bootstrap(publisher);
        await publisher.ApplyAsync(Request(true, [Event(scan, "rejected-source")]));
        await publisher.ProcessPendingAsync();
        Assert.Single(payloads);
        await publisher.ApplyAsync(Request(true, [Event(scan, "rejected-source"), Event(scan, "new-source")]));
        await publisher.ProcessPendingAsync();
        Assert.Equal(2, payloads.Count);
        await publisher.ApplyAsync(Request(true, [Event(scan, "duplicate-after-success")]));
        await publisher.ProcessPendingAsync();
        Assert.Equal(2, payloads.Count);
    }

    [Fact]
    public async Task LeavingABodyPreventsStalePlanetAugmentationOnCarrierDocking()
    {
        using EddnPublisher publisher = Create();
        await Bootstrap(publisher);
        await publisher.ApplyAsync(
            Request(
                true,
                [
                    Event(
                        """{"event":"ApproachBody","timestamp":"2026-11-10T12:00:01Z","StarSystem":"Test A","SystemAddress":123,"Body":"Test A 1","BodyID":4}"""
                    ),
                    Event("""{"event":"LeaveBody","timestamp":"2026-11-10T12:00:02Z","Body":"Test A 1","BodyID":4}"""),
                    Event(
                        """{"event":"Docked","timestamp":"2026-11-10T12:00:03Z","StarSystem":"Test A","SystemAddress":123,"MarketID":128000111,"StationName":"Test Carrier","StationType":"FleetCarrier"}"""
                    ),
                ]
            )
        );
        await publisher.ProcessPendingAsync();
        var message = (JObject)Assert.Single(payloads)["message"]!;
        Assert.Equal("Test Carrier", message.Value<string>("StationName"));
        Assert.Null(message["Body"]);
        Assert.Null(message["BodyType"]);
    }

    [Fact]
    public async Task TemporaryLocationStillProvidesContextForFollowingGalaxyScans()
    {
        using EddnPublisher publisher = Create();
        await Bootstrap(publisher);
        await publisher.ApplyAsync(
            Request(
                true,
                [
                    Event(
                        """{"event":"Location","timestamp":"2026-11-10T12:00:01Z","StarSystem":"Test B","SystemAddress":456,"StarPos":[7,8,9],"MarketID":127000512,"StationName":"$Operations_Runner_Name:#index=1;","Docked":true}"""
                    ),
                    Event(
                        """{"event":"Scan","timestamp":"2026-11-10T12:00:02Z","StarSystem":"Test B","SystemAddress":456,"BodyName":"Test B 1","BodyID":4}"""
                    ),
                ]
            )
        );
        await publisher.ProcessPendingAsync();
        var message = (JObject)Assert.Single(payloads)["message"]!;
        Assert.Equal("Scan", message.Value<string>("event"));
        Assert.Equal("Test B", message.Value<string>("StarSystem"));
        Assert.Equal([7, 8, 9], message["StarPos"]!.Values<int>());
    }

    [Fact]
    public async Task LiveOrganicSamplingUsesCapturedMatchingStatusWithoutUploadingAnalysis()
    {
        using EddnPublisher publisher = Create();
        await Bootstrap(publisher);
        EddnApplyRequest request = Request(
            true,
            [
                Event("""{"event":"ApproachBody","timestamp":"2026-11-10T12:00:01Z","Body":"Test A 1","BodyID":4}"""),
                Event(
                    """{"event":"ScanOrganic","timestamp":"2026-11-10T12:00:02Z","ScanType":"Log","Genus":"$Genus_Name;","Species":"$Species_Name;","SystemAddress":123,"Body":4}"""
                ),
                Event(
                    """{"event":"ScanOrganic","timestamp":"2026-11-10T12:00:03Z","ScanType":"Analyse","Genus":"$Genus_Name;","Species":"$Species_Name;","SystemAddress":123,"Body":4}"""
                ),
            ]
        );
        request = WithStatus(
            request,
            new EliteStatus
            {
                BodyName = "Test A 1",
                Latitude = 12.5,
                Longitude = -43.25,
                Flags = StatusFlags.HasLatLong,
            }
        );
        EddnPublicationResult result = await publisher.ApplyAsync(request);
        Assert.Empty(result.Warnings);
        await publisher.ProcessPendingAsync();
        JObject payload = Assert.Single(payloads);
        Assert.Equal("https://eddn.edcd.io/schemas/scanorganic/1", payload.Value<string>("$schemaRef"));
        Assert.Equal("Log", payload["message"]!.Value<string>("ScanType"));
        Assert.Equal(12.5, payload["message"]!.Value<double>("Latitude"));
        Assert.Equal(-43.25, payload["message"]!.Value<double>("Longitude"));
    }

    private static EddnApplyRequest WithStatus(EddnApplyRequest request, EliteStatus status) =>
        new()
        {
            JournalEvents = request.JournalEvents,
            CommanderName = request.CommanderName,
            FrontierId = request.FrontierId,
            Enabled = request.Enabled,
            AllowPublishing = request.AllowPublishing,
            GameVersion = request.GameVersion,
            GameBuild = request.GameBuild,
            Status = status,
        };

    private EddnPublisher Create(bool rejectFirst = false) =>
        new(
            "2.1.3.0-rc.77.11",
            new HttpClient(new CaptureHandler(payloads, rejectFirst)),
            new Uri("https://live.example.test/upload/"),
            Path.Combine(root, "queue.json"),
            utcNow: () => now,
            automaticProcessing: false
        );

    private static async Task Bootstrap(EddnPublisher publisher, string commander = "Test Cmdr") =>
        await publisher.ApplyAsync(
            Request(
                false,
                [
                    Event(
                        new JObject
                        {
                            ["event"] = "LoadGame",
                            ["Commander"] = commander,
                            ["Horizons"] = true,
                            ["Odyssey"] = true,
                        }.ToString()
                    ),
                    Event(
                        """{"event":"Location","timestamp":"2026-11-10T12:00:00Z","StarSystem":"Test A","SystemAddress":123,"StarPos":[1.5,-2,3]}"""
                    ),
                ],
                commander
            )
        );

    private static EddnApplyRequest Request(
        bool publishing,
        IReadOnlyList<JournalEventEnvelope> events,
        string commander = "Test Cmdr"
    ) =>
        new()
        {
            JournalEvents = events,
            CommanderName = commander,
            FrontierId = commander == "Test Cmdr" ? "F1" : "F2",
            Enabled = true,
            AllowPublishing = publishing,
            GameVersion = "4.4.2.1",
            GameBuild = "r123/r0 ",
        };

    private static JournalEventEnvelope Event(string json, string? source = null)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out string? reason), reason);
        return entry! with { SourceRecordIdentity = source };
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    private sealed class CaptureHandler(List<JObject> payloads, bool rejectFirst) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            using var stream = new MemoryStream(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            payloads.Add(JObject.Parse(await reader.ReadToEndAsync(cancellationToken)));
            return new HttpResponseMessage(
                rejectFirst && payloads.Count == 1 ? HttpStatusCode.BadRequest : HttpStatusCode.OK
            )
            {
                Content = new StringContent("OK"),
            };
        }
    }
}
