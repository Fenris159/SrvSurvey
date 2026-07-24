using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
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
        viewModel.UpdateCargo(new CargoSnapshot(
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
        viewModel.UpdateMarket(new MarketSnapshot(
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
            ColonizationBuildCatalog.LoadEmbedded());
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
            return Task.FromResult<ColonizationFleetCarrier?>(null);
        }

        public Task<IReadOnlyDictionary<string, int>>
            ReplaceFleetCarrierCargoAsync(
                long marketId,
                IReadOnlyDictionary<string, int> cargo,
                string apiKey,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(cargo);
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
    }
}
