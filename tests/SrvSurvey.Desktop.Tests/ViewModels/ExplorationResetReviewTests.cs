using SrvSurvey.Core.Exploration.History;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ExplorationHistoryViewModelTests
{
    [Fact]
    public async Task TwoQueuedInitialClicksCannotConfirmAResetThatWasNotVisibleWhenClicked()
    {
        using var fixture = new Fixture();
        await using MainWindowViewModel main = CreateLedgerMain(fixture);
        await main.RefreshAsync();
        await using HeldExplorationPoll held = await HoldExplorationPollAsync(main, fixture);
        Task first = main.ResetExplorationValueAsync();
        Task second = main.ResetExplorationValueAsync();
        await held.ReleaseAsync();
        await Task.WhenAll(first, second);
        Assert.True(main.IsResetExplorationPending);
        Assert.Equal("777 CR", main.EstimatedExplorationValue);
        Assert.Equal(9, (await fixture.Profiles.LoadAsync("F100", true)).Data!.Exploration.ScanCount);
    }

    [Fact]
    public async Task CancellingAQueuedConfirmationCannotReopenThePrompt()
    {
        using var fixture = new Fixture();
        await using MainWindowViewModel main = CreateLedgerMain(fixture);
        await main.RefreshAsync();
        await main.ResetExplorationValueAsync();
        await using HeldExplorationPoll held = await HoldExplorationPollAsync(main, fixture);
        Task confirm = main.ResetExplorationValueAsync();
        main.CancelResetExplorationCommand.Execute(null);
        await held.ReleaseAsync();
        await confirm;
        Assert.False(main.IsResetExplorationPending);
        Assert.Equal("777 CR", main.EstimatedExplorationValue);
        await main.ResetExplorationValueAsync();
        Assert.True(main.IsResetExplorationPending);
    }

    [Fact]
    public async Task ClosingWhileConfirmationWaitsForAPollStillCompletesTheAcceptedReset()
    {
        using var fixture = new Fixture();
        await using MainWindowViewModel main = CreateLedgerMain(fixture);
        await main.RefreshAsync();
        await main.ExplorationHistory.PendingSynchronization;
        main.ExplorationHistory.AutoResetEstimatedValue = true;
        await main.ExplorationHistory.PendingPreferenceSave;
        await main.ResetExplorationValueAsync();
        await using HeldExplorationPoll held = await HoldExplorationPollAsync(main, fixture);
        Task confirm = main.ResetExplorationValueAsync();
        Task closing = main.DisposeAsync().AsTask();
        await held.ReleaseAsync();
        await confirm;
        await closing;
        CommanderProfileData saved = (await fixture.Profiles.LoadAsync("F100", true)).Data!;
        Assert.Equal(0, saved.Exploration.EstimatedRewards);
        Assert.Equal(0, saved.Exploration.ScanCount);
        Assert.Equal(43, saved.Exploration.JumpCount);
        Assert.NotNull(saved.ExplorationSurveyStart);
        Assert.Equal(
            0,
            (await new ExplorationHistoryCatalog(fixture.Data, "F100", true).ReadSummaryAsync()).TrackedUnsoldValue
        );
    }

    [Fact]
    public async Task ACancellationDrainedScanStaysBeforeAQueuedResetAndAfterRestart()
    {
        using var fixture = new Fixture();
        await using (MainWindowViewModel main = CreateLedgerMain(fixture))
        {
            await main.RefreshAsync();
            await main.ExplorationHistory.PendingSynchronization;
            main.ExplorationHistory.AutoResetEstimatedValue = true;
            await main.ExplorationHistory.PendingPreferenceSave;
            await main.ResetExplorationValueAsync();
            using var cancellation = new CancellationTokenSource();
            await using HeldExplorationPoll held = await HoldExplorationPollAsync(main, fixture, cancellation.Token);
            Task confirm = main.ResetExplorationValueAsync();
            await cancellation.CancelAsync();
            Task closing = main.DisposeAsync().AsTask();
            await held.ReleaseAsync();
            await confirm;
            await closing;
            CommanderProfileData saved = (await fixture.Profiles.LoadAsync("F100", true)).Data!;
            Assert.Equal(0, saved.Exploration.ScanCount);
            Assert.Equal(0, saved.Exploration.EstimatedRewards);
            Assert.Equal(
                0,
                (await new ExplorationHistoryCatalog(fixture.Data, "F100", true).ReadSummaryAsync()).TrackedUnsoldValue
            );
        }
        await using MainWindowViewModel reopened = CreateLedgerMain(fixture);
        await reopened.RefreshAsync();
        await reopened.ExplorationHistory.PendingSynchronization;
        Assert.Equal(0, reopened.ExplorationHistory.AutomaticEstimatedValue);
        Assert.Equal(0, (await fixture.Profiles.LoadAsync("F100", true)).Data!.Exploration.ScanCount);
    }

    [Fact]
    public async Task AFaultedProfileLoadCannotResetOrSaveThePreviousCommandersTotals()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.Data, "F200-live.json"), "malformed profile");
        await using MainWindowViewModel main = CreateLedgerMain(fixture);
        await main.RefreshAsync();
        await File.AppendAllLinesAsync(
            fixture.JournalPath("F100"),
            ["""{"timestamp":"2026-10-01T14:00:00Z","event":"Commander","FID":"F200","Name":"Second Commander"}"""]
        );
        await main.RefreshAsync();
        await main.ResetExplorationOdometerAsync();
        Assert.False(main.IsResetExplorationPending);
        await main.ResetExplorationOdometerAsync();
        Assert.False(main.IsResetExplorationPending);
        Assert.False(main.ResetExplorationOdometerCommand.CanExecute(null));
        Assert.Equal("malformed profile", await File.ReadAllTextAsync(Path.Combine(fixture.Data, "F200-live.json")));
        Assert.Equal(42, (await fixture.Profiles.LoadAsync("F100", true)).Data!.Exploration.JumpCount);
    }

    [Fact]
    public async Task AResetIncludesHistorysVisibleUnreadScanInTheSharedStartingPoint()
    {
        using var fixture = new Fixture();
        await using MainWindowViewModel main = CreateLedgerMain(fixture);
        await main.RefreshAsync();
        await main.ExplorationHistory.PendingSynchronization;
        main.ExplorationHistory.AutoResetEstimatedValue = true;
        await main.ExplorationHistory.PendingPreferenceSave;
        await AppendReviewScanAsync(fixture);
        await main.ExplorationHistory.RefreshAsync();
        Assert.Equal(472306, main.ExplorationHistory.AutomaticEstimatedValue);
        await main.ResetExplorationValueAsync();
        await main.ResetExplorationValueAsync();
        Assert.Equal(0, main.ExplorationHistory.AutomaticEstimatedValue);
        await main.RefreshAsync();
        await main.ExplorationHistory.PendingSynchronization;
        Assert.Equal(0, main.ExplorationHistory.AutomaticEstimatedValue);
        CommanderProfileData saved = (await fixture.Profiles.LoadAsync("F100", true)).Data!;
        Assert.Equal(0, saved.Exploration.ScanCount);
        Assert.Equal(0, saved.Exploration.EstimatedRewards);
        await File.AppendAllLinesAsync(
            fixture.JournalPath("F100"),
            [
                """{"timestamp":"2026-10-01T15:00:00Z","event":"Scan","SystemAddress":100,"BodyID":2,"BodyName":"Alpha Survey 2","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
            ]
        );
        await main.RefreshAsync();
        await main.ExplorationHistory.PendingSynchronization;
        Assert.Equal(449200, main.ExplorationHistory.AutomaticEstimatedValue);
        Assert.Equal(1, (await fixture.Profiles.LoadAsync("F100", true)).Data!.Exploration.ScanCount);
    }

    [Fact]
    public async Task AFailedProfileWriteLeavesBothTheOldTotalsAndTheOldCatalogStartingPoint()
    {
        using var fixture = new Fixture();
        await using (MainWindowViewModel main = CreateLedgerMain(fixture))
        {
            await main.RefreshAsync();
            await main.ExplorationHistory.PendingSynchronization;
            main.ExplorationHistory.AutoResetEstimatedValue = true;
            await main.ExplorationHistory.PendingPreferenceSave;
            await main.ResetExplorationValueAsync();
            File.Move(fixture.ProfilePath, fixture.ProfilePath + ".saved");
            Directory.CreateDirectory(fixture.ProfilePath);
            try
            {
                await main.ResetExplorationValueAsync();
                Assert.Contains("could not be reset", main.ExplorationStatusMessage);
                Assert.Equal(23106, main.ExplorationHistory.AutomaticEstimatedValue);
            }
            finally
            {
                Directory.Delete(fixture.ProfilePath);
                File.Move(fixture.ProfilePath + ".saved", fixture.ProfilePath);
            }
        }
        await using MainWindowViewModel reopened = CreateLedgerMain(fixture);
        await reopened.RefreshAsync();
        await reopened.ExplorationHistory.PendingSynchronization;
        Assert.Equal(23106, reopened.ExplorationHistory.AutomaticEstimatedValue);
        Assert.Equal(9, (await fixture.Profiles.LoadAsync("F100", true)).Data!.Exploration.ScanCount);
    }

    [Fact]
    public async Task AnInterruptedCatalogUpdateReusesTheProfileStartingPointOnRestart()
    {
        using var fixture = new Fixture();
        var catalog = new ExplorationHistoryCatalog(fixture.Data, "F100", true);
        await catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint point = await catalog.CaptureSurveyResetPointAsync();
        await fixture.Profiles.SaveExplorationResetAsync(
            "F100",
            "History Commander",
            true,
            new(0, 0, 42, 0, 0, 0),
            point
        );
        await fixture.Profiles.SaveExplorationTrackingAsync("F100", "History Commander", true, true);
        // The process stopped after the atomic profile write, before applying the point to SQLite.
        await using MainWindowViewModel reopened = CreateLedgerMain(fixture);
        await reopened.RefreshAsync();
        await reopened.ExplorationHistory.PendingSynchronization;
        Assert.Equal(0, reopened.ExplorationHistory.AutomaticEstimatedValue);
        await AppendReviewScanAsync(fixture);
        await reopened.RefreshAsync();
        await reopened.ExplorationHistory.PendingSynchronization;
        Assert.Equal(449200, reopened.ExplorationHistory.AutomaticEstimatedValue);
        CommanderProfileData saved = (await fixture.Profiles.LoadAsync("F100", true)).Data!;
        Assert.Equal(point.Id, saved.ExplorationSurveyStart!.Id);
        Assert.Equal(1, saved.Exploration.ScanCount);
        Assert.Equal(42, saved.Exploration.JumpCount);
    }

    private static Task AppendReviewScanAsync(Fixture fixture) =>
        File.AppendAllLinesAsync(
            fixture.JournalPath("F100"),
            [
                """{"timestamp":"2026-10-01T14:01:00Z","event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Alpha Survey 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
            ]
        );

    private static async Task<HeldExplorationPoll> HoldExplorationPollAsync(
        MainWindowViewModel main,
        Fixture fixture,
        CancellationToken cancellationToken = default
    )
    {
        var lease = new FileStream(
            fixture.ProfilePath + ".lease",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(main.ExplorationJumps) && main.ExplorationJumps == "43")
            {
                entered.TrySetResult();
            }
        }
        main.PropertyChanged += Changed;
        Task poll = Task.CompletedTask;
        try
        {
            await File.AppendAllLinesAsync(
                fixture.JournalPath("F100"),
                ["""{"timestamp":"2026-10-01T13:00:00Z","event":"StartJump","JumpType":"Hyperspace"}"""],
                cancellationToken
            );
            if (cancellationToken.CanBeCanceled)
            {
                await AppendReviewScanAsync(fixture);
            }
            poll = cancellationToken.CanBeCanceled
                ? main.MonitorAsync(cancellationToken: cancellationToken)
                : main.RefreshAsync();
            await Task.WhenAny(entered.Task, poll);
            Assert.Equal("43", main.ExplorationJumps);
            return new(lease, poll);
        }
        catch
        {
            await lease.DisposeAsync();
            await poll;
            throw;
        }
        finally
        {
            main.PropertyChanged -= Changed;
        }
    }

    private sealed class HeldExplorationPoll(FileStream lease, Task poll) : IAsyncDisposable
    {
        public async Task ReleaseAsync()
        {
            await lease.DisposeAsync();
            await poll;
        }

        public ValueTask DisposeAsync() => new(ReleaseAsync());
    }
}
