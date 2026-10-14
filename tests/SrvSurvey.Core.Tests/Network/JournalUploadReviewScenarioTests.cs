using System.Net;
using SrvSurvey.Core.EdAstro;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Tests.Network;

public sealed partial class JournalUploadScenarioTests
{
    /// <summary>Explicit removal clears retained authenticated uploads even after a temporary blank-key apply paused the sender.</summary>
    [Theory]
    [InlineData("Inara")]
    [InlineData("EDSM")]
    public async Task ExplicitCredentialRemovalRetiresReportsAfterTemporaryCredentialFailure(string provider)
    {
        Directory.CreateDirectory(directory);
        string journal = Path.Combine(directory, "Journal.2026-10-13T120000.01.log");
        await File.WriteAllTextAsync(
            journal,
            "{\"timestamp\":\"2026-10-13T12:00:00Z\",\"event\":\"LoadGame\",\"Commander\":\"Explorer\",\"FID\":\"F123\",\"Odyssey\":true}\n"
        );
        var monitor = new JournalDirectoryMonitor(directory, "F123");
        JournalMonitorUpdate bootstrap = await monitor.PollAsync();
        await File.AppendAllTextAsync(
            journal,
            "{\"timestamp\":\"2026-10-13T12:00:01Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\",\"SystemAddress\":10477373803,\"StarPos\":[0,0,0],\"JumpDist\":12.5}\n"
        );
        var failed = new ProviderHandler(provider, HttpStatusCode.ServiceUnavailable);
        using (var http = new HttpClient(failed))
        {
            await Run(
                provider,
                http,
                bootstrap,
                await monitor.PollAsync(),
                missingCredentials: true,
                discardRetainedReports: true
            );
        }
        Assert.Contains(failed.Posts, body => body.Contains(ExpectedEvent(provider), StringComparison.Ordinal));
        clock.Now += TimeSpan.FromHours(1);
        var restored = new ProviderHandler(provider, HttpStatusCode.OK);
        using var restoredHttp = new HttpClient(restored);
        await Run(provider, restoredHttp, await new JournalDirectoryMonitor(directory, "F123").PollAsync(), null);
        Assert.DoesNotContain(restored.Posts, body => body.Contains(ExpectedEvent(provider), StringComparison.Ordinal));
    }

    /// <summary>An unexpected foreign identity cannot relabel the remainder of a monitored batch under the current account.</summary>
    [Theory]
    [InlineData("Inara")]
    [InlineData("EDSM")]
    public async Task ForeignCommanderBoundaryCannotRelabelFollowingEvents(string provider)
    {
        Directory.CreateDirectory(directory);
        string journal = Path.Combine(directory, "Journal.Identity.log");
        await File.WriteAllTextAsync(
            journal,
            "{\"timestamp\":\"2026-10-14T12:00:00Z\",\"event\":\"LoadGame\",\"Commander\":\"Explorer\",\"FID\":\"F123\",\"Odyssey\":true}\n"
        );
        var monitor = new JournalDirectoryMonitor(directory, "F123");
        JournalMonitorUpdate bootstrap = await monitor.PollAsync();
        await File.AppendAllTextAsync(
            journal,
            """
            {"timestamp":"2026-10-14T12:00:01Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":1,"StarPos":[0,0,0],"JumpDist":1}
            {"timestamp":"2026-10-14T12:00:02Z","event":"LoadGame","Commander":"Other","FID":"F999","Odyssey":true}
            {"timestamp":"2026-10-14T12:00:03Z","event":"FSDJump","StarSystem":"Foreign System","SystemAddress":2,"StarPos":[1,0,0],"JumpDist":1}

            """
        );
        var handler = new ProviderHandler(provider, HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        await Run(provider, client, bootstrap, await monitor.PollAsync());
        Assert.Contains(handler.Posts, body => body.Contains("Sol", StringComparison.Ordinal));
        Assert.All(handler.Posts, body => Assert.DoesNotContain("Foreign System", body, StringComparison.Ordinal));
    }

    /// <summary>Legacy reports preserve their original uploader and shared opt-out while new commanders use independent queues.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyEddnQueueDrainsWithoutRelabelingTheCommander(bool otherProfileOptedOut)
    {
        Directory.CreateDirectory(directory);
        string journal = Path.Combine(directory, "Journal.Legacy.log");
        await File.WriteAllTextAsync(
            journal,
            "{\"timestamp\":\"2026-10-14T12:00:00Z\",\"event\":\"LoadGame\",\"Commander\":\"Explorer\",\"FID\":\"F123\",\"Odyssey\":true}\n"
        );
        var monitor = new JournalDirectoryMonitor(directory, "F123");
        JournalMonitorUpdate bootstrap = await monitor.PollAsync();
        await File.AppendAllTextAsync(
            journal,
            "{\"timestamp\":\"2026-10-14T12:00:01Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\",\"SystemAddress\":1,\"StarPos\":[0,0,0],\"JumpDist\":1}\n"
        );
        using (var failed = new HttpClient(new ProviderHandler("EDDN", HttpStatusCode.ServiceUnavailable)))
        {
            await Run("EDDN", failed, bootstrap, await monitor.PollAsync());
        }
        clock.Now += TimeSpan.FromHours(1);
        var handler = new ProviderHandler("EDDN", HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        using var publisher = new EddnCommanderPublisher(
            "2.1.3.0",
            Path.Combine(directory, "accounts"),
            Path.Combine(directory, "uploads", "eddn.json"),
            client,
            utcNow: () => clock.Now,
            automaticProcessing: false
        );
        using var other = new EddnCommanderPublisher(
            "2.1.3.0",
            Path.Combine(directory, "other-profile"),
            Path.Combine(directory, "uploads", "eddn.json"),
            client,
            automaticProcessing: false
        );
        other.SetEnabled(!otherProfileOptedOut);
        await publisher.ApplyAsync(
            new EddnApplyRequest
            {
                JournalEvents = [],
                FrontierId = "F999",
                CommanderName = "Other",
                Enabled = true,
            }
        );
        await publisher.FlushAsync();
        if (otherProfileOptedOut)
        {
            Assert.Empty(handler.Posts);
            return;
        }
        string body = Assert.Single(handler.Posts);
        Assert.Contains("Explorer", body, StringComparison.Ordinal);
        Assert.Contains("Sol", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Other", body, StringComparison.Ordinal);
    }

    /// <summary>Selected accounts recover without journal activity, preserve suspension, and leave other commanders' leases available.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedEddnAccountRecoversWithoutJournalActivity(bool suspended)
    {
        string accounts = Path.Combine(directory, "accounts");
        string legacy = Path.Combine(directory, "legacy.json");
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"timestamp":"2026-10-14T12:00:01Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":1,"StarPos":[0,0,0],"JumpDist":1}""",
                out JournalEventEnvelope? entry,
                out string? error
            ),
            error
        );
        using (var client = new HttpClient(new ProviderHandler("EDDN", HttpStatusCode.ServiceUnavailable)))
        using (var failed = new EddnCommanderPublisher("2.1.3.0", accounts, legacy, client, automaticProcessing: false))
        {
            foreach ((string fid, string name) in new[] { ("F123", "Explorer"), ("F999", "Other") })
            {
                await failed.ApplyAsync(
                    new EddnApplyRequest
                    {
                        JournalEvents = [entry!],
                        FrontierId = fid,
                        CommanderName = name,
                        JournalPath = Path.Combine(directory, "Journal." + fid + ".log"),
                        GameVersion = "4.1.0.100",
                        Enabled = true,
                        AllowPublishing = true,
                    }
                );
            }
            await failed.FlushAsync();
        }
        clock.Now += TimeSpan.FromHours(1);
        var handler = new ProviderHandler("EDDN", HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        using var restored = new EddnCommanderPublisher(
            "2.1.3.0",
            accounts,
            legacy,
            http,
            utcNow: () => clock.Now,
            automaticProcessing: false
        );
        restored.SetEnabled(true);
        restored.SetSuspended(suspended);
        restored.RestorePendingAccount(null);
        restored.RestorePendingAccount("missing");
        restored.RestorePendingAccount("f123");
        restored.RestorePendingAccount("F123");
        await restored.FlushAsync();
        Assert.Equal(suspended ? 0 : 1, handler.Posts.Count);
        restored.SetSuspended(false);
        await restored.FlushAsync();
        Assert.Contains("Explorer", Assert.Single(handler.Posts), StringComparison.Ordinal);
        var otherHandler = new ProviderHandler("EDDN", HttpStatusCode.OK);
        using var otherHttp = new HttpClient(otherHandler);
        using var other = new EddnCommanderPublisher(
            "2.1.3.0",
            accounts,
            legacy,
            otherHttp,
            utcNow: () => clock.Now,
            automaticProcessing: false
        );
        other.SetEnabled(true);
        other.RestorePendingAccount("F999");
        await other.FlushAsync();
        Assert.Contains("Other", Assert.Single(otherHandler.Posts), StringComparison.Ordinal);
        await restored.FlushAsync();
        Assert.Single(handler.Posts);
        restored.Dispose();
        restored.RestorePendingAccount("F999");
    }

    /// <summary>A missing identity cannot create an account queue, and disposed routing cannot restart delivery.</summary>
    [Fact]
    public async Task EddnRoutingRequiresIdentityAndStopsAfterDisposal()
    {
        using var client = new HttpClient(new ProviderHandler("EDDN", HttpStatusCode.OK));
        using var publisher = new EddnCommanderPublisher(
            "2.1.3.0",
            Path.Combine(directory, "accounts"),
            Path.Combine(directory, "legacy.json"),
            client,
            automaticProcessing: false
        );
        EddnPublicationResult missing = await publisher.ApplyAsync(
            new EddnApplyRequest { JournalEvents = [], Enabled = true }
        );
        Assert.Empty(missing.Published);
        publisher.Dispose();
        publisher.SetEnabled(true);
        publisher.SetSuspended(false);
        EddnPublicationResult stopped = await publisher.ApplyAsync(
            new EddnApplyRequest
            {
                JournalEvents = [],
                FrontierId = "F123",
                Enabled = true,
            }
        );
        Assert.Empty(stopped.Published);
    }

    /// <summary>A failed durable append stays local, reports the failure, and cannot stop later journal capture.</summary>
    [Theory]
    [InlineData("EDAstro")]
    [InlineData("VoxStellar")]
    public async Task CaptureStorageFailureDoesNotStopJournalProcessing(string provider)
    {
        var handler = new ProviderHandler(provider, HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        string storage = Path.Combine(directory, "uploads");
        string eventName = provider == "EDAstro" ? "Docked" : "FSDJump";
        Assert.True(
            JournalEventEnvelope.TryParse(
                "{\"timestamp\":\"2026-10-13T12:00:00Z\",\"event\":\""
                    + eventName
                    + "\",\"StarSystem\":\"Sol\",\"StationName\":\"Galileo\"}",
                out JournalEventEnvelope? parsed,
                out string? error
            ),
            error
        );
        JournalEventEnvelope entry = parsed! with { SourceRecordIdentity = "Journal.Review.log:100" };
        if (provider == "EDAstro")
        {
            var warnings = new List<string>();
            using var publisher = new EdAstroPublisher(
                "2.1.3.0",
                http,
                log: warnings.Add,
                utcNow: () => clock.Now,
                automaticProcessing: false,
                trafficBudget: new EdAstroTrafficBudget(),
                outboxDirectory: storage
            );
            publisher.SetEnabled(true);
            publisher.Apply(new([], "Explorer", "Journal.Review.log", true, false, true));
            string blocked = BlockUploadLog(storage, "edastro");
            publisher.Apply(new([entry], "Explorer", "Journal.Review.log", true, true, true));
            Assert.Contains(warnings, warning => warning.Contains("persist", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(handler.Posts);
            Directory.Delete(blocked);
            publisher.Apply(new([entry], "Explorer", "Journal.Review.log", true, true, true));
            await publisher.FlushAsync();
            clock.Now += TimeSpan.FromSeconds(10);
            await publisher.FlushAsync();
        }
        else
        {
            using var publisher = new VoxStellarPublisher(
                "2.1.3.0",
                "signing-key",
                new VoxStellarPublisherOptions
                {
                    Client = http,
                    BatchInterval = TimeSpan.Zero,
                    OutboxDirectory = storage,
                }
            );
            await publisher.ApplyAsync(
                new()
                {
                    JournalEvents = [],
                    CommanderName = "Explorer",
                    Enabled = true,
                    AllowPublishing = false,
                }
            );
            string blocked = BlockUploadLog(storage, "voxstellar");
            VoxStellarPublicationResult failed = await publisher.ApplyAsync(
                new()
                {
                    JournalEvents = [entry, entry],
                    CommanderName = "Explorer",
                    Enabled = true,
                    AllowPublishing = true,
                }
            );
            Assert.Empty(failed.QueuedEventNames);
            Assert.Contains(failed.Warnings, warning => warning.Contains("2 event(s)", StringComparison.Ordinal));
            Assert.Empty(handler.Posts);
            Directory.Delete(blocked);
            VoxStellarPublicationResult queued = await publisher.ApplyAsync(
                new()
                {
                    JournalEvents = [entry],
                    CommanderName = "Explorer",
                    Enabled = true,
                    AllowPublishing = true,
                }
            );
            Assert.Single(queued.QueuedEventNames);
        }
        Assert.Single(handler.Posts);
    }

    /// <summary>Blocks the actual leased log path without replacing the storage implementation with a mock.</summary>
    private static string BlockUploadLog(string storage, string provider)
    {
        string lease = Assert.Single(Directory.GetFiles(Path.Combine(storage, provider), "*.jsonl.lease"));
        string path = lease[..^".lease".Length];
        Directory.CreateDirectory(path);
        return path;
    }
}
