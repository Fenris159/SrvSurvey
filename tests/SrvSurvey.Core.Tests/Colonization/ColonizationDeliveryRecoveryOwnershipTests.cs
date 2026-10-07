using System.Net;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class ColonizationDeliveryRecoveryTests
{
    /// <summary>The recovery interface protects an uploading delivery from replay or dismissal until it finishes.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveContributionCannotBeReconciledBeforeAcknowledgement(bool reject)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingRavenClient
        {
            Workspace = new([Project("build-1", "Port", 100, 10, 20)], [], null, []),
            ContributionGate = gate.Task,
        };
        if (reject)
        {
            client.ContributionFailures.Enqueue(
                new RavenColonialServiceException(HttpStatusCode.TooManyRequests, "contribute", "retry later")
            );
        }
        ColonizationDeliveryRecovery recovery = Create(client);
        JournalEventEnvelope contribution = Contribution(25);
        Task<IReadOnlyList<ColonizationDeliveryNotice>> upload = ApplyAndSynchronizeAsync(recovery, contribution);
        string[] selected = [contribution.RawJson];

        Assert.False(recovery.CanReconcileContributions);
        Assert.Null(await recovery.RetryVerifiedContributionsAsync(selected));
        recovery.DismissVerifiedContributions(selected);
        Assert.Single(recovery.GetPendingContributions());
        Assert.Single(client.Contributions);

        gate.SetResult();
        await upload;

        Assert.True(recovery.CanReconcileContributions);
        Assert.Single(client.Contributions);
        if (reject)
        {
            Assert.False(Assert.Single(recovery.GetPendingContributions()).OutcomeUnknown);
        }
        else
        {
            Assert.Empty(recovery.GetPendingContributions());
        }
    }

    /// <summary>Explicit recovery protects the sending delivery from overlapping retries and dismissal.</summary>
    [Fact]
    public async Task VerifiedRetryCannotOverlapAnotherRetryOrDismissItsDelivery()
    {
        var client = new RecordingRavenClient
        {
            Workspace = new([Project("build-1", "Port", 100, 10, 20)], [], null, []),
        };
        client.ContributionFailures.Enqueue(new HttpRequestException("lost acknowledgement"));
        ColonizationDeliveryRecovery recovery = Create(client);
        JournalEventEnvelope contribution = Contribution(25);
        await ApplyAndSynchronizeAsync(recovery, contribution);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ContributionGate = gate.Task;
        string[] selected = [contribution.RawJson];
        Task<IReadOnlyList<ColonizationDeliveryNotice>?> first = recovery.RetryVerifiedContributionsAsync(selected);

        Assert.Null(await recovery.RetryVerifiedContributionsAsync(selected));
        recovery.DismissVerifiedContributions(selected);
        Assert.Single(recovery.GetPendingContributions());
        Assert.Equal(2, client.Contributions.Count);

        gate.SetResult();
        Assert.Empty((await first)!);
        Assert.Empty(recovery.GetPendingContributions());
        Assert.True(recovery.CanReconcileContributions);
    }

    /// <summary>Disabling integration rejects verified upload while leaving confirmed-credit dismissal available.</summary>
    [Fact]
    public async Task DisabledIntegrationCannotRetryVerifiedContribution()
    {
        var client = new RecordingRavenClient
        {
            Workspace = new([Project("build-1", "Port", 100, 10, 20)], [], null, []),
        };
        client.ContributionFailures.Enqueue(new HttpRequestException("lost acknowledgement"));
        ColonizationDeliveryRecovery recovery = Create(client);
        JournalEventEnvelope contribution = Contribution(25);
        await ApplyAndSynchronizeAsync(recovery, contribution);
        recovery.IsEnabled = false;

        Assert.Null(await recovery.RetryVerifiedContributionsAsync([contribution.RawJson]));
        Assert.Single(client.Contributions);
        Assert.Single(recovery.GetPendingContributions());
        recovery.DismissVerifiedContributions([contribution.RawJson]);
        Assert.Empty(recovery.GetPendingContributions());
    }

    /// <summary>Project results from a superseded profile cannot be linked, installed, or used to credit its journal delivery.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SupersededProjectLookupCannotPublishForReplacementProfile(bool contribution, bool profileOnly)
    {
        var gate = new TaskCompletionSource<ColonizationProject?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingRavenClient { SiteProjectResponseTask = gate.Task };
        ColonizationDeliveryRecovery recovery = Create(client);
        JournalEventEnvelope[] events = contribution ? [Contribution(25)] : [ConstructionDock()];
        recovery.UpdateSystemContext("Test System", 20, positionChanged: false);
        Task<IReadOnlyList<ColonizationDeliveryNotice>> synchronization = ApplyAndSynchronizeAsync(recovery, events);
        if (profileOnly)
        {
            recovery.SetProfile("F456", true, null);
        }
        else
        {
            recovery.SetCommander("Other Cmdr");
        }
        ColonizationProject replacement = Project("replacement", "New port", 50, 30, 40);
        recovery.ReplaceWorkspace([replacement], []);

        gate.SetResult(Project("old-build", "Old port", 100, 10, 20, architectName: recovery.CommanderName));

        Assert.Empty(await synchronization);
        Assert.Empty(client.LinkRequests);
        Assert.Empty(client.Contributions);
        Assert.Empty(recovery.GetPendingContributions());
        Assert.Equal(replacement, Assert.Single(recovery.Projects));
    }

    /// <summary>A late metadata or remaining-requirements acknowledgement never repopulates the replacement workspace.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersededProjectPatchCannotRepopulateReplacementWorkspace(bool depot)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingRavenClient
        {
            Workspace = new([Project("old-build", "Old port", 100, 10, 20)], [], null, []),
            UpdateProjectGate = gate.Task,
        };
        ColonizationDeliveryRecovery recovery = Create(client);
        JournalEventEnvelope journalEvent = depot ? Depot(100, 25) : ConstructionDock("Builders");
        Task<IReadOnlyList<ColonizationDeliveryNotice>> synchronization = ApplyAndSynchronizeAsync(
            recovery,
            journalEvent
        );
        recovery.SetCommander("Other Cmdr");
        ColonizationProject replacement = Project("replacement", "New port", 50, 30, 40);
        recovery.ReplaceWorkspace([replacement], []);

        gate.SetResult();

        Assert.Empty(await synchronization);
        Assert.Equal(replacement, Assert.Single(recovery.Projects));
        Assert.Equal(0, observer.ProjectChanges);
    }

    /// <summary>A completed old delivery retires its recovery record without publishing requirements into a new profile.</summary>
    [Fact]
    public async Task SupersededContributionAcknowledgementRetiresOnlyItsOriginatingRecord()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingRavenClient
        {
            Workspace = new([Project("old-build", "Old port", 100, 10, 20)], [], null, []),
            ContributionGate = gate.Task,
        };
        ColonizationDeliveryRecovery recovery = Create(client);
        Task<IReadOnlyList<ColonizationDeliveryNotice>> synchronization = ApplyAndSynchronizeAsync(
            recovery,
            Contribution(25)
        );
        recovery.SetCommander("Other Cmdr");
        ColonizationProject replacement = Project("replacement", "New port", 50, 30, 40);
        recovery.ReplaceWorkspace([replacement], []);

        gate.SetResult();

        Assert.Empty(await synchronization);
        Assert.Equal("Test Cmdr", Assert.Single(client.Contributions).CommanderName);
        Assert.Empty(client.ProjectUpdates);
        Assert.Empty(store.LoadPendingContributions());
        Assert.Equal(replacement, Assert.Single(recovery.Projects));
    }
}
