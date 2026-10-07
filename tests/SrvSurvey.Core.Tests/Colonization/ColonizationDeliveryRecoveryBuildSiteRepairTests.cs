using System.Globalization;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class ColonizationDeliveryRecoveryTests
{
    /// <summary>Repairs a docked site's market ID once and persists the repeat guard across restart.</summary>
    [Fact]
    public async Task DockedLocationRepairsSiteAndPersistsRepeatGuard()
    {
        var client = new RecordingRavenClient
        {
            SystemSitesResponse =
            [
                new ColonizationSystemSite
                {
                    Id = "&4310842115",
                    Name = "Gold Enterprise",
                    Status = ColonizationSystemSiteStatus.Complete,
                },
            ],
        };
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key");
        JournalEventEnvelope location = Event(
            "Location",
            """
            "Docked":true,"MarketID":4310842115,
            "SystemAddress":123456789,"StarSystem":"Test System",
            "StationName":"Gold Enterprise","StationType":"Dodec"
            """
        );

        IReadOnlyList<ColonizationDeliveryNotice> notices = await recovery.SynchronizeLiveEventsAsync(
            [location],
            allowPublishing: true
        );
        await recovery.SynchronizeLiveEventsAsync([location], allowPublishing: true);

        Assert.Equal(1, client.SystemSiteLoadCount);
        SystemSitePatchCall patch = Assert.Single(client.SystemSitePatches);
        Assert.Equal("123456789", patch.SystemNameOrAddress);
        Assert.Equal("&4310842115", patch.SiteId);
        Assert.Equal(4_310_842_115, patch.Patch.MarketId);
        Assert.Null(patch.Patch.Name);
        Assert.Equal("secret-key", patch.ApiKey);
        Assert.Equal(
            new ColonizationDeliveryNotice(ColonizationDeliveryNoticeKind.BuildSiteMarketRepaired, "Gold Enterprise"),
            Assert.Single(notices)
        );

        var reloadedClient = new RecordingRavenClient { SystemSitesResponse = client.SystemSitesResponse };
        ColonizationDeliveryRecovery reloaded = Create(reloadedClient, apiKey: "secret-key");
        await reloaded.SynchronizeLiveEventsAsync([location], allowPublishing: true);

        Assert.Equal(0, reloadedClient.SystemSiteLoadCount);
        Assert.Empty(reloadedClient.SystemSitePatches);
    }

    /// <summary>Repairs a mismatched site name and keeps the server repair when its guard cannot be saved.</summary>
    [Fact]
    public async Task RepairsSiteNameEvenWhenRepeatGuardCannotBeSaved()
    {
        var client = new RecordingRavenClient
        {
            SystemSitesResponse =
            [
                new ColonizationSystemSite
                {
                    Id = "site-1",
                    Name = "Old Name",
                    MarketId = 4_310_999_999,
                    Status = ColonizationSystemSiteStatus.Complete,
                },
            ],
        };
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key");
        store.SaveFailure = new IOException("read-only");
        JournalEventEnvelope docked = Event(
            "Docked",
            """
            "MarketID":4310999999,"SystemAddress":20,
            "StationName":"Dampier Gateway","StationType":"Outpost"
            """
        );

        IReadOnlyList<ColonizationDeliveryNotice> notices = await recovery.SynchronizeLiveEventsAsync(
            [docked],
            allowPublishing: true
        );

        Assert.Equal("Dampier Gateway", Assert.Single(client.SystemSitePatches).Patch.Name);
        Assert.Equal(ColonizationDeliveryNoticeKind.BuildSiteNameRepaired, Assert.Single(notices).Kind);
        Assert.Null(recovery.BuildSiteRepairWarning);
    }

    /// <summary>Retries transient lookups with backoff and repairs on a later dock once Raven responds.</summary>
    [Fact]
    public async Task FailedOrNoMatchSiteRepairCanRetryLater()
    {
        var client = new RecordingRavenClient();
        client.SystemSiteFailures.Enqueue(new HttpRequestException("temporary one"));
        client.SystemSiteFailures.Enqueue(new HttpRequestException("temporary two"));
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key");
        JournalEventEnvelope docked = Event(
            "Docked",
            """
            "MarketID":4310999999,"SystemAddress":20,
            "StationName":"Dampier Gateway","StationType":"Outpost"
            """
        );

        await recovery.SynchronizeLiveEventsAsync([docked], allowPublishing: true);

        Assert.Equal([TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3)], delays);
        Assert.Equal(3, client.SystemSiteLoadCount);
        Assert.Empty(client.SystemSitePatches);

        client.SystemSitesResponse =
        [
            new ColonizationSystemSite
            {
                Id = "x1",
                Name = "Dampier Gateway",
                MarketId = 3_963_024_386,
                Status = ColonizationSystemSiteStatus.Complete,
            },
        ];
        await recovery.SynchronizeLiveEventsAsync([docked], allowPublishing: true);

        Assert.Equal(4, client.SystemSiteLoadCount);
        Assert.Equal(4_310_999_999, Assert.Single(client.SystemSitePatches).Patch.MarketId);
    }

    /// <summary>Successful no-op lookups and repairs clear earlier warnings, including after docking in another system.</summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    public async Task SiteRepairRecoveryClearsEarlierWarning(bool changeSystem, int siteState)
    {
        (ColonizationDeliveryRecovery recovery, RecordingRavenClient client, JournalEventEnvelope docked) =
            await CreateFailedSiteRepairAsync();
        long systemAddress = changeSystem ? 21 : 20;
        recovery.UpdateSystemContext(changeSystem ? "Other System" : "Failing System", systemAddress, false);
        if (siteState > 0)
        {
            client.SystemSitesResponse =
            [
                new ColonizationSystemSite
                {
                    Id = "site-1",
                    Name = "Dampier Gateway",
                    MarketId = siteState == 1 ? 4_310_999_999 : 3_963_024_386,
                    Status = ColonizationSystemSiteStatus.Complete,
                },
            ];
        }

        JournalEventEnvelope nextDock = changeSystem
            ? Event(
                "Docked",
                """
                "MarketID":4310999999,"SystemAddress":21,"StarSystem":"Other System",
                "StationName":"Dampier Gateway","StationType":"Outpost"
                """
            )
            : docked;
        await ApplyAndSynchronizeAsync(recovery, nextDock);
        await recovery.SynchronizeLiveEventsAsync([nextDock], allowPublishing: true);

        Assert.Null(recovery.BuildSiteRepairWarning);
        Assert.Equal(siteState == 2 ? 1 : 0, client.SystemSitePatches.Count);
        Assert.All(
            client.SystemSiteRequests.Skip(3),
            request => Assert.Equal(systemAddress.ToString(CultureInfo.InvariantCulture), request)
        );
    }

    /// <summary>Unrelated events and skipped docking contexts cannot turn an unresolved repair failure into success.</summary>
    [Fact]
    public async Task UnrelatedEventsAndSkippedDocksPreserveCurrentSiteRepairWarning()
    {
        (ColonizationDeliveryRecovery recovery, RecordingRavenClient client, JournalEventEnvelope docked) =
            await CreateFailedSiteRepairAsync();
        ColonizationBuildSiteRepairWarning? warning = recovery.BuildSiteRepairWarning;

        await recovery.SynchronizeLiveEventsAsync([Event("Music", "\"MusicTrack\":\"DockingComputer\"")], true);
        await recovery.SynchronizeLiveEventsAsync([docked], allowPublishing: false);
        await recovery.SynchronizeLiveEventsAsync(
            [
                Event(
                    "Docked",
                    """
                    "MarketID":1,"SystemAddress":20,"StarSystem":"Failing System",
                    "StationName":"ABC-123","StationType":"FleetCarrier"
                    """
                ),
            ],
            allowPublishing: true
        );

        Assert.Equal(warning, recovery.BuildSiteRepairWarning);
        Assert.Equal(3, client.SystemSiteLoadCount);
    }

    /// <summary>Leaving the affected system, changing commanders, or disabling Raven clears its scoped repair warning.</summary>
    [Theory]
    [InlineData("system")]
    [InlineData("commander")]
    [InlineData("disabled")]
    public async Task SiteRepairWarningClearsWhenContextChanges(string change)
    {
        ColonizationDeliveryRecovery recovery = (await CreateFailedSiteRepairAsync()).Recovery;
        int notifications = observer.RepairWarningChanges;

        ChangeRepairContext(recovery, change);

        Assert.Null(recovery.BuildSiteRepairWarning);
        Assert.Equal(notifications + 1, observer.RepairWarningChanges);
    }

    /// <summary>A request that fails after its system, commander, or enabled state changes cannot restore an obsolete warning.</summary>
    [Theory]
    [InlineData("system")]
    [InlineData("commander")]
    [InlineData("disabled")]
    public async Task DelayedSiteRepairFailureCannotRestoreWarningInAnotherContext(string change)
    {
        (ColonizationDeliveryRecovery recovery, RecordingRavenClient client, JournalEventEnvelope docked) =
            await CreateFailedSiteRepairAsync();
        var response = new TaskCompletionSource<IReadOnlyList<ColonizationSystemSite>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        client.SystemSiteResponseTask = response.Task;
        Task pending = recovery.SynchronizeLiveEventsAsync([docked], allowPublishing: true);
        Assert.Equal(4, client.SystemSiteLoadCount);

        ChangeRepairContext(recovery, change);
        response.SetException(new HttpRequestException("late failure from old context"));
        await pending;

        Assert.Null(recovery.BuildSiteRepairWarning);
    }

    /// <summary>An earlier dock in a journal batch cannot display its failure over the newer active system.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EarlierSystemDockFailureDoesNotShowInCurrentSystem(bool hasSystemAddress)
    {
        (ColonizationDeliveryRecovery recovery, RecordingRavenClient client, JournalEventEnvelope docked) =
            await CreateFailedSiteRepairAsync();
        recovery.UpdateSystemContext("Other System", hasSystemAddress ? 21 : null, positionChanged: false);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            client.SystemSiteFailures.Enqueue(new HttpRequestException("earlier system failed"));
        }

        await recovery.SynchronizeLiveEventsAsync([docked], allowPublishing: true);

        Assert.Null(recovery.BuildSiteRepairWarning);
        Assert.Equal("20", client.SystemSiteRequests[^1]);
    }

    /// <summary>A failure is shown when no system context is known and the active dock is in the failing system.</summary>
    [Fact]
    public async Task RepairFailureWithoutSystemContextUsesActiveDock()
    {
        (ColonizationDeliveryRecovery recovery, _, _) = await CreateFailedSiteRepairAsync(provideSystemContext: false);

        Assert.Equal(
            new ColonizationBuildSiteRepairWarning(20, "Docked", "Failing System", "server unavailable for system 20"),
            recovery.BuildSiteRepairWarning
        );
    }

    /// <summary>A successful lookup cannot hide a failed patch; the warning clears after the patch itself recovers.</summary>
    [Fact]
    public async Task FailedSitePatchPreservesWarningUntilRepairSucceeds()
    {
        (ColonizationDeliveryRecovery recovery, RecordingRavenClient client, JournalEventEnvelope docked) =
            await CreateFailedSiteRepairAsync();
        client.SystemSitesResponse =
        [
            new ColonizationSystemSite
            {
                Id = "site-1",
                Name = "Dampier Gateway",
                MarketId = 3_963_024_386,
                Status = ColonizationSystemSiteStatus.Complete,
            },
        ];
        client.SystemSitePatchFailure = new HttpRequestException("site patch unavailable");
        await recovery.SynchronizeLiveEventsAsync([docked], allowPublishing: true);

        Assert.Equal(
            new ColonizationBuildSiteRepairWarning(20, "Docked", "Failing System", "site patch unavailable"),
            recovery.BuildSiteRepairWarning
        );

        client.SystemSitePatchFailure = null;
        IReadOnlyList<ColonizationDeliveryNotice> notices = await recovery.SynchronizeLiveEventsAsync(
            [docked],
            allowPublishing: true
        );
        await recovery.SynchronizeLiveEventsAsync([docked], allowPublishing: true);

        Assert.Null(recovery.BuildSiteRepairWarning);
        AssertNotice(notices, ColonizationDeliveryNoticeKind.BuildSiteMarketRepaired);
        Assert.Equal(2, client.SystemSitePatches.Count);
    }

    /// <summary>A new-system docking event clears the earlier warning before the main window updates system context.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task JournalSystemChangeClearsSiteRepairWarningBeforeContextUpdate(
        bool pendingFailure,
        bool undockFirst
    )
    {
        (ColonizationDeliveryRecovery recovery, RecordingRavenClient client, JournalEventEnvelope docked) =
            await CreateFailedSiteRepairAsync(provideSystemContext: false);
        var response = new TaskCompletionSource<IReadOnlyList<ColonizationSystemSite>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Task? pending = null;
        if (pendingFailure)
        {
            client.SystemSiteResponseTask = response.Task;
            pending = recovery.SynchronizeLiveEventsAsync([docked], allowPublishing: true);
        }
        if (undockFirst)
        {
            recovery.ApplyJournalEvents([Event("Undocked", "\"MarketID\":4310999999")], null);
            Assert.NotNull(recovery.BuildSiteRepairWarning);
        }
        JournalEventEnvelope nextDock = Event(
            "Docked",
            """
            "MarketID":4310999999,"SystemAddress":21,"StarSystem":"Other System",
            "StationName":"Dampier Gateway","StationType":"Outpost"
            """
        );
        recovery.ApplyJournalEvents([nextDock], null);
        Assert.Null(recovery.BuildSiteRepairWarning);
        if (pending is not null)
        {
            response.SetException(new HttpRequestException("late failure from old system"));
            await pending;
        }
        client.SystemSiteResponseTask = null;
        await recovery.SynchronizeLiveEventsAsync([nextDock], allowPublishing: true);

        Assert.Null(recovery.BuildSiteRepairWarning);
        Assert.Equal("21", client.SystemSiteRequests[^1]);
    }

    /// <summary>Repair needs a live event and saved key, and skips carriers and undocked locations.</summary>
    [Fact]
    public async Task SiteRepairHonorsBootstrapCredentialAndDockSafetyGates()
    {
        var client = new RecordingRavenClient();
        ColonizationDeliveryRecovery recovery = Create(client);
        JournalEventEnvelope completedPort = Event(
            "Docked",
            """
            "MarketID":4310999999,"SystemAddress":20,
            "StationName":"Dampier Gateway","StationType":"Outpost"
            """
        );

        await recovery.SynchronizeLiveEventsAsync([completedPort], allowPublishing: true);
        recovery.SetProfile("F123", true, "secret-key");
        await recovery.SynchronizeLiveEventsAsync([completedPort], allowPublishing: false);
        await recovery.SynchronizeLiveEventsAsync(
            [
                Event(
                    "Docked",
                    """
                    "MarketID":4310999999,"SystemAddress":20,
                    "StationName":"ABC-123","StationType":"FleetCarrier"
                    """
                ),
            ],
            allowPublishing: true
        );
        await recovery.SynchronizeLiveEventsAsync(
            [
                Event(
                    "Location",
                    """
                    "Docked":false,"MarketID":4310999999,"SystemAddress":20,
                    "StationName":"Dampier Gateway","StationType":"Outpost"
                    """
                ),
            ],
            allowPublishing: true
        );

        Assert.Equal(0, client.SystemSiteLoadCount);
        Assert.Empty(client.SystemSitePatches);
    }

    /// <summary>Warns about a matched row that cannot be repaired because it lacks its persisted ID.</summary>
    [Fact]
    public async Task MissingRepairIdentityIsReported()
    {
        var client = new RecordingRavenClient
        {
            SystemSitesResponse = [new() { Name = "Port", Status = ColonizationSystemSiteStatus.Complete }],
        };
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "key");
        recovery.UpdateSystemContext("Test", 20, positionChanged: false);
        JournalEventEnvelope dock = Event(
            "Docked",
            "\"MarketID\":4300000123,\"SystemAddress\":20,\"StarSystem\":\"Test\",\"StationName\":\"Port\",\"StationType\":\"Outpost\""
        );

        await ApplyAndSynchronizeAsync(recovery, dock);

        Assert.Equal(
            new ColonizationBuildSiteRepairWarning(20, "Docked", "Test", null),
            recovery.BuildSiteRepairWarning
        );
        Assert.Empty(client.SystemSitePatches);
    }

    /// <summary>Replays a failed docking lookup with immediate retry delays and a known active system.</summary>
    private async Task<(
        ColonizationDeliveryRecovery Recovery,
        RecordingRavenClient Client,
        JournalEventEnvelope Docked
    )> CreateFailedSiteRepairAsync(bool provideSystemContext = true)
    {
        var client = new RecordingRavenClient();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            client.SystemSiteFailures.Enqueue(new HttpRequestException("server unavailable for system 20"));
        }
        ColonizationDeliveryRecovery recovery = Create(client, apiKey: "secret-key");
        if (provideSystemContext)
        {
            recovery.UpdateSystemContext("Failing System", 20, positionChanged: false);
        }
        JournalEventEnvelope docked = Event(
            "Docked",
            """
            "MarketID":4310999999,"SystemAddress":20,"StarSystem":"Failing System",
            "StationName":"Dampier Gateway","StationType":"Outpost"
            """
        );
        await ApplyAndSynchronizeAsync(recovery, docked);
        Assert.Equal("server unavailable for system 20", recovery.BuildSiteRepairWarning?.Detail);
        return (recovery, client, docked);
    }

    private static void ChangeRepairContext(ColonizationDeliveryRecovery recovery, string change)
    {
        switch (change)
        {
            case "system":
                recovery.UpdateSystemContext("Other System", 21, positionChanged: false);
                break;
            case "commander":
                recovery.SetCommander("Other Cmdr");
                break;
            default:
                recovery.IsEnabled = false;
                break;
        }
    }
}
