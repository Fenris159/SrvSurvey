using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json.Nodes;
using SrvSurvey.Core.EdAstro;
using SrvSurvey.Core.Edsm;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Inara;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Tests.Network;

/// <summary>Runs journal-to-HTTP recovery scenarios against authenticated providers without real external writes.</summary>
[Trait("Category", "JournalUploadScenario")]
public sealed partial class JournalUploadScenarioTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-uploads-" + Guid.NewGuid().ToString("N")
    );
    private readonly ScenarioClock clock = new();
    private JournalUploadPrivacy? uploadPrivacy;
    private static readonly InaraPublicationOptions InaraOptions = new(
        "private-api-key",
        "Explorer",
        "F123",
        "4.1.0.100",
        true
    );
    private static readonly EdsmPublicationOptions EdsmOptions = new(
        "private-api-key",
        "Explorer EDSM",
        "Explorer",
        "F123",
        "4.1.0.100",
        "r300000",
        true
    );

    /// <summary>Retains failed live evidence across reopening, without uploading bootstrap history or persisting credentials.</summary>
    [Theory]
    [InlineData("Inara", false)]
    [InlineData("EDSM", false)]
    [InlineData("EDAstro", false)]
    [InlineData("VoxStellar", false)]
    [InlineData("EDDN", false)]
    [InlineData("Inara", true)]
    [InlineData("EDSM", true)]
    public async Task FailedLiveEvidenceSurvivesRestart(string provider, bool missingCredentials)
    {
        Directory.CreateDirectory(directory);
        string journal = Path.Combine(directory, "Journal.2026-10-12T120000.01.log");
        await File.WriteAllTextAsync(
            journal,
            "{\"timestamp\":\"2026-10-12T12:00:00Z\",\"event\":\"LoadGame\",\"Commander\":\"Explorer\",\"FID\":\"F123\",\"Odyssey\":true}\n"
        );
        var monitor = new JournalDirectoryMonitor(directory, "F123");
        JournalMonitorUpdate bootstrap = await monitor.PollAsync();
        await File.AppendAllTextAsync(
            journal,
            provider == "EDAstro"
                ? "{\"timestamp\":\"2026-10-12T12:00:01Z\",\"event\":\"Docked\",\"StarSystem\":\"Sol\",\"StationName\":\"Galileo\",\"MarketID\":128666762}\n"
                : "{\"timestamp\":\"2026-10-12T12:00:01Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\",\"SystemAddress\":10477373803,\"StarPos\":[0,0,0],\"JumpDist\":12.5}\n"
        );
        JournalMonitorUpdate live = await monitor.PollAsync();
        var failure = new ProviderHandler(provider, HttpStatusCode.ServiceUnavailable);
        using (var http = new HttpClient(failure))
        {
            await Run(provider, http, bootstrap, live, missingCredentials: missingCredentials);
        }
        Assert.NotEmpty(failure.Posts);
        clock.Now += TimeSpan.FromHours(1);
        var recovery = new ProviderHandler(provider, HttpStatusCode.OK);
        using (var http = new HttpClient(recovery))
        {
            await Run(provider, http, await new JournalDirectoryMonitor(directory, "F123").PollAsync(), null);
        }
        Assert.True(
            recovery.Posts.Any(body => body.Contains(ExpectedEvent(provider), StringComparison.Ordinal)),
            string.Join("\n", recovery.Posts)
        );
        Assert.Contains(
            recovery.Posts,
            body =>
                body.Contains("2026-10-12T12:00:01Z", StringComparison.Ordinal)
                && body.Contains("Sol", StringComparison.Ordinal)
        );
        string[] files = Directory.GetFiles(Path.Combine(directory, "uploads"), "*", SearchOption.AllDirectories);
        if (provider != "EDDN")
        {
            Assert.NotEmpty(files);
        }
        foreach (string file in files)
        {
            if (!file.EndsWith(".lease", StringComparison.Ordinal))
            {
                Assert.DoesNotContain("private-api-key", await File.ReadAllTextAsync(file), StringComparison.Ordinal);
            }
        }
        clock.Now += TimeSpan.FromHours(1);
        var acknowledged = new ProviderHandler(provider, HttpStatusCode.OK);
        using var finalHttp = new HttpClient(acknowledged);
        await Run(provider, finalHttp, await new JournalDirectoryMonitor(directory, "F123").PollAsync(), null);
        Assert.DoesNotContain(
            acknowledged.Posts,
            body => body.Contains(ExpectedEvent(provider), StringComparison.Ordinal)
        );
    }

    /// <summary>Names the provider's expected mapping of the captured live record independently of the sender implementation.</summary>
    private static string ExpectedEvent(string provider) =>
        provider switch
        {
            "Inara" => "addCommanderTravelFSDJump",
            "EDAstro" => "Docked",
            _ => "FSDJump",
        };

    /// <summary>An unused broken webhook response body must not replay a report whose successful status was already received.</summary>
    [Fact]
    public async Task SuccessfulWebhookHeadersRetireReportEvenIfUnusedBodyIsBroken()
    {
        Directory.CreateDirectory(directory);
        string journal = Path.Combine(directory, "Journal.2026-10-12T120000.01.log");
        await File.WriteAllTextAsync(
            journal,
            "{\"timestamp\":\"2026-10-12T12:00:00Z\",\"event\":\"LoadGame\",\"Commander\":\"Explorer\",\"FID\":\"F123\",\"Odyssey\":true}\n"
        );
        var monitor = new JournalDirectoryMonitor(directory, "F123");
        JournalMonitorUpdate bootstrap = await monitor.PollAsync();
        await File.AppendAllTextAsync(
            journal,
            "{\"timestamp\":\"2026-10-12T12:00:01Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\",\"SystemAddress\":10477373803,\"StarPos\":[0,0,0],\"JumpDist\":12.5}\n"
        );
        var accepted = new ProviderHandler("VoxStellar", HttpStatusCode.OK) { FailResponseBody = true };
        using (var http = new HttpClient(accepted))
        {
            await Run("VoxStellar", http, bootstrap, await monitor.PollAsync());
        }
        Assert.Single(accepted.Posts);
        clock.Now += TimeSpan.FromHours(1);
        var recovered = new ProviderHandler("VoxStellar", HttpStatusCode.OK);
        using var recoveredHttp = new HttpClient(recovered);
        await Run("VoxStellar", recoveredHttp, await new JournalDirectoryMonitor(directory, "F123").PollAsync(), null);
        Assert.Empty(recovered.Posts);
    }

    /// <summary>Confirms a source reread after acknowledged delivery cannot produce another provider write.</summary>
    [Theory]
    [InlineData("Inara")]
    [InlineData("EDSM")]
    [InlineData("EDAstro")]
    [InlineData("VoxStellar")]
    [InlineData("EDDN")]
    public async Task AcknowledgedSourceRereadIsNotPublishedAgain(string provider)
    {
        Directory.CreateDirectory(directory);
        string journal = Path.Combine(directory, "Journal.2026-10-12T120000.01.log");
        await File.WriteAllTextAsync(
            journal,
            "{\"timestamp\":\"2026-10-12T12:00:00Z\",\"event\":\"LoadGame\",\"Commander\":\"Explorer\",\"FID\":\"F123\",\"Odyssey\":true}\n"
        );
        var monitor = new JournalDirectoryMonitor(directory, "F123");
        JournalMonitorUpdate bootstrap = await monitor.PollAsync();
        await File.AppendAllTextAsync(
            journal,
            provider == "EDAstro"
                ? "{\"timestamp\":\"2026-10-12T12:00:01Z\",\"event\":\"Docked\",\"StarSystem\":\"Sol\",\"StationName\":\"Galileo\",\"MarketID\":128666762}\n"
                : "{\"timestamp\":\"2026-10-12T12:00:01Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\",\"SystemAddress\":10477373803,\"StarPos\":[0,0,0],\"JumpDist\":12.5}\n"
        );
        JournalMonitorUpdate live = await monitor.PollAsync();
        var accepted = new ProviderHandler(provider, HttpStatusCode.OK);
        using (var http = new HttpClient(accepted))
        {
            await Run(provider, http, bootstrap, live);
        }
        Assert.Contains(accepted.Posts, body => body.Contains(ExpectedEvent(provider), StringComparison.Ordinal));
        clock.Now += TimeSpan.FromHours(1);
        var reread = new ProviderHandler(provider, HttpStatusCode.OK);
        using var repeatedHttp = new HttpClient(reread);
        await Run(provider, repeatedHttp, bootstrap, live);
        Assert.DoesNotContain(reread.Posts, body => body.Contains(ExpectedEvent(provider), StringComparison.Ordinal));
    }

    /// <summary>Feeds real monitored journal updates through the selected publisher's public application and flush methods.</summary>
    private async Task Run(
        string provider,
        HttpClient http,
        JournalMonitorUpdate bootstrap,
        JournalMonitorUpdate? live,
        bool crashWorker = false,
        bool missingCredentials = false,
        bool discardRetainedReports = false
    )
    {
        string storage = Path.Combine(directory, "uploads");
        if (provider == "Inara")
        {
            using var publisher = new InaraPublisher("2.1.3.0", http, clock, outboxDirectory: storage);
            if (uploadPrivacy is not null)
            {
                publisher.SetUploadPrivacy(uploadPrivacy);
            }
            await publisher.ApplyAsync(
                new(
                    bootstrap.JournalEvents,
                    null,
                    null,
                    bootstrap.JournalPath,
                    false,
                    false,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    InaraOptions
                )
            );
            if (live is not null)
            {
                await publisher.ApplyAsync(
                    new(
                        live.JournalEvents,
                        null,
                        null,
                        live.JournalPath,
                        true,
                        false,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        InaraOptions
                    )
                );
            }
            await AwaitCrash(crashWorker);
            await publisher.FlushAsync();
            if (missingCredentials)
            {
                await publisher.ApplyAsync(
                    new(
                        [],
                        null,
                        null,
                        bootstrap.JournalPath,
                        false,
                        false,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        InaraOptions with
                        {
                            ApiKey = null,
                        }
                    )
                );
            }
            publisher.CancelPendingPublication(discardRetainedReports);
        }
        else if (provider == "EDSM")
        {
            using var publisher = new EdsmPublisher("2.1.3.0", http, clock, outboxDirectory: storage);
            if (uploadPrivacy is not null)
            {
                publisher.SetUploadPrivacy(uploadPrivacy);
            }
            await publisher.ApplyAsync(new(bootstrap.JournalEvents, bootstrap.JournalPath, false, EdsmOptions));
            if (live is not null)
            {
                await publisher.ApplyAsync(new(live.JournalEvents, live.JournalPath, true, EdsmOptions));
            }
            await AwaitCrash(crashWorker);
            await publisher.FlushAsync();
            if (missingCredentials)
            {
                await publisher.ApplyAsync(new([], bootstrap.JournalPath, false, EdsmOptions with { ApiKey = null }));
            }
            publisher.CancelPendingPublication(discardRetainedReports);
        }
        else if (provider == "EDAstro")
        {
            using var publisher = new EdAstroPublisher(
                "2.1.3.0",
                http,
                utcNow: () => clock.Now,
                automaticProcessing: false,
                trafficBudget: new EdAstroTrafficBudget(),
                outboxDirectory: storage
            );
            if (uploadPrivacy is not null)
            {
                publisher.SetUploadPrivacy(uploadPrivacy);
            }
            publisher.SetEnabled(true);
            publisher.Apply(new(bootstrap.JournalEvents, "Explorer", bootstrap.JournalPath, true, false, true));
            if (live is not null)
            {
                publisher.Apply(new(live.JournalEvents, "Explorer", live.JournalPath, true, true, true));
            }
            await AwaitCrash(crashWorker);
            await publisher.FlushAsync();
            clock.Now += TimeSpan.FromSeconds(10);
            await publisher.FlushAsync();
        }
        else if (provider == "VoxStellar")
        {
            using var publisher = new VoxStellarPublisher(
                "2.1.3.0",
                "signing-key",
                new VoxStellarPublisherOptions
                {
                    Client = http,
                    TimeProvider = clock,
                    BatchInterval = TimeSpan.Zero,
                    OutboxDirectory = storage,
                }
            );
            await publisher.ApplyAsync(
                new()
                {
                    JournalEvents = bootstrap.JournalEvents,
                    CommanderName = "Explorer",
                    Enabled = true,
                    AllowPublishing = false,
                }
            );
            if (live is not null)
            {
                await publisher.ApplyAsync(
                    new()
                    {
                        JournalEvents = live.JournalEvents,
                        CommanderName = "Explorer",
                        Enabled = true,
                        AllowPublishing = true,
                    }
                );
            }
            await AwaitCrash(crashWorker);
        }
        else
        {
            using var publisher = new EddnPublisher(
                "2.1.3.0",
                http,
                outboxPath: Path.Combine(storage, "eddn.json"),
                utcNow: () => clock.Now,
                automaticProcessing: false
            );
            if (uploadPrivacy is not null)
            {
                publisher.SetUploadPrivacy(uploadPrivacy);
            }
            await publisher.ApplyAsync(EddnRequest(bootstrap, false));
            if (live is not null)
            {
                await publisher.ApplyAsync(EddnRequest(live, true));
            }
            await AwaitCrash(crashWorker);
            clock.Now += TimeSpan.FromSeconds(10);
            await publisher.ProcessPendingAsync();
        }
    }

    /// <summary>Provides attributable live game metadata to the EDDN schema projection.</summary>
    private EddnApplyRequest EddnRequest(JournalMonitorUpdate update, bool publish) =>
        new()
        {
            JournalEvents = update.JournalEvents,
            CommanderName = "Explorer",
            FrontierId = "F123",
            GameVersion = "4.1.0.100",
            GameBuild = "r300000",
            JournalDirectory = directory,
            JournalPath = update.JournalPath,
            Enabled = true,
            AllowPublishing = publish,
        };

    /// <summary>Marks the write-ahead boundary before a separate test process is forcibly terminated.</summary>
    private async Task AwaitCrash(bool crashWorker)
    {
        if (crashWorker)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "ready"), "captured");
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Starts this test assembly as a retained-upload writer and terminates it without running disposal.</summary>
    [Theory]
    [InlineData("Inara")]
    [InlineData("EDSM")]
    [InlineData("EDAstro")]
    [InlineData("VoxStellar")]
    [InlineData("EDDN")]
    public async Task CapturedEvidenceSurvivesUnexpectedProcessTermination(string provider)
    {
        Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(JournalUploadScenarioTests).Assembly.Location);
        start.ArgumentList.Add("--journal-upload-crash-worker");
        start.ArgumentList.Add(provider);
        start.ArgumentList.Add(directory);
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!File.Exists(Path.Combine(directory, "ready")))
            {
                Assert.False(process.HasExited, "Upload writer exited before its durable boundary.");
                await Task.Delay(20, deadline.Token);
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync();
        }
        Assert.True(File.Exists(Path.Combine(directory, "ready")), await output + await errors);
        clock.Now += TimeSpan.FromHours(1);
        var recovery = new ProviderHandler(provider, HttpStatusCode.OK);
        using var http = new HttpClient(recovery);
        await Run(provider, http, await new JournalDirectoryMonitor(directory, "F123").PollAsync(), null);
        Assert.True(
            recovery.Posts.Any(body => body.Contains(ExpectedEvent(provider), StringComparison.Ordinal)),
            string.Join("\n", recovery.Posts)
        );
    }

    /// <summary>Creates real live journal evidence in the isolated child writer process.</summary>
    internal static async Task RunCrashWorkerAsync(string provider, string folder)
    {
        var fixture = new JournalUploadScenarioTests(folder);
        string journal = Path.Combine(folder, "Journal.2026-10-12T120000.01.log");
        await File.WriteAllTextAsync(
            journal,
            "{\"timestamp\":\"2026-10-12T12:00:00Z\",\"event\":\"LoadGame\",\"Commander\":\"Explorer\",\"FID\":\"F123\",\"Odyssey\":true}\n"
        );
        var monitor = new JournalDirectoryMonitor(folder, "F123");
        JournalMonitorUpdate bootstrap = await monitor.PollAsync();
        await File.AppendAllTextAsync(
            journal,
            provider == "EDAstro"
                ? "{\"timestamp\":\"2026-10-12T12:00:01Z\",\"event\":\"Docked\",\"StarSystem\":\"Sol\",\"StationName\":\"Galileo\",\"MarketID\":128666762}\n"
                : "{\"timestamp\":\"2026-10-12T12:00:01Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\",\"SystemAddress\":10477373803,\"StarPos\":[0,0,0],\"JumpDist\":12.5}\n"
        );
        using var http = new HttpClient(new ProviderHandler(provider, HttpStatusCode.ServiceUnavailable));
        await fixture.Run(provider, http, bootstrap, await monitor.PollAsync(), crashWorker: true);
    }

    /// <summary>Creates an isolated scenario fixture for the test runner.</summary>
    public JournalUploadScenarioTests() { }

    /// <summary>Attaches the child writer to the parent's isolated storage.</summary>
    private JournalUploadScenarioTests(string folder) => directory = folder;

    /// <summary>Removes only this fixture's journal and retained upload files.</summary>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Controls wall time while retaining normal timers for publisher lifecycle behavior.</summary>
    private sealed class ScenarioClock : TimeProvider
    {
        /// <summary>Advances provider deadlines without substituting captured journal timestamps.</summary>
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>Returns the scenario's current provider time.</summary>
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Captures provider wire requests and supplies complete provider-specific acknowledgements.</summary>
    private sealed class ProviderHandler(string provider, HttpStatusCode status) : HttpMessageHandler
    {
        /// <summary>Simulates a body failure after successful provider headers.</summary>
        public bool FailResponseBody { get; init; }
        public Func<CancellationToken, Task>? BeforePost { get; init; }
        public string EdAstroEvents { get; init; } = "[\"Docked\"]";

        /// <summary>Captures actual provider request bodies for independent wire-contract assertions.</summary>
        public List<string> Posts { get; } = [];

        /// <summary>Serves EDSM policy and acknowledges exactly the submitted event count.</summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.Method == HttpMethod.Get)
            {
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        provider == "EDAstro" ? EdAstroEvents : "[\"Fileheader\",\"Shutdown\"]"
                    ),
                };
            }
            if (BeforePost is { } before)
            {
                await before(cancellationToken);
            }
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (provider == "EDSM")
            {
                body = Uri.UnescapeDataString(body.Replace('+', ' '));
            }
            if (provider == "EDDN")
            {
                byte[] bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                using var compressed = new MemoryStream(bytes);
                using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip);
                body = await reader.ReadToEndAsync(cancellationToken);
            }
            Posts.Add(body);
            if (FailResponseBody)
            {
                return new(status) { Content = new BrokenResponseContent() };
            }
            if (provider is "EDDN" or "VoxStellar" or "EDAstro")
            {
                return new(status) { Content = new StringContent(provider == "EDAstro" ? "{\"status\":200}" : "ok") };
            }
            int count =
                provider == "Inara"
                    ? JsonNode.Parse(body)!["events"]!.AsArray().Count
                    : JsonNode.Parse(body[(body.IndexOf("message=", StringComparison.Ordinal) + 8)..])!.AsArray().Count;
            string response =
                provider == "Inara"
                    ? "{\"header\":{\"eventStatus\":200},\"events\":["
                        + string.Join(',', Enumerable.Repeat("{\"eventStatus\":200}", count))
                        + "]}"
                    : "{\"msgnum\":100,\"events\":["
                        + string.Join(',', Enumerable.Repeat("{\"msgnum\":100}", count))
                        + "]}";
            return new(status) { Content = new StringContent(response) };
        }
    }

    /// <summary>Fails only if the transport unnecessarily buffers an acknowledgement's unused body.</summary>
    private sealed class BrokenResponseContent : HttpContent
    {
        /// <summary>Represents a truncated external response stream.</summary>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(new IOException("Response body was truncated."));

        /// <summary>Reports an unknown body length without reading it.</summary>
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
