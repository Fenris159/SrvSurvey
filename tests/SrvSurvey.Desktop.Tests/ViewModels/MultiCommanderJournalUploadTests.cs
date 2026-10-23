using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text.Json.Nodes;
using SrvSurvey.Core.EdAstro;
using SrvSurvey.Core.Edsm;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Inara;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

/// <summary>Exercises two independent application instances through real journal readers and provider HTTP serialization.</summary>
[Trait("Category", "JournalUploadScenario")]
public sealed class MultiCommanderJournalUploadTests
{
    /// <summary>Verified folders enable each provider without crossing account credentials, payloads, or companion snapshots.</summary>
    [Theory]
    [InlineData("EDSM", false, false)]
    [InlineData("EDAstro", false, false)]
    [InlineData("EDDN", false, false)]
    [InlineData("VoxStellar", false, false)]
    [InlineData("Inara", false, false)]
    [InlineData("EDSM", true, false)]
    [InlineData("EDAstro", true, false)]
    [InlineData("EDDN", true, false)]
    [InlineData("VoxStellar", true, false)]
    [InlineData("Inara", true, false)]
    [InlineData("EDSM", false, true)]
    [InlineData("EDAstro", false, true)]
    public async Task InterleavedCommandersUseOnlyTheirOwnJournalAndCompanions(
        string provider,
        bool sharedFolder,
        bool endSession
    )
    {
        string root = Path.Combine(Path.GetTempPath(), "SrvSurvey-multi-upload-" + Guid.NewGuid().ToString("N"));
        var paths = new AppDataPaths(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "cache"),
            []
        );
        try
        {
            string firstFolder = await CreateJournalAsync(root, "Steam", "F1", "First");
            string secondFolder = await CreateJournalAsync(root, sharedFolder ? "Steam" : "Epic", "F2", "Second");
            await SaveProfilesAsync(paths);
            using var firstSender = new Sender(provider, root);
            using var secondSender = new Sender(provider, root);
            using MainWindowViewModel first = Create(firstFolder, "F1", paths, firstSender);
            using MainWindowViewModel second = Create(secondFolder, "F2", paths, secondSender);
            await first.RefreshAsync();
            await second.RefreshAsync();
            await first.RefreshAsync();
            if (endSession)
            {
                // Publish the peer's activity while both clients still own verified source leases.
                await AppendActivityAsync(secondFolder, "F2", "Second", 202, 98765);
                await second.RefreshAsync();
            }
            await AppendActivityAsync(firstFolder, "F1", "First", 101, 12345);
            if (endSession)
            {
                await File.AppendAllTextAsync(
                    Path.Combine(firstFolder, "Journal.F1.log"),
                    """{"timestamp":"2026-10-14T12:00:07Z","event":"Music","MusicTrack":"MainMenu"}""" + "\n"
                );
            }
            await first.RefreshAsync();
            if (!endSession)
            {
                await AppendActivityAsync(secondFolder, "F2", "Second", 202, 98765);
                await second.RefreshAsync();
            }
            await firstSender.FlushAsync(provider == "VoxStellar" && !sharedFolder);
            await secondSender.FlushAsync(provider == "VoxStellar" && !sharedFolder);
            bool journalAllowed = !sharedFolder || provider == "Inara";
            Assert.True(journalAllowed == !firstSender.Handler.Posts.IsEmpty, firstSender.Diagnostics);
            Assert.True(journalAllowed == !secondSender.Handler.Posts.IsEmpty, secondSender.Diagnostics);
            if (!journalAllowed)
            {
                return;
            }
            AssertOwnPayloads(firstSender, "First", "Second");
            AssertOwnPayloads(secondSender, "Second", "First");
            if (provider == "Inara")
            {
                string[] firstInventories = firstSender
                    .Handler.Posts.Where(body => body.Contains("setCommanderInventoryCargo", StringComparison.Ordinal))
                    .ToArray();
                string[] secondInventories = secondSender
                    .Handler.Posts.Where(body => body.Contains("setCommanderInventoryCargo", StringComparison.Ordinal))
                    .ToArray();
                Assert.Equal(!sharedFolder, firstInventories.Length > 0);
                Assert.Equal(!sharedFolder, secondInventories.Length > 0);
                if (!sharedFolder)
                {
                    Assert.Contains("12345", string.Join('\n', firstInventories), StringComparison.Ordinal);
                    Assert.Contains("98765", string.Join('\n', secondInventories), StringComparison.Ordinal);
                }
            }
            if (provider == "EDDN")
            {
                Assert.Contains(
                    firstSender.Handler.Posts,
                    body =>
                        body.Contains("/commodity/", StringComparison.Ordinal)
                        && body.Contains("101", StringComparison.Ordinal)
                );
                Assert.Contains(
                    secondSender.Handler.Posts,
                    body =>
                        body.Contains("/commodity/", StringComparison.Ordinal)
                        && body.Contains("202", StringComparison.Ordinal)
                );
            }
            int firstCount = firstSender.Handler.Posts.Count;
            int secondCount = secondSender.Handler.Posts.Count;
            await first.RefreshAsync();
            await second.RefreshAsync();
            await firstSender.FlushAsync();
            await secondSender.FlushAsync();
            Assert.Equal(firstCount, firstSender.Handler.Posts.Count);
            Assert.Equal(secondCount, secondSender.Handler.Posts.Count);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    /// <summary>Failed simultaneous uploads recover under their original commander accounts after both applications restart.</summary>
    [Theory]
    [InlineData("EDSM")]
    [InlineData("EDAstro")]
    [InlineData("EDDN")]
    [InlineData("VoxStellar")]
    [InlineData("Inara")]
    public async Task TwoCommanderRetainedUploadsStayWithTheirOwnersAfterRestart(string provider)
    {
        string root = Path.Combine(Path.GetTempPath(), "SrvSurvey-multi-recovery-" + Guid.NewGuid().ToString("N"));
        var paths = new AppDataPaths(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "cache"),
            []
        );
        try
        {
            string firstFolder = await CreateJournalAsync(root, "Steam", "F1", "First");
            string secondFolder = await CreateJournalAsync(root, "Epic", "F2", "Second");
            await SaveProfilesAsync(paths);
            using (var firstSender = new Sender(provider, root))
            using (var secondSender = new Sender(provider, root))
            using (MainWindowViewModel first = Create(firstFolder, "F1", paths, firstSender))
            using (MainWindowViewModel second = Create(secondFolder, "F2", paths, secondSender))
            {
                firstSender.Handler.Status = HttpStatusCode.ServiceUnavailable;
                secondSender.Handler.Status = HttpStatusCode.ServiceUnavailable;
                await first.RefreshAsync();
                await second.RefreshAsync();
                await first.RefreshAsync();
                await AppendActivityAsync(firstFolder, "F1", "First", 101, 12345);
                await first.RefreshAsync();
                await AppendActivityAsync(secondFolder, "F2", "Second", 202, 98765);
                await second.RefreshAsync();
                await firstSender.FlushAsync(provider == "VoxStellar");
                await secondSender.FlushAsync(provider == "VoxStellar");
                Assert.False(firstSender.Handler.Posts.IsEmpty);
                Assert.False(secondSender.Handler.Posts.IsEmpty);
            }
            if (provider is "EDDN" or "EDAstro")
            {
                await File.AppendAllTextAsync(
                    Path.Combine(firstFolder, "Journal.F1.log"),
                    """{"timestamp":"2026-10-14T12:00:07Z","event":"Music","MusicTrack":"MainMenu"}""" + "\n"
                );
                await File.AppendAllTextAsync(
                    Path.Combine(secondFolder, "Journal.F2.log"),
                    """{"timestamp":"2026-10-14T12:00:07Z","event":"Shutdown"}""" + "\n"
                );
            }
            using var recoveredFirst = new Sender(provider, root);
            using var recoveredSecond = new Sender(provider, root);
            recoveredFirst.Advance(TimeSpan.FromHours(4));
            recoveredSecond.Advance(TimeSpan.FromHours(4));
            using MainWindowViewModel firstRestart = Create(firstFolder, "F1", paths, recoveredFirst);
            using MainWindowViewModel secondRestart = Create(secondFolder, "F2", paths, recoveredSecond);
            await firstRestart.RefreshAsync();
            await secondRestart.RefreshAsync();
            await firstRestart.RefreshAsync();
            await recoveredFirst.FlushAsync(provider == "VoxStellar");
            await recoveredSecond.FlushAsync(provider == "VoxStellar");
            AssertOwnPayloads(recoveredFirst, "First", "Second");
            AssertOwnPayloads(recoveredSecond, "Second", "First");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    /// <summary>Configures separate personal API credentials before either application can publish.</summary>
    private static async Task SaveProfilesAsync(AppDataPaths paths)
    {
        var profiles = new CommanderProfileStore(paths.DataDirectory);
        await profiles.SaveInaraApiKeyAsync("F1", "First", true, "first-key");
        await profiles.SaveInaraApiKeyAsync("F2", "Second", true, "second-key");
        await profiles.SaveEdsmCredentialsAsync("F1", "First", true, "First", "first-key");
        await profiles.SaveEdsmCredentialsAsync("F2", "Second", true, "Second", "second-key");
    }

    /// <summary>Verifies actual provider payloads and account headers against the independently named source systems.</summary>
    private static void AssertOwnPayloads(Sender sender, string own, string other)
    {
        Assert.Contains(sender.Handler.Posts, body => body.Contains(own + " System", StringComparison.Ordinal));
        Assert.All(
            sender.Handler.Posts,
            body => Assert.DoesNotContain(other + " System", body, StringComparison.Ordinal)
        );
        if (sender.Provider is "Inara" or "EDSM")
        {
            Assert.All(
                sender.Handler.Posts,
                body => Assert.Contains(own.ToLowerInvariant() + "-key", body, StringComparison.Ordinal)
            );
        }
        if (sender.Provider is "EDDN" or "VoxStellar")
        {
            Assert.All(sender.Handler.Posts, body => Assert.Contains(own, body, StringComparison.Ordinal));
        }
    }

    /// <summary>Binds one real application projection to a pinned commander and a selected real provider sender.</summary>
    private static MainWindowViewModel Create(string folder, string fid, AppDataPaths paths, Sender sender)
    {
        AppDataPaths profilePaths = paths with { SettingsFrontierId = fid };
        new NetworkPrivacySettingsStore(profilePaths.UiSettingsPath).Save(
            NetworkPrivacyPreferences.Default with
            {
                EddnUploadEnabled = sender.Provider == "EDDN",
                EdAstroUploadEnabled = sender.Provider == "EDAstro",
            }
        );
        new VoxStellarSettingsStore(profilePaths.UiSettingsPath).Save(
            new VoxStellarPreferences(sender.Provider == "VoxStellar")
        );
        MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(
            folder,
            builder =>
            {
                builder
                    .WithAppDataPaths(paths with { SettingsFrontierId = fid })
                    .WithTargetFrontierId(fid)
                    .WithGameWindowSwitcher(new TwoGames());
                sender.Configure(builder);
            }
        );
        model.NetworkPrivacy.EddnUploadEnabled = sender.Provider == "EDDN";
        model.NetworkPrivacy.EdAstroUploadEnabled = sender.Provider == "EDAstro";
        model.VoxStellar.JournalUploadEnabled = sender.Provider == "VoxStellar";
        return model;
    }

    /// <summary>Creates Steam/Epic-like physical source folders with attributable current journal headers.</summary>
    private static async Task<string> CreateJournalAsync(string root, string folder, string fid, string commander)
    {
        string path = Path.Combine(root, folder);
        Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(
            Path.Combine(path, $"Journal.{fid}.log"),
            $$"""
            {"timestamp":"2026-10-14T12:00:00Z","event":"Fileheader","gameversion":"4.1.0.100","build":"r300000","Odyssey":true}
            {"timestamp":"2026-10-14T12:00:01Z","event":"Commander","Name":"{{commander}}","FID":"{{fid}}"}
            {"timestamp":"2026-10-14T12:00:02Z","event":"LoadGame","Commander":"{{commander}}","FID":"{{fid}}","Odyssey":true,"Ship":"Python"}

            """
        );
        return path;
    }

    /// <summary>Writes independently distinguishable journal activity and matching cargo/market companions.</summary>
    private static async Task AppendActivityAsync(string folder, string fid, string commander, int market, int cargo)
    {
        await File.WriteAllTextAsync(
            Path.Combine(folder, "Cargo.json"),
            $$"""{"timestamp":"2026-10-14T12:00:06Z","event":"Cargo","Vessel":"Ship","Count":{{cargo}},"Inventory":[{"Name":"steel","Count":{{cargo}}}]}"""
        );
        await File.WriteAllTextAsync(
            Path.Combine(folder, "Market.json"),
            $$"""{"timestamp":"2026-10-14T12:00:06Z","event":"Market","MarketID":{{market}},"StarSystem":"{{commander}} System","StationName":"{{commander}} Port","StationType":"Coriolis","Items":[{"Name":"Steel","MeanPrice":10,"BuyPrice":11,"SellPrice":9,"Stock":100,"StockBracket":2,"Demand":1000,"DemandBracket":3}]}"""
        );
        await File.AppendAllTextAsync(
            Path.Combine(folder, $"Journal.{fid}.log"),
            $$"""
            {"timestamp":"2026-10-14T12:00:03Z","event":"FSDJump","StarSystem":"{{commander}} System","SystemAddress":{{market}},"StarPos":[1,2,3],"JumpDist":12.5}
            {"timestamp":"2026-10-14T12:00:04Z","event":"Docked","StarSystem":"{{commander}} System","SystemAddress":{{market}},"StationName":"{{commander}} Port","MarketID":{{market}},"StationType":"Coriolis"}
            {"timestamp":"2026-10-14T12:00:05Z","event":"Cargo","Vessel":"Ship","Count":{{cargo}}}
            {"timestamp":"2026-10-14T12:00:06Z","event":"Market","MarketID":{{market}}}

            """
        );
    }

    /// <summary>Represents two running Elite clients without interacting with the desktop.</summary>
    private sealed class TwoGames : IGameWindowSwitcher
    {
        /// <summary>Reports both clients as active.</summary>
        public int GetAvailableWindowCount() => 2;

        /// <summary>Does not activate a real window.</summary>
        public bool TryActivateCurrent() => false;

        /// <summary>Does not switch real windows.</summary>
        public bool TryActivateNext() => false;

        /// <summary>Has no native resources.</summary>
        public void Dispose() { }
    }

    /// <summary>Owns a real provider and deterministic clock while all HTTP traffic remains captured locally.</summary>
    private sealed class Sender : IDisposable
    {
        private readonly IDisposable publisher;
        private readonly HttpClient client;
        private readonly Clock clock = new();
        public string Provider { get; }
        public ProviderHandler Handler { get; }
        private readonly ConcurrentQueue<string> logs = new();
        private readonly TaskCompletionSource firstVoxBatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Diagnostics =>
            $"{Provider} pending={(publisher as EdAstroPublisher)?.PendingCount}; {string.Join("; ", logs)}";

        /// <summary>Shares the durable root between both instances to exercise account-specific queue ownership.</summary>
        public Sender(string provider, string root)
        {
            Provider = provider;
            Handler = new ProviderHandler(provider);
            client = new HttpClient(Handler);
            string storage = Path.Combine(root, "uploads");
            publisher = provider switch
            {
                "Inara" => new InaraPublisher("2.1.3.0", client, clock, outboxDirectory: storage),
                "EDSM" => new EdsmPublisher("2.1.3.0", client, clock, outboxDirectory: storage),
                "EDAstro" => new EdAstroPublisher(
                    "2.1.3.0",
                    client,
                    utcNow: () => clock.Now,
                    automaticProcessing: false,
                    log: logs.Enqueue,
                    trafficBudget: new EdAstroTrafficBudget(),
                    outboxDirectory: storage
                ),
                "EDDN" => new EddnCommanderPublisher(
                    "2.1.3.0",
                    Path.Combine(storage, "eddn"),
                    Path.Combine(root, "eddn-legacy.json"),
                    client,
                    utcNow: () => clock.Now,
                    automaticProcessing: false
                ),
                _ => new VoxStellarPublisher(
                    "2.1.3.0",
                    "test-signing-key",
                    new VoxStellarPublisherOptions
                    {
                        Client = client,
                        OutboxDirectory = storage,
                        TimeProvider = clock,
                        BatchInterval = TimeSpan.Zero,
                        Log = CaptureLog,
                    }
                ),
            };
        }

        /// <summary>Injects the actual sender rather than replacing projection decisions with a fake.</summary>
        public void Configure(MainWindowViewModelTestBuilder builder)
        {
            switch (publisher)
            {
                case InaraPublisher inara:
                    builder.WithInaraPublisher(inara);
                    break;
                case EdsmPublisher edsm:
                    builder.WithEdsmPublisher(edsm);
                    break;
                case EdAstroPublisher astro:
                    builder.WithEdAstroPublisher(astro);
                    break;
                case EddnCommanderPublisher eddn:
                    builder.WithEddnPublisher(eddn);
                    break;
                case VoxStellarPublisher vox:
                    builder.WithVoxStellarPublisher(vox);
                    break;
            }
        }

        /// <summary>Advances time beyond persisted cooldowns without changing captured journal timestamps.</summary>
        public void Advance(TimeSpan duration) => clock.Now += duration;

        /// <summary>Advances real sender deadlines and waits for their bounded flush paths.</summary>
        public async Task FlushAsync(bool expectVoxUpload = false)
        {
            clock.Now += TimeSpan.FromHours(3);
            switch (publisher)
            {
                case InaraPublisher inara:
                    await inara.FlushAsync();
                    break;
                case EdsmPublisher edsm:
                    await edsm.FlushAsync();
                    break;
                case EdAstroPublisher astro:
                    await astro.FlushAsync();
                    clock.Now += TimeSpan.FromSeconds(10);
                    await astro.FlushAsync();
                    break;
                case EddnCommanderPublisher eddn:
                    await eddn.FlushAsync();
                    break;
                case VoxStellarPublisher:
                    if (expectVoxUpload)
                    {
                        await firstVoxBatch.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    await Task.Delay(TimeSpan.FromMilliseconds(50));
                    break;
            }
        }

        /// <summary>Observes a completed webhook batch without stopping the sender before duplicate checks.</summary>
        private void CaptureLog(string message)
        {
            logs.Enqueue(message);
            if (message.StartsWith("VoxStellar upload batch:", StringComparison.Ordinal))
            {
                firstVoxBatch.TrySetResult();
            }
        }

        /// <summary>Closes provider-owned queue leases before removing the scenario folders.</summary>
        public void Dispose()
        {
            publisher.Dispose();
            client.Dispose();
        }
    }

    /// <summary>Advances retry deadlines independently of original journal evidence.</summary>
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>Provides the test sender's wall clock.</summary>
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Captures complete provider wire bodies and acknowledges exactly the events submitted.</summary>
    private sealed class ProviderHandler(string provider) : HttpMessageHandler
    {
        public ConcurrentQueue<string> Posts { get; } = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        /// <summary>Serves provider policy and decodes transport envelopes without contacting a real service.</summary>
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
                        provider == "EDAstro" ? "[\"Docked\"]" : "[\"Fileheader\",\"Shutdown\"]"
                    ),
                };
            }
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (provider == "EDSM")
            {
                body = Uri.UnescapeDataString(body.Replace('+', ' '));
            }
            if (provider == "EDDN")
            {
                using var bytes = new MemoryStream(await request.Content.ReadAsByteArrayAsync(cancellationToken));
                using var gzip = new GZipStream(bytes, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip);
                body = await reader.ReadToEndAsync(cancellationToken);
            }
            Posts.Enqueue(body);
            string response = "ok";
            if (provider is "Inara" or "EDSM")
            {
                int count =
                    provider == "Inara"
                        ? JsonNode.Parse(body)!["events"]!.AsArray().Count
                        : JsonNode
                            .Parse(body[(body.IndexOf("message=", StringComparison.Ordinal) + 8)..])!
                            .AsArray()
                            .Count;
                response =
                    provider == "Inara"
                        ? "{\"header\":{\"eventStatus\":200},\"events\":["
                            + string.Join(',', Enumerable.Repeat("{\"eventStatus\":200}", count))
                            + "]}"
                        : "{\"msgnum\":100,\"events\":["
                            + string.Join(',', Enumerable.Repeat("{\"msgnum\":100}", count))
                            + "]}";
            }
            if (provider == "EDAstro")
            {
                response = "{\"status\":200}";
            }
            return new(Status) { Content = new StringContent(response) };
        }
    }
}
