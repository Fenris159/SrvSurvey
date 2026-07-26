using System.Net;
using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Tests.Network;

public sealed class EddnPublisherTests
{
    [Fact]
    public async Task BootstrapBuildsContextAndDevPublishesSanitizedLiveEvent()
    {
        var requests = new List<RecordedRequest>();
        var publisher = CreatePublisher(requests);

        var bootstrap = await publisher.ApplyAsync(
            [
                Event("""
                    {"timestamp":"2026-07-25T12:00:00Z","event":"Fileheader","gameversion":"4.1.2.3","build":"r123/r0 "}
                    """),
                Event("""
                    {"timestamp":"2026-07-25T12:00:01Z","event":"LoadGame","Commander":"Test Cmdr","Horizons":true,"Odyssey":true}
                    """),
                Event("""
                    {"timestamp":"2026-07-25T12:00:02Z","event":"Location","StarSystem":"Test A","SystemAddress":123,"StarPos":[1.5,-2,3]}
                    """),
            ],
            status: null,
            enabled: true,
            environment: "dev",
            allowPublishing: false);
        var live = await publisher.ApplyAsync(
            [Event("""
                {"timestamp":"2026-07-25T12:01:00Z","event":"FSSBodySignals","SystemAddress":123,"BodyName":"Test A 1","BodyID":4,"Signals":[{"Type":"$SAA_SignalType_Biological;","Type_Localised":"Biological","Count":2}]}
                """)],
            status: null,
            enabled: true,
            environment: "dev",
            allowPublishing: true);

        Assert.Empty(bootstrap.Published);
        Assert.Empty(bootstrap.Warnings);
        var published = Assert.Single(live.Published);
        Assert.Equal("FSSBodySignals", published.EventName);
        Assert.Equal(
            "https://eddn.edcd.io/schemas/fssbodysignals/1/test",
            published.SchemaReference);
        var request = Assert.Single(requests);
        Assert.Equal("https://dev.example.test/upload/", request.Uri.ToString());
        Assert.Equal(HttpVersion.Version11, request.Version);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.StartsWith("application/json", request.ContentType);
        using var json = JsonDocument.Parse(request.Content);
        var root = json.RootElement;
        Assert.Equal(
            published.SchemaReference,
            root.GetProperty("$schemaRef").GetString());
        var header = root.GetProperty("header");
        Assert.Equal("Test Cmdr", header.GetProperty("uploaderID").GetString());
        Assert.Equal("4.1.2.3", header.GetProperty("gameversion").GetString());
        Assert.Equal("r123/r0 ", header.GetProperty("gamebuild").GetString());
        Assert.Equal("SrvSurvey", header.GetProperty("softwareName").GetString());
        Assert.Equal("2.0.95", header.GetProperty("softwareVersion").GetString());
        var message = root.GetProperty("message");
        Assert.Equal("Test A", message.GetProperty("StarSystem").GetString());
        Assert.Equal(
            [1.5, -2, 3],
            message.GetProperty("StarPos").EnumerateArray()
                .Select(value => value.GetDouble())
                .ToArray());
        Assert.True(message.GetProperty("horizons").GetBoolean());
        Assert.True(message.GetProperty("odyssey").GetBoolean());
        Assert.False(
            message.GetProperty("Signals")[0]
                .TryGetProperty("Type_Localised", out _));
    }

    [Fact]
    public async Task LiveJournalMessageStripsCommanderSpecificFieldsRecursively()
    {
        var requests = new List<RecordedRequest>();
        var publisher = CreatePublisher(requests);
        await BootstrapAsync(publisher);

        var result = await publisher.ApplyAsync(
            [Event("""
                {"timestamp":"2026-07-25T12:02:00Z","event":"FSDJump","StarSystem":"Test B","SystemAddress":456,"StarPos":[4,5,6],"Wanted":true,"FuelLevel":8.5,"FuelUsed":1.2,"JumpDist":20,"Factions":[{"Name":"Faction","MyReputation":90,"HomeSystem":"Elsewhere","Government_Localised":"Democracy"}]}
                """)],
            status: null,
            enabled: true,
            environment: "live",
            allowPublishing: true);

        Assert.Single(result.Published);
        using var json = JsonDocument.Parse(Assert.Single(requests).Content);
        var root = json.RootElement;
        Assert.Equal(
            "https://eddn.edcd.io/schemas/journal/1",
            root.GetProperty("$schemaRef").GetString());
        var message = root.GetProperty("message");
        Assert.False(message.TryGetProperty("Wanted", out _));
        Assert.False(message.TryGetProperty("FuelLevel", out _));
        Assert.False(message.TryGetProperty("FuelUsed", out _));
        Assert.False(message.TryGetProperty("JumpDist", out _));
        var faction = message.GetProperty("Factions")[0];
        Assert.False(faction.TryGetProperty("MyReputation", out _));
        Assert.False(faction.TryGetProperty("HomeSystem", out _));
        Assert.False(faction.TryGetProperty("Government_Localised", out _));
    }

    [Fact]
    public async Task CodexBodyIdentityRequiresStatusAndJournalAgreement()
    {
        var requests = new List<RecordedRequest>();
        var publisher = CreatePublisher(requests);
        await BootstrapAsync(
            publisher,
            Event("""
                {"timestamp":"2026-07-25T12:00:03Z","event":"ApproachBody","BodyName":"Test A 1","BodyID":4}
                """));

        await publisher.ApplyAsync(
            [CodexEvent("2026-07-25T12:03:00Z")],
            new EliteStatus { BodyName = "Test A 1" },
            enabled: true,
            environment: "dev",
            allowPublishing: true);
        await publisher.ApplyAsync(
            [CodexEvent("2026-07-25T12:04:00Z")],
            new EliteStatus { BodyName = "Test A 2" },
            enabled: true,
            environment: "dev",
            allowPublishing: true);
        await publisher.ApplyAsync(
            [CodexEvent("2026-07-25T12:05:00Z")],
            new EliteStatus(),
            enabled: true,
            environment: "dev",
            allowPublishing: true);

        Assert.Equal(3, requests.Count);
        using var matching = JsonDocument.Parse(requests[0].Content);
        var matchingMessage = matching.RootElement.GetProperty("message");
        Assert.Equal("Test A 1", matchingMessage.GetProperty("BodyName").GetString());
        Assert.Equal(4, matchingMessage.GetProperty("BodyID").GetInt32());
        Assert.False(matchingMessage.TryGetProperty("IsNewEntry", out _));
        Assert.False(matchingMessage.TryGetProperty("NewTraitsDiscovered", out _));

        using var mismatched = JsonDocument.Parse(requests[1].Content);
        var mismatchedMessage = mismatched.RootElement.GetProperty("message");
        Assert.Equal("Test A 2", mismatchedMessage.GetProperty("BodyName").GetString());
        Assert.False(mismatchedMessage.TryGetProperty("BodyID", out _));

        using var absent = JsonDocument.Parse(requests[2].Content);
        var absentMessage = absent.RootElement.GetProperty("message");
        Assert.False(absentMessage.TryGetProperty("BodyName", out _));
        Assert.False(absentMessage.TryGetProperty("BodyID", out _));
    }

    [Fact]
    public async Task MismatchedSystemIsSkippedWithoutNetworkRequest()
    {
        var requests = new List<RecordedRequest>();
        var publisher = CreatePublisher(requests);
        await BootstrapAsync(publisher);

        var result = await publisher.ApplyAsync(
            [Event("""
                {"timestamp":"2026-07-25T12:06:00Z","event":"FSSBodySignals","SystemAddress":999,"BodyName":"Elsewhere 1","BodyID":2,"Signals":[]}
                """)],
            status: null,
            enabled: true,
            environment: "live",
            allowPublishing: true);

        Assert.Empty(requests);
        Assert.Empty(result.Published);
        Assert.Contains("did not match", Assert.Single(result.Warnings));
    }

    [Fact]
    public async Task MissingSchemaFieldIsSkippedWithoutNetworkRequest()
    {
        var requests = new List<RecordedRequest>();
        var publisher = CreatePublisher(requests);
        await BootstrapAsync(publisher);

        var result = await publisher.ApplyAsync(
            [Event("""
                {"timestamp":"2026-07-25T12:06:30Z","event":"DockingDenied","MarketID":1,"StationName":"Port"}
                """)],
            status: null,
            enabled: true,
            environment: "live",
            allowPublishing: true);

        Assert.Empty(requests);
        Assert.Empty(result.Published);
        Assert.Contains("Reason", Assert.Single(result.Warnings));
    }

    [Fact]
    public async Task FailedMessageIsBoundedAndDoesNotBlockNextMessage()
    {
        var calls = 0;
        var requests = new List<RecordedRequest>();
        var handler = new StubHandler(async request =>
        {
            requests.Add(await RecordAsync(request));
            calls++;
            return calls == 1
                ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(new string('x', 10_000)),
                }
                : new HttpResponseMessage(HttpStatusCode.OK);
        });
        var publisher = CreatePublisher(requests, handler, recordInHandler: false);
        await BootstrapAsync(publisher);

        var result = await publisher.ApplyAsync(
            [
                Event("""
                    {"timestamp":"2026-07-25T12:07:00Z","event":"DockingGranted","MarketID":1,"StationName":"Port","LandingPad":2}
                    """),
                Event("""
                    {"timestamp":"2026-07-25T12:07:01Z","event":"DockingDenied","MarketID":1,"StationName":"Port","Reason":"NoSpace"}
                    """),
            ],
            status: null,
            enabled: true,
            environment: "dev",
            allowPublishing: true);

        Assert.Equal(2, calls);
        Assert.Equal(2, requests.Count);
        Assert.Single(result.Published);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("HTTP 400", warning);
        Assert.True(warning.Length < 2_200);
    }

    [Theory]
    [InlineData(null, "dev")]
    [InlineData("unknown", "dev")]
    [InlineData(" BETA ", "beta")]
    [InlineData("LIVE", "live")]
    public void EnvironmentIsNormalized(string? value, string expected)
    {
        Assert.Equal(expected, EddnPublisher.NormalizeEnvironment(value));
    }

    private static async Task BootstrapAsync(
        IEddnPublisher publisher,
        params JournalEventEnvelope[] additionalEvents)
    {
        await publisher.ApplyAsync(
            [
                Event("""
                    {"timestamp":"2026-07-25T12:00:00Z","event":"Fileheader","gameversion":"4.1.2.3","build":"r123/r0 "}
                    """),
                Event("""
                    {"timestamp":"2026-07-25T12:00:01Z","event":"LoadGame","Commander":"Test Cmdr","Horizons":true,"Odyssey":true}
                    """),
                Event("""
                    {"timestamp":"2026-07-25T12:00:02Z","event":"Location","StarSystem":"Test A","SystemAddress":123,"StarPos":[1.5,-2,3]}
                    """),
                .. additionalEvents,
            ],
            status: null,
            enabled: true,
            environment: "dev",
            allowPublishing: false);
    }

    private static JournalEventEnvelope CodexEvent(string timestamp)
    {
        return Event(
            $$"""
              {"timestamp":"{{timestamp}}","event":"CodexEntry","System":"Test A","SystemAddress":123,"EntryID":10,"Name":"$Codex_Ent_Bacterial_01_Name;","Region":"$Codex_RegionName_18;","Category":"$Codex_Category_Biology;","BodyID":4,"IsNewEntry":true,"NewTraitsDiscovered":true}
              """);
    }

    private static JournalEventEnvelope Event(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var result, out var error),
            error);
        return result!;
    }

    private static EddnPublisher CreatePublisher(
        List<RecordedRequest> requests,
        HttpMessageHandler? handler = null,
        bool recordInHandler = true)
    {
        handler ??= new StubHandler(async request =>
        {
            if (recordInHandler)
            {
                requests.Add(await RecordAsync(request));
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        return new EddnPublisher(
            "2.0.95",
            new HttpClient(handler),
            new Dictionary<string, Uri>(StringComparer.Ordinal)
            {
                ["dev"] = new("https://dev.example.test/upload/"),
                ["beta"] = new("https://beta.example.test/upload/"),
                ["live"] = new("https://live.example.test/upload/"),
            });
    }

    private static async Task<RecordedRequest> RecordAsync(
        HttpRequestMessage request)
    {
        return new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Version,
            request.Content?.Headers.ContentType?.ToString() ?? string.Empty,
            await request.Content!.ReadAsStringAsync());
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        Version Version,
        string ContentType,
        string Content);

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return response(request);
        }
    }
}
