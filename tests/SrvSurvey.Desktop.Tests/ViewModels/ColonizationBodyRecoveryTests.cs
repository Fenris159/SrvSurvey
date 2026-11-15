using System.Net;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationBodyRecoveryTests
{
    [Fact]
    public async Task ACompletionReadDuringBackfillStillStartsItsCheckWhenDeliveredLive()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with { Bodies = [fixture.Record.Bodies![0]] };
        JournalEventEnvelope complete = Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}") with
        {
            SourceContentIdentity = "completion-record",
        };
        fixture.Editor.BodyJournalHistoryReader = (_, _) =>
            Task.FromResult<IReadOnlyList<JournalEventEnvelope>>([.. LocalScans(), complete]);
        await fixture.LoadAsync();
        Assert.Equal(0, fixture.BodyWrites);
        fixture.Editor.ApplyJournalEvents([complete]);
        await fixture.Editor.BodyCatalogUpdateTask;
        Assert.Equal(1, fixture.BodyWrites);
        Assert.Equal([0, 1], fixture.Published!.Select(body => body.Number));
    }

    [Fact]
    public async Task ALegacyPrefixCannotCompleteTheLiveCatalogAfterASameAccountRotation()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with { Bodies = [fixture.Record.Bodies![0]] };
        using MainWindowViewModel main = fixture.CreateMain();
        await main.ApplyMonitorUpdateAsync(
            new(
                null,
                [
                    Event("{\"event\":\"Fileheader\",\"Odyssey\":false}"),
                    Event("{\"event\":\"Commander\",\"Name\":\"Test Cmdr\",\"FID\":\"F1\"}"),
                    Event("{\"event\":\"LoadGame\",\"Commander\":\"Test Cmdr\",\"FID\":\"F1\",\"Odyssey\":false}"),
                    Event("{\"event\":\"Location\",\"StarSystem\":\"Example\",\"SystemAddress\":42}"),
                ],
                null,
                null,
                null,
                null,
                [],
                true
            ),
            true
        );
        main.Colonization.IsEnabled = true;
        main.Colonization.UpdateSystemContext("Example", null, 42);
        await main.Colonization.SystemEditor.LoadAsync();
        await main.ApplyMonitorUpdateAsync(
            new(
                null,
                [
                    LocalScans()[1],
                    Event("{\"event\":\"Fileheader\",\"Odyssey\":true}"),
                    Event("{\"event\":\"Commander\",\"Name\":\"Test Cmdr\",\"FID\":\"F1\"}"),
                    Event("{\"event\":\"LoadGame\",\"Commander\":\"Test Cmdr\",\"FID\":\"F1\",\"Odyssey\":true}"),
                    LocalScans()[0],
                    Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"),
                ],
                null,
                null,
                null,
                null,
                [],
                false
            ),
            true
        );
        await main.Colonization.SystemEditor.BodyCatalogUpdateTask;
        Assert.Equal(0, fixture.BodyWrites);
        Assert.Contains("1/2", main.Colonization.SystemEditor.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifiedStartupSitesSurviveAnAwaitingProbeAndTheNextUnidentifiedHeader()
    {
        using var fixture = new Fixture();
        using MainWindowViewModel main = fixture.CreateMain();
        main.Colonization.IsEnabled = true;
        await main.ApplyMonitorUpdateAsync(
            new(
                null,
                [
                    Event("{\"event\":\"Fileheader\",\"Odyssey\":true}"),
                    Event("{\"event\":\"Commander\",\"Name\":\"Test Cmdr\",\"FID\":\"F1\"}"),
                    Event("{\"event\":\"Location\",\"StarSystem\":\"Example\",\"SystemAddress\":42}"),
                    Event(
                        "{\"event\":\"ApproachSettlement\",\"SystemAddress\":42,\"BodyID\":1,\"Name\":\"Site\",\"MarketID\":321}"
                    ),
                    Event("{\"event\":\"Shutdown\"}"),
                ],
                null,
                null,
                null,
                null,
                [],
                true
            )
            {
                IsAwaitingCommanderIdentity = true,
            },
            true
        );
        main.Colonization.UpdateSystemContext("Example", null, 42);
        await main.Colonization.SystemEditor.LoadAsync();
        Assert.Equal(321, main.Colonization.SystemEditor.Sites[0].MarketId);
        Assert.False(main.Colonization.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
        await main.ApplyMonitorUpdateAsync(new(null, [], null, null, null, null, [], false), true);
        Assert.True(main.Colonization.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
        await main.ApplyMonitorUpdateAsync(
            new(null, [Event("{\"event\":\"Fileheader\",\"Odyssey\":true}")], null, null, null, null, [], false),
            true
        );
        await main.Colonization.SystemEditor.LoadAsync();
        Assert.Equal(321, main.Colonization.SystemEditor.Sites[0].MarketId);
        Assert.True(main.Colonization.SystemEditor.HasLocalChanges);
        Assert.False(main.Colonization.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
        Assert.Equal(0, fixture.BodyWrites);
    }

    [Fact]
    public async Task LegacyVersionOnlyHeadersCannotPublishBodiesForTheLiveProfile()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with { Bodies = [fixture.Record.Bodies![0]] };
        using MainWindowViewModel main = fixture.CreateMain();
        await main.ApplyMonitorUpdateAsync(
            new(
                null,
                [
                    Event("{\"event\":\"Fileheader\",\"Odyssey\":true}"),
                    Event("{\"event\":\"Commander\",\"Name\":\"Test Cmdr\",\"FID\":\"F1\"}"),
                    Event("{\"event\":\"LoadGame\",\"Commander\":\"Test Cmdr\",\"FID\":\"F1\",\"Odyssey\":true}"),
                    Event("{\"event\":\"Location\",\"StarSystem\":\"Example\",\"SystemAddress\":42}"),
                ],
                null,
                null,
                null,
                null,
                [],
                true
            ),
            true
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Test Cmdr");
        main.Colonization.UpdateSystemContext("Example", null, 42);
        await main.Colonization.SystemEditor.LoadAsync();
        await main.ApplyMonitorUpdateAsync(
            new(
                null,
                [
                    Event("{\"event\":\"Fileheader\",\"gameversion\":\"3.8.0.0\"}"),
                    Event("{\"event\":\"Commander\",\"Name\":\"Test Cmdr\",\"FID\":\"F1\"}"),
                    Event("{\"event\":\"LoadGame\",\"Commander\":\"Test Cmdr\",\"FID\":\"F1\",\"Odyssey\":false}"),
                    .. LocalScans(),
                    Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"),
                ],
                null,
                null,
                null,
                null,
                [],
                false
            ),
            true
        );
        await main.Colonization.SystemEditor.BodyCatalogUpdateTask;
        Assert.Equal(0, fixture.BodyWrites);
        Assert.False(main.Colonization.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
    }

    [Fact]
    public async Task ANewCommandersBatchCannotAdoptRecordsFlushedBeforeItsIdentityBoundary()
    {
        using var fixture = new Fixture();
        using MainWindowViewModel main = fixture.CreateMain();
        await main.ApplyMonitorUpdateAsync(
            new(
                null,
                [Event("{\"event\":\"Commander\",\"Name\":\"Other Cmdr\",\"FID\":\"F-A\"}")],
                null,
                null,
                null,
                null,
                [],
                true
            ),
            true
        );
        main.Colonization.IsEnabled = true;
        await main.ApplyMonitorUpdateAsync(
            new(
                null,
                [
                    .. LocalScans(),
                    Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"),
                    Event("{\"event\":\"Fileheader\",\"Odyssey\":true}"),
                    Event("{\"event\":\"Commander\",\"Name\":\"Test Cmdr\",\"FID\":\"F-B\"}"),
                    Event("{\"event\":\"Location\",\"StarSystem\":\"Example\",\"SystemAddress\":42}"),
                ],
                null,
                null,
                null,
                null,
                [],
                false
            ),
            true
        );
        await main.Colonization.SetCommanderAsync("Test Cmdr");
        main.Colonization.UpdateSystemContext("Example", null, 42);
        await main.Colonization.SystemEditor.LoadAsync();
        Assert.True(main.Colonization.SystemEditor.IsLoaded);
        await main.Colonization.SystemEditor.UploadScannedBodiesAsync();
        Assert.Equal(0, fixture.BodyWrites);
    }

    [Fact]
    public async Task AnIdentitylessHeaderCancelsRecoveryAndStaysSuspendedOnTheFollowingPoll()
    {
        using var fixture = new Fixture();
        using MainWindowViewModel main = fixture.CreateMain();
        await main.ApplyMonitorUpdateAsync(
            new(
                null,
                [
                    Event("{\"event\":\"Commander\",\"Name\":\"Test Cmdr\",\"FID\":\"F1\"}"),
                    Event("{\"event\":\"Location\",\"StarSystem\":\"Example\",\"SystemAddress\":42}"),
                ],
                null,
                null,
                null,
                null,
                [],
                true
            ),
            true
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Test Cmdr");
        main.Colonization.UpdateSystemContext("Example", null, 42);
        await main.Colonization.SystemEditor.LoadAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DelayProvider = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        Task recovery = main.Colonization.SystemEditor.RediscoverBodiesAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await main.ApplyMonitorUpdateAsync(
            new(null, [Event("{\"event\":\"Fileheader\",\"Odyssey\":true}")], null, null, null, null, [], false),
            true
        );
        await main.ApplyMonitorUpdateAsync(new(null, [], null, null, null, null, [], false), true);
        release.SetResult();
        await recovery;
        Assert.Equal(0, fixture.BodyWrites);
        Assert.False(main.Colonization.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
        await main.ApplyMonitorUpdateAsync(
            new(
                null,
                [Event("{\"event\":\"Commander\",\"Name\":\"Test Cmdr\",\"FID\":\"F1\"}")],
                null,
                null,
                null,
                null,
                [],
                false
            ),
            true
        );
        Assert.True(main.Colonization.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
    }

    [Fact]
    public async Task BufferedScansCannotRestoreBodyIdentitiesOutsideTheTenJumpWindow()
    {
        using var fixture = new Fixture();
        fixture.Editor.UpdateContext(new(true, "Test Cmdr", "Example", 42, "test-key"));
        JournalEventEnvelope star = Event(
            "{\"timestamp\":\"2026-11-15T12:12:00Z\",\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":0,\"BodyName\":\"Example\",\"StarType\":\"G\"}"
        );
        JournalEventEnvelope complete = Event(
            "{\"timestamp\":\"2026-11-15T12:13:00Z\",\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"
        );
        fixture.Editor.BodyJournalHistoryReader = (_, _) =>
            Task.FromResult<IReadOnlyList<JournalEventEnvelope>>([star, complete]);
        fixture.Editor.ApplyJournalEvents(
            [
                Event(
                    "{\"timestamp\":\"2026-11-15T12:00:00Z\",\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":1,\"BodyName\":\"Example 1\",\"PlanetClass\":\"Rocky body\",\"TidalLock\":true}"
                ),
                .. Enumerable
                    .Range(1, 11)
                    .Select(minute =>
                        Event(
                            $$"""{"timestamp":"2026-11-15T12:{{minute:D2}}:00Z","event":"FSDJump","SystemAddress":43}"""
                        )
                    ),
                star,
                complete,
            ],
            allowBodyPublishing: false
        );
        await fixture.Editor.LoadAsync();
        await fixture.Editor.UploadScannedBodiesAsync();
        Assert.Equal(0, fixture.BodyWrites);
        Assert.Contains("1/2", fixture.Editor.StatusMessage, StringComparison.Ordinal);
        fixture.Editor.ApplyJournalEvents([
            Event(
                "{\"timestamp\":\"2026-11-15T12:14:00Z\",\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":1,\"BodyName\":\"Example 1\",\"PlanetClass\":\"Rocky body\"}"
            ),
        ]);
        Assert.Equal(0, fixture.BodyWrites);
        await fixture.Editor.UploadScannedBodiesAsync();
        Assert.Equal([0, 1], fixture.Published!.Select(body => body.Number));
        Assert.Contains("tidal", fixture.Published![1].Features);
    }

    [Fact]
    public async Task ANewIncompleteHonkDuringTheRavenReadCannotPublishAPartialReplacement()
    {
        using var fixture = new Fixture();
        fixture.Editor.HasLocalFssCompletion = _ => true;
        await fixture.LoadAsync();
        fixture.Editor.ApplyJournalEvents(
            [
                .. LocalScans(),
                Event("{\"event\":\"FSSDiscoveryScan\",\"SystemAddress\":42,\"BodyCount\":2,\"Progress\":1}"),
            ],
            allowBodyPublishing: false
        );
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DelayRavenRead = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        Task upload = fixture.Editor.UploadScannedBodiesAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Editor.ApplyJournalEvents(
            [Event("{\"event\":\"FSSDiscoveryScan\",\"SystemAddress\":42,\"BodyCount\":3,\"Progress\":0.5}")],
            allowBodyPublishing: false
        );
        release.SetResult();
        await upload;
        Assert.Equal(0, fixture.BodyWrites);
        Assert.Contains("changed during", fixture.Editor.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AShutdownEndedStartupBatchStillEnrichesSitesWithoutPublishingBodies()
    {
        using var fixture = new Fixture();
        using MainWindowViewModel main = fixture.CreateMain();
        main.Colonization.IsEnabled = true;
        await main.ApplyMonitorUpdateAsync(
            new(
                null,
                [
                    Event("{\"event\":\"Fileheader\",\"Odyssey\":true,\"part\":1}"),
                    Event("{\"event\":\"Commander\",\"Name\":\"Test Cmdr\",\"FID\":\"F1\"}"),
                    Event("{\"event\":\"Location\",\"StarSystem\":\"Example\",\"SystemAddress\":42}"),
                    Event(
                        "{\"event\":\"ApproachSettlement\",\"SystemAddress\":42,\"BodyID\":1,\"Name\":\"Site\",\"MarketID\":321}"
                    ),
                    Event("{\"event\":\"Shutdown\"}"),
                ],
                null,
                null,
                null,
                null,
                [],
                true
            ),
            isManualRefresh: true
        );
        await main.Colonization.SystemEditor.LoadAsync();
        Assert.True(main.Colonization.SystemEditor.IsLoaded);
        Assert.Equal(321, main.Colonization.SystemEditor.Sites[0].MarketId);
        Assert.True(main.Colonization.SystemEditor.HasLocalChanges);
        Assert.Equal(0, fixture.BodyWrites);
    }

    [Fact]
    public async Task FailedAutomaticImportKeepsTheLoadedSystemAvailableForWebRecovery()
    {
        using var fixture = new Fixture { FailImport = true };
        fixture.Record = fixture.Record with { Bodies = null };
        await fixture.LoadAsync();
        Assert.True(fixture.Editor.IsLoaded);
        Assert.True(fixture.Editor.RediscoverBodiesCommand.CanExecute(null));
        Assert.Contains("body import failed", fixture.Editor.StatusMessage, StringComparison.Ordinal);
        await fixture.Editor.RediscoverBodiesAsync();
        Assert.Equal([0, 1], fixture.Published!.Select(body => body.Number));
        Assert.Equal(1, fixture.BodyWrites);
    }

    [Fact]
    public async Task SuccessfulRecoveryDismissesTheObsoleteImportPromptWithoutDiscardingSiteEdits()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with { Bodies = null };
        await fixture.LoadAsync();
        fixture.Editor.RequestBodyImport();
        Assert.True(fixture.Editor.IsBodyImportConfirmationPending);
        fixture.Editor.Sites[0].Name = "Edited locally";
        await fixture.Editor.RediscoverBodiesAsync();
        Assert.False(fixture.Editor.IsBodyImportConfirmationPending);
        Assert.True(fixture.Editor.CanReview);
        Assert.True(fixture.Editor.HasLocalChanges);
        Assert.Equal("Edited locally", fixture.Editor.Sites[0].Name);
        Assert.Equal(0, fixture.SiteWrites);
    }

    [Fact]
    public async Task VerifiedInitialBatchIsReplayedAfterTheWorkspaceAdoptsItsCommander()
    {
        using var fixture = new Fixture();
        using ColonizationViewModel workspace = fixture.CreateWorkspace();
        workspace.IsEnabled = true;
        workspace.UpdateSystemContext("Example", null, 42);
        workspace.ApplyJournalEvents(
            [
                Event(
                    "{\"event\":\"ApproachSettlement\",\"SystemAddress\":42,\"BodyID\":1,\"Name\":\"Site\",\"MarketID\":321}"
                ),
            ],
            "Test Cmdr",
            allowBodyPublishing: false
        );
        await workspace.SetCommanderAsync("Test Cmdr");
        await workspace.SystemEditor.LoadAsync();
        Assert.Equal(321, workspace.SystemEditor.Sites[0].MarketId);
        Assert.True(workspace.SystemEditor.HasLocalChanges);
        Assert.Equal(0, fixture.BodyWrites);
    }

    [Fact]
    public async Task LocalBackfillUsesTheCommandersBoundJournalSourceInsteadOfThePrimaryFolder()
    {
        using var fixture = new Fixture();
        using MainWindowViewModel main = await fixture.CreateMainWithSecondaryJournalSourceAsync();
        Assert.Equal("Test Cmdr", main.Colonization.CommanderName);
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Test Cmdr");
        main.Colonization.UpdateSystemContext("Example", null, 42);
        await main.Colonization.SystemEditor.LoadAsync();
        Assert.True(
            main.Colonization.SystemEditor.UpdateScannedBodiesCommand.CanExecute(null),
            main.Colonization.SystemEditor.StatusMessage
        );
        await main.Colonization.SystemEditor.UploadScannedBodiesAsync();
        Assert.Equal(1, fixture.BodyWrites);
        Assert.Equal([0, 1], fixture.Published!.Select(body => body.Number));
    }

    [Fact]
    public async Task DiagnosticReplayCannotWriteBodiesThroughEitherEditorAction()
    {
        using var fixture = new Fixture();
        using MainWindowViewModel main = fixture.CreateMain(diagnosticReplay: true);
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Test Cmdr");
        main.Colonization.UpdateSystemContext("Example", null, 42);
        await main.Colonization.SystemEditor.LoadAsync();
        Assert.True(main.Colonization.SystemEditor.IsLoaded);
        main.Colonization.SystemEditor.ApplyJournalEvents([
            .. LocalScans(),
            Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"),
        ]);
        await main.Colonization.SystemEditor.BodyCatalogUpdateTask;
        await main.Colonization.SystemEditor.UploadScannedBodiesAsync();
        await main.Colonization.SystemEditor.RediscoverBodiesAsync();
        Assert.False(main.Colonization.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
        Assert.Equal(0, fixture.BodyWrites);
    }

    [Fact]
    public async Task AnEntireAwaitingIdentityProjectionKeepsTheLoadedEditorsBodyCommandsSuspended()
    {
        using var fixture = new Fixture();
        using MainWindowViewModel main = fixture.CreateMain();
        await main.ApplyMonitorUpdateAsync(
            new(
                null,
                [
                    Event("{\"event\":\"Commander\",\"Name\":\"Test Cmdr\",\"FID\":\"F-A\"}"),
                    Event("{\"event\":\"Location\",\"StarSystem\":\"Example\",\"SystemAddress\":42}"),
                ],
                null,
                null,
                null,
                null,
                [],
                true
            ),
            isManualRefresh: true
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Test Cmdr");
        main.Colonization.UpdateSystemContext("Example", null, 42);
        await main.Colonization.SystemEditor.LoadAsync();
        Assert.True(main.Colonization.SystemEditor.RediscoverBodiesCommand.CanExecute(null));

        await main.ApplyMonitorUpdateAsync(
            new(null, [], null, null, null, null, [], false) { IsAwaitingCommanderIdentity = true },
            isManualRefresh: true
        );

        Assert.True(main.Colonization.SystemEditor.IsLoaded);
        Assert.False(main.Colonization.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
        Assert.Equal(0, fixture.BodyWrites);
    }

    [Fact]
    public async Task SameCommanderIdentityRecoveryRestoresTheLoadedCatalogForScannedUploads()
    {
        using var fixture = new Fixture();
        using ColonizationViewModel workspace = fixture.CreateWorkspace();
        workspace.IsEnabled = true;
        await workspace.SetCommanderAsync("Test Cmdr");
        workspace.UpdateSystemContext("Example", null, 42);
        workspace.SystemEditor.BodyJournalHistoryReader = (_, _) =>
            Task.FromResult<IReadOnlyList<JournalEventEnvelope>>([
                .. LocalScans(),
                Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"),
            ]);
        await workspace.SystemEditor.LoadAsync();
        workspace.PrepareBodyCatalogJournalBatch([], "F-A", awaitingIdentity: true);
        Assert.False(workspace.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
        await workspace.SetCommanderAsync("Test Cmdr");
        Assert.True(workspace.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
        await workspace.SystemEditor.UploadScannedBodiesAsync();
        Assert.Equal(1, fixture.BodyWrites);
    }

    [Fact]
    public async Task AForeignCommanderBatchCannotPublishUsingThePreviousWorkspace()
    {
        using var fixture = new Fixture();
        using ColonizationViewModel workspace = fixture.CreateWorkspace();
        workspace.IsEnabled = true;
        await workspace.SetCommanderAsync("Test Cmdr");
        workspace.UpdateSystemContext("Example", null, 42);
        await workspace.SystemEditor.LoadAsync();
        workspace.SystemEditor.ApplyJournalEvents(
            [.. LocalScans(), Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}")],
            allowBodyPublishing: false
        );
        fixture.Record = fixture.Record with { Bodies = [fixture.Record.Bodies![0]] };

        workspace.ApplyJournalEvents(
            [Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}")],
            "Other Cmdr",
            allowBodyPublishing: true
        );
        await workspace.SystemEditor.BodyCatalogUpdateTask;

        Assert.Equal(0, fixture.BodyWrites);
        Assert.False(workspace.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
    }

    [Fact]
    public async Task ACommanderBoundaryCancelsAnOldUploadBeforeAnyProjectionAwait()
    {
        using var fixture = new Fixture();
        using ColonizationViewModel workspace = fixture.CreateWorkspace();
        workspace.IsEnabled = true;
        await workspace.SetCommanderAsync("Test Cmdr");
        workspace.UpdateSystemContext("Example", null, 42);
        await workspace.SystemEditor.LoadAsync();
        workspace.SystemEditor.ApplyJournalEvents(
            [.. LocalScans(), Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}")],
            allowBodyPublishing: false
        );
        fixture.Record = fixture.Record with { Bodies = [fixture.Record.Bodies![0]] };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DelayRavenRead = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        Task upload = workspace.SystemEditor.UpdateScannedBodiesAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        workspace.PrepareBodyCatalogJournalBatch(
            [Event("{\"event\":\"Commander\",\"Name\":\"Other Cmdr\",\"FID\":\"F-B\"}")],
            "F-A"
        );
        release.SetResult();
        await upload;
        Assert.Equal(0, fixture.BodyWrites);
        Assert.False(workspace.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
    }

    [Fact]
    public async Task LiveCompletionDuringInitialLoadIsCheckedAfterHydration()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with { Bodies = [fixture.Record.Bodies![0]] };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DelayRavenRead = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        Task load = fixture.LoadAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Editor.ApplyJournalEvents([
            .. LocalScans(),
            Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"),
        ]);
        release.SetResult();
        await load;
        await fixture.Editor.BodyCatalogUpdateTask;
        Assert.Equal(1, fixture.BodyWrites);
    }

    [Fact]
    public async Task CredentialChangesInvalidateAnInFlightRediscoveryWithoutRetargetingTheEditor()
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DelayProvider = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        Task recovery = fixture.Editor.RediscoverBodiesAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Editor.UpdateContext(new(true, "Test Cmdr", "Example", 42, "changed-key"));
        release.SetResult();
        await recovery;
        Assert.True(fixture.Editor.IsLoaded);
        Assert.Equal(0, fixture.BodyWrites);
    }

    [Fact]
    public async Task CommanderChangeDoesNotReuseAnotherCommandersBufferedScans()
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        fixture.Editor.ApplyJournalEvents(
            [.. LocalScans(), Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}")],
            allowBodyPublishing: false
        );
        fixture.Record = fixture.Record with { Architect = "Other Cmdr", Bodies = [fixture.Record.Bodies![0]] };
        fixture.Editor.UpdateContext(new(true, "Other Cmdr", "Example", 42, "other-key"));
        await fixture.Editor.LoadAsync();
        await fixture.Editor.UploadScannedBodiesAsync();
        Assert.Equal(0, fixture.BodyWrites);
    }

    [Fact]
    public async Task RecoveredBodiesAreRecognizedBySubsequentJournalSiteUpdates()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with
        {
            Bodies = [fixture.Record.Bodies![0]],
            Sites = [fixture.Record.Sites[0] with { BodyNumber = -1 }],
        };
        await fixture.LoadAsync();
        await fixture.Editor.RediscoverBodiesAsync();
        fixture.Editor.ApplyJournalEvents([
            Event(
                "{\"event\":\"ApproachSettlement\",\"SystemAddress\":42,\"BodyID\":1,\"Name\":\"Site\",\"MarketID\":123}"
            ),
        ]);
        Assert.Equal(1, fixture.Editor.Sites[0].BodyNumber);
        Assert.Equal(123, fixture.Editor.Sites[0].MarketId);
    }

    [Fact]
    public async Task BootstrapScansRemainLocalUntilALiveTriggerOrManualUpload()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with { Bodies = [fixture.Record.Bodies![0]] };
        await fixture.LoadAsync();
        fixture.Editor.ApplyJournalEvents(
            [.. LocalScans(), Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}")],
            allowBodyPublishing: false
        );
        await fixture.Editor.BodyCatalogUpdateTask;
        Assert.Equal(0, fixture.BodyWrites);
        await fixture.Editor.UploadScannedBodiesAsync();
        Assert.Equal(1, fixture.BodyWrites);
    }

    [Fact]
    public async Task JournalHistoryFailureDoesNotBlockRemoteRediscovery()
    {
        using var fixture = new Fixture();
        fixture.Editor.BodyJournalHistoryReader = (_, _) => throw new IOException("Unavailable history");
        var logs = new List<string>();
        fixture.Editor.BodyCatalogLog = logs.Add;
        await fixture.LoadAsync();
        await fixture.Editor.RediscoverBodiesAsync();
        Assert.Equal(1, fixture.BodyWrites);
        Assert.Equal(["Raven body journal backfill could not be read: Unavailable history"], logs);
    }

    [Fact]
    public async Task HistoricalCompleteScansDoNotUploadJustByLoadingTheEditor()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with { Bodies = [fixture.Record.Bodies![0]] };
        fixture.Editor.BodyJournalHistoryReader = (_, _) =>
            Task.FromResult<IReadOnlyList<JournalEventEnvelope>>([
                .. LocalScans(),
                Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"),
            ]);
        await fixture.LoadAsync();
        Assert.Equal(0, fixture.BodyWrites);
        await fixture.Editor.UpdateScannedBodiesAsync();
        Assert.Equal(1, fixture.BodyWrites);
    }

    [Fact]
    public async Task AChangedArchitectStopsRediscoveryBeforePublishing()
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        fixture.Record = fixture.Record with { Architect = "Other Cmdr" };
        await fixture.Editor.RediscoverBodiesAsync();
        Assert.Equal(0, fixture.BodyWrites);
        Assert.Contains("architect changed", fixture.Editor.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RediscoveryUsesTheLoadedRemoteSystemAndPreservesSiteEdits()
    {
        using var fixture = new Fixture();
        fixture.Editor.UpdateContext(new(true, "Test Cmdr", "Live Elsewhere", 99, "test-key"));
        fixture.Editor.SystemNameInput = "Example";
        await fixture.Editor.LoadAsync();
        fixture.Editor.Sites[0].Name = "Edited locally";

        await fixture.Editor.RediscoverBodiesAsync();

        Assert.Equal(42, fixture.PublishedAddress);
        Assert.Equal([0, 1], fixture.Published!.Select(body => body.Number));
        Assert.Equal(1000000, fixture.Published!.Single(body => body.Number == 1).Radius);
        Assert.Equal("Edited locally", fixture.Editor.Sites[0].Name);
        Assert.True(fixture.Editor.HasLocalChanges);
        Assert.Contains("Updated 2 Raven bodies for Example", fixture.Editor.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(0, fixture.SiteWrites);
        Assert.Equal(2, fixture.ExternalReads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RediscoveryListsEachRemovalAndHonorsTheAnswer(bool accept)
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with
        {
            Bodies =
            [
                .. fixture.Record.Bodies!,
                new()
                {
                    Number = 2,
                    Name = "Example absent",
                    Type = "rb",
                },
            ],
        };
        await fixture.LoadAsync();
        RavenBodyRemovalWarning? warning = null;
        fixture.Editor.BodyRemovalConfirmationRequested += value =>
        {
            warning = value;
            return Task.FromResult(accept);
        };

        await fixture.Editor.RediscoverBodiesAsync();

        Assert.NotNull(warning);
        Assert.Equal("Example", warning.SystemName);
        Assert.Equal(42, warning.SystemAddress);
        Assert.Equal(["Example absent (BodyID 2)"], warning.BodyNames);
        Assert.Equal(accept ? 1 : 0, fixture.BodyWrites);
        Assert.Equal(0, fixture.SiteWrites);
        if (!accept)
        {
            Assert.Equal("Rediscovery cancelled. Raven's body catalog was not changed.", fixture.Editor.StatusMessage);
        }
    }

    [Fact]
    public async Task NoConfirmationHandlerCannotApproveRemovingBodies()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with
        {
            Bodies =
            [
                new()
                {
                    Number = 2,
                    Name = "Example absent",
                    Type = "rb",
                },
            ],
        };
        await fixture.LoadAsync();

        await fixture.Editor.RediscoverBodiesAsync();

        Assert.Equal(0, fixture.BodyWrites);
    }

    [Fact]
    public async Task ANewlyLoadedSelectionBecomesTheRediscoveryTarget()
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        fixture.Record = new()
        {
            SystemAddress = 84,
            Name = "Other",
            Architect = "Test Cmdr",
            Bodies = [],
        };
        fixture.Editor.SystemNameInput = "Other";
        await fixture.Editor.LoadAsync();

        await fixture.Editor.RediscoverBodiesAsync();

        Assert.Equal(84, fixture.PublishedAddress);
        Assert.Equal(["Other", "Other 1"], fixture.Published!.Select(body => body.Name));
    }

    [Fact]
    public async Task ChangingTheEditorWhileSourcesAreLoadingPreventsAnObsoleteWrite()
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DelayProvider = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        Task recovery = fixture.Editor.RediscoverBodiesAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Editor.SystemNameInput = "Other";
        release.SetResult();
        await recovery;

        Assert.Equal(0, fixture.BodyWrites);
        Assert.False(fixture.Editor.IsLoaded);
    }

    [Fact]
    public async Task RavenChangesDuringRemovalConfirmationRequireAnotherReview()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with
        {
            Bodies =
            [
                .. fixture.Record.Bodies!,
                new()
                {
                    Number = 2,
                    Name = "Example absent",
                    Type = "rb",
                },
            ],
        };
        await fixture.LoadAsync();
        fixture.Editor.BodyRemovalConfirmationRequested += _ =>
        {
            fixture.Record = fixture.Record with
            {
                Bodies =
                [
                    .. fixture.Record.Bodies,
                    new()
                    {
                        Number = 3,
                        Name = "Example newly added",
                        Type = "rb",
                    },
                ],
            };
            return Task.FromResult(true);
        };

        await fixture.Editor.RediscoverBodiesAsync();

        Assert.Equal(0, fixture.BodyWrites);
        Assert.Contains("changed while the warning was open", fixture.Editor.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalUploadWaitsForCompletionAndDoesNotRunForEachNewScan()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with { Bodies = [fixture.Record.Bodies![0]] };
        await fixture.LoadAsync();
        fixture.Editor.ApplyJournalEvents([
            Event("{\"event\":\"FSSDiscoveryScan\",\"SystemAddress\":42,\"BodyCount\":2,\"Progress\":0.5}"),
        ]);
        await fixture.Editor.BodyCatalogUpdateTask;
        Assert.Equal(0, fixture.BodyWrites);
        fixture.Editor.ApplyJournalEvents(LocalScans());
        await fixture.Editor.BodyCatalogUpdateTask;
        Assert.Equal(0, fixture.BodyWrites);

        fixture.Editor.ApplyJournalEvents([
            Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"),
        ]);
        await fixture.Editor.BodyCatalogUpdateTask;

        Assert.Equal(1, fixture.BodyWrites);
        Assert.Equal([0, 1], fixture.Published!.Select(body => body.Number));
        fixture.Editor.ApplyJournalEvents([Event("{\"event\":\"NavBeaconScan\",\"SystemAddress\":42}")]);
        await fixture.Editor.BodyCatalogUpdateTask;
        Assert.Equal(1, fixture.BodyWrites);
    }

    [Fact]
    public async Task LocalParityDoesNotAskTheRediscoveryRemovalQuestion()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with
        {
            Bodies =
            [
                new()
                {
                    Number = 99,
                    Name = "Example stale",
                    Type = "rb",
                },
            ],
        };
        await fixture.LoadAsync();
        fixture.Editor.BodyRemovalConfirmationRequested += _ =>
            throw new InvalidOperationException("Local scans must not ask this question.");
        fixture.Editor.ApplyJournalEvents([
            .. LocalScans(),
            Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"),
        ]);
        await fixture.Editor.BodyCatalogUpdateTask;

        Assert.Equal(1, fixture.BodyWrites);
        Assert.Equal([0, 1], fixture.Published!.Select(body => body.Number));
    }

    [Fact]
    public async Task ManualScannedUploadCanRefreshAnEqualCountCatalog()
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        fixture.Editor.ApplyJournalEvents([
            .. LocalScans(),
            Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"),
        ]);
        await fixture.Editor.BodyCatalogUpdateTask;
        Assert.Equal(0, fixture.BodyWrites);

        await fixture.Editor.UploadScannedBodiesAsync();

        Assert.Equal(1, fixture.BodyWrites);
    }

    [Fact]
    public async Task ProviderFailureDoesNotPublishAPartialCatalog()
    {
        using var fixture = new Fixture { FailEdsm = true };
        await fixture.LoadAsync();

        await fixture.Editor.RediscoverBodiesAsync();

        Assert.Equal(0, fixture.BodyWrites);
        Assert.Contains("could not be completed", fixture.Editor.StatusMessage, StringComparison.Ordinal);
    }

    private static JournalEventEnvelope[] LocalScans() =>
        [
            Event("{\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":0,\"BodyName\":\"Example\",\"StarType\":\"G\"}"),
            Event(
                "{\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":1,\"BodyName\":\"Example 1\",\"PlanetClass\":\"Rocky body\",\"Radius\":1000000}"
            ),
        ];

    private static JournalEventEnvelope Event(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out _));
        return entry!;
    }

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly HttpClient http;
        private readonly string workspaceDirectory = Path.Combine(
            Path.GetTempPath(),
            "raven-body-workspace-" + Guid.NewGuid().ToString("N")
        );

        public MainWindowViewModel CreateMain(bool diagnosticReplay = false) =>
            MainWindowViewModelTestBuilder.Create(
                null,
                builder =>
                {
                    builder.WithExternalNetworkClient(http);
                    if (diagnosticReplay)
                    {
                        builder.AsDiagnosticReplay("Body catalog replay test");
                    }
                }
            );

        public ColonizationViewModel CreateWorkspace()
        {
            var workspace = new ColonizationViewModel(
                new SrvSurvey.Desktop.Configuration.ColonizationSettingsStore(
                    Path.Combine(workspaceDirectory, "settings.json")
                ),
                new RavenColonialClient(http, new Uri("https://raven.test/"))
            );
            workspace.SystemEditor.BodyRecoveryClient = new ColonizationBodyRecoveryClient(http);
            return workspace;
        }

        public async Task<MainWindowViewModel> CreateMainWithSecondaryJournalSourceAsync()
        {
            string primary = Path.Combine(workspaceDirectory, "primary");
            string secondary = Path.Combine(workspaceDirectory, "secondary");
            Directory.CreateDirectory(primary);
            Directory.CreateDirectory(secondary);
            await File.WriteAllTextAsync(
                Path.Combine(primary, "Journal.2026-11-15T130000.01.log"),
                """
                {"event":"Fileheader","Odyssey":true,"part":1}
                {"event":"Commander","FID":"F2","Name":"Other Cmdr"}
                {"event":"Location","StarSystem":"Example","SystemAddress":42}
                """
            );
            await File.WriteAllTextAsync(
                Path.Combine(secondary, "Journal.2026-11-14T120000.01.log"),
                """
                {"event":"Fileheader","Odyssey":true,"part":1}
                {"event":"Commander","FID":"F1","Name":"Test Cmdr"}
                {"event":"Scan","SystemAddress":42,"BodyID":0,"BodyName":"Example","StarType":"G"}
                {"event":"Scan","SystemAddress":42,"BodyID":1,"BodyName":"Example 1","PlanetClass":"Rocky body"}
                {"event":"FSSAllBodiesFound","SystemAddress":42,"Count":2}
                """
            );
            await File.WriteAllTextAsync(
                Path.Combine(secondary, "Journal.2026-11-15T120000.01.log"),
                """
                {"event":"Fileheader","Odyssey":true,"part":1}
                {"event":"Commander","FID":"F1","Name":"Test Cmdr"}
                {"event":"LoadGame","FID":"F1","Commander":"Test Cmdr","Odyssey":true}
                {"event":"Location","StarSystem":"Example","SystemAddress":42}
                """
            );
            var paths = new AppDataPaths(
                Path.Combine(workspaceDirectory, "config"),
                Path.Combine(workspaceDirectory, "data"),
                Path.Combine(workspaceDirectory, "cache"),
                []
            );
            new JournalSettingsStore(paths.SharedUiSettingsPath).Save(new JournalPreferences(primary, [secondary]));
            MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
                null,
                builder => builder.WithAppDataPaths(paths).WithTargetFrontierId("F1").WithExternalNetworkClient(http)
            );
            try
            {
                await main.RefreshAsync();
                return main;
            }
            catch
            {
                await main.DisposeAsync();
                throw;
            }
        }

        public Fixture()
        {
            http = new HttpClient(this, disposeHandler: false);
            Editor = new(
                new RavenColonialClient(http, new Uri("https://raven.test/")),
                ColonizationBuildCatalog.LoadEmbedded()
            )
            {
                BodyRecoveryClient = new ColonizationBodyRecoveryClient(http),
            };
        }

        public ColonizationSystemEditorViewModel Editor { get; }
        public ColonizationSystemRecord Record { get; set; } =
            new()
            {
                SystemAddress = 42,
                Name = "Example",
                Architect = "Test Cmdr",
                Bodies =
                [
                    new()
                    {
                        Number = 0,
                        Name = "Example",
                        Type = "st",
                    },
                    new()
                    {
                        Number = 1,
                        Name = "Example 1",
                        Type = "rb",
                    },
                ],
                Sites =
                [
                    new()
                    {
                        Id = "site-1",
                        Name = "Site",
                        BodyNumber = 1,
                        BuildType = "outpost",
                        Status = ColonizationSystemSiteStatus.Complete,
                    },
                ],
            };
        public int BodyWrites { get; private set; }
        public int SiteWrites { get; private set; }
        public int ExternalReads { get; private set; }
        public long? PublishedAddress { get; private set; }
        public List<ColonizationSystemBody>? Published { get; private set; }
        public Func<Task>? DelayProvider { get; set; }
        public Func<Task>? DelayRavenRead { get; set; }
        public bool FailEdsm { get; set; }
        public bool FailImport { get; set; }

        public async Task LoadAsync()
        {
            Editor.UpdateContext(new(true, "Test Cmdr", "Example", 42, "test-key"));
            await Editor.LoadAsync();
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Uri uri = request.RequestUri!;
            if (uri.Host != "raven.test" && !uri.AbsolutePath.StartsWith("/api/v2/system/", StringComparison.Ordinal))
            {
                ExternalReads++;
                if (DelayProvider is { } delay)
                {
                    await delay();
                }

                if (FailEdsm && uri.Host == "www.edsm.net")
                {
                    return new(HttpStatusCode.ServiceUnavailable);
                }

                string data = JsonSerializer.Serialize(
                    new
                    {
                        id64 = Record.SystemAddress,
                        name = Record.Name,
                        bodies = new object[]
                        {
                            new
                            {
                                bodyId = 0,
                                name = Record.Name,
                                type = "Star",
                                subType = "G (White-Yellow) Star",
                                solarRadius = 1,
                            },
                            new
                            {
                                bodyId = 1,
                                name = Record.Name + " 1",
                                type = "Planet",
                                subType = "Rocky body",
                                radius = 1000,
                            },
                        },
                    }
                );
                return Json(uri.Host == "spansh.co.uk" ? "{\"system\":" + data + "}" : data);
            }
            if (request.Method == HttpMethod.Put)
            {
                if (!uri.AbsolutePath.EndsWith("/bodies", StringComparison.Ordinal))
                {
                    SiteWrites++;
                    return Json("{}");
                }
                BodyWrites++;
                PublishedAddress = long.Parse(
                    uri.Segments[^2].Trim('/'),
                    System.Globalization.CultureInfo.InvariantCulture
                );
                string json = await request.Content!.ReadAsStringAsync(cancellationToken);
                Published = JsonSerializer.Deserialize<List<ColonizationSystemBody>>(json)!;
                Record = Record with { Bodies = Published };
                return Json(json);
            }
            if (
                FailImport
                && request.Method == HttpMethod.Post
                && uri.AbsolutePath.EndsWith("/bodies", StringComparison.Ordinal)
            )
            {
                return new(HttpStatusCode.ServiceUnavailable);
            }
            if (DelayRavenRead is { } wait)
            {
                await wait();
            }

            return Json(JsonSerializer.Serialize(Record));
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Editor.Dispose();
                http.Dispose();
                if (Directory.Exists(workspaceDirectory))
                {
                    Directory.Delete(workspaceDirectory, recursive: true);
                }
            }
            base.Dispose(disposing);
        }
    }
}
