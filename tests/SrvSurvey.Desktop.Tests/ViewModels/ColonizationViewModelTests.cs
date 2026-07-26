using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationViewModelTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-colonization-view-model-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DoesNotFetchWithoutExplicitConsent()
    {
        var client = new StubRavenColonialClient();
        var viewModel = Create(client);

        await viewModel.SetCommanderAsync("Test Cmdr");

        Assert.False(viewModel.IsEnabled);
        Assert.Equal(0, client.LoadCount);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.Contains("off", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LoadsProjectsAndCalculatesSelectedCargoTrips()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new ColonizationCommanderProjects(
            [
                Project("shown", "Port", remaining: 300),
                Project("hidden", "Hub", remaining: 100),
            ],
            ["hidden"],
            "shown",
            []),
        };
        var viewModel = Create(client);
        viewModel.IsEnabled = true;
        viewModel.ApplyJournalEvents(
            [Event("Loadout", "\"CargoCapacity\":128")]);

        await viewModel.SetCommanderAsync("Test Cmdr");

        Assert.Equal(1, client.LoadCount);
        Assert.Equal(2, viewModel.Projects.Count);
        Assert.True(viewModel.Projects.Single(row =>
            row.Project.BuildId == "shown").IsPrimary);
        Assert.False(viewModel.Projects.Single(row =>
            row.Project.BuildId == "hidden").IsShown);
        Assert.Equal(
            "Cargo required: 300 | 3 trips in current ship",
            viewModel.ProjectSummary);
    }

    [Fact]
    public async Task SavesProjectSelectionOnlyOnExplicitSave()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new ColonizationCommanderProjects(
                [Project("build-1", "Port", remaining: 100)],
                [],
                null,
                []),
        };
        var viewModel = Create(client);
        viewModel.IsEnabled = true;
        await viewModel.SetCommanderAsync("Test Cmdr");

        viewModel.Projects[0].IsShown = false;

        Assert.True(viewModel.HasUnsavedProjectVisibility);
        Assert.Equal(0, client.SaveCount);

        await viewModel.SaveProjectVisibilityAsync();

        Assert.Equal(1, client.SaveCount);
        Assert.Equal(["build-1"], client.LastSavedHiddenIds);
        Assert.False(viewModel.HasUnsavedProjectVisibility);
    }

    [Fact]
    public void ProjectsLiveConstructionDepotIntoResourceRows()
    {
        var viewModel = Create(new StubRavenColonialClient());

        viewModel.ApplyJournalEvents(
        [
            Event(
                "Docked",
                """
                "MarketID":10,"SystemAddress":20,"StarSystem":"Test",
                "StationName":"Orbital Construction Site: Hope",
                "StationServices":["colonisationcontribution"]
                """),
            Event(
                "ColonisationConstructionDepot",
                """
                "MarketID":10,"ConstructionProgress":0.25,
                "ResourcesRequired":[
                  {"Name":"$steel_name;","Name_Localised":"Steel","RequiredAmount":100,"ProvidedAmount":25,"Payment":5000},
                  {"Name":"$water_name;","Name_Localised":"Water","RequiredAmount":10,"ProvidedAmount":9,"Payment":600}
                ]
                """),
        ]);

        Assert.Equal(
            "Orbital Construction Site: Hope",
            viewModel.ConstructionTitle);
        Assert.Equal(2, viewModel.ConstructionResources.Count);
        Assert.Equal("Steel", viewModel.ConstructionResources[0].Name);
        Assert.Equal("75 remaining",
            viewModel.ConstructionResources[0].RemainingText);
        Assert.Contains("76 cargo remaining", viewModel.ConstructionStatus);
    }

    [Fact]
    public async Task FeedsConsentedLiveContextIntoProjectEditor()
    {
        var viewModel = Create(new StubRavenColonialClient());
        viewModel.IsEnabled = true;
        await viewModel.SetCommanderAsync("Test Cmdr");
        viewModel.UpdateSystemContext(
            "Test",
            new GalacticCoordinate(1, 2, 3));

        viewModel.ApplyJournalEvents(
        [
            Event(
                "Docked",
                """
                "MarketID":10,"SystemAddress":20,"StarSystem":"Test",
                "StationName":"Orbital Construction Site: Hope",
                "StationServices":["colonisationcontribution"]
                """),
            Event(
                "ColonisationConstructionDepot",
                """
                "MarketID":10,"ConstructionProgress":0.25,
                "ResourcesRequired":[
                  {"Name":"$steel_name;","Name_Localised":"Steel","RequiredAmount":100,"ProvidedAmount":25,"Payment":5000}
                ]
                """),
        ]);

        Assert.True(viewModel.ProjectEditor.CanPrepare);
        Assert.True(viewModel.ProjectEditor.PrepareCommand.CanExecute(null));
    }

    [Fact]
    public async Task FeedsConsentedCommanderAndAddressIntoSystemEditor()
    {
        var viewModel = Create(new StubRavenColonialClient());
        viewModel.IsEnabled = true;
        await viewModel.SetCommanderAsync("Test Cmdr");
        viewModel.SetCommanderProfile(
            "F123",
            isOdyssey: true,
            apiKey: "secret");
        viewModel.UpdateSystemContext(
            "Test System",
            new GalacticCoordinate(1, 2, 3),
            systemAddress: 42);

        Assert.True(viewModel.SystemEditor.CanLoad);
        Assert.True(viewModel.SystemEditor.LoadCommand.CanExecute(null));
        Assert.Equal("Test System", viewModel.SystemEditor.SystemTitle);
    }

    [Fact]
    public async Task RefreshFailureKeepsExistingProjectRows()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new ColonizationCommanderProjects(
                [Project("build-1", "Port", remaining: 100)],
                [],
                null,
                []),
        };
        var viewModel = Create(client);
        viewModel.IsEnabled = true;
        await viewModel.SetCommanderAsync("Test Cmdr");
        client.Failure = new HttpRequestException("offline");

        await viewModel.RefreshAsync();

        Assert.Single(viewModel.Projects);
        Assert.Contains("offline", viewModel.StatusMessage);
    }

    [Fact]
    public async Task OfflineFirstRunKeepsImportedColonizationCache()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "F123-colony.json"),
            """
            {
              "cmdr": "Test Cmdr",
              "primaryBuildId": "cached-build",
              "hiddenIDs": [],
              "projects": [
                {
                  "buildId": "cached-build",
                  "buildType": "no_truss",
                  "buildName": "Cached port",
                  "systemName": "Cached System",
                  "maxNeed": 1000,
                  "sumNeed": 300,
                  "commodities": {"steel": 300}
                }
              ],
              "linkedFCs": {}
            }
            """);
        var client = new StubRavenColonialClient
        {
            Failure = new HttpRequestException("offline"),
        };
        var viewModel = new ColonizationViewModel(
            new ColonizationSettingsStore(Path.Combine(directory, "ui.json")),
            client,
            ColonizationBuildCatalog.LoadEmbedded(),
            new CommanderProfileStore(directory),
            new LegacyColonizationProfileStore(directory));
        viewModel.IsEnabled = true;
        viewModel.SetCommanderProfile("F123", true, apiKey: null);

        await viewModel.SetCommanderAsync("Test Cmdr");

        var project = Assert.Single(viewModel.Projects);
        Assert.Equal("cached-build", project.Project.BuildId);
        Assert.True(project.IsPrimary);
        Assert.Equal("Cargo required: 300", viewModel.ProjectSummary);
        Assert.Contains("offline", viewModel.StatusMessage);
        Assert.Equal(1, client.LoadCount);
    }

    [Fact]
    public async Task FeedsProjectsCarriersAndShipCargoIntoOverlay()
    {
        var project = Project("build-1", "Port", remaining: 100) with
        {
            Commodities = new Dictionary<string, int> { ["steel"] = 100 },
            LinkedFleetCarriers =
            [
                new ColonizationProjectFleetCarrier { MarketId = 42 },
            ],
        };
        var client = new StubRavenColonialClient
        {
            Workspace = new ColonizationCommanderProjects(
                [project],
                [],
                null,
                [
                    new ColonizationFleetCarrier
                    {
                        MarketId = 42,
                        Name = "ABC-123",
                        Cargo = new Dictionary<string, int>
                        {
                            ["steel"] = 60,
                        },
                    },
                ]),
        };
        var viewModel = Create(client);
        viewModel.IsEnabled = true;

        await viewModel.SetCommanderAsync("Test Cmdr");
        await viewModel.UpdateCargoAsync(new CargoSnapshot(
            DateTimeOffset.UtcNow,
            "Cargo",
            "Ship",
            25,
            [new CargoItem("steel", "Steel", 25, 0)]));

        var row = Assert.Single(viewModel.CommodityOverlay.Plan.Rows);
        Assert.Equal(25, row.InShip);
        Assert.Equal(60, row.OnFleetCarriers);
    }

    [Fact]
    public async Task PublishesOptedInShipCargoForVisibleProjects()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new ColonizationCommanderProjects(
                [Project("build-1", "Port", remaining: 100)],
                [],
                null,
                []),
        };
        var viewModel = Create(client);
        viewModel.IsEnabled = true;
        viewModel.ShipCargoPublishingEnabled = true;
        viewModel.SetCommanderProfile("F123", true, "secret-key");
        viewModel.ApplyJournalEvents(
        [
            Event(
                "Loadout",
                "\"Ship\":\"python\",\"ShipName\":\"Raven One\",\"CargoCapacity\":192"),
        ]);
        await viewModel.SetCommanderAsync("Test Cmdr");

        await viewModel.UpdateCargoAsync(new CargoSnapshot(
            DateTimeOffset.UtcNow,
            "Cargo",
            "Ship",
            27,
            [new CargoItem("steel", "Steel", 27, 0)]));

        Assert.Equal(1, client.PublishShipCount);
        var ship = Assert.IsType<ColonizationCurrentShip>(
            client.LastPublishedShip);
        Assert.Equal("Test Cmdr", ship.CommanderName);
        Assert.Equal("Raven One", ship.Name);
        Assert.Equal("python", ship.Type);
        Assert.Equal(192, ship.MaximumCargo);
        Assert.Equal(27, ship.Cargo["steel"]);
        Assert.Contains("Published", viewModel.ShipCargoPublishingStatus);
    }

    [Fact]
    public async Task DoesNotPublishShipCargoWithoutOptInOrVisibleProjects()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new ColonizationCommanderProjects(
                [Project("hidden", "Port", remaining: 100)],
                ["hidden"],
                null,
                []),
        };
        var viewModel = Create(client);
        viewModel.IsEnabled = true;
        viewModel.SetCommanderProfile("F123", true, "secret-key");
        viewModel.ApplyJournalEvents(
        [
            Event(
                "Loadout",
                "\"Ship\":\"python\",\"CargoCapacity\":192"),
        ]);
        await viewModel.SetCommanderAsync("Test Cmdr");
        var cargo = new CargoSnapshot(
            DateTimeOffset.UtcNow,
            "Cargo",
            "Ship",
            1,
            [new CargoItem("steel", "Steel", 1, 0)]);

        await viewModel.UpdateCargoAsync(cargo);
        Assert.Equal(0, client.PublishShipCount);

        viewModel.ShipCargoPublishingEnabled = true;
        await viewModel.UpdateCargoAsync(cargo with
        {
            Timestamp = cargo.Timestamp.AddSeconds(1),
        });

        Assert.Equal(0, client.PublishShipCount);
        Assert.Contains("no visible", viewModel.ShipCargoPublishingStatus);
    }

    [Fact]
    public async Task FeedsPostDockMarketStockIntoOverlay()
    {
        var project = Project("build-1", "Port", remaining: 100) with
        {
            Commodities = new Dictionary<string, int> { ["steel"] = 100 },
            LinkedFleetCarriers =
            [
                new ColonizationProjectFleetCarrier { MarketId = 42 },
            ],
        };
        var client = new StubRavenColonialClient
        {
            Workspace = new ColonizationCommanderProjects(
                [project],
                [],
                null,
                [
                    new ColonizationFleetCarrier
                    {
                        MarketId = 42,
                        Cargo = new Dictionary<string, int>
                        {
                            ["steel"] = 80,
                        },
                    },
                ]),
        };
        var viewModel = Create(client);
        viewModel.IsEnabled = true;
        await viewModel.SetCommanderAsync("Test Cmdr");
        viewModel.ApplyJournalEvents(
        [
            Event(
                "Loadout",
                "\"CargoCapacity\":64"),
            Event(
                "Docked",
                """
                "MarketID":900,"SystemAddress":20,"StarSystem":"Test",
                "StationName":"Supply Station","StationServices":["commodities"]
                """),
        ]);
        await viewModel.UpdateMarketAsync(new MarketSnapshot(
            DateTimeOffset.Parse("2026-07-24T12:00:01Z"),
            "Market",
            900,
            "Supply Station",
            "Coriolis",
            string.Empty,
            "Test",
            [
                new MarketItem(
                    1,
                    "$Steel_Name;",
                    "Steel",
                    "$MARKET_category_metals;",
                    "Metals",
                    1,
                    1,
                    1,
                    1,
                    0,
                    50,
                    0,
                    true,
                    false,
                    false),
            ]));

        var row = Assert.Single(viewModel.CommodityOverlay.Plan.Rows);
        Assert.True(row.IsAvailableAtCurrentMarket);
        Assert.True(row.CanCompleteFleetCarrierLoad);
    }

    [Fact]
    public async Task SavesCommanderKeyWithoutExposingItInStatus()
    {
        var client = new StubRavenColonialClient();
        var viewModel = Create(client);
        viewModel.SetCommanderProfile("F123", isOdyssey: true, apiKey: null);
        viewModel.RavenApiKey = "secret-key";

        await viewModel.SaveRavenApiKeyAsync();

        var store = new CommanderProfileStore(directory);
        var profile = await store.LoadAsync("F123", isOdyssey: true);
        Assert.Equal("secret-key", profile.Data?.RavenColonialApiKey);
        Assert.True(viewModel.HasStoredRavenApiKey);
        Assert.DoesNotContain("secret-key", viewModel.RavenCredentialStatus);
    }

    [Fact]
    public async Task SyncsLinkedCarrierOnlyAfterExplicitOptIn()
    {
        var project = Project("build-1", "Port", remaining: 100) with
        {
            Commodities = new Dictionary<string, int> { ["steel"] = 100 },
            LinkedFleetCarriers =
            [
                new ColonizationProjectFleetCarrier { MarketId = 42 },
            ],
        };
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Name = "ABC-123",
            DisplayName = "Supply carrier",
            Cargo = new Dictionary<string, int> { ["steel"] = 75 },
        };
        var client = new StubRavenColonialClient
        {
            Workspace = new ColonizationCommanderProjects(
                [project],
                [],
                null,
                [carrier]),
            FleetCarrierResponse = carrier,
        };
        var viewModel = Create(client);
        viewModel.IsEnabled = true;
        await viewModel.SetCommanderAsync("Test Cmdr");
        viewModel.SetCommanderProfile(
            "F123",
            isOdyssey: true,
            apiKey: "secret-key");
        viewModel.ApplyJournalEvents(
        [
            Event(
                "Docked",
                """
                "MarketID":42,"SystemAddress":20,"StarSystem":"Test",
                "StationName":"Supply carrier ABC-123","StationType":"FleetCarrier",
                "StationServices":["commodities"]
                """),
        ]);
        var market = new MarketSnapshot(
            DateTimeOffset.Parse("2026-07-24T12:00:01Z"),
            "Market",
            42,
            "Supply carrier ABC-123",
            "FleetCarrier",
            "all",
            "Test",
            [
                new MarketItem(
                    1,
                    "$Steel_Name;",
                    "Steel",
                    "$MARKET_category_metals;",
                    "Metals",
                    1,
                    1,
                    1,
                    1,
                    0,
                    80,
                    0,
                    true,
                    false,
                    false),
            ]);

        await viewModel.UpdateMarketAsync(market);
        Assert.Equal(0, client.ReplaceCargoCount);

        viewModel.FleetCarrierCargoSyncEnabled = true;
        await viewModel.UpdateMarketAsync(market with
        {
            Timestamp = market.Timestamp.AddSeconds(1),
        });

        Assert.Equal(1, client.ReplaceCargoCount);
        Assert.Equal(80, client.LastReplacement?["steel"]);
        Assert.Contains("Updated 1 cargo", viewModel.FleetCarrierSyncStatus);
        Assert.False(viewModel.CommodityOverlay.HasPendingCargo);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private ColonizationViewModel Create(StubRavenColonialClient client)
    {
        return new ColonizationViewModel(
            new ColonizationSettingsStore(
                Path.Combine(directory, "ui.json")),
            client,
            ColonizationBuildCatalog.LoadEmbedded(),
            new CommanderProfileStore(directory));
    }

    private static ColonizationProject Project(
        string id,
        string name,
        int remaining)
    {
        return new ColonizationProject
        {
            BuildId = id,
            BuildType = "no_truss",
            BuildName = name,
            SystemName = "Test System",
            MaximumRequired = 1_000,
            RemainingRequired = remaining,
        };
    }

    private static JournalEventEnvelope Event(
        string eventName,
        string properties)
    {
        var json = $$"""
            {"timestamp":"2026-07-24T12:00:00Z","event":"{{eventName}}",{{properties}}}
            """;
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var result, out var error),
            error);
        return result!;
    }

    private sealed class StubRavenColonialClient : IRavenColonialClient
    {
        public ColonizationCommanderProjects Workspace { get; set; } = new(
            [],
            [],
            null,
            []);

        public Exception? Failure { get; set; }

        public int LoadCount { get; private set; }

        public int SaveCount { get; private set; }

        public int ReplaceCargoCount { get; private set; }

        public int PublishShipCount { get; private set; }

        public ColonizationCurrentShip? LastPublishedShip { get; private set; }

        public ColonizationFleetCarrier? FleetCarrierResponse { get; set; }

        public IReadOnlyDictionary<string, int>? LastReplacement
        {
            get;
            private set;
        }

        public IReadOnlyList<string> LastSavedHiddenIds { get; private set; } =
            [];

        public Task<ColonizationCommanderProjects> GetCommanderProjectsAsync(
            string commanderName,
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Failure is null
                ? Task.FromResult(Workspace)
                : Task.FromException<ColonizationCommanderProjects>(Failure);
        }

        public Task<IReadOnlyList<string>> SaveHiddenProjectIdsAsync(
            string commanderName,
            IEnumerable<string> hiddenProjectIds,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            LastSavedHiddenIds = hiddenProjectIds.ToArray();
            return Task.FromResult(LastSavedHiddenIds);
        }

        public Task<ColonizationProject?> GetProjectAsync(
            string buildId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ColonizationProject?>(null);
        }

        public Task<IReadOnlyList<ColonizationSystemSite>> GetSystemSitesAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ColonizationSystemSite>>([]);
        }

        public Task<string?> GetSystemArchitectAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<ColonizationSystemRecord> GetSystemAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ColonizationSystemRecord> ImportSystemBodiesAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ColonizationSystemRecord> UpdateSystemSitesAsync(
            string systemNameOrAddress,
            ColonizationSystemSiteUpdate update,
            string apiKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ColonizationProject?> CreateProjectAsync(
            ColonizationProjectCreate project,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ColonizationProject?>(null);
        }

        public Task<ColonizationFleetCarrier?> GetFleetCarrierAsync(
            long marketId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(FleetCarrierResponse);
        }

        public Task<IReadOnlyDictionary<string, int>>
            ReplaceFleetCarrierCargoAsync(
                long marketId,
                IReadOnlyDictionary<string, int> cargo,
                string apiKey,
                CancellationToken cancellationToken = default)
        {
            ReplaceCargoCount++;
            LastReplacement = cargo;
            var updated = new Dictionary<string, int>(
                FleetCarrierResponse?.Cargo
                    ?? new Dictionary<string, int>(),
                StringComparer.OrdinalIgnoreCase);
            foreach (var pair in cargo)
            {
                updated[pair.Key] = pair.Value;
            }

            return Task.FromResult<IReadOnlyDictionary<string, int>>(updated);
        }

        public Task<IReadOnlyDictionary<string, int>>
            AdjustFleetCarrierCargoAsync(
                long marketId,
                IReadOnlyDictionary<string, int> cargoChanges,
                string apiKey,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(cargoChanges);
        }

        public Task PublishCurrentShipAsync(
            ColonizationCurrentShip ship,
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            PublishShipCount++;
            LastPublishedShip = ship;
            return Task.CompletedTask;
        }
    }
}
