using System.Net;
using System.Net.Http.Json;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class RavenMultiCommanderCargoTests
{
    [Fact]
    public async Task MissingCargoProfileDoesNotSuppressAttributedConstructionContributions()
    {
        string root = Path.Combine(Path.GetTempPath(), "SrvSurvey-raven-no-profile-" + Guid.NewGuid().ToString("N"));
        var paths = new AppDataPaths(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "cache"),
            []
        );
        using var handler = new RavenHandler();
        using var network = new HttpClient(handler);
        try
        {
            string folder = await CreateJournalAsync(root, "first", "F1", "First", 10);
            using MainWindowViewModel model = Create(folder, "F1", paths, network);
            await model.RefreshAsync();
            model.Colonization.SetCommanderProfile(null, true, null);
            await DeliverAsync(folder, "F1", 3, 7);

            await model.RefreshAsync();

            Assert.Equal(("First", 3), Assert.Single(handler.Deliveries));
            Assert.Empty(handler.Ships);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    /// <summary>Two live clients report only the cargo and credentials belonging to their bound commander folder.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeparateFoldersPublishRavenCargoWithoutEnablingOtherSharedConsumers(bool sharedFolder)
    {
        string root = Path.Combine(Path.GetTempPath(), "SrvSurvey-raven-multi-" + Guid.NewGuid().ToString("N"));
        var paths = new AppDataPaths(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "cache"),
            []
        );
        var handler = new RavenHandler();
        using var network = new HttpClient(handler);
        try
        {
            string firstFolder = await CreateJournalAsync(root, "first", "F1", "First", 10);
            string secondFolder = await CreateJournalAsync(root, sharedFolder ? "first" : "second", "F2", "Second", 30);
            var profiles = new CommanderProfileStore(paths.DataDirectory);
            await profiles.SaveRavenColonialApiKeyAsync("F1", "First", true, "key-first");
            await profiles.SaveRavenColonialApiKeyAsync("F2", "Second", true, "key-second");
            using MainWindowViewModel first = Create(firstFolder, "F1", paths, network);
            using MainWindowViewModel second = Create(secondFolder, "F2", paths, network);
            await first.RefreshAsync();
            await second.RefreshAsync();
            if (sharedFolder)
            {
                Assert.True(first.Colonization.SharedCargoSuppressed);
                Assert.True(second.Colonization.SharedCargoSuppressed);
                Assert.Empty(handler.Ships);
                return;
            }
            Assert.Empty(handler.Ships);
            await WriteCargoAsync(firstFolder, 11, 3);
            await WriteCargoAsync(secondFolder, 31, 3);
            await first.RefreshAsync();
            await second.RefreshAsync();
            Assert.False(first.Colonization.SharedCargoSuppressed);
            Assert.False(second.Colonization.SharedCargoSuppressed);
            Assert.True(first.IsSharedCargoSuppressed);
            Assert.True(second.IsSharedCargoSuppressed);
            Assert.Null(first.CurrentCargo);
            Assert.Null(second.CurrentCargo);
            Assert.True(first.DockToDock.SharedCargoSuppressed);
            Assert.True(second.DockToDock.SharedCargoSuppressed);
            Assert.Contains(
                handler.Ships,
                entry =>
                    entry.Key == "key-first"
                    && entry.Ship.CommanderName == "First"
                    && entry.Ship.Cargo.GetValueOrDefault("steel") == 11
            );
            Assert.Contains(
                handler.Ships,
                entry =>
                    entry.Key == "key-second"
                    && entry.Ship.CommanderName == "Second"
                    && entry.Ship.Cargo.GetValueOrDefault("steel") == 31
            );
            Assert.All(
                handler.Ships,
                entry => Assert.Equal(entry.Key == "key-first" ? "First" : "Second", entry.Ship.CommanderName)
            );
            await DeliverAsync(firstFolder, "F1", 3, 8);
            await first.RefreshAsync();
            await DeliverAsync(secondFolder, "F2", 7, 24);
            await second.RefreshAsync();
            Assert.Collection(
                handler.Deliveries,
                entry =>
                {
                    Assert.Equal("First", entry.Commander);
                    Assert.Equal(3, entry.Steel);
                },
                entry =>
                {
                    Assert.Equal("Second", entry.Commander);
                    Assert.Equal(7, entry.Steel);
                }
            );
            await first.RefreshAsync();
            await second.RefreshAsync();
            Assert.Equal(2, handler.Deliveries.Count);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Theory]
    [InlineData("Shutdown")]
    [InlineData("Music")]
    public async Task ShutdownReleasesSourceBeforeAnotherCommanderReusesItsFolder(string endEvent)
    {
        string root = Path.Combine(Path.GetTempPath(), "SrvSurvey-raven-shutdown-" + Guid.NewGuid().ToString("N"));
        var paths = new AppDataPaths(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "cache"),
            []
        );
        using var handler = new RavenHandler();
        using var network = new HttpClient(handler);
        try
        {
            string firstFolder = await CreateJournalAsync(root, "first", "F1", "First", 10);
            string secondFolder = await CreateJournalAsync(root, "second", "F2", "Second", 30);
            var profiles = new CommanderProfileStore(paths.DataDirectory);
            await profiles.SaveRavenColonialApiKeyAsync("F1", "First", true, "key-first");
            await profiles.SaveRavenColonialApiKeyAsync("F2", "Second", true, "key-second");
            using MainWindowViewModel first = Create(firstFolder, "F1", paths, network);
            using MainWindowViewModel second = Create(secondFolder, "F2", paths, network);
            await first.RefreshAsync();
            await second.RefreshAsync();
            await WriteCargoAsync(firstFolder, 11, 3);
            await first.RefreshAsync();
            Assert.Contains(
                handler.Ships,
                entry => entry.Key == "key-first" && entry.Ship.Cargo.GetValueOrDefault("steel") == 11
            );
            await File.AppendAllTextAsync(
                Path.Combine(firstFolder, "Journal.F1.log"),
                (
                    endEvent == "Music"
                        ? """{"timestamp":"2026-10-14T12:00:04Z","event":"Music","MusicTrack":"MainMenu"}"""
                        : """{"timestamp":"2026-10-14T12:00:04Z","event":"Shutdown"}"""
                ) + "\n"
            );
            await first.RefreshAsync();
            handler.Ships.Clear();

            await CreateJournalAsync(root, "first", "F3", "Foreign", 90);
            await WriteCargoAsync(firstFolder, 91, 6);
            await first.RefreshAsync();
            await WriteCargoAsync(secondFolder, 31, 6);
            await second.RefreshAsync();

            Assert.True(first.Colonization.SharedCargoSuppressed);
            Assert.DoesNotContain(handler.Ships, entry => entry.Key == "key-first");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    /// <summary>Creates a pinned offline application instance with a simulated two-game desktop.</summary>
    private static MainWindowViewModel Create(string folder, string fid, AppDataPaths paths, HttpClient network)
    {
        MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(
            folder,
            builder =>
                builder
                    .WithAppDataPaths(paths with { SettingsFrontierId = fid })
                    .WithTargetFrontierId(fid)
                    .WithExternalNetworkClient(network)
                    .WithGameWindowSwitcher(new TwoGames())
        );
        model.Colonization.IsEnabled = true;
        model.Colonization.ShipCargoPublishingEnabled = true;
        return model;
    }

    /// <summary>Creates a distinct journal identity, ship loadout and cargo source.</summary>
    private static async Task<string> CreateJournalAsync(
        string root,
        string folder,
        string fid,
        string commander,
        int cargo
    )
    {
        string path = Path.Combine(root, folder);
        Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(
            Path.Combine(path, $"Journal.{fid}.log"),
            $$"""
            {"timestamp":"2026-10-14T12:00:00Z","event":"Commander","Name":"{{commander}}","FID":"{{fid}}"}
            {"timestamp":"2026-10-14T12:00:01Z","event":"LoadGame","Commander":"{{commander}}","FID":"{{fid}}","Odyssey":true,"Ship":"Python"}
            {"timestamp":"2026-10-14T12:00:01Z","event":"Loadout","Ship":"Python","CargoCapacity":192}

            """
        );
        await WriteCargoAsync(path, cargo, 2);
        return path;
    }

    /// <summary>Publishes a fresh full companion snapshot in a commander's physical journal folder.</summary>
    private static Task WriteCargoAsync(string folder, int cargo, int second) =>
        File.WriteAllTextAsync(
            Path.Combine(folder, "Cargo.json"),
            $$"""{"timestamp":"2026-10-14T12:00:0{{second}}Z","event":"Cargo","Vessel":"Ship","Count":{{cargo}},"Inventory":[{"Name":"steel","Count":{{cargo}}}]}"""
        );

    /// <summary>Simulates one commander's docking and contribution while the other client's journal stays unchanged.</summary>
    private static async Task DeliverAsync(string folder, string fid, int amount, int remainingCargo)
    {
        await File.AppendAllTextAsync(
            Path.Combine(folder, $"Journal.{fid}.log"),
            $$"""
            {"timestamp":"2026-10-14T12:00:04Z","event":"Docked","MarketID":42,"SystemAddress":1,"StarSystem":"Sol","StationName":"Orbital Construction Site: Port","StationServices":["colonisationcontribution"]}
            {"timestamp":"2026-10-14T12:00:05Z","event":"ColonisationContribution","MarketID":42,"Contributions":[{"Name":"$steel_name;","Amount":{{amount}}}]}

            """
        );
        await WriteCargoAsync(folder, remainingCargo, 6);
    }

    /// <summary>Returns two game windows without changing any provider's production detection behavior.</summary>
    private sealed class TwoGames : IGameWindowSwitcher
    {
        /// <summary>Provides the simulated two-client desktop count.</summary>
        public int GetAvailableWindowCount() => 2;

        /// <summary>Avoids activating any real game window.</summary>
        public bool TryActivateCurrent() => false;

        /// <summary>Avoids switching the user's real desktop focus.</summary>
        public bool TryActivateNext() => false;

        /// <summary>Requires no native resources to release.</summary>
        public void Dispose() { }
    }

    /// <summary>Captures real Raven request payloads while all other network traffic stays offline.</summary>
    private sealed class RavenHandler : HttpMessageHandler
    {
        public List<(string Key, ColonizationCurrentShip Ship)> Ships { get; } = [];
        public List<(string Commander, int Steel)> Deliveries { get; } = [];
        private ColonizationProject project = new()
        {
            BuildId = "shared",
            BuildName = "Port",
            SystemAddress = 1,
            MarketId = 42,
            SystemName = "Sol",
            MaximumRequired = 100,
            RemainingRequired = 100,
            Commodities = new Dictionary<string, int> { ["steel"] = 100 },
        };

        /// <summary>Records authenticated ship payloads and supplies only the Raven workspace responses needed by the scenario.</summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/api/project/shared/contribute/", StringComparison.Ordinal))
            {
                Dictionary<string, int> cargo = (
                    await request.Content!.ReadFromJsonAsync<Dictionary<string, int>>(cancellationToken)
                )!;
                Deliveries.Add((path.Split('/')[^1], cargo["steel"]));
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (path == "/api/project/shared" && request.Method == HttpMethod.Patch)
            {
                ColonizationProjectUpdate update = (
                    await request.Content!.ReadFromJsonAsync<ColonizationProjectUpdate>(cancellationToken)
                )!;
                if (update.Commodities is not null)
                {
                    project = project with
                    {
                        Commodities = update.Commodities.ToDictionary(),
                        RemainingRequired = update.Commodities.Values.Sum(),
                    };
                }
            }
            if (path is "/api/project/shared" or "/api/system/1/42")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(project) };
            }
            if (path == "/api/cmdr/currentShip")
            {
                ColonizationCurrentShip ship = (
                    await request.Content!.ReadFromJsonAsync<ColonizationCurrentShip>(cancellationToken)
                )!;
                Ships.Add((Assert.Single(request.Headers.GetValues("rcc-key")), ship));
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (path.EndsWith("/active", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { project }) };
            }
            if (path.EndsWith("/primary", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null") };
            }
            if (
                path.EndsWith("/hiddenIDs", StringComparison.Ordinal)
                || path.EndsWith("/fc/all", StringComparison.Ordinal)
            )
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
