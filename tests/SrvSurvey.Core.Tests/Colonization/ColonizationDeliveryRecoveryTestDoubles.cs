using System.Globalization;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class ColonizationDeliveryRecoveryTests
{
    private readonly InMemoryRecoveryStore store = new();
    private readonly RecordingObserver observer = new();
    private readonly List<TimeSpan> delays = [];
    private DateTimeOffset now = DateTimeOffset.UtcNow;

    /// <summary>Creates an enabled recovery module for an active commander with the client's workspace loaded.</summary>
    private ColonizationDeliveryRecovery Create(
        RecordingRavenClient client,
        string? apiKey = null,
        bool carrierSync = false,
        string commander = "Test Cmdr"
    )
    {
        var recovery = new ColonizationDeliveryRecovery(
            client,
            store,
            observer,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            () => now
        )
        {
            IsEnabled = true,
            FleetCarrierCargoSyncEnabled = carrierSync,
        };
        recovery.SetProfile("F123", true, apiKey);
        recovery.SetCommander(commander);
        LoadWorkspace(recovery, client);
        return recovery;
    }

    /// <summary>Installs the client's commander workspace as a successful project refresh would.</summary>
    private static void LoadWorkspace(ColonizationDeliveryRecovery recovery, RecordingRavenClient client)
    {
        recovery.ReplaceWorkspace(client.Workspace.Projects, client.Workspace.FleetCarriers);
    }

    /// <summary>Applies journal context and synchronizes the same batch as a live journal read.</summary>
    private static Task<IReadOnlyList<ColonizationDeliveryNotice>> ApplyAndSynchronizeAsync(
        ColonizationDeliveryRecovery recovery,
        params JournalEventEnvelope[] events
    )
    {
        recovery.ApplyJournalEvents(events, null);
        return recovery.SynchronizeLiveEventsAsync(events, allowPublishing: true);
    }

    /// <summary>Delivers a market snapshot and runs the automatic synchronization that follows it.</summary>
    private static async Task UpdateMarketAsync(ColonizationDeliveryRecovery recovery, MarketSnapshot market)
    {
        recovery.UpdateMarket(market);
        if (recovery.FleetCarrierCargoSyncEnabled)
        {
            await recovery.SyncFleetCarrierCargoAsync(force: false);
        }
    }

    private static void AssertNotice(
        IEnumerable<ColonizationDeliveryNotice> notices,
        ColonizationDeliveryNoticeKind kind
    ) => Assert.Contains(notices, notice => notice.Kind == kind);

    private static JournalEventEnvelope Event(string eventName, string properties)
    {
        string propertySuffix = string.IsNullOrWhiteSpace(properties) ? string.Empty : "," + properties;
        string json = $$"""
            {"timestamp":"2026-07-24T12:00:00Z","event":"{{eventName}}"{{propertySuffix}}}
            """;
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? result, out string? error), error);
        return result!;
    }

    /// <summary>Docks at the construction site for market 10 in system 20, optionally reporting its faction.</summary>
    private static JournalEventEnvelope ConstructionDock(string? faction = null) =>
        Event(
            "Docked",
            "\"MarketID\":10,\"SystemAddress\":20,\"StarSystem\":\"Test System\","
                + "\"StationName\":\"Orbital Construction Site: Hope\","
                + (faction is null ? string.Empty : $"\"StationFaction\":{{\"Name\":\"{faction}\"}},")
                + "\"StationServices\":[\"colonisationcontribution\"]"
        );

    /// <summary>Reports market 10's steel requirement, optionally with an explicit completion flag.</summary>
    private static JournalEventEnvelope Depot(int required, int provided, bool? complete = null)
    {
        string completion = complete switch
        {
            true => "\"ConstructionComplete\":true,",
            false => "\"ConstructionComplete\":false,",
            null => string.Empty,
        };
        string progress = complete is null ? "0.25" : "1";
        return Event(
            "ColonisationConstructionDepot",
            $"\"MarketID\":10,\"ConstructionProgress\":{progress},{completion}"
                + "\"ResourcesRequired\":[{\"Name\":\"$steel_name;\",\"Name_Localised\":\"Steel\","
                + $"\"RequiredAmount\":{required},\"ProvidedAmount\":{provided},\"Payment\":5000}}]"
        );
    }

    private static JournalEventEnvelope Contribution(int steel, long marketId = 10) =>
        Event(
            "ColonisationContribution",
            $"\"MarketID\":{marketId},\"Contributions\":[{{\"Name\":\"$steel_name;\",\"Amount\":{steel}}}]"
        );

    private static JournalEventEnvelope CarrierDock(bool squadron = false, string stationName = "ABC-123") =>
        Event(
            "Docked",
            $"\"MarketID\":42,\"SystemAddress\":20,\"StarSystem\":\"Test\",\"StationName\":\"{stationName}\",\"StationType\":\"FleetCarrier\",\"StationServices\":[\"commodities\"{(squadron ? ",\"squadronBank\"" : "")}]"
        );

    private static ColonizationProject Project(
        string id,
        string name,
        int remaining,
        long marketId = 0,
        long systemAddress = 0,
        string? factionName = null,
        string? architectName = null
    )
    {
        return new ColonizationProject
        {
            BuildId = id,
            BuildType = "no_truss",
            BuildName = name,
            SystemName = "Test System",
            MarketId = marketId,
            SystemAddress = systemAddress,
            FactionName = factionName,
            ArchitectName = architectName,
            MaximumRequired = 1_000,
            RemainingRequired = remaining,
            Commodities = new Dictionary<string, int> { ["steel"] = remaining },
        };
    }

    private static ColonizationFleetCarrier Carrier(int steel, string name = "ABC-123") =>
        new()
        {
            MarketId = 42,
            Name = name,
            Cargo = new Dictionary<string, int> { ["steel"] = steel },
        };

    /// <summary>Provides a linked carrier with matching local and server baselines.</summary>
    private static RecordingRavenClient CarrierClient(int steel = 100, string name = "ABC-123")
    {
        ColonizationFleetCarrier carrier = Carrier(steel, name);
        return new RecordingRavenClient { Workspace = new([], [], null, [carrier]), FleetCarrierResponse = carrier };
    }

    private static MarketSnapshot LinkedCarrierMarket(int stock, string stationName = "ABC-123")
    {
        return new MarketSnapshot(
            DateTimeOffset.Parse("2026-07-24T12:00:01Z", CultureInfo.InvariantCulture),
            "Market",
            42,
            stationName,
            "FleetCarrier",
            "all",
            "Test",
            [
                new MarketItem(
                    1,
                    "$Steel_Name;",
                    "Steel",
                    "$commodity_metals;",
                    "Metals",
                    0,
                    100,
                    100,
                    1,
                    0,
                    stock,
                    0,
                    true,
                    false,
                    false
                ),
            ]
        );
    }

    private static FrontierAccountSnapshot LinkedCarrierCapiSnapshot(bool isDocked, int steel)
    {
        var fetchedAt = DateTimeOffset.Parse("2026-09-18T12:00:00Z", CultureInfo.InvariantCulture);
        var carrier = new FrontierCarrierSnapshot(
            "ABC-123",
            "ABC-123",
            "Test",
            "NormalOperation",
            "All",
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            [],
            [new FrontierInventorySnapshot("Commodity", "Steel", steel, 0)],
            [],
            [],
            [],
            [],
            Market: new FrontierMarketSnapshot(42, "ABC-123", "FleetCarrier", [], [], [], [], [], [], fetchedAt)
        );
        return new FrontierAccountSnapshot(
            "Test Cmdr",
            0,
            0,
            isDocked,
            true,
            "Test",
            "ABC-123",
            null,
            [],
            [],
            [],
            carrier,
            fetchedAt,
            CarrierFetchedAt: fetchedAt
        );
    }

    private static CargoInventoryState ShipCargo(int steel)
    {
        var cargo = new CargoInventoryState();
        cargo.Reset(
            new CargoSnapshot(
                DateTimeOffset.Parse("2026-07-24T12:00:00Z", CultureInfo.InvariantCulture),
                "Cargo",
                "Ship",
                steel,
                steel == 0 ? [] : [new CargoItem("steel", "Steel", steel, 0)]
            )
        );
        return cargo;
    }

    /// <summary>Keeps recovery state between module instances, copying it as a persisted file would.</summary>
    private sealed class InMemoryRecoveryStore : IColonizationDeliveryRecoveryStore
    {
        private ColonizationPendingContribution[] contributions = [];
        private ColonizationPendingCargoAdjustment[] adjustments = [];
        private ColonizationBuildSiteRepairVisit[] visits = [];

        /// <summary>Makes the next saves fail as an unwritable recovery file would.</summary>
        public Exception? SaveFailure { get; set; }

        public IReadOnlyList<ColonizationPendingContribution> LoadPendingContributions() =>
            contributions.Select(Copy).ToArray();

        public void SavePendingContributions(IReadOnlyList<ColonizationPendingContribution> contributions)
        {
            ThrowIfSaveFails();
            this.contributions = contributions.Select(Copy).ToArray();
        }

        public IReadOnlyList<ColonizationPendingCargoAdjustment> LoadPendingCargoAdjustments() =>
            adjustments.Select(Copy).ToArray();

        public void SavePendingCargoAdjustments(IReadOnlyList<ColonizationPendingCargoAdjustment> adjustments)
        {
            ThrowIfSaveFails();
            this.adjustments = adjustments.Select(Copy).ToArray();
        }

        public IReadOnlyList<ColonizationBuildSiteRepairVisit> LoadBuildSiteRepairVisits() => visits;

        public void SaveBuildSiteRepairVisits(IEnumerable<ColonizationBuildSiteRepairVisit> visits)
        {
            ThrowIfSaveFails();
            this.visits = visits.ToArray();
        }

        private void ThrowIfSaveFails()
        {
            if (SaveFailure is not null)
            {
                throw SaveFailure;
            }
        }

        private static ColonizationPendingContribution Copy(ColonizationPendingContribution item) =>
            item with
            {
                Cargo = new Dictionary<string, int>(item.Cargo),
            };

        private static ColonizationPendingCargoAdjustment Copy(ColonizationPendingCargoAdjustment item) =>
            item with
            {
                Delta = new Dictionary<string, int>(item.Delta, StringComparer.OrdinalIgnoreCase),
                Before = item.Before is null
                    ? null
                    : new Dictionary<string, int>(item.Before, StringComparer.OrdinalIgnoreCase),
            };
    }

    /// <summary>Records every change the module reports to presentation.</summary>
    private sealed class RecordingObserver : IColonizationDeliveryObserver
    {
        public int ProjectChanges { get; private set; }

        public int FleetCarrierChanges { get; private set; }

        public int PendingContributionChanges { get; private set; }

        public int RepairWarningChanges { get; private set; }

        public int BodyChanges { get; private set; }

        public int DockingRefreshRequests { get; private set; }

        public List<string[]?> PendingCargo { get; } = [];

        public List<ColonizationDeliveryNotice> FleetCarrierStatuses { get; } = [];

        public List<ColonizationDeliveryNotice> Statuses { get; } = [];

        public List<bool> BusyChanges { get; } = [];

        public void ProjectsChanged() => ProjectChanges++;

        public void FleetCarriersChanged() => FleetCarrierChanges++;

        public void PendingFleetCarrierCargoChanged(IEnumerable<string>? commodities) =>
            PendingCargo.Add(commodities?.ToArray());

        public void FleetCarrierStatusChanged(ColonizationDeliveryNotice notice) => FleetCarrierStatuses.Add(notice);

        public void FleetCarrierSyncBusyChanged(bool busy) => BusyChanges.Add(busy);

        public void StatusChanged(ColonizationDeliveryNotice notice) => Statuses.Add(notice);

        public void PendingContributionsChanged() => PendingContributionChanges++;

        public void BuildSiteRepairWarningChanged() => RepairWarningChanges++;

        public void CurrentBodyChanged() => BodyChanges++;

        public void DockingRefreshRequested() => DockingRefreshRequests++;
    }

    /// <summary>Records Raven requests and supplies controllable responses and failures.</summary>
    private sealed class RecordingRavenClient : IRavenColonialClient
    {
        public ColonizationCommanderProjects Workspace { get; set; } = new([], [], null, []);

        /// <summary>Controls absolute cargo replacement while journal transactions arrive concurrently.</summary>
        public Func<
            IReadOnlyDictionary<string, int>,
            Task<IReadOnlyDictionary<string, int>>
        >? ReplaceCargo { get; set; }

        /// <summary>Injects ordered relative cargo failures to test retained writes and safe retries.</summary>
        public Queue<Exception> AdjustmentFailures { get; } = new();

        /// <summary>Injects contribution failures to distinguish definite rejection from uncertain credit.</summary>
        public Queue<Exception> ContributionFailures { get; } = new();

        /// <summary>Holds delivery acknowledgement while callers attempt reconciliation or switch profiles.</summary>
        public Task? ContributionGate { get; set; }

        /// <summary>Records cancellation forwarded from journal monitoring to the delivery request.</summary>
        public CancellationToken LastContributionCancellation { get; private set; }

        public int ReplaceCargoCount { get; private set; }

        public int GetFleetCarrierCount { get; private set; }

        public int PublishCarrierCount { get; private set; }

        public List<FleetCarrierAdjustmentCall> FleetCarrierAdjustments { get; } = [];

        public int SiteProjectLoadCount { get; private set; }

        public int SystemSiteLoadCount { get; private set; }

        public int MarkCompleteCount { get; private set; }

        public List<ColonizationProjectUpdate> ProjectUpdates { get; } = [];

        public List<ContributionCall> Contributions { get; } = [];

        public List<LinkCall> LinkRequests { get; } = [];

        public List<SystemUpdateCall> SystemUpdates { get; } = [];

        public List<SystemSitePatchCall> SystemSitePatches { get; } = [];

        public Queue<Exception> SystemSiteFailures { get; } = new();

        /// <summary>Records the systems queried during dock repair to detect retries leaking across contexts.</summary>
        public List<string> SystemSiteRequests { get; } = [];

        /// <summary>Delays site retrieval to test repair warnings arriving after the commander leaves the system.</summary>
        public Task<IReadOnlyList<ColonizationSystemSite>>? SystemSiteResponseTask { get; set; }

        /// <summary>Injects a site repair failure without altering unrelated project or cargo responses.</summary>
        public Exception? SystemSitePatchFailure { get; set; }

        /// <summary>Injects completion failures so tests can verify completion is retried without duplicate deliveries.</summary>
        public Queue<Exception> ProjectCompletionFailures { get; } = new();

        public IReadOnlyList<ColonizationSystemSite> SystemSitesResponse { get; set; } = [];

        public ColonizationFleetCarrier? FleetCarrierResponse { get; set; }

        public ColonizationFleetCarrierRegistration? LastCarrierRegistration { get; private set; }

        /// <summary>Makes carrier registration fail before any cargo is written.</summary>
        public Exception? PublishFailure { get; set; }

        public ColonizationProject? SiteProjectResponse { get; set; }

        /// <summary>Holds a project lookup so its result can arrive after the commander changes.</summary>
        public Task<ColonizationProject?>? SiteProjectResponseTask { get; set; }

        public IReadOnlyDictionary<string, int>? LastReplacement { get; private set; }

        public TaskCompletionSource<bool>? GateGetFleetCarrier { get; set; }

        public ManualResetEventSlim? EnteredGetFleetCarrier { get; set; }

        public Task<ColonizationCommanderProjects> GetCommanderProjectsAsync(
            string commanderName,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Workspace);

        public Task<string?> GetCommanderByApiKeyAsync(string apiKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> SaveHiddenProjectIdsAsync(
            string commanderName,
            IEnumerable<string> hiddenProjectIds,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<string>>(hiddenProjectIds.ToArray());

        public Task<ColonizationProject?> GetProjectAsync(
            string buildId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<ColonizationProject?>(null);

        public Task<ColonizationProject?> GetProjectAsync(
            long systemAddress,
            long marketId,
            CancellationToken cancellationToken = default
        )
        {
            SiteProjectLoadCount++;
            return SiteProjectResponseTask ?? Task.FromResult(SiteProjectResponse);
        }

        /// <summary>Holds project updates until released so a profile change can supersede them.</summary>
        public Task? UpdateProjectGate { get; set; }

        /// <summary>Applies supplied metadata and commodity fields to the fake project without altering omitted fields.</summary>
        public async Task<ColonizationProject> UpdateProjectAsync(
            ColonizationProjectUpdate update,
            CancellationToken cancellationToken = default
        )
        {
            if (UpdateProjectGate is { } gate)
            {
                await gate;
            }
            return ApplyProjectUpdate(update);
        }

        private ColonizationProject ApplyProjectUpdate(ColonizationProjectUpdate update)
        {
            ProjectUpdates.Add(update);
            ColonizationProject source =
                Workspace.Projects.FirstOrDefault(project =>
                    string.Equals(project.BuildId, update.BuildId, StringComparison.OrdinalIgnoreCase)
                )
                ?? (
                    SiteProjectResponse is { } site
                    && string.Equals(site.BuildId, update.BuildId, StringComparison.OrdinalIgnoreCase)
                        ? site
                        : null
                )
                ?? new ColonizationProject
                {
                    BuildId = update.BuildId,
                    BuildName = update.BuildId,
                    Commodities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                };
            Dictionary<string, int> commodities = new(source.Commodities, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, int> pair in update.Commodities ?? new Dictionary<string, int>())
            {
                commodities[pair.Key] = pair.Value;
            }

            int remaining = update.Commodities is null
                ? source.RemainingRequired
                : commodities.Values.Sum(value => Math.Max(0, value));
            ColonizationProject updated = source with
            {
                BodyNumber = update.BodyNumber ?? source.BodyNumber,
                BodyName = update.BodyName ?? source.BodyName,
                FactionName = update.FactionName ?? source.FactionName,
                MaximumRequired = update.MaximumRequired ?? source.MaximumRequired,
                RemainingRequired = remaining,
                Commodities = commodities,
            };
            if (Workspace.Projects.Any(project => project.BuildId == updated.BuildId))
            {
                Workspace = Workspace with
                {
                    Projects = Workspace
                        .Projects.Select(project => project.BuildId == updated.BuildId ? updated : project)
                        .ToArray(),
                };
            }

            if (
                SiteProjectResponse is not null
                && string.Equals(SiteProjectResponse.BuildId, updated.BuildId, StringComparison.OrdinalIgnoreCase)
            )
            {
                SiteProjectResponse = updated;
            }

            return updated;
        }

        public Task MarkProjectCompleteAsync(string buildId, CancellationToken cancellationToken = default)
        {
            MarkCompleteCount++;
            return ProjectCompletionFailures.TryDequeue(out Exception? failure)
                ? Task.FromException(failure)
                : Task.CompletedTask;
        }

        public async Task ContributeToProjectAsync(
            string buildId,
            string commanderName,
            IReadOnlyDictionary<string, int> contributions,
            CancellationToken cancellationToken = default
        )
        {
            LastContributionCancellation = cancellationToken;
            Contributions.Add(new ContributionCall(buildId, commanderName, contributions));
            if (ContributionGate is { } gate)
            {
                await gate;
            }
            if (ContributionFailures.TryDequeue(out Exception? failure))
            {
                throw failure;
            }
        }

        public Task SetPrimaryProjectAsync(
            string commanderName,
            string? buildId,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task LinkCommanderAsync(
            string buildId,
            string commanderName,
            CancellationToken cancellationToken = default
        )
        {
            LinkRequests.Add(new LinkCall(buildId, commanderName));
            return Task.CompletedTask;
        }

        public Task UnlinkCommanderAsync(
            string buildId,
            string commanderName,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task<IReadOnlyList<ColonizationSystemSite>> GetSystemSitesAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default
        )
        {
            SystemSiteLoadCount++;
            SystemSiteRequests.Add(systemNameOrAddress);
            return SystemSiteResponseTask
                ?? (
                    SystemSiteFailures.TryDequeue(out Exception? failure)
                        ? Task.FromException<IReadOnlyList<ColonizationSystemSite>>(failure)
                        : Task.FromResult(SystemSitesResponse)
                );
        }

        public Task<string?> GetSystemArchitectAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<string?>(null);

        public Task<ColonizationSystemRecord> GetSystemAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ColonizationSystemRecord> ImportSystemBodiesAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ColonizationSystemRecord> UpdateSystemSitesAsync(
            string systemNameOrAddress,
            ColonizationSystemSiteUpdate update,
            string apiKey,
            CancellationToken cancellationToken = default
        )
        {
            SystemUpdates.Add(new SystemUpdateCall(systemNameOrAddress, update, apiKey));
            return Task.FromResult(
                new ColonizationSystemRecord
                {
                    SystemAddress = 42,
                    Name = systemNameOrAddress,
                    Architect = update.Architect,
                    Sites = update.UpdatedSites,
                }
            );
        }

        public Task PatchSystemSiteAsync(
            string systemNameOrAddress,
            string siteId,
            ColonizationSystemSitePatch patch,
            string apiKey,
            CancellationToken cancellationToken = default
        )
        {
            SystemSitePatches.Add(new SystemSitePatchCall(systemNameOrAddress, siteId, patch, apiKey));
            return SystemSitePatchFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        }

        public Task<ColonizationProject?> CreateProjectAsync(
            ColonizationProjectCreate project,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<ColonizationProject?>(null);

        public async Task<ColonizationFleetCarrier?> GetFleetCarrierAsync(
            long marketId,
            CancellationToken cancellationToken = default
        )
        {
            GetFleetCarrierCount++;
            EnteredGetFleetCarrier?.Set();
            if (GateGetFleetCarrier is not null)
            {
                await GateGetFleetCarrier.Task.WaitAsync(cancellationToken);
            }

            return FleetCarrierResponse;
        }

        public Task<ColonizationFleetCarrier> PublishFleetCarrierAsync(
            ColonizationFleetCarrierRegistration carrier,
            string apiKey,
            CancellationToken cancellationToken = default
        )
        {
            PublishCarrierCount++;
            LastCarrierRegistration = carrier;
            if (PublishFailure is not null)
            {
                return Task.FromException<ColonizationFleetCarrier>(PublishFailure);
            }
            return Task.FromResult(
                FleetCarrierResponse
                    ?? new()
                    {
                        MarketId = carrier.MarketId,
                        Name = carrier.Name,
                        DisplayName = carrier.DisplayName,
                    }
            );
        }

        /// <summary>Returns a controllable replacement result to reproduce concurrent carrier baseline writes.</summary>
        public Task<IReadOnlyDictionary<string, int>> ReplaceFleetCarrierCargoAsync(
            long marketId,
            IReadOnlyDictionary<string, int> cargo,
            string apiKey,
            CancellationToken cancellationToken = default
        )
        {
            ReplaceCargoCount++;
            LastReplacement = cargo;
            if (ReplaceCargo is not null)
            {
                return ReplaceCargo(cargo);
            }
            var updated = new Dictionary<string, int>(
                FleetCarrierResponse?.Cargo ?? [],
                StringComparer.OrdinalIgnoreCase
            );
            foreach (KeyValuePair<string, int> pair in cargo)
            {
                updated[pair.Key] = pair.Value;
            }

            return Task.FromResult<IReadOnlyDictionary<string, int>>(updated);
        }

        /// <summary>Records relative cargo adjustments and injects transport or service failures for reconciliation tests.</summary>
        public Task<IReadOnlyDictionary<string, int>> AdjustFleetCarrierCargoAsync(
            long marketId,
            IReadOnlyDictionary<string, int> cargoChanges,
            string apiKey,
            CancellationToken cancellationToken = default
        )
        {
            FleetCarrierAdjustments.Add(
                new FleetCarrierAdjustmentCall(
                    marketId,
                    new Dictionary<string, int>(cargoChanges, StringComparer.OrdinalIgnoreCase)
                )
            );
            if (AdjustmentFailures.TryDequeue(out Exception? failure))
            {
                return Task.FromException<IReadOnlyDictionary<string, int>>(failure);
            }

            var updated = new Dictionary<string, int>(
                FleetCarrierResponse?.Cargo ?? [],
                StringComparer.OrdinalIgnoreCase
            );
            foreach (KeyValuePair<string, int> pair in cargoChanges)
            {
                updated[pair.Key] = Math.Max(0, updated.GetValueOrDefault(pair.Key) + pair.Value);
            }

            if (FleetCarrierResponse is not null)
            {
                FleetCarrierResponse = FleetCarrierResponse with { Cargo = updated };
            }

            return Task.FromResult<IReadOnlyDictionary<string, int>>(updated);
        }

        public Task PublishCurrentShipAsync(
            ColonizationCurrentShip ship,
            string apiKey,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }

    private sealed record ContributionCall(
        string BuildId,
        string CommanderName,
        IReadOnlyDictionary<string, int> Commodities
    );

    private sealed record LinkCall(string BuildId, string CommanderName);

    private sealed record SystemUpdateCall(
        string SystemNameOrAddress,
        ColonizationSystemSiteUpdate Update,
        string ApiKey
    );

    private sealed record SystemSitePatchCall(
        string SystemNameOrAddress,
        string SiteId,
        ColonizationSystemSitePatch Patch,
        string ApiKey
    );

    private sealed record FleetCarrierAdjustmentCall(long MarketId, IReadOnlyDictionary<string, int> Changes);
}
