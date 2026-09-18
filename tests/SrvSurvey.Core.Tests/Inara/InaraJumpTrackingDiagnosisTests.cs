using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Inara;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Inara;

/// <summary>
/// Diagnostic feedback loop for "post-Wille FSDJumps while SrvSurvey was running
/// should produce Inara travel events". Uses the real 2026-09-18 morning sequence.
/// </summary>
public sealed class InaraJumpTrackingDiagnosisTests
{
    private static readonly InaraPublicationOptions Options = new(
        ApiKey: "personal-key",
        CommanderName: "Fenris Nihilus",
        FrontierId: "F472567",
        GameVersion: "4.4.1.1",
        IsOdyssey: true
    );

    [Fact]
    public async Task MorningTimbalderisJumpSequencePublishesTravelFsdJumps()
    {
        var handler = new CapturingHandler();
        using var publisher = new InaraPublisher("2.1.3.0", new HttpClient(handler));

        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-09-18T06:49:30Z",
                          "event": "LoadGame",
                          "Commander": "Fenris Nihilus",
                          "FID": "F472567",
                          "gameversion": "4.4.1.1",
                          "Credits": 1000
                        }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-09-18T06:50:34Z",
                          "event": "Location",
                          "Docked": true,
                          "StarSystem": "HIP 50070",
                          "StarPos": [50.0, 0.0, 20.0],
                          "StationName": "Test"
                        }
                        """
                    ),
                ],
                allowPublishing: true
            )
        );

        await publisher.ApplyAsync(
            CreateUpdate(
                [
                    Event(
                        """
                        {
                          "timestamp": "2026-09-18T07:02:21Z",
                          "event": "FSDJump",
                          "StarSystem": "Crucis Sector HB-X b1-7",
                          "StarPos": [1.0, 2.0, 3.0]
                        }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-09-18T07:03:12Z",
                          "event": "FSDJump",
                          "StarSystem": "Sounti",
                          "StarPos": [4.0, 5.0, 6.0]
                        }
                        """
                    ),
                    Event(
                        """
                        {
                          "timestamp": "2026-09-18T07:04:04Z",
                          "event": "FSDJump",
                          "StarSystem": "Timbalderis",
                          "StarPos": [69.09375, 10.03125, 18.40625]
                        }
                        """
                    ),
                ],
                allowPublishing: true
            )
        );

        InaraPublicationResult flushed = await publisher.FlushAsync();
        Assert.True(handler.RequestCount > 0, "Expected at least one Inara HTTP request after FlushAsync.");
        Assert.True(flushed.AcceptedEventCount > 0, "Expected Inara to accept queued events.");

        string[] travelSystems = handler
            .Payloads.SelectMany(payload => payload["events"]!.OfType<JObject>())
            .Where(item => item.Value<string>("eventName") == "addCommanderTravelFSDJump")
            .Select(item => item["eventData"]!.Value<string>("starsystemName")!)
            .ToArray();

        Assert.Equal(["Crucis Sector HB-X b1-7", "Sounti", "Timbalderis"], travelSystems);
    }

    private static InaraPublicationUpdate CreateUpdate(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        bool allowPublishing
    )
    {
        return new InaraPublicationUpdate(
            journalEvents,
            Status: null,
            Cargo: null,
            JournalPath: @"C:\Users\Drew\Saved Games\Frontier Developments\Elite Dangerous\Journal.2026-09-18T014831.01.log",
            AllowPublishing: allowPublishing,
            AllowSharedData: true,
            SystemName: null,
            StationName: null,
            BodyName: null,
            ShipType: null,
            ShipId: null,
            ShipName: null,
            ShipIdent: null,
            Options
        );
    }

    private static JournalEventEnvelope Event(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? envelope, out string? error), error);
        return envelope!;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public List<JObject> Payloads { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestCount++;
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Payloads.Add(JObject.Parse(body));
            var events = (JArray)JObject.Parse(body)["events"]!;
            var responseEvents = new JArray(
                events.Select(_ => new JObject { ["eventStatus"] = 200, ["eventStatusText"] = "OK" })
            );
            string responseJson = new JObject
            {
                ["header"] = new JObject { ["eventStatus"] = 200 },
                ["events"] = responseEvents,
            }.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }
}
