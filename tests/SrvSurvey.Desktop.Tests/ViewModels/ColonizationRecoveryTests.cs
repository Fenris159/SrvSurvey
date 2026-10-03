using System.Net;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    private DateTimeOffset recoveryTime = DateTimeOffset.UtcNow;

    /// <summary>Loads the replacement commander automatically after the obsolete request releases its busy ownership.</summary>
    [Fact]
    public async Task CommanderSwitchQueuesReplacementRefresh()
    {
        var gate = new TaskCompletionSource<ColonizationCommanderProjects>();
        var client = new StubRavenColonialClient
        {
            LoadWorkspace = name =>
                name == "Old"
                    ? gate.Task
                    : Task.FromResult(new ColonizationCommanderProjects([Project("new", "Port", 100)], [], null, [])),
        };
        using ColonizationViewModel vm = Create(client);
        vm.IsEnabled = true;
        Task old = vm.SetCommanderAsync("Old");
        await vm.SetCommanderAsync("New");
        gate.SetResult(new([], [], null, []));
        await old;
        Assert.Equal("New", vm.CommanderName);
        Assert.Equal("new", Assert.Single(vm.Projects).Project.BuildId);
        Assert.Equal(2, client.LoadCount);
    }

    /// <summary>Never stores or applies a key validated for an invalidated profile.</summary>
    [Fact]
    public async Task KeyValidationCannotMigrateToReplacementProfile()
    {
        var gate = new TaskCompletionSource<string?>();
        var client = new StubRavenColonialClient { ApiKeyValidation = gate };
        using ColonizationViewModel vm = Create(client);
        await vm.SetCommanderAsync("Test Cmdr");
        vm.SetCommanderProfile("F123", true, null);
        vm.RavenApiKey = "old-key";
        Task save = vm.SaveRavenApiKeyAsync();
        vm.SetCommanderProfile("F456", false, "new-key");
        gate.SetResult("Test Cmdr");
        await save;
        CommanderProfileLoadResult profile = await new CommanderProfileStore(directory).LoadAsync("F456", false);
        Assert.NotEqual("old-key", profile.Data?.RavenColonialApiKey);
        Assert.Equal("new-key", vm.RavenApiKey);
    }

    /// <summary>Attributes a market transaction to its event-time dock despite a later undock in the same poll.</summary>
    [Fact]
    public async Task SynchronizesCargoBeforeBatchUndock()
    {
        StubRavenColonialClient client = CarrierClient();
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        JournalEventEnvelope[] batch =
        [
            CarrierDock(),
            Event("MarketBuy", "\"MarketID\":42,\"Type\":\"steel\",\"Count\":5"),
            Event("Undocked", "\"StationName\":\"ABC-123\""),
        ];
        vm.ApplyJournalEvents(batch);
        await vm.SynchronizeLiveProjectsAsync(batch, true);
        Assert.Equal(-5, Assert.Single(client.FleetCarrierAdjustments).Changes["steel"]);
    }

    /// <summary>Reconciles a consumed squadron difference after failure instead of silently losing it.</summary>
    [Fact]
    public async Task RetainsFailedSquadronCargoAdjustment()
    {
        StubRavenColonialClient client = CarrierClient();
        client.AdjustmentFailures.Enqueue(
            new RavenColonialServiceException(HttpStatusCode.TooManyRequests, "adjust", "unavailable")
        );
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        vm.ApplyJournalEvents([CarrierDock(squadron: true)]);
        var cargo = new CargoInventoryState();
        cargo.Apply(Event("Cargo", "\"Inventory\":[{\"Name\":\"steel\",\"Count\":10}]"));
        cargo.GetDiff();
        cargo.CaptureBeforeSnapshot();
        cargo.Apply(
            Event("CargoTransfer", "\"Transfers\":[{\"Type\":\"steel\",\"Count\":5,\"Direction\":\"tocarrier\"}]")
        );
        await vm.SynchronizeLiveProjectsAsync([], true, cargo, true);
        recoveryTime = recoveryTime.AddSeconds(6);
        await vm.SynchronizeLiveProjectsAsync([], true, cargo, true);
        Assert.Equal(2, client.FleetCarrierAdjustments.Count);
        Assert.Equal(105, client.FleetCarrierResponse!.Cargo["steel"]);
    }

    /// <summary>Does not repeat an ambiguous cargo write when the server already contains its expected result.</summary>
    [Fact]
    public async Task ReconcilesAlreadyAppliedCargoWithoutDuplicating()
    {
        StubRavenColonialClient client = CarrierClient();
        client.AdjustmentFailures.Enqueue(new HttpRequestException("response lost"));
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        vm.ApplyJournalEvents([CarrierDock()]);
        await vm.SynchronizeLiveProjectsAsync(
            [Event("MarketBuy", "\"MarketID\":42,\"Type\":\"steel\",\"Count\":5")],
            true
        );
        client.FleetCarrierResponse = client.FleetCarrierResponse! with { Cargo = new() { ["steel"] = 95 } };
        recoveryTime = recoveryTime.AddSeconds(6);
        await vm.SynchronizeLiveProjectsAsync([], true);
        Assert.Single(client.FleetCarrierAdjustments);
        Assert.Equal(95, vm.LinkedFleetCarriers[0].Cargo["steel"]);
    }

    /// <summary>Queues a market transaction while carrier publication installs its absolute cargo baseline.</summary>
    [Fact]
    public async Task PublicationDoesNotOverwriteConcurrentBuy()
    {
        StubRavenColonialClient client = CarrierClient();
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        vm.ApplyJournalEvents([CarrierDock()]);
        vm.FleetCarrierCargoSyncEnabled = false;
        await vm.UpdateMarketAsync(LinkedCarrierMarket(80));
        vm.FleetCarrierCargoSyncEnabled = true;
        var entered = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        client.ReplaceCargo = async cargo =>
        {
            entered.SetResult();
            await gate.Task;
            client.FleetCarrierResponse = client.FleetCarrierResponse! with
            {
                Cargo = cargo.ToDictionary(pair => pair.Key, pair => pair.Value),
            };
            return client.FleetCarrierResponse.Cargo;
        };
        Task publish = vm.PublishCurrentFleetCarrierAsync();
        await entered.Task;
        await vm.SynchronizeLiveProjectsAsync(
            [Event("MarketBuy", "\"MarketID\":42,\"Type\":\"steel\",\"Count\":5")],
            true
        );
        Assert.Empty(client.FleetCarrierAdjustments);
        gate.SetResult();
        await publish;
        Assert.Equal(75, client.FleetCarrierResponse!.Cargo["steel"]);
    }

    /// <summary>Warns about a matched row that cannot be repaired because it lacks its persisted ID.</summary>
    [Fact]
    public async Task MissingRepairIdentityIsReported()
    {
        var client = new StubRavenColonialClient
        {
            SystemSitesResponse = [new() { Name = "Port", Status = ColonizationSystemSiteStatus.Complete }],
        };
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        vm.UpdateSystemContext("Test", null, 20);
        JournalEventEnvelope dock = Event(
            "Docked",
            "\"MarketID\":4300000123,\"SystemAddress\":20,\"StarSystem\":\"Test\",\"StationName\":\"Port\",\"StationType\":\"Outpost\""
        );
        vm.ApplyJournalEvents([dock]);
        await vm.SynchronizeLiveProjectsAsync([dock], true);
        Assert.Contains("no persisted ID", vm.StatusMessage);
        Assert.Empty(client.SystemSitePatches);
    }

    /// <summary>Retries a rejected contribution even when the next poll contains no new journal events.</summary>
    [Fact]
    public async Task RetriesRetainedContributionOnIdlePoll()
    {
        ColonizationProject project = Project("build", "Port", 100) with { MarketId = 42 };
        var client = new StubRavenColonialClient { Workspace = new([project], [], null, []) };
        client.ContributionFailures.Enqueue(
            new RavenColonialServiceException(HttpStatusCode.TooManyRequests, "contribute", "unavailable")
        );
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        JournalEventEnvelope contribution = Event(
            "ColonisationContribution",
            "\"MarketID\":42,\"Contributions\":[{\"Name\":\"steel\",\"Amount\":5}]"
        );
        await vm.SynchronizeLiveProjectsAsync([contribution], true);
        recoveryTime = recoveryTime.AddSeconds(6);
        await vm.SynchronizeLiveProjectsAsync([], true);
        Assert.Equal(2, client.Contributions.Count);
    }

    /// <summary>Retains an uncertain contribution without blindly duplicating credit after a lost response.</summary>
    [Fact]
    public async Task DoesNotBlindlyReplayUncertainContribution()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new([Project("build", "Port", 100) with { MarketId = 42 }], [], null, []),
        };
        client.ContributionFailures.Enqueue(new HttpRequestException("response lost"));
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        await vm.SynchronizeLiveProjectsAsync(
            [
                Event(
                    "ColonisationContribution",
                    "\"MarketID\":42,\"Contributions\":[{\"Name\":\"steel\",\"Amount\":5}]"
                ),
            ],
            true
        );
        recoveryTime = recoveryTime.AddSeconds(6);
        await vm.SynchronizeLiveProjectsAsync([], true);
        Assert.Single(client.Contributions);
        Assert.Contains("uncertain", vm.StatusMessage);
    }

    /// <summary>Repairs body metadata independently of an already-correct project faction.</summary>
    [Fact]
    public async Task DockingRepairsBodyMetadata()
    {
        ColonizationProject project = Project("build", "Port", 100) with
        {
            MarketId = 42,
            FactionName = "Faction",
            BodyNumber = -1,
            BodyName = "Unknown",
        };
        var client = new StubRavenColonialClient { Workspace = new([project], [], null, []) };
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        vm.UpdateSystemContext("Test", null, 20);
        JournalEventEnvelope dock = Event(
            "Docked",
            "\"MarketID\":42,\"SystemAddress\":20,\"StarSystem\":\"Test\",\"StationName\":\"Orbital Construction Site: Port\",\"StationFaction\":{\"Name\":\"Faction\"},\"BodyID\":2,\"Body\":\"Test A 2\",\"StationServices\":[\"colonisationcontribution\"]"
        );
        vm.ApplyJournalEvents([dock]);
        await vm.SynchronizeLiveProjectsAsync([dock], true);
        ColonizationProjectUpdate update = Assert.Single(client.ProjectUpdates);
        Assert.Equal(2, update.BodyNumber);
        Assert.Equal("Test A 2", update.BodyName);
    }

    /// <summary>Retains delivery credit across restart and retries only after the user confirms Raven did not record it.</summary>
    [Fact]
    public async Task ReconcilesUncertainContributionAfterRestart()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new([Project("build", "Port", 100) with { MarketId = 42 }], [], null, []),
        };
        client.ContributionFailures.Enqueue(new HttpRequestException("lost reply"));
        using (ColonizationViewModel first = await CreateRecoveryAsync(client))
        {
            await first.SynchronizeLiveProjectsAsync(
                [
                    Event(
                        "ColonisationContribution",
                        "\"MarketID\":42,\"Contributions\":[{\"Name\":\"steel\",\"Amount\":5}]"
                    ),
                ],
                true
            );
            Assert.True(first.HasUncertainContributions);
            Assert.Contains("5 units", first.PendingContributionSummary);
        }
        using ColonizationViewModel restarted = await CreateRecoveryAsync(client);
        Assert.True(restarted.HasUncertainContributions);
        Assert.Single(restarted.UnconfirmedContributions).IsSelected = true;
        await restarted.RetryUnconfirmedContributionsAsync();
        Assert.Equal(2, client.Contributions.Count);
        Assert.False(restarted.HasUncertainContributions);
        Assert.Empty(restarted.PendingContributionSummary);
        using ColonizationViewModel afterAcknowledgement = await CreateRecoveryAsync(client);
        Assert.False(afterAcknowledgement.HasUncertainContributions);
    }

    /// <summary>Keeps server failures uncertain and allows verification to dismiss acknowledged credit without replay.</summary>
    [Fact]
    public async Task DismissesVerifiedContributionWithoutDuplicateCredit()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new([Project("build", "Port", 100) with { MarketId = 42 }], [], null, []),
        };
        client.ContributionFailures.Enqueue(
            new RavenColonialServiceException(HttpStatusCode.InternalServerError, "contribute", "failed after commit")
        );
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        await vm.SynchronizeLiveProjectsAsync(
            [
                Event(
                    "ColonisationContribution",
                    "\"MarketID\":42,\"Contributions\":[{\"Name\":\"steel\",\"Amount\":5}]"
                ),
            ],
            true
        );
        recoveryTime = recoveryTime.AddSeconds(6);
        await vm.SynchronizeLiveProjectsAsync([], true);
        Assert.Single(client.Contributions);
        Assert.True(vm.HasUncertainContributions);
        Assert.Single(vm.UnconfirmedContributions).IsSelected = true;
        vm.DismissConfirmedContributions();
        Assert.False(vm.HasUncertainContributions);
        using ColonizationViewModel restarted = await CreateRecoveryAsync(client);
        Assert.Empty(restarted.PendingContributionSummary);
    }

    /// <summary>Does not let another commander reconcile or upload the originating profile's retained delivery.</summary>
    [Fact]
    public async Task PendingDeliveriesStayWithOriginalCommander()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new([Project("build", "Port", 100) with { MarketId = 42 }], [], null, []),
        };
        client.ContributionFailures.Enqueue(new HttpRequestException("lost reply"));
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        await vm.SynchronizeLiveProjectsAsync(
            [
                Event(
                    "ColonisationContribution",
                    "\"MarketID\":42,\"Contributions\":[{\"Name\":\"steel\",\"Amount\":5}]"
                ),
            ],
            true
        );
        await vm.SetCommanderAsync("Another Cmdr");
        Assert.False(vm.HasUncertainContributions);
        Assert.Empty(vm.PendingContributionSummary);
        await vm.RetryUnconfirmedContributionsAsync();
        vm.DismissConfirmedContributions();
        Assert.Single(client.Contributions);
        await vm.SetCommanderAsync("Test Cmdr");
        Assert.True(vm.HasUncertainContributions);
    }

    /// <summary>Processes later transactions in order only after reconciling an earlier uncertain cargo write.</summary>
    [Fact]
    public async Task CargoAdjustmentsDoNotOvertakeUncertainWrites()
    {
        StubRavenColonialClient client = CarrierClient();
        client.AdjustmentFailures.Enqueue(new HttpRequestException("reply lost"));
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        vm.ApplyJournalEvents([CarrierDock()]);
        await vm.SynchronizeLiveProjectsAsync(
            [
                Event("MarketBuy", "\"MarketID\":42,\"Type\":\"steel\",\"Count\":5"),
                Event("MarketSell", "\"MarketID\":42,\"Type\":\"steel\",\"Count\":2"),
            ],
            true
        );
        Assert.Single(client.FleetCarrierAdjustments);
        client.FleetCarrierResponse = client.FleetCarrierResponse! with { Cargo = new() { ["steel"] = 95 } };
        recoveryTime = recoveryTime.AddSeconds(6);
        await vm.SynchronizeLiveProjectsAsync([], true);
        Assert.Equal(2, client.FleetCarrierAdjustments.Count);
        Assert.Equal(97, client.FleetCarrierResponse.Cargo["steel"]);
    }

    /// <summary>Uses a newer market baseline to reconcile a concurrent server change without guessing whether a failed delta applied.</summary>
    [Fact]
    public async Task MarketRefreshReconcilesUncertainCargo()
    {
        StubRavenColonialClient client = CarrierClient();
        client.AdjustmentFailures.Enqueue(new HttpRequestException("reply lost"));
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        vm.ApplyJournalEvents([CarrierDock()]);
        await vm.SynchronizeLiveProjectsAsync(
            [Event("MarketBuy", "\"MarketID\":42,\"Type\":\"steel\",\"Count\":5")],
            true
        );
        client.FleetCarrierResponse = client.FleetCarrierResponse! with { Cargo = new() { ["steel"] = 90 } };
        recoveryTime = recoveryTime.AddSeconds(6);
        await vm.SynchronizeLiveProjectsAsync([], true);
        Assert.Contains("changed concurrently", vm.StatusMessage);
        await vm.UpdateMarketAsync(LinkedCarrierMarket(90) with { Timestamp = recoveryTime.AddSeconds(1) });
        recoveryTime = recoveryTime.AddSeconds(6);
        await vm.SynchronizeLiveProjectsAsync([], true);
        Assert.Single(client.FleetCarrierAdjustments);
        Assert.Equal(90, vm.LinkedFleetCarriers[0].Cargo["steel"]);
    }

    /// <summary>Uses journal transfer deltas when a squadron batch ends undocked, consuming the aggregate difference to prevent a later duplicate.</summary>
    [Fact]
    public async Task SquadronTransferUsesEventTimeDockBeforeUndock()
    {
        StubRavenColonialClient client = CarrierClient();
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        vm.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
        var cargo = new CargoInventoryState();
        cargo.Apply(Event("Cargo", "\"Inventory\":[{\"Name\":\"steel\",\"Count\":10}]"));
        cargo.GetDiff();
        cargo.CaptureBeforeSnapshot();
        JournalEventEnvelope transfer = Event(
            "CargoTransfer",
            "\"Transfers\":[{\"Type\":\"steel\",\"Count\":5,\"Direction\":\"tocarrier\"}]"
        );
        cargo.Apply(transfer);
        JournalEventEnvelope[] events =
        [
            CarrierDock(squadron: true),
            transfer,
            Event("Undocked", "\"StationName\":\"ABC-123\""),
        ];
        vm.ApplyJournalEvents(events);
        await vm.SynchronizeLiveProjectsAsync(events, true, cargo, true);
        Assert.Equal(5, Assert.Single(client.FleetCarrierAdjustments).Changes["steel"]);
        Assert.Empty(cargo.GetDiff());
    }

    /// <summary>Does not replay an uncertain cargo write against an unchanged count, and recovers it safely across restart.</summary>
    [Fact]
    public async Task RetainsUncertainCargoAcrossRestart()
    {
        StubRavenColonialClient client = CarrierClient();
        client.AdjustmentFailures.Enqueue(new HttpRequestException("reply lost"));
        using (ColonizationViewModel vm = await CreateRecoveryAsync(client))
        {
            vm.ApplyJournalEvents([CarrierDock()]);
            await vm.SynchronizeLiveProjectsAsync(
                [Event("MarketBuy", "\"MarketID\":42,\"Type\":\"steel\",\"Count\":5")],
                true
            );
            recoveryTime = recoveryTime.AddSeconds(6);
            await vm.SynchronizeLiveProjectsAsync([], true);
            Assert.Single(client.FleetCarrierAdjustments);
            Assert.Contains("uncertain", vm.StatusMessage);
        }
        using ColonizationViewModel restarted = await CreateRecoveryAsync(client);
        await restarted.SynchronizeLiveProjectsAsync([], true);
        Assert.Single(client.FleetCarrierAdjustments);
        client.FleetCarrierResponse = client.FleetCarrierResponse! with { Cargo = new() { ["steel"] = 95 } };
        recoveryTime = recoveryTime.AddSeconds(6);
        await restarted.SynchronizeLiveProjectsAsync([], true);
        Assert.Single(client.FleetCarrierAdjustments);
        Assert.Equal(95, restarted.LinkedFleetCarriers[0].Cargo["steel"]);
        Assert.Empty(
            new ColonizationSettingsStore(Path.Combine(directory, "recovery.json")).LoadPendingCargoAdjustments()
        );
    }

    /// <summary>Preserves definitely rejected cargo updates across multiple failures before accepting their eventual acknowledgement.</summary>
    [Fact]
    public async Task RetriesRepeatedCargoRejectionsWithoutLosingDelta()
    {
        StubRavenColonialClient client = CarrierClient();
        client.AdjustmentFailures.Enqueue(
            new RavenColonialServiceException(HttpStatusCode.TooManyRequests, "adjust", "rate limit")
        );
        client.AdjustmentFailures.Enqueue(
            new RavenColonialServiceException(HttpStatusCode.TooManyRequests, "adjust", "rate limit")
        );
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        vm.ApplyJournalEvents([CarrierDock()]);
        await vm.SynchronizeLiveProjectsAsync(
            [Event("MarketBuy", "\"MarketID\":42,\"Type\":\"steel\",\"Count\":5")],
            true
        );
        recoveryTime = recoveryTime.AddSeconds(6);
        await vm.SynchronizeLiveProjectsAsync([], true);
        Assert.Contains("remain pending", vm.StatusMessage);
        recoveryTime = recoveryTime.AddSeconds(6);
        await vm.SynchronizeLiveProjectsAsync([], true);
        Assert.Equal(3, client.FleetCarrierAdjustments.Count);
        Assert.Equal(95, client.FleetCarrierResponse!.Cargo["steel"]);
    }

    /// <summary>Propagates the journal monitor's cancellation token to the actual delivery upload.</summary>
    [Fact]
    public async Task ContributionUploadUsesMonitorCancellation()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new([Project("build", "Port", 100) with { MarketId = 42 }], [], null, []),
        };
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        using var cancellation = new CancellationTokenSource();
        await vm.SynchronizeLiveProjectsAsync(
            [
                Event(
                    "ColonisationContribution",
                    "\"MarketID\":42,\"Contributions\":[{\"Name\":\"steel\",\"Amount\":5}]"
                ),
            ],
            true,
            cancellationToken: cancellation.Token
        );
        Assert.Equal(cancellation.Token, client.LastContributionCancellation);
    }

    /// <summary>Creates an enabled profile with a controllable retry clock.</summary>
    private async Task<ColonizationViewModel> CreateRecoveryAsync(StubRavenColonialClient client)
    {
        var vm = new ColonizationViewModel(
            new ColonizationSettingsStore(Path.Combine(directory, "recovery.json")),
            client,
            commanderProfileStore: new CommanderProfileStore(directory),
            utcNow: () => recoveryTime
        );
        vm.IsEnabled = true;
        vm.SetCommanderProfile("F123", true, "key");
        await vm.SetCommanderAsync("Test Cmdr");
        vm.FleetCarrierCargoSyncEnabled = true;
        return vm;
    }

    /// <summary>Provides a linked carrier with matching local and server baselines.</summary>
    private static StubRavenColonialClient CarrierClient()
    {
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Name = "ABC-123",
            Cargo = new() { ["steel"] = 100 },
        };
        return new StubRavenColonialClient { Workspace = new([], [], null, [carrier]), FleetCarrierResponse = carrier };
    }

    /// <summary>Produces a personal or squadron carrier docking event for recovery simulations.</summary>
    private static JournalEventEnvelope CarrierDock(bool squadron = false) =>
        Event(
            "Docked",
            $"\"MarketID\":42,\"SystemAddress\":20,\"StarSystem\":\"Test\",\"StationName\":\"ABC-123\",\"StationType\":\"FleetCarrier\",\"StationServices\":[\"commodities\"{(squadron ? ",\"squadronBank\"" : "")}] "
        );
}
