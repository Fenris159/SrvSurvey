using System.Net;
using System.Text.Json.Nodes;
using SrvSurvey.Core.EdAstro;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.EdAstro;

/// <summary>Exercises real journal ingestion and EDAstro publication across process-lifetime boundaries.</summary>
[Trait("Category", "JournalUploadScenario")]
public sealed class EdAstroJournalUploadScenarioTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-edastro-upload-" + Guid.NewGuid().ToString("N")
    );
    private DateTimeOffset now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Recovers a failed live report after reopening without substituting the new session's event context.</summary>
    [Fact]
    public async Task FailedLiveJournalReportSurvivesRestartWithItsOriginalPayload()
    {
        Directory.CreateDirectory(directory);
        string journal = Path.Combine(directory, "Journal.2026-10-12T120000.01.log");
        await File.WriteAllTextAsync(
            journal,
            "{\"timestamp\":\"2026-10-12T12:00:00Z\",\"event\":\"LoadGame\",\"Commander\":\"Explorer\",\"FID\":\"F123\",\"Odyssey\":true}\n"
        );
        var monitor = new JournalDirectoryMonitor(directory, "F123");
        await monitor.PollAsync();
        await File.AppendAllTextAsync(
            journal,
            "{\"timestamp\":\"2026-10-12T12:00:01Z\",\"event\":\"Docked\",\"StarSystem\":\"Sol\",\"SystemAddress\":10477373803,\"StationName\":\"Galileo\",\"MarketID\":128666762}\n"
        );
        JournalMonitorUpdate live = await monitor.PollAsync();
        var failing = new ProviderHandler(HttpStatusCode.ServiceUnavailable);
        using (var http = new HttpClient(failing))
        using (EdAstroPublisher publisher = Create(http))
        {
            publisher.SetEnabled(true);
            publisher.Apply(Request(live));
            await publisher.FlushAsync();
            now += TimeSpan.FromSeconds(10);
            await publisher.FlushAsync();
            Assert.Single(failing.Posts);
        }

        now += TimeSpan.FromHours(1);
        var recovered = new ProviderHandler(HttpStatusCode.OK);
        using var recoveredHttp = new HttpClient(recovered);
        using (EdAstroPublisher publisher = Create(recoveredHttp))
        {
            publisher.SetEnabled(true);
            publisher.Apply(Request(await new JournalDirectoryMonitor(directory, "F123").PollAsync()));
            await publisher.FlushAsync();
            now += TimeSpan.FromSeconds(10);
            await publisher.FlushAsync();
            JsonArray body = JsonNode.Parse(Assert.Single(recovered.Posts))!.AsArray();
            Assert.Equal("Docked", body[1]!["event"]!.GetValue<string>());
            Assert.Equal("2026-10-12T12:00:01Z", body[1]!["timestamp"]!.GetValue<string>());
            Assert.Equal("Sol", body[1]!["StarSystem"]!.GetValue<string>());
            Assert.Equal("Galileo", body[1]!["StationName"]!.GetValue<string>());
        }

        var acknowledged = new ProviderHandler(HttpStatusCode.OK);
        using var acknowledgedHttp = new HttpClient(acknowledged);
        using EdAstroPublisher finalPublisher = Create(acknowledgedHttp);
        finalPublisher.SetEnabled(true);
        finalPublisher.Apply(Request(await new JournalDirectoryMonitor(directory, "F123").PollAsync()));
        now += TimeSpan.FromHours(1);
        await finalPublisher.FlushAsync();
        Assert.Empty(acknowledged.Posts);
    }

    /// <summary>Keeps distinct identical journal records while suppressing repeated reads of the same source positions.</summary>
    [Fact]
    public async Task IdenticalJournalLinesAreDistinctButRereadingTheirSourceIsNot()
    {
        Directory.CreateDirectory(directory);
        string journal = Path.Combine(directory, "Journal.2026-10-12T120000.01.log");
        await File.WriteAllTextAsync(
            journal,
            "{\"timestamp\":\"2026-10-12T12:00:00Z\",\"event\":\"LoadGame\",\"Commander\":\"Explorer\",\"FID\":\"F123\",\"Odyssey\":true}\n"
        );
        var monitor = new JournalDirectoryMonitor(directory, "F123");
        await monitor.PollAsync();
        const string docked =
            "{\"timestamp\":\"2026-10-12T12:00:01Z\",\"event\":\"Docked\",\"StarSystem\":\"Sol\",\"StationName\":\"Galileo\"}\n";
        await File.AppendAllTextAsync(journal, docked + docked);
        JournalMonitorUpdate live = await monitor.PollAsync();
        var handler = new ProviderHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        using EdAstroPublisher publisher = Create(http);
        publisher.SetEnabled(true);
        publisher.Apply(Request(live));
        publisher.Apply(Request(live));
        await publisher.FlushAsync();
        now += TimeSpan.FromSeconds(10);
        await publisher.FlushAsync();
        JsonArray body = JsonNode.Parse(Assert.Single(handler.Posts))!.AsArray();
        Assert.Equal(3, body.Count);
        Assert.Equal("Docked", body[1]!["event"]!.GetValue<string>());
        Assert.Equal("Docked", body[2]!["event"]!.GetValue<string>());
    }

    /// <summary>Uses the real publisher with isolated durable storage and a controlled provider clock.</summary>
    private EdAstroPublisher Create(HttpClient http) =>
        new(
            "2.1.3.0",
            http,
            utcNow: () => now,
            automaticProcessing: false,
            trafficBudget: new EdAstroTrafficBudget(),
            outboxDirectory: Path.Combine(directory, "uploads")
        );

    /// <summary>Forwards only the monitor's live evidence while allowing persisted uploads to recover during bootstrap.</summary>
    private static EdAstroApplyRequest Request(JournalMonitorUpdate update) =>
        new(update.JournalEvents, "Explorer", update.JournalPath, true, !update.IsBootstrapRead, true);

    /// <summary>Removes the fixture's isolated journal and upload storage.</summary>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Captures real outgoing payloads and simulates the external provider's policy and acknowledgement.</summary>
    private sealed class ProviderHandler(HttpStatusCode status) : HttpMessageHandler
    {
        /// <summary>Captures actual provider request bodies for independent wire-contract assertions.</summary>
        public List<string> Posts { get; } = [];

        /// <summary>Returns the advertised event list or records the journal request before replying.</summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.Method == HttpMethod.Get)
            {
                return new(HttpStatusCode.OK) { Content = new StringContent("[\"Docked\"]") };
            }
            Posts.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new(status) { Content = new StringContent("{\"status\":200}") };
        }
    }
}
