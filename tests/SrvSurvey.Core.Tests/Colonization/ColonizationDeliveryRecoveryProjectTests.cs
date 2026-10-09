using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class ColonizationDeliveryRecoveryTests
{
    /// <summary>Docking repairs the faction, a contribution is credited once, and the depot publishes remaining need.</summary>
    [Fact]
    public async Task LiveConstructionEventsSynchronizeLegacyProjectMutations()
    {
        var client = new RecordingRavenClient
        {
            Workspace = new([Project("build-1", "Port", 100, 10, 20, factionName: "Old faction")], [], null, []),
        };
        ColonizationDeliveryRecovery recovery = Create(client);

        IReadOnlyList<ColonizationDeliveryNotice> notices = await ApplyAndSynchronizeAsync(
            recovery,
            ConstructionDock("New faction"),
            Contribution(25),
            Depot(100, 25)
        );

        Assert.Equal(2, client.ProjectUpdates.Count);
        Assert.Equal("New faction", client.ProjectUpdates[0].FactionName);
        Assert.Equal(75, client.ProjectUpdates[1].Commodities!["steel"]);
        ContributionCall contribution = Assert.Single(client.Contributions);
        Assert.Equal("build-1", contribution.BuildId);
        Assert.Equal("Test Cmdr", contribution.CommanderName);
        Assert.Equal(25, contribution.Commodities["steel"]);
        Assert.Equal(75, Assert.Single(recovery.Projects).RemainingRequired);
        AssertNotice(notices, ColonizationDeliveryNoticeKind.RemainingUpdatedAfterContribution);
        Assert.Contains(
            notices,
            notice => notice is { Kind: ColonizationDeliveryNoticeKind.ContributionPublished, Count: 25 }
        );
        Assert.True(observer.ProjectChanges > 0);
    }

    /// <summary>Republishes absolute remaining need after a contribution even when no depot event follows.</summary>
    [Fact]
    public async Task ContributionPublishesRemainingEvenWithoutAFollowingDepotEvent()
    {
        var client = new RecordingRavenClient
        {
            Workspace = new([Project("build-1", "Port", 100, 10, 20)], [], null, []),
        };
        ColonizationDeliveryRecovery recovery = Create(client);

        IReadOnlyList<ColonizationDeliveryNotice> notices = await ApplyAndSynchronizeAsync(
            recovery,
            ConstructionDock("Builders"),
            Contribution(25)
        );

        Assert.Contains(
            client.ProjectUpdates,
            update => update.Commodities is not null && update.Commodities.GetValueOrDefault("steel") == 75
        );
        Assert.Equal(25, Assert.Single(client.Contributions).Commodities["steel"]);
        Assert.Equal(75, Assert.Single(recovery.Projects).Commodities["steel"]);
        AssertNotice(notices, ColonizationDeliveryNoticeKind.RemainingUpdatedAfterContribution);
    }

    /// <summary>Republishes depot need from the depot when it already moved ahead of the cached project.</summary>
    [Fact]
    public async Task ContributionAfterDepotPublishesDepotRemaining()
    {
        var client = new RecordingRavenClient
        {
            Workspace = new([Project("build-1", "Port", 100, 10, 20)], [], null, []),
        };
        ColonizationDeliveryRecovery recovery = Create(client);
        recovery.ApplyJournalEvents([ConstructionDock("Builders"), Depot(100, 40)], null);

        await recovery.SynchronizeLiveEventsAsync([Contribution(25)], allowPublishing: true);

        ColonizationProjectUpdate update = Assert.Single(client.ProjectUpdates);
        Assert.Equal(60, update.Commodities!["steel"]);
        Assert.NotNull(update.ConstructionDepot);
    }

    /// <summary>Skips a repeated depot event whose patch payload was already sent while docked.</summary>
    [Fact]
    public async Task DuplicateDepotEventsSkipIdenticalPatchPayloads()
    {
        var client = new RecordingRavenClient
        {
            Workspace = new([Project("build-1", "Port", 100, 10, 20)], [], null, []),
        };
        ColonizationDeliveryRecovery recovery = Create(client);
        await ApplyAndSynchronizeAsync(recovery, ConstructionDock("Builders"), Depot(100, 25));

        // Put stale need back into the workspace without undocking so the identical depot
        // payload hits the signature guard instead of the "already up to date" short-circuit.
        client.Workspace = new(
            [
                Project("build-1", "Port", 100, 10, 20) with
                {
                    Commodities = new Dictionary<string, int> { ["steel"] = 100 },
                    RemainingRequired = 100,
                },
            ],
            [],
            null,
            []
        );
        LoadWorkspace(recovery, client);
        await ApplyAndSynchronizeAsync(recovery, Depot(100, 25));

        Assert.Equal(
            75,
            Assert.Single(client.ProjectUpdates, update => update.Commodities is not null).Commodities!["steel"]
        );
    }

    /// <summary>Zeroes phantom commodity slots while preserving real need when a docked site project loads.</summary>
    [Fact]
    public async Task ClearsPhantomCommoditySlotsWhenLoadingASiteProject()
    {
        var client = new RecordingRavenClient
        {
            SiteProjectResponse = new ColonizationProject
            {
                BuildId = "site-build",
                BuildName = "Site port",
                SystemName = "Test System",
                MarketId = 10,
                SystemAddress = 20,
                MaximumRequired = 100,
                RemainingRequired = 74,
                Commodities = new Dictionary<string, int> { ["steel"] = 75, ["titanium"] = -1 },
            },
        };
        ColonizationDeliveryRecovery recovery = Create(client);

        await ApplyAndSynchronizeAsync(recovery, ConstructionDock("Builders"));

        Assert.Contains(
            client.ProjectUpdates,
            update =>
                update.Commodities is not null
                && update.Commodities.GetValueOrDefault("titanium") == 0
                && !update.Commodities.ContainsKey("steel")
        );
        ColonizationProject project = Assert.Single(recovery.Projects);
        Assert.Equal(0, project.Commodities["titanium"]);
        Assert.Equal(75, project.Commodities["steel"]);
    }

    /// <summary>Undocking forgets the last depot patch so an identical need can resync after redocking.</summary>
    [Fact]
    public async Task UndockingClearsDepotPatchSignatureSoLaterIdenticalNeedCanResync()
    {
        var client = new RecordingRavenClient
        {
            Workspace = new([Project("build-1", "Port", 100, 10, 20)], [], null, []),
        };
        ColonizationDeliveryRecovery recovery = Create(client);
        await ApplyAndSynchronizeAsync(recovery, ConstructionDock("Builders"), Depot(100, 25));

        recovery.ApplyJournalEvents(
            [Event("Undocked", """ "MarketID":10,"StationName":"Orbital Construction Site: Hope" """)],
            null
        );
        client.Workspace = new(
            [
                Project("build-1", "Port", 100, 10, 20) with
                {
                    Commodities = new Dictionary<string, int> { ["steel"] = 100 },
                    RemainingRequired = 100,
                },
            ],
            [],
            null,
            []
        );
        LoadWorkspace(recovery, client);
        await ApplyAndSynchronizeAsync(recovery, ConstructionDock("Builders"), Depot(100, 25));

        Assert.Equal(
            2,
            client.ProjectUpdates.Count(update =>
                update.Commodities is not null && update.Commodities.GetValueOrDefault("steel") == 75
            )
        );
    }

    /// <summary>Journal bootstrap reads never write project mutations.</summary>
    [Fact]
    public async Task BootstrapNeverSynchronizesProjectMutations()
    {
        var client = new RecordingRavenClient
        {
            Workspace = new([Project("build-1", "Port", 100, 10, 20)], [], null, []),
        };
        ColonizationDeliveryRecovery recovery = Create(client);
        JournalEventEnvelope depot = Depot(100, 25);
        recovery.ApplyJournalEvents([depot], null);

        IReadOnlyList<ColonizationDeliveryNotice> notices = await recovery.SynchronizeLiveEventsAsync(
            [depot],
            allowPublishing: false
        );

        Assert.Empty(notices);
        Assert.Empty(client.ProjectUpdates);
        Assert.Empty(client.Contributions);
        Assert.Equal(0, client.MarkCompleteCount);
    }

    /// <summary>Requirements above the supported total are rejected instead of overflowing.</summary>
    [Fact]
    public async Task RejectsRequirementsAboveSupportedTotal()
    {
        var client = new RecordingRavenClient
        {
            Workspace = new([Project("build-1", "Port", 100, 10, 20)], [], null, []),
        };
        ColonizationDeliveryRecovery recovery = Create(client);
        JournalEventEnvelope depot = Event(
            "ColonisationConstructionDepot",
            "\"MarketID\":10,\"ConstructionProgress\":0.25,\"ResourcesRequired\":["
                + "{\"Name\":\"$steel_name;\",\"RequiredAmount\":2147483647,\"ProvidedAmount\":0},"
                + "{\"Name\":\"$water_name;\",\"RequiredAmount\":2147483647,\"ProvidedAmount\":0}]"
        );

        IReadOnlyList<ColonizationDeliveryNotice> notices = await ApplyAndSynchronizeAsync(recovery, depot);

        AssertNotice(notices, ColonizationDeliveryNoticeKind.RequirementsAboveSupportedTotal);
        Assert.Empty(client.ProjectUpdates);
    }

    /// <summary>Reports a contribution or depot whose market has no Raven project.</summary>
    [Fact]
    public async Task ReportsUnknownProjectForContributionAndDepot()
    {
        var client = new RecordingRavenClient();
        ColonizationDeliveryRecovery recovery = Create(client);

        IReadOnlyList<ColonizationDeliveryNotice> notices = await ApplyAndSynchronizeAsync(
            recovery,
            ConstructionDock(),
            Contribution(5),
            Depot(100, 25)
        );

        AssertNotice(notices, ColonizationDeliveryNoticeKind.ContributionProjectUnknown);
        AssertNotice(notices, ColonizationDeliveryNoticeKind.DepotProjectUnknown);
        Assert.Empty(client.Contributions);
    }

    /// <summary>Registers the live commander as architect for the current system after beacon deployment.</summary>
    [Fact]
    public async Task LiveBeaconDeploymentRegistersCurrentCommanderAsArchitect()
    {
        var client = new RecordingRavenClient();
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key");
        recovery.UpdateSystemContext("Test System", 42, positionChanged: true);

        IReadOnlyList<ColonizationDeliveryNotice> notices = await ApplyAndSynchronizeAsync(
            recovery,
            Event("ColonisationBeaconDeployed", string.Empty)
        );

        SystemUpdateCall call = Assert.Single(client.SystemUpdates);
        Assert.Equal("Test System", call.SystemNameOrAddress);
        Assert.Equal("Test Cmdr", call.Update.Architect);
        Assert.Empty(call.Update.UpdatedSites);
        Assert.Empty(call.Update.DeletedSiteIds);
        Assert.Equal("secret-key", call.ApiKey);
        Assert.Equal(
            new ColonizationDeliveryNotice(
                ColonizationDeliveryNoticeKind.ArchitectRegistered,
                "Test Cmdr",
                "Test System"
            ),
            Assert.Single(notices)
        );
    }

    /// <summary>Beacon registration needs a live event, a saved key, and a known system.</summary>
    [Fact]
    public async Task BeaconArchitectUpdateRequiresLiveEventSavedKeyAndSystem()
    {
        var client = new RecordingRavenClient();
        ColonizationDeliveryRecovery recovery = Create(client);
        recovery.UpdateSystemContext("Test System", 42, positionChanged: true);
        JournalEventEnvelope beacon = Event("ColonisationBeaconDeployed", string.Empty);

        await recovery.SynchronizeLiveEventsAsync([beacon], allowPublishing: false);
        Assert.Empty(client.SystemUpdates);

        AssertNotice(
            await recovery.SynchronizeLiveEventsAsync([beacon], allowPublishing: true),
            ColonizationDeliveryNoticeKind.ArchitectNeedsApiKey
        );
        recovery.UpdateApiKey("secret-key");
        recovery.UpdateSystemContext(null, null, positionChanged: true);
        AssertNotice(
            await recovery.SynchronizeLiveEventsAsync([beacon], allowPublishing: true),
            ColonizationDeliveryNoticeKind.ArchitectNeedsSystem
        );
        Assert.Empty(client.SystemUpdates);
    }

    /// <summary>Completion bypasses cargo updates, honors the live-event gate, and is published only once.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public async Task CompletedDepotMarksProjectCompleteOnce(int remaining)
    {
        var client = new RecordingRavenClient
        {
            Workspace = new([Project("build-1", "Port", remaining, 10, 20)], [], null, []),
        };
        ColonizationDeliveryRecovery recovery = Create(client);
        JournalEventEnvelope[] events = [ConstructionDock(), Depot(1000, 1000, complete: true)];
        recovery.ApplyJournalEvents(events, null);

        await recovery.SynchronizeLiveEventsAsync(events, allowPublishing: false);
        Assert.Equal(0, client.MarkCompleteCount);
        Assert.False(Assert.Single(recovery.Projects).IsComplete);

        IReadOnlyList<ColonizationDeliveryNotice> notices = await recovery.SynchronizeLiveEventsAsync(
            events,
            allowPublishing: true
        );
        await recovery.SynchronizeLiveEventsAsync([events[1]], allowPublishing: true);

        Assert.Equal(1, client.MarkCompleteCount);
        ColonizationProject completed = Assert.Single(recovery.Projects);
        Assert.True(completed.IsComplete);
        Assert.Equal(0, completed.RemainingRequired);
        Assert.Empty(completed.Commodities);
        Assert.Empty(client.ProjectUpdates);
        AssertNotice(notices, ColonizationDeliveryNoticeKind.ProjectMarkedComplete);
    }

    /// <summary>A completion-only event still completes the project after an earlier event cleared its cargo needs.</summary>
    [Fact]
    public async Task CompletionAfterZeroRemainingDepotMarksProjectComplete()
    {
        var client = new RecordingRavenClient
        {
            Workspace = new([Project("build-1", "Port", 25, 10, 20)], [], null, []),
        };
        ColonizationDeliveryRecovery recovery = Create(client);
        await ApplyAndSynchronizeAsync(recovery, ConstructionDock(), Depot(1000, 1000, complete: false));

        Assert.Single(client.ProjectUpdates);
        Assert.Equal(0, client.MarkCompleteCount);
        Assert.False(Assert.Single(recovery.Projects).IsComplete);

        JournalEventEnvelope completed = Depot(1000, 1000, complete: true);
        await ApplyAndSynchronizeAsync(recovery, completed);
        await recovery.SynchronizeLiveEventsAsync([completed], allowPublishing: true);

        Assert.Equal(1, client.MarkCompleteCount);
        Assert.True(Assert.Single(recovery.Projects).IsComplete);
        Assert.Single(client.ProjectUpdates);
    }

    /// <summary>A failed completion notification leaves the project incomplete so a later live event retries it.</summary>
    [Fact]
    public async Task FailedCompletionNotificationRetriesOnLaterDepotEvent()
    {
        var client = new RecordingRavenClient
        {
            Workspace = new([Project("build-1", "Port", 0, 10, 20)], [], null, []),
        };
        client.ProjectCompletionFailures.Enqueue(new HttpRequestException("completion unavailable"));
        ColonizationDeliveryRecovery recovery = Create(client);
        JournalEventEnvelope depot = Depot(1000, 1000, complete: true);

        IReadOnlyList<ColonizationDeliveryNotice> notices = await ApplyAndSynchronizeAsync(
            recovery,
            ConstructionDock(),
            depot
        );

        Assert.Equal(1, client.MarkCompleteCount);
        Assert.False(Assert.Single(recovery.Projects).IsComplete);
        Assert.Contains(
            notices,
            notice =>
                notice
                    is {
                        Kind: ColonizationDeliveryNoticeKind.LiveEventSkipped,
                        Name: "ColonisationConstructionDepot",
                        Detail: "completion unavailable"
                    }
        );

        await recovery.SynchronizeLiveEventsAsync([depot], allowPublishing: true);
        await recovery.SynchronizeLiveEventsAsync([depot], allowPublishing: true);

        Assert.Equal(2, client.MarkCompleteCount);
        Assert.True(Assert.Single(recovery.Projects).IsComplete);
        Assert.Empty(client.ProjectUpdates);
    }

    /// <summary>Docking loads an unlisted site project by system and market without linking a non-architect.</summary>
    [Fact]
    public async Task DockingLoadsUntrackedProjectBySystemAndMarketWithoutLinkingNonArchitect()
    {
        var client = new RecordingRavenClient
        {
            SiteProjectResponse = Project("other-build", "Other port", 50, 10, 20, architectName: "Someone Else"),
        };
        ColonizationDeliveryRecovery recovery = Create(client);

        IReadOnlyList<ColonizationDeliveryNotice> notices = await ApplyAndSynchronizeAsync(
            recovery,
            ConstructionDock()
        );

        Assert.Equal(1, client.SiteProjectLoadCount);
        Assert.Equal("other-build", Assert.Single(recovery.Projects).BuildId);
        Assert.Empty(client.LinkRequests);
        Assert.Empty(client.ProjectUpdates);
        Assert.Equal(
            new ColonizationDeliveryNotice(ColonizationDeliveryNoticeKind.UntrackedProjectLoaded, "Other port"),
            Assert.Single(notices)
        );
    }

    /// <summary>Docking links the commander only when they are the project's architect.</summary>
    [Fact]
    public async Task DockingAutoLinksOnlyWhenCommanderMatchesArchitect()
    {
        var client = new RecordingRavenClient
        {
            SiteProjectResponse = Project("architect-build", "My port", 50, 10, 20, architectName: "Test Cmdr"),
        };
        ColonizationDeliveryRecovery recovery = Create(client);

        IReadOnlyList<ColonizationDeliveryNotice> notices = await ApplyAndSynchronizeAsync(
            recovery,
            ConstructionDock()
        );

        LinkCall link = Assert.Single(client.LinkRequests);
        Assert.Equal("architect-build", link.BuildId);
        Assert.Equal("Test Cmdr", link.CommanderName);
        AssertNotice(notices, ColonizationDeliveryNoticeKind.ProjectLinked);
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
        var client = new RecordingRavenClient { Workspace = new([project], [], null, []) };
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "key");
        recovery.UpdateSystemContext("Test", 20, positionChanged: false);
        JournalEventEnvelope dock = Event(
            "Docked",
            "\"MarketID\":42,\"SystemAddress\":20,\"StarSystem\":\"Test\",\"StationName\":\"Orbital Construction Site: Port\",\"StationFaction\":{\"Name\":\"Faction\"},\"BodyID\":2,\"Body\":\"Test A 2\",\"StationServices\":[\"colonisationcontribution\"]"
        );

        IReadOnlyList<ColonizationDeliveryNotice> notices = await ApplyAndSynchronizeAsync(recovery, dock);

        ColonizationProjectUpdate update = Assert.Single(client.ProjectUpdates);
        Assert.Null(update.FactionName);
        Assert.Equal(2, update.BodyNumber);
        Assert.Equal("Test A 2", update.BodyName);
        AssertNotice(notices, ColonizationDeliveryNoticeKind.ProjectMetadataUpdated);
    }

    /// <summary>Falls back to the commander's known body only for the same commander and system.</summary>
    [Fact]
    public async Task DockingUsesKnownBodyOnlyForTheSameCommanderAndSystem()
    {
        ColonizationProject project = Project("build-1", "Port", 100, 10, 20) with { BodyNumber = -1 };
        var client = new RecordingRavenClient { Workspace = new([project], [], null, []) };
        ColonizationDeliveryRecovery recovery = Create(client);
        recovery.UpdateSystemContext("Test System", 20, positionChanged: false);
        recovery.ApplyJournalEvents([Event("Location", "\"Body\":\"Test 4 a\",\"BodyID\":12")], null);
        Assert.Equal(12, recovery.CurrentBodyId);
        Assert.Equal(1, observer.BodyChanges);

        await ApplyAndSynchronizeAsync(recovery, ConstructionDock());

        ColonizationProjectUpdate update = Assert.Single(client.ProjectUpdates);
        Assert.Equal(12, update.BodyNumber);
        Assert.Equal("Test 4 a", update.BodyName);

        recovery.ApplyJournalEvents([Event("SupercruiseEntry", string.Empty)], null);
        Assert.Null(recovery.CurrentBodyId);
        Assert.Equal(2, observer.BodyChanges);
    }

    /// <summary>Docking permission asks presentation to refresh only at a construction site or with known projects, never during bootstrap.</summary>
    [Fact]
    public async Task DockingPermissionRequestsRefreshOnlyForRelevantLiveDocks()
    {
        ColonizationDeliveryRecovery recovery = Create(new RecordingRavenClient());
        JournalEventEnvelope regular = Event("DockingGranted", "\"StationName\":\"Regular port\"");

        await recovery.SynchronizeLiveEventsAsync([regular], allowPublishing: false);
        await recovery.SynchronizeLiveEventsAsync([regular], allowPublishing: true);
        Assert.Equal(0, observer.DockingRefreshRequests);

        await recovery.SynchronizeLiveEventsAsync(
            [Event("DockingGranted", "\"StationName\":\"Orbital Construction Site: Hope\"")],
            allowPublishing: true
        );
        Assert.Equal(1, observer.DockingRefreshRequests);

        recovery.ReplaceWorkspace([Project("build-1", "Port", 100)], []);
        await recovery.SynchronizeLiveEventsAsync([regular], allowPublishing: true);
        Assert.Equal(2, observer.DockingRefreshRequests);
    }

    /// <summary>A created project is installed before cleanup and replaced by its cleaned form for the same profile only.</summary>
    [Fact]
    public async Task InstallsCreatedProjectAndIgnoresCleanupForReplacedProfile()
    {
        var client = new RecordingRavenClient();
        ColonizationDeliveryRecovery recovery = Create(client);
        ColonizationProject created = Project("new-build", "New port", 50) with
        {
            Commodities = new Dictionary<string, int> { ["steel"] = 50, ["titanium"] = -1 },
        };

        await recovery.InstallCreatedProjectAsync(created);

        Assert.Equal(0, Assert.Single(recovery.Projects).Commodities["titanium"]);
        Assert.Equal(2, observer.ProjectChanges);

        var gate = new TaskCompletionSource();
        var gatedClient = new RecordingRavenClient { UpdateProjectGate = gate.Task };
        ColonizationDeliveryRecovery gated = Create(gatedClient);
        Task install = gated.InstallCreatedProjectAsync(created);
        gated.SetProfile("F456", true, null);
        gate.SetResult();
        await install;
        Assert.Equal(-1, Assert.Single(gated.Projects).Commodities["titanium"]);
    }
}
