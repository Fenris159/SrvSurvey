using System.Net;
using System.IO.Compression;
using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Tests.Network;

public sealed class EddnPublisherTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("{\"event\":\"LoadGame\",\"Commander\":\"Test Cmdr\"}", null)]
    [InlineData("{\"event\":\"LoadGame\",\"Commander\":\"Test Cmdr\",\"Odyssey\":false,\"Horizons\":false}", false)]
    public async Task ExpansionFlagsComeFromLatestLoadGameRatherThanFileheader(
        string? loadGame,
        bool? expectedExpansion)
    {
        var requests = new List<RecordedRequest>();
        using var publisher = CreatePublisher(requests);
        var subsequentEvents = new List<JournalEventEnvelope>
        {
            Event("""{"event":"Fileheader","gameversion":"4.1.2.3","build":"r123/r0 ","Odyssey":true,"Horizons":true}"""),
        };
        if (loadGame is not null)
        {
            subsequentEvents.Add(Event(loadGame));
        }

        await BootstrapAsync(publisher, subsequentEvents.ToArray());
        await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [CodexEvent("2026-07-25T12:01:00Z")],
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = true,
        });
        await publisher.ProcessPendingAsync();

        using var json = JsonDocument.Parse(Assert.Single(requests).Content);
        var message = json.RootElement.GetProperty("message");
        foreach (var name in new[] { "odyssey", "horizons" })
        {
            Assert.Equal(expectedExpansion.HasValue, message.TryGetProperty(name, out var value));
            if (expectedExpansion.HasValue)
            {
                Assert.Equal(expectedExpansion.Value, value.GetBoolean());
            }
        }
    }

    [Fact]
    public async Task BootstrapBuildsContextAndLiveSchemasPublishThroughLiveGateway()
    {
        var requests = new List<RecordedRequest>();
        var publisher = CreatePublisher(requests);

        var bootstrap = await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [
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
            Status = null,
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = false
        });
        var live = await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [Event("""
                {"timestamp":"2026-07-25T12:01:00Z","event":"FSSBodySignals","SystemAddress":123,"BodyName":"Test A 1","BodyID":4,"Signals":[{"Type":"$SAA_SignalType_Biological;","Type_Localised":"Biological","Count":2}]}
                """)],
            Status = null,
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = true
        });
        await publisher.ProcessPendingAsync();

        Assert.Empty(bootstrap.Published);
        Assert.Empty(bootstrap.Warnings);
        var published = Assert.Single(live.Published);
        Assert.Equal("FSSBodySignals", published.EventName);
        Assert.Equal(
            "https://eddn.edcd.io/schemas/fssbodysignals/1",
            published.SchemaReference);
        Assert.False(published.UsesTestSchemas);
        var request = Assert.Single(requests);
        Assert.Equal("https://live.example.test/upload/", request.Uri.ToString());
        Assert.Equal(HttpVersion.Version11, request.Version);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.StartsWith("application/json", request.ContentType);
        Assert.Equal("gzip", request.ContentEncoding);
        Assert.Null(request.Authorization);
        using var json = JsonDocument.Parse(request.Content);
        var root = json.RootElement;
        Assert.Equal(
            published.SchemaReference,
            root.GetProperty("$schemaRef").GetString());
        var header = root.GetProperty("header");
        Assert.Equal("Test Cmdr", header.GetProperty("uploaderID").GetString());
        Assert.Equal("4.1.2.3", header.GetProperty("gameversion").GetString());
        Assert.Equal("r123/r0 ", header.GetProperty("gamebuild").GetString());
        Assert.Equal(
            "SrvSurvey-XP",
            header.GetProperty("softwareName").GetString());
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
    public async Task JournalMessageStripsCommanderSpecificFieldsRecursively()
    {
        var requests = new List<RecordedRequest>();
        var publisher = CreatePublisher(requests);
        await BootstrapAsync(publisher);

        var result = await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [Event("""
                {"timestamp":"2026-07-25T12:02:00Z","event":"FSDJump","StarSystem":"Test B","SystemAddress":456,"StarPos":[4,5,6],"Wanted":true,"FuelLevel":8.5,"FuelUsed":1.2,"JumpDist":20,"Factions":[{"Name":"Faction","MyReputation":90,"HomeSystem":"Elsewhere","Government_Localised":"Democracy"}]}
                """)],
            Status = null,
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = true
        });
        await publisher.ProcessPendingAsync();

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
    public async Task JumpFlushesSignalBatchAgainstSourceSystem()
    {
        var requests = new List<RecordedRequest>();
        using var publisher = CreatePublisher(requests);
        await BootstrapAsync(publisher);

        var result = await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [
                Event("""
                    {"timestamp":"2026-07-25T12:01:00Z","event":"FSSSignalDiscovered","SystemAddress":123,"SignalName":"High Grade Emissions","SignalType":"USS","USSType":"$USS_Type_VeryValuableSalvage;","ThreatLevel":0}
                    """),
                Event("""
                    {"timestamp":"2026-07-25T12:02:00Z","event":"FSDJump","StarSystem":"Test B","SystemAddress":456,"StarPos":[4,5,6]}
                    """),
            ],
            Status = null,
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = true
        });
        await publisher.ProcessPendingAsync();

        Assert.Equal(2, result.Published.Count);
        Assert.Equal(2, requests.Count);
        using var signalPayload = JsonDocument.Parse(requests[0].Content);
        var signalMessage = signalPayload.RootElement.GetProperty("message");
        Assert.Equal(
            "FSSSignalDiscovered",
            signalMessage.GetProperty("event").GetString());
        Assert.Equal(123, signalMessage.GetProperty("SystemAddress").GetInt64());
        Assert.Equal("Test A", signalMessage.GetProperty("StarSystem").GetString());
        Assert.Single(signalMessage.GetProperty("signals").EnumerateArray());
        using var jumpPayload = JsonDocument.Parse(requests[1].Content);
        Assert.Equal(
            "Test B",
            jumpPayload.RootElement.GetProperty("message")
                .GetProperty("StarSystem")
                .GetString());
    }

    [Fact]
    public async Task CodexBodyIdentityOnlyUsesContextWhenStatusAndJournalAgree()
    {
        var requests = new List<RecordedRequest>();
        var publisher = CreatePublisher(requests);
        await BootstrapAsync(
            publisher,
            Event("""
                {"timestamp":"2026-07-25T12:00:03Z","event":"ApproachBody","BodyName":"Test A 1","BodyID":4}
                """));

        await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [CodexEvent("2026-07-25T12:03:00Z")],
            Status = new EliteStatus { BodyName = "Test A 1" },
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = true
        });
        await publisher.ProcessPendingAsync();
        await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [CodexEvent("2026-07-25T12:04:00Z")],
            Status = new EliteStatus { BodyName = "Test A 2" },
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = true
        });
        await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [CodexEvent("2026-07-25T12:05:00Z")],
            Status = new EliteStatus(),
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = true
        });
        await publisher.ProcessPendingAsync();

        Assert.Equal(3, requests.Count);
        using var matching = JsonDocument.Parse(requests[0].Content);
        var matchingMessage = matching.RootElement.GetProperty("message");
        Assert.Equal("Test A 1", matchingMessage.GetProperty("BodyName").GetString());
        Assert.Equal(4, matchingMessage.GetProperty("BodyID").GetInt32());
        Assert.False(matchingMessage.TryGetProperty("IsNewEntry", out _));
        Assert.False(matchingMessage.TryGetProperty("NewTraitsDiscovered", out _));

        using var mismatched = JsonDocument.Parse(requests[1].Content);
        var mismatchedMessage = mismatched.RootElement.GetProperty("message");
        Assert.False(mismatchedMessage.TryGetProperty("BodyName", out _));
        Assert.Equal(4, mismatchedMessage.GetProperty("BodyID").GetInt32());

        using var absent = JsonDocument.Parse(requests[2].Content);
        var absentMessage = absent.RootElement.GetProperty("message");
        Assert.False(absentMessage.TryGetProperty("BodyName", out _));
        Assert.Equal(4, absentMessage.GetProperty("BodyID").GetInt32());
    }

    [Fact]
    public async Task MismatchedSystemIsSkippedWithoutNetworkRequest()
    {
        var requests = new List<RecordedRequest>();
        var publisher = CreatePublisher(requests);
        await BootstrapAsync(publisher);

        var result = await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [Event("""
                {"timestamp":"2026-07-25T12:06:00Z","event":"FSSBodySignals","SystemAddress":999,"BodyName":"Elsewhere 1","BodyID":2,"Signals":[]}
                """)],
            Status = null,
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = true
        });

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

        var result = await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [Event("""
                {"timestamp":"2026-07-25T12:06:30Z","event":"DockingDenied","MarketID":1,"StationName":"Port"}
                """)],
            Status = null,
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = true
        });

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

        var result = await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [
                Event("""
                    {"timestamp":"2026-07-25T12:07:00Z","event":"DockingGranted","MarketID":1,"StationName":"Port","LandingPad":2}
                    """),
                Event("""
                    {"timestamp":"2026-07-25T12:07:01Z","event":"DockingDenied","MarketID":1,"StationName":"Port","Reason":"NoSpace"}
                    """),
            ],
            Status = null,
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = true
        });
        await publisher.ProcessPendingAsync();

        Assert.Equal(2, calls);
        Assert.Equal(2, requests.Count);
        Assert.Equal(2, result.Published.Count);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task MatchingMarketCompanionFileIsQueuedWithoutBlockingJournalApply()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-EddnCompanion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Market.json"),
                """
                {"timestamp":"2026-07-25T12:01:01Z","event":"Market","MarketID":42,"StationName":"Test Port","StationType":"Orbis","StarSystem":"Test A","Items":[]}
                """);
            var requests = new List<RecordedRequest>();
            using var publisher = CreatePublisher(requests);
            await publisher.ApplyAsync(new EddnApplyRequest
            {
                JournalEvents = [
                    Event("""{"timestamp":"2026-07-25T12:00:00Z","event":"Fileheader","gameversion":"4.1.2.3","build":"r123/r0","Odyssey":true}"""),
                    Event("""{"timestamp":"2026-07-25T12:00:01Z","event":"LoadGame","Commander":"Test Cmdr","Horizons":true,"Odyssey":true}"""),
                    Event("""{"timestamp":"2026-07-25T12:00:02Z","event":"Location","StarSystem":"Test A","SystemAddress":123,"StarPos":[1.5,-2,3]}"""),
                ],
                Status = null,
                Enabled = true,
                CommanderName = "Test Cmdr",
                FrontierId = "F123",
                GameVersion = "4.1.2.3",
                GameBuild = "r123/r0 ",
                AllowPublishing = false,
                JournalDirectory = directory
            });

            var result = await publisher.ApplyAsync(new EddnApplyRequest
            {
                JournalEvents = [Event("""{"timestamp":"2026-07-25T12:01:00Z","event":"Market","MarketID":42}""")],
                Status = null,
                Enabled = true,
                CommanderName = "Test Cmdr",
                FrontierId = "F123",
                GameVersion = "4.1.2.3",
                GameBuild = "r123/r0 ",
                AllowPublishing = true,
                JournalDirectory = directory
            });

            Assert.Empty(result.Published);
            await publisher.WaitForCompanionReadsAsync();
            Assert.Equal(1, publisher.PendingCount);
            await publisher.ProcessPendingAsync();

            using var payload = JsonDocument.Parse(Assert.Single(requests).Content);
            Assert.Equal(
                "https://eddn.edcd.io/schemas/commodity/3",
                payload.RootElement.GetProperty("$schemaRef").GetString());
            var message = payload.RootElement.GetProperty("message");
            Assert.Equal("Test A", message.GetProperty("systemName").GetString());
            Assert.Equal(42, message.GetProperty("marketId").GetInt64());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MulticrewAndSharedCompanionInputsAreSuppressedIndependently()
    {
        var requests = new List<RecordedRequest>();
        using var publisher = CreatePublisher(requests);
        await BootstrapAsync(publisher);

        var crew = await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [
                Event("""{"timestamp":"2026-07-25T12:01:00Z","event":"JoinACrew"}"""),
                Event("""{"timestamp":"2026-07-25T12:01:01Z","event":"DockingGranted","MarketID":1,"StationName":"Port","LandingPad":2}"""),
            ],
            Status = null,
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = true
        });
        var shared = await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [
                Event("""{"timestamp":"2026-07-25T12:01:59Z","event":"QuitACrew"}"""),
                Event("""{"timestamp":"2026-07-25T12:02:00Z","event":"Market","MarketID":1}"""),
            ],
            Status = null,
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = true,
            JournalDirectory = Path.GetTempPath(),
            AllowSharedData = false
        });

        Assert.Empty(crew.Published);
        Assert.Empty(shared.Published);
        Assert.Contains(
            shared.Warnings,
            warning => warning.Contains(
                "shared companion files are suppressed",
                StringComparison.Ordinal));
        Assert.Equal(0, publisher.PendingCount);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task OptOutDiscardsQueuedMessagesBeforeNetworkDelivery()
    {
        var requests = new List<RecordedRequest>();
        using var publisher = CreatePublisher(requests);
        await BootstrapAsync(publisher);
        var queued = await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [Event("""{"timestamp":"2026-07-25T12:01:00Z","event":"DockingGranted","MarketID":1,"StationName":"Port","LandingPad":2}""")],
            Status = null,
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = true
        });

        Assert.Single(queued.Published);
        Assert.Equal(1, publisher.PendingCount);
        publisher.SetEnabled(false);
        await publisher.ProcessPendingAsync();

        Assert.Equal(0, publisher.PendingCount);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task OperationalSuspensionPreservesQueueAndBlocksNewMessages()
    {
        var requests = new List<RecordedRequest>();
        using var publisher = CreatePublisher(requests);
        await BootstrapAsync(publisher);
        var queued = await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [Event("""{"timestamp":"2026-07-25T12:01:00Z","event":"DockingGranted","MarketID":1,"StationName":"First Port","LandingPad":2}""")],
            Status = null,
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = true
        });
        Assert.Single(queued.Published);

        publisher.SetSuspended(true);
        var blocked = await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [Event("""{"timestamp":"2026-07-25T12:02:00Z","event":"DockingGranted","MarketID":2,"StationName":"Second Port","LandingPad":3}""")],
            Status = null,
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = true
        });
        await publisher.ProcessPendingAsync();

        Assert.Empty(blocked.Published);
        Assert.Contains(
            blocked.Warnings,
            warning => warning.Contains(
                "multiple Elite windows",
                StringComparison.Ordinal));
        Assert.Equal(1, publisher.PendingCount);
        Assert.Empty(requests);

        publisher.SetSuspended(false);
        await publisher.ProcessPendingAsync();

        Assert.Equal(0, publisher.PendingCount);
        Assert.Single(requests);
    }

    [Fact]
    public async Task QueuedMessagesRetainTheirCapturedCommanderAcrossSessionSwitch()
    {
        var requests = new List<RecordedRequest>();
        using var publisher = CreatePublisher(requests);
        await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents =
            [
                Event("""{"timestamp":"2026-07-25T12:00:00Z","event":"LoadGame","Commander":"First Cmdr","FID":"F1","Odyssey":true}"""),
                Event("""{"timestamp":"2026-07-25T12:00:01Z","event":"DockingGranted","MarketID":1,"StationName":"First Port","LandingPad":2}"""),
            ],
            Enabled = true,
            AllowPublishing = true,
            CommanderName = "First Cmdr",
            FrontierId = "F1",
            GameVersion = "4.1",
            GameBuild = "r1",
            JournalPath = Path.Combine(
                Path.GetTempPath(),
                "Journal.2026-07-25T120000.01.log"),
        });
        await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents =
            [
                Event("""{"timestamp":"2026-07-25T12:01:00Z","event":"LoadGame","Commander":"Second Cmdr","FID":"F2","Odyssey":true}"""),
                Event("""{"timestamp":"2026-07-25T12:01:01Z","event":"DockingGranted","MarketID":2,"StationName":"Second Port","LandingPad":3}"""),
            ],
            Enabled = true,
            AllowPublishing = true,
            CommanderName = "Second Cmdr",
            FrontierId = "F2",
            GameVersion = "4.1",
            GameBuild = "r2",
            JournalPath = Path.Combine(
                Path.GetTempPath(),
                "Journal.2026-07-25T120100.01.log"),
        });

        await publisher.ProcessPendingAsync();

        Assert.Equal(2, requests.Count);
        Assert.Equal(
            ["First Cmdr", "Second Cmdr"],
            requests.Select(request => JsonDocument.Parse(request.Content))
                .Select(payload => payload.RootElement
                    .GetProperty("header")
                    .GetProperty("uploaderID")
                    .GetString())
                .OfType<string>()
                .ToArray());
    }

    [Fact]
    public async Task ContinuedJournalPartPreservesCommanderIdentityAndContext()
    {
        var requests = new List<RecordedRequest>();
        using var publisher = CreatePublisher(requests);
        var firstPath = Path.Combine(
            Path.GetTempPath(),
            "Journal.2026-07-25T120000.01.log");
        var secondPath = Path.Combine(
            Path.GetTempPath(),
            "Journal.2026-07-25T120000.02.log");
        await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [
                Event("""{"timestamp":"2026-07-25T12:00:00Z","event":"Fileheader","part":1,"gameversion":"4.1","build":"r1","Odyssey":true}"""),
                Event("""{"timestamp":"2026-07-25T12:00:01Z","event":"LoadGame","Commander":"Test Cmdr","Odyssey":true}"""),
                Event("""{"timestamp":"2026-07-25T12:00:02Z","event":"Location","StarSystem":"Test A","SystemAddress":123,"StarPos":[1,2,3]}"""),
            ],
            Status = null,
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = false,
            JournalPath = firstPath
        });
        await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [
                Event("""{"timestamp":"2026-07-25T12:10:00Z","event":"Fileheader","part":2,"gameversion":"4.1","build":"r2","Odyssey":true}"""),
                Event("""{"timestamp":"2026-07-25T12:10:01Z","event":"Continued","Part":1}"""),
            ],
            Status = null,
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = false,
            JournalPath = secondPath
        });

        var result = await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [Event("""{"timestamp":"2026-07-25T12:11:00Z","event":"FSSBodySignals","SystemAddress":123,"BodyName":"Test A 1","BodyID":4,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}]}""")],
            Status = null,
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = true,
            JournalPath = secondPath
        });
        await publisher.ProcessPendingAsync();

        Assert.Single(result.Published);
        using var payload = JsonDocument.Parse(Assert.Single(requests).Content);
        var header = payload.RootElement.GetProperty("header");
        Assert.Equal("Test Cmdr", header.GetProperty("uploaderID").GetString());
        Assert.Equal(
            "r123/r0 ",
            header.GetProperty("gamebuild").GetString());
        Assert.Equal(
            "Test A",
            payload.RootElement.GetProperty("message")
                .GetProperty("StarSystem")
                .GetString());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task UnrelatedJournalFileRequiresFreshCommanderIdentity(
        int part)
    {
        var requests = new List<RecordedRequest>();
        using var publisher = CreatePublisher(requests);
        await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents =
            [
                Event("""{"timestamp":"2026-07-25T12:00:00Z","event":"Fileheader","part":1,"gameversion":"4.1","build":"r1"}"""),
                Event("""{"timestamp":"2026-07-25T12:00:01Z","event":"LoadGame","Commander":"Test Cmdr","Odyssey":true}"""),
            ],
            Status = null,
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r1",
            AllowPublishing = false,
            JournalPath = Path.Combine(
                Path.GetTempPath(),
                "Journal.2026-07-25T120000.01.log"),
        });
        await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents =
            [
                Event($$"""{"timestamp":"2026-07-25T12:10:00Z","event":"Fileheader","part":{{part}},"gameversion":"4.1","build":"r2"}"""),
            ],
            Status = null,
            Enabled = true,
            AllowPublishing = false,
            JournalPath = Path.Combine(
                Path.GetTempPath(),
                "Journal.2026-07-25T121000.01.log"),
        });

        var result = await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [Event("""{"timestamp":"2026-07-25T12:11:00Z","event":"DockingGranted","MarketID":1,"StationName":"Port","LandingPad":2}""")],
            Status = null,
            Enabled = true,
            AllowPublishing = true
        });
        await publisher.ProcessPendingAsync();

        Assert.Empty(result.Published);
        Assert.Equal(0, publisher.PendingCount);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task CompanionReadFromReplacedSessionCannotEnterOutbox()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-EddnGeneration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var requests = new List<RecordedRequest>();
            using var publisher = CreatePublisher(requests);
            await publisher.ApplyAsync(new EddnApplyRequest
            {
                JournalEvents = [
                    Event("""{"timestamp":"2026-07-25T12:00:00Z","event":"Fileheader","gameversion":"4.1","build":"r1"}"""),
                    Event("""{"timestamp":"2026-07-25T12:00:01Z","event":"LoadGame","Commander":"First Cmdr","Odyssey":true}"""),
                    Event("""{"timestamp":"2026-07-25T12:00:02Z","event":"Location","StarSystem":"Test A","SystemAddress":123,"StarPos":[1,2,3]}"""),
                ],
                Status = null,
                Enabled = true,
                CommanderName = "First Cmdr",
                FrontierId = "F123",
                GameVersion = "4.1.2.3",
                GameBuild = "r1",
                AllowPublishing = false,
                JournalDirectory = directory
            });
            await publisher.ApplyAsync(new EddnApplyRequest
            {
                JournalEvents = [Event("""{"timestamp":"2026-07-25T12:01:00Z","event":"Market","MarketID":42}""")],
                Status = null,
                Enabled = true,
                CommanderName = "First Cmdr",
                FrontierId = "F123",
                GameVersion = "4.1.2.3",
                GameBuild = "r1",
                AllowPublishing = true,
                JournalDirectory = directory
            });

            await publisher.ApplyAsync(new EddnApplyRequest
            {
                JournalEvents = [Event("""{"timestamp":"2026-07-25T12:02:00Z","event":"Fileheader","gameversion":"4.1","build":"r2"}""")],
                Status = null,
                Enabled = true,
                CommanderName = "Second Cmdr",
                FrontierId = "F234",
                GameVersion = "4.1.2.3",
                GameBuild = "r2",
                AllowPublishing = false,
                JournalDirectory = directory
            });
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Market.json"),
                """
                {"timestamp":"2026-07-25T12:01:01Z","event":"Market","MarketID":42,"StationName":"Old Port","StationType":"Orbis","StarSystem":"Test A","Items":[]}
                """);

            await publisher.WaitForCompanionReadsAsync();

            Assert.Equal(0, publisher.PendingCount);
            Assert.Empty(requests);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DisposeCompletesWhenPublisherWasCreatedOnUiSynchronizationContext()
    {
        using var completed = new ManualResetEventSlim();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new NonPumpingSynchronizationContext());
            try
            {
                using var publisher = CreatePublisher([]);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "EDDN UI-context disposal test",
        };

        thread.Start();

        Assert.True(
            completed.Wait(TimeSpan.FromSeconds(5)),
            "EDDN disposal deadlocked while waiting for its UI-context-bound writer.");
        Assert.Null(failure);
        Assert.True(thread.Join(TimeSpan.FromSeconds(1)));
    }

    private static async Task BootstrapAsync(
        EddnPublisher publisher,
        params JournalEventEnvelope[] additionalEvents)
    {
        await publisher.ApplyAsync(new EddnApplyRequest
        {
            JournalEvents = [
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
            Status = null,
            Enabled = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1.2.3",
            GameBuild = "r123/r0 ",
            AllowPublishing = false
        });
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
            new Uri("https://live.example.test/upload/"),
            Path.Combine(
                Path.GetTempPath(),
                "SrvSurvey-EddnPublisherTests-"
                    + Guid.NewGuid().ToString("N")
                    + ".json"),
            automaticProcessing: false);
    }

    private static async Task<RecordedRequest> RecordAsync(
        HttpRequestMessage request)
    {
        var requestContent = request.Content!;
        var bytes = await requestContent.ReadAsByteArrayAsync();
        string content;
        if (requestContent.Headers.ContentEncoding.Contains("gzip"))
        {
            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            content = await reader.ReadToEndAsync();
        }
        else
        {
            content = System.Text.Encoding.UTF8.GetString(bytes);
        }

        return new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Version,
            requestContent.Headers.ContentType?.ToString() ?? string.Empty,
            string.Join(',', requestContent.Headers.ContentEncoding),
            request.Headers.Authorization?.ToString(),
            content);
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        Version Version,
        string ContentType,
        string ContentEncoding,
        string? Authorization,
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

    private sealed class NonPumpingSynchronizationContext
        : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
        }
    }
}
