using System.Net;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class ColonizationDeliveryRecoveryTests
{
    /// <summary>A rejected credit retry publishes its originally retained absolute need, rather than leaving Raven stale.</summary>
    [Fact]
    public async Task RetainedRejectedDeliveryRecoversCreditAndRemainingRequirementsAfterRestart()
    {
        RecordingRavenClient client = RequirementsClient();
        client.ContributionFailures.Enqueue(RejectedCredit());
        ColonizationDeliveryRecovery recovery = Create(client);
        await ApplyAndSynchronizeAsync(recovery, Contribution(25));
        ColonizationPendingContribution retained = Assert.Single(store.LoadPendingContributions());
        Assert.False(retained.OutcomeUnknown);
        Assert.Equal(75, retained.Requirements!.Commodities["steel"]);

        ColonizationDeliveryRecovery restarted = Create(client);
        await restarted.RetryPendingWritesAsync();

        Assert.Equal(2, client.Contributions.Count);
        Assert.Equal(75, Assert.Single(client.Workspace.Projects).Commodities["steel"]);
        Assert.Empty(store.LoadPendingContributions());
    }

    /// <summary>Loss of delivery acknowledgement cannot authorize automatic re-credit; confirmed credit recovers only requirements.</summary>
    [Fact]
    public async Task LostCreditAcknowledgementRequiresVerificationAndPreservesItsRequirementTarget()
    {
        RecordingRavenClient client = RequirementsClient();
        client.ContributionFailures.Enqueue(new HttpRequestException("credit applied, reply lost"));
        ColonizationDeliveryRecovery recovery = Create(client);
        JournalEventEnvelope contribution = Contribution(25);
        await ApplyAndSynchronizeAsync(recovery, contribution);

        ColonizationDeliveryRecovery restarted = Create(client);
        AssertNotice(
            await restarted.RetryPendingWritesAsync(),
            ColonizationDeliveryNoticeKind.ContributionOutcomeUncertain
        );
        Assert.Single(client.Contributions);
        restarted.DismissVerifiedContributions([contribution.RawJson]);
        Assert.Empty(restarted.GetPendingContributions());
        Assert.True(Assert.Single(store.LoadPendingContributions()).CreditAcknowledged);
        Assert.Empty(client.ProjectUpdates);

        ColonizationDeliveryRecovery afterVerification = Create(client);
        await afterVerification.RetryPendingWritesAsync();

        Assert.Single(client.Contributions);
        Assert.Equal(75, Assert.Single(client.Workspace.Projects).RemainingRequired);
        Assert.Empty(store.LoadPendingContributions());
    }

    /// <summary>When the commander verifies absent credit, replay credits once and then updates the persisted absolute requirements.</summary>
    [Fact]
    public async Task VerifiedAbsentCreditRecoversBothPhases()
    {
        RecordingRavenClient client = RequirementsClient();
        client.ContributionFailures.Enqueue(new HttpRequestException("credit not applied, reply lost"));
        ColonizationDeliveryRecovery recovery = Create(client);
        JournalEventEnvelope contribution = Contribution(25);
        await ApplyAndSynchronizeAsync(recovery, contribution);

        ColonizationDeliveryRecovery restarted = Create(client);
        await restarted.RetryVerifiedContributionsAsync([contribution.RawJson]);

        Assert.Equal(2, client.Contributions.Count);
        Assert.Equal(75, Assert.Single(client.Workspace.Projects).RemainingRequired);
        Assert.Empty(store.LoadPendingContributions());
    }

    /// <summary>Rejected and lost absolute-write acknowledgements survive restart without credit replay or a second subtraction.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbsoluteRequirementReplayIsIdempotentAfterLostOrRejectedAcknowledgement(bool applied)
    {
        RecordingRavenClient client = RequirementsClient();
        client.ApplyFailedProjectUpdate = applied;
        client.ProjectUpdateFailures.Enqueue(
            applied ? new HttpRequestException("requirements applied, reply lost") : RejectedCredit()
        );
        ColonizationDeliveryRecovery recovery = Create(client);
        IReadOnlyList<ColonizationDeliveryNotice> notices = await ApplyAndSynchronizeAsync(recovery, Contribution(25));
        AssertNotice(notices, ColonizationDeliveryNoticeKind.ContributionPublished);
        AssertNotice(notices, ColonizationDeliveryNoticeKind.ContributionRequirementsPendingFailed);
        ColonizationPendingContribution retained = Assert.Single(store.LoadPendingContributions());
        Assert.True(retained.CreditAcknowledged);
        Assert.True(retained.OutcomeUnknown);
        Assert.Empty(recovery.GetPendingContributions());
        Assert.Equal(75, retained.Requirements!.Commodities["steel"]);

        ColonizationDeliveryRecovery restarted = Create(client);
        await restarted.RetryPendingWritesAsync();

        Assert.Single(client.Contributions);
        Assert.Equal(75, Assert.Single(client.Workspace.Projects).RemainingRequired);
        Assert.All(client.ProjectUpdates, update => Assert.Equal(75, update.Commodities!["steel"]));
        Assert.Empty(store.LoadPendingContributions());
    }

    /// <summary>Failure before the write-ahead marker stops credit, reports the persistence error, and leaves a safe retry in memory.</summary>
    [Fact]
    public async Task RecoveryMustBeDurableBeforeCreditCanBeSent()
    {
        RecordingRavenClient client = RequirementsClient();
        store.SaveFailure = new IOException("recovery storage unavailable");
        ColonizationDeliveryRecovery recovery = Create(client);
        IReadOnlyList<ColonizationDeliveryNotice> notices = await ApplyAndSynchronizeAsync(recovery, Contribution(25));

        Assert.Empty(client.Contributions);
        Assert.Contains(
            notices,
            notice =>
                notice.Kind == ColonizationDeliveryNoticeKind.ContributionRecoveryNotSaved
                && notice.Detail == "recovery storage unavailable"
        );
        Assert.Empty(client.ProjectUpdates);
        Assert.False(Assert.Single(recovery.GetPendingContributions()).OutcomeUnknown);
        Assert.Contains(
            observer.Statuses,
            notice =>
                notice.Kind == ColonizationDeliveryNoticeKind.ContributionRecoveryNotSaved
                && notice.Detail == "recovery storage unavailable"
        );

        store.SaveFailure = null;
        now = now.AddSeconds(6);
        await recovery.RetryPendingWritesAsync();
        Assert.Single(client.Contributions);
        Assert.Equal(75, Assert.Single(client.Workspace.Projects).RemainingRequired);
    }

    /// <summary>A crash after accepted credit but before durable acknowledgement restores an uncertain marker, never an automatic credit retry.</summary>
    [Fact]
    public async Task LostAcknowledgedPhaseSaveCannotDoubleCreditAfterRestart()
    {
        RecordingRavenClient client = RequirementsClient();
        store.ContributionSaveFailure = count => count >= 2 ? new IOException("disk disconnected after credit") : null;
        ColonizationDeliveryRecovery recovery = Create(client);
        await ApplyAndSynchronizeAsync(recovery, Contribution(25));
        ColonizationPendingContribution durable = Assert.Single(store.LoadPendingContributions());
        Assert.True(durable.OutcomeUnknown);
        Assert.False(durable.CreditAcknowledged);
        Assert.NotNull(durable.Requirements);
        Assert.Empty(client.ProjectUpdates);

        store.ContributionSaveFailure = null;
        ColonizationDeliveryRecovery restarted = Create(client);
        await restarted.RetryPendingWritesAsync();
        Assert.Single(client.Contributions);
        restarted.DismissVerifiedContributions([durable.EventId]);
        now = now.AddSeconds(6);
        await restarted.RetryPendingWritesAsync();

        Assert.Single(client.Contributions);
        Assert.Equal(75, Assert.Single(client.Workspace.Projects).RemainingRequired);
        Assert.Empty(store.LoadPendingContributions());
    }

    /// <summary>A crash after requirements apply but before local retirement leaves an acknowledged absolute write that can safely repeat.</summary>
    [Fact]
    public async Task FailedRetirementSaveReplaysRequirementsWithoutCreditOrSubtraction()
    {
        RecordingRavenClient client = RequirementsClient();
        store.ContributionSaveFailure = count => count == 4 ? new IOException("crash during retirement") : null;
        ColonizationDeliveryRecovery recovery = Create(client);
        await ApplyAndSynchronizeAsync(recovery, Contribution(25));
        Assert.True(Assert.Single(store.LoadPendingContributions()).CreditAcknowledged);
        Assert.Equal(75, Assert.Single(client.Workspace.Projects).RemainingRequired);

        ColonizationDeliveryRecovery restarted = Create(client);
        await restarted.RetryPendingWritesAsync();

        Assert.Single(client.Contributions);
        Assert.Equal(75, Assert.Single(client.Workspace.Projects).RemainingRequired);
        Assert.Empty(store.LoadPendingContributions());
    }

    /// <summary>Multiple journal deliveries retain cumulative absolute need, and recovering an older delivery cannot undo later progress.</summary>
    [Fact]
    public async Task MultipleRetainedDeliveriesRecoverCumulativeNeedInOrder()
    {
        RecordingRavenClient client = RequirementsClient();
        client.ContributionFailures.Enqueue(RejectedCredit());
        client.ContributionFailures.Enqueue(RejectedCredit());
        ColonizationDeliveryRecovery recovery = Create(client);
        await ApplyAndSynchronizeAsync(recovery, Contribution(25), Contribution(15));
        Assert.Equal(
            [75, 60],
            store.LoadPendingContributions().Select(item => item.Requirements!.Commodities["steel"])
        );

        ColonizationDeliveryRecovery restarted = Create(client);
        await restarted.RetryPendingWritesAsync();

        Assert.Equal(4, client.Contributions.Count);
        Assert.Equal(60, Assert.Single(client.Workspace.Projects).RemainingRequired);
        Assert.Empty(store.LoadPendingContributions());
    }

    /// <summary>Older absolute recovery never raises remote need that has already fallen due to other commanders' deliveries.</summary>
    [Fact]
    public async Task RequirementRecoveryPreservesNewerRemoteProgress()
    {
        RecordingRavenClient client = RequirementsClient();
        client.ProjectUpdateFailures.Enqueue(RejectedCredit());
        ColonizationDeliveryRecovery recovery = Create(client);
        await ApplyAndSynchronizeAsync(recovery, Contribution(25));
        client.Workspace = new([Project("build-1", "Port", 20, 10, 20)], [], null, []);

        await Create(client).RetryPendingWritesAsync();

        Assert.Single(client.Contributions);
        Assert.Equal(20, Assert.Single(client.Workspace.Projects).RemainingRequired);
        Assert.Equal(20, client.ProjectUpdates[^1].Commodities!["steel"]);
    }

    /// <summary>Matching later journal depot state supersedes older requirements; completion is recovered without reopening the site.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequirementRecoveryUsesNewerDepotAndCompletion(bool complete)
    {
        RecordingRavenClient client = RequirementsClient();
        client.ProjectUpdateFailures.Enqueue(RejectedCredit());
        ColonizationDeliveryRecovery recovery = Create(client);
        await ApplyAndSynchronizeAsync(recovery, Contribution(25));
        ColonizationDeliveryRecovery restarted = Create(client);
        JournalEventEnvelope newer = WithTimestamp(Depot(100, complete ? 100 : 40, complete), "2026-07-24T12:00:10Z");
        restarted.ApplyJournalEvents([newer], null);

        await restarted.RetryPendingWritesAsync();

        Assert.Single(client.Contributions);
        Assert.Equal(complete ? 1 : 0, client.MarkCompleteCount);
        Assert.Equal(complete ? 0 : 60, Assert.Single(restarted.Projects).RemainingRequired);
        Assert.Empty(store.LoadPendingContributions());
    }

    /// <summary>A depot following deliveries in the same poll stays authoritative even after undocking, avoiding a second subtraction.</summary>
    [Fact]
    public async Task FollowingDepotCoversEveryDeliveryBeforeBatchUndock()
    {
        RecordingRavenClient client = RequirementsClient();
        ColonizationDeliveryRecovery recovery = Create(client);
        await ApplyAndSynchronizeAsync(
            recovery,
            ConstructionDock(),
            Contribution(25),
            Contribution(15),
            Depot(100, 40),
            Event("Undocked", "")
        );

        Assert.Equal(2, client.Contributions.Count);
        Assert.Equal(60, Assert.Single(client.Workspace.Projects).RemainingRequired);
        Assert.All(client.ProjectUpdates, update => Assert.Equal(60, update.Commodities!["steel"]));
    }

    /// <summary>A remote completed site makes obsolete requirement recovery a no-op, while credit history stays acknowledged.</summary>
    [Fact]
    public async Task CompletedRemoteSiteRetiresRequirementsWithoutAnyMutation()
    {
        RecordingRavenClient client = RequirementsClient();
        client.ProjectUpdateFailures.Enqueue(RejectedCredit());
        ColonizationDeliveryRecovery recovery = Create(client);
        await ApplyAndSynchronizeAsync(recovery, Contribution(25));
        client.Workspace = new([Project("build-1", "Port", 0, 10, 20) with { IsComplete = true }], [], null, []);

        await Create(client).RetryPendingWritesAsync();

        Assert.Single(client.Contributions);
        Assert.Single(client.ProjectUpdates);
        Assert.Equal(0, client.MarkCompleteCount);
        Assert.Empty(store.LoadPendingContributions());
    }

    /// <summary>Requirements follow the originating Frontier profile even after credit has been acknowledged and hidden from reconciliation.</summary>
    [Fact]
    public async Task AcknowledgedRequirementsCannotMigrateAcrossCommanderProfiles()
    {
        RecordingRavenClient client = RequirementsClient();
        client.ProjectUpdateFailures.Enqueue(RejectedCredit());
        ColonizationDeliveryRecovery recovery = Create(client);
        await ApplyAndSynchronizeAsync(recovery, Contribution(25));
        recovery.SetProfile("F456", true, null);
        now = now.AddSeconds(6);
        await recovery.RetryPendingWritesAsync();
        Assert.Single(client.ProjectUpdates);
        Assert.True(Assert.Single(store.LoadPendingContributions()).CreditAcknowledged);
        recovery.SetProfile("F123", true, null);
        now = now.AddSeconds(6);
        await recovery.RetryPendingWritesAsync();

        Assert.Single(client.Contributions);
        Assert.Equal(75, Assert.Single(client.Workspace.Projects).RemainingRequired);
        Assert.Empty(store.LoadPendingContributions());
    }

    /// <summary>Legacy definite rejections gain a durable absolute target before being credited, preserving existing settings compatibility.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyRetainedDeliveryIsMigratedBeforeCreditReplay(bool unknownMarket)
    {
        RecordingRavenClient client = RequirementsClient();
        if (unknownMarket)
        {
            client.Workspace = new([Project("build-1", "Port", 100)], [], null, []);
        }
        store.SavePendingContributions([
            new(
                "Test Cmdr|F123|True",
                "build-1",
                "Test Cmdr",
                new() { ["steel"] = 25 },
                unknownMarket ? "legacy" : Contribution(25).RawJson,
                false
            ),
        ]);

        await Create(client).RetryPendingWritesAsync();

        Assert.Single(client.Contributions);
        Assert.Equal(75, Assert.Single(client.Workspace.Projects).RemainingRequired);
        Assert.Empty(store.LoadPendingContributions());
    }

    /// <summary>Completion intent is persisted before its remote notification, so restart retries completion without restoring stale need.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedCompletionRecoverySurvivesAnotherRestart(bool staleIncomplete)
    {
        RecordingRavenClient client = RequirementsClient();
        client.ProjectUpdateFailures.Enqueue(RejectedCredit());
        ColonizationDeliveryRecovery recovery = Create(client);
        await ApplyAndSynchronizeAsync(recovery, Contribution(25));
        ColonizationDeliveryRecovery restarted = Create(client);
        restarted.ApplyJournalEvents([WithTimestamp(Depot(100, 100, true), "2026-07-24T12:00:10Z")], null);
        client.ProjectCompletionFailures.Enqueue(new HttpRequestException("completion reply unavailable"));

        AssertNotice(
            await restarted.RetryPendingWritesAsync(),
            ColonizationDeliveryNoticeKind.ContributionRequirementsPendingFailed
        );
        Assert.True(Assert.Single(store.LoadPendingContributions()).Requirements!.Depot!.IsComplete);

        ColonizationDeliveryRecovery afterCompletionFailure = Create(client);
        if (staleIncomplete)
        {
            afterCompletionFailure.ApplyJournalEvents(
                [WithTimestamp(Depot(100, 100, false), "2026-07-24T12:00:10Z")],
                null
            );
        }
        await afterCompletionFailure.RetryPendingWritesAsync();

        Assert.Single(client.Contributions);
        Assert.Single(client.ProjectUpdates);
        Assert.Equal(2, client.MarkCompleteCount);
        Assert.True(Assert.Single(afterCompletionFailure.Projects).IsComplete);
        Assert.Empty(store.LoadPendingContributions());
    }

    /// <summary>Legacy verified credit has no saved pre-credit baseline; current Raven need is preserved instead of guessing another subtraction.</summary>
    [Fact]
    public async Task LegacyVerifiedCreditCannotSubtractAlreadyAppliedRequirementsAgain()
    {
        RecordingRavenClient client = RequirementsClient();
        client.Workspace = new([Project("build-1", "Port", 75, 10, 20)], [], null, []);
        string eventId = Contribution(25).RawJson;
        store.SavePendingContributions([
            new("Test Cmdr|F123|True", "build-1", "Test Cmdr", new() { ["steel"] = 25 }, eventId, true),
        ]);
        ColonizationDeliveryRecovery recovery = Create(client);
        recovery.DismissVerifiedContributions([eventId]);

        await Create(client).RetryPendingWritesAsync();

        Assert.Empty(client.Contributions);
        Assert.Equal(75, Assert.Single(client.Workspace.Projects).RemainingRequired);
        Assert.Empty(store.LoadPendingContributions());
    }

    /// <summary>An unavailable remote project leaves acknowledged requirements retained rather than re-crediting or dropping work.</summary>
    [Fact]
    public async Task MissingProjectKeepsAcknowledgedRequirementsRecoverable()
    {
        RecordingRavenClient client = RequirementsClient();
        client.ProjectUpdateFailures.Enqueue(RejectedCredit());
        ColonizationDeliveryRecovery recovery = Create(client);
        await ApplyAndSynchronizeAsync(recovery, Contribution(25));
        client.Workspace = new([], [], null, []);

        AssertNotice(
            await Create(client).RetryPendingWritesAsync(),
            ColonizationDeliveryNoticeKind.ContributionRequirementsPendingFailed
        );
        Assert.Single(client.Contributions);
        Assert.True(Assert.Single(store.LoadPendingContributions()).CreditAcknowledged);
    }

    /// <summary>A following authoritative depot initializes real need over missing and negative template slots while keeping known progress.</summary>
    [Theory]
    [InlineData(-1, 75)]
    [InlineData(null, 75)]
    [InlineData(0, 0)]
    [InlineData(20, 20)]
    public async Task RequirementRecoveryInitializesTemplateSlotsWithoutRaisingKnownNeed(int? remoteNeed, int expected)
    {
        RecordingRavenClient client = RequirementsClient();
        Dictionary<string, int> commodities = remoteNeed is { } value ? new() { ["steel"] = value } : [];
        client.Workspace = new(
            [Project("build-1", "Port", remoteNeed ?? 0, 10, 20) with { Commodities = commodities }],
            [],
            null,
            []
        );
        ColonizationDeliveryRecovery recovery = Create(client);

        JournalEventEnvelope contribution = Contribution(25);
        recovery.ApplyJournalEvents([ConstructionDock(), contribution, Depot(100, 25)], null);
        await recovery.SynchronizeLiveEventsAsync([contribution], allowPublishing: true);

        Assert.Single(client.Contributions);
        Assert.Equal(expected, client.ProjectUpdates[^1].Commodities!["steel"]);
        Assert.Equal(expected, Assert.Single(client.Workspace.Projects).Commodities["steel"]);
        Assert.Empty(store.LoadPendingContributions());
    }

    /// <summary>A real site lookup preserves unknown template slots until preceding or following depot need initializes them.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SiteLookupKeepsTemplateInitializationAcrossJournalOrderAndUndock(bool following, bool omitted)
    {
        Dictionary<string, int> commodities = omitted ? [] : new() { ["steel"] = -1 };
        commodities["titanium"] = -1;
        var client = new RecordingRavenClient
        {
            SiteProjectResponse = Project("build-1", "Port", 0, 10, 20) with { Commodities = commodities },
        };
        ColonizationDeliveryRecovery recovery = Create(client);
        JournalEventEnvelope dock = ConstructionDock();
        JournalEventEnvelope contribution = following
            ? Contribution(25)
            : WithTimestamp(Contribution(25), "2026-07-24T12:00:10Z");
        JournalEventEnvelope depot = Depot(100, following ? 25 : 0);
        JournalEventEnvelope[] events = following
            ? [dock, contribution, depot, Event("Undocked", "")]
            : [dock, depot, contribution];
        recovery.ApplyJournalEvents(events, null);

        await recovery.SynchronizeLiveEventsAsync([dock, contribution], allowPublishing: true);

        Assert.Equal(1, client.SiteProjectLoadCount);
        Assert.Single(client.Contributions);
        Assert.Equal(75, client.SiteProjectResponse.Commodities["steel"]);
        Assert.Equal(0, client.SiteProjectResponse.Commodities["titanium"]);
        if (following)
        {
            Assert.Equal(100, client.SiteProjectResponse.MaximumRequired);
        }
        Assert.Equal(75, Assert.Single(recovery.Projects).Commodities["steel"]);
        Assert.Empty(store.LoadPendingContributions());
    }

    /// <summary>Finishing an old profile's identical journal entry cannot release another profile's active credit guard.</summary>
    [Fact]
    public async Task InFlightDeliveryOwnershipSurvivesIdenticalJournalEntriesAcrossProfiles()
    {
        RecordingRavenClient client = RequirementsClient();
        var oldGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var newGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ContributionGate = oldGate.Task;
        ColonizationDeliveryRecovery recovery = Create(client);
        JournalEventEnvelope contribution = Contribution(25);
        Task<IReadOnlyList<ColonizationDeliveryNotice>> oldUpload = ApplyAndSynchronizeAsync(recovery, contribution);
        recovery.SetCommander("Other Cmdr");
        LoadWorkspace(recovery, client);
        client.ContributionGate = newGate.Task;
        Task<IReadOnlyList<ColonizationDeliveryNotice>> newUpload = ApplyAndSynchronizeAsync(recovery, contribution);

        oldGate.SetResult();
        await oldUpload;
        Assert.False(recovery.CanReconcileContributions);
        Assert.Null(await recovery.RetryVerifiedContributionsAsync([contribution.RawJson]));
        recovery.DismissVerifiedContributions([contribution.RawJson]);
        Assert.Single(recovery.GetPendingContributions());

        newGate.SetResult();
        await newUpload;
        Assert.True(recovery.CanReconcileContributions);
        Assert.Equal(["Test Cmdr", "Other Cmdr"], client.Contributions.Select(item => item.CommanderName));
        Assert.Empty(recovery.GetPendingContributions());
        Assert.True(Assert.Single(store.LoadPendingContributions()).CreditAcknowledged);
    }

    /// <summary>A preceding same-time completed depot remains durable even when Raven already records zero need.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ZeroNeedCompletionSurvivesUndockAndRestart(bool rejectCredit, bool loseCreditAcknowledgement)
    {
        RecordingRavenClient client = RequirementsClient();
        client.Workspace = new([Project("build-1", "Port", 0, 10, 20)], [], null, []);
        client.ProjectCompletionFailures.Enqueue(RejectedCredit());
        if (rejectCredit || loseCreditAcknowledgement)
        {
            client.ContributionFailures.Enqueue(
                rejectCredit ? RejectedCredit() : new HttpRequestException("credit acknowledgement lost")
            );
        }
        else
        {
            client.ProjectCompletionFailures.Enqueue(new HttpRequestException("completion acknowledgement lost"));
        }
        var creditGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ContributionGate = creditGate.Task;
        ColonizationDeliveryRecovery recovery = Create(client);
        JournalEventEnvelope contribution = Contribution(25);
        Task<IReadOnlyList<ColonizationDeliveryNotice>> upload = ApplyAndSynchronizeAsync(
            recovery,
            ConstructionDock(),
            Depot(100, 100, true),
            contribution,
            Event("Undocked", "")
        );
        ColonizationPendingContribution beforeCredit = Assert.Single(store.LoadPendingContributions());
        creditGate.SetResult();
        await upload;

        Assert.Null(recovery.CreateConstructionSnapshot().CurrentDepot);
        Assert.Equal(0, beforeCredit.Requirements!.Commodities["steel"]);
        Assert.True(beforeCredit.Requirements.Depot?.IsComplete);
        ColonizationPendingContribution retained = Assert.Single(store.LoadPendingContributions());
        Assert.True(retained.Requirements!.Depot?.IsComplete);
        Assert.Equal(!rejectCredit && !loseCreditAcknowledgement, retained.CreditAcknowledged);

        ColonizationDeliveryRecovery restarted = Create(client);
        if (loseCreditAcknowledgement)
        {
            AssertNotice(
                await restarted.RetryPendingWritesAsync(),
                ColonizationDeliveryNoticeKind.ContributionOutcomeUncertain
            );
            Assert.Single(client.Contributions);
            restarted.DismissVerifiedContributions([contribution.RawJson]);
            now = now.AddSeconds(6);
        }
        await restarted.RetryPendingWritesAsync();

        Assert.Equal(rejectCredit ? 2 : 1, client.Contributions.Count);
        Assert.Equal(rejectCredit || loseCreditAcknowledgement ? 2 : 3, client.MarkCompleteCount);
        Assert.True(Assert.Single(restarted.Projects).IsComplete);
        Assert.Empty(client.ProjectUpdates);
        Assert.Empty(store.LoadPendingContributions());
    }

    private static RecordingRavenClient RequirementsClient() =>
        new() { Workspace = new([Project("build-1", "Port", 100, 10, 20)], [], null, []) };

    private static RavenColonialServiceException RejectedCredit() =>
        new(HttpStatusCode.TooManyRequests, "write", "try later");

    private static JournalEventEnvelope WithTimestamp(JournalEventEnvelope journalEvent, string timestamp)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(
                journalEvent.RawJson.Replace("2026-07-24T12:00:00Z", timestamp, StringComparison.Ordinal),
                out JournalEventEnvelope? result,
                out string? error
            ),
            error
        );
        return result!;
    }
}
