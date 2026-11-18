using System.Data.Common;
using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Exploration.History;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ExplorationHistoryViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValueResetStartsANewPeriodWithoutResettingTravelOrDisablingAutomaticTracking(bool automatic)
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(
            fixture.ProfilePath,
            """{"fid":"F100","commander":"History Commander","isOdyssey":true,"explRewards":777,"countJumps":42,"distanceTravelled":123.5,"countScans":9,"countDSS":4,"countLanded":2}"""
        );
        await using (MainWindowViewModel main = CreateLedgerMain(fixture))
        {
            await main.RefreshAsync();
            await main.ExplorationHistory.PendingSynchronization;
            main.ExplorationHistory.AutoResetEstimatedValue = automatic;
            await main.ExplorationHistory.PendingPreferenceSave;
            await main.ResetExplorationValueAsync();
            Assert.True(main.IsResetExplorationPending);
            Assert.Equal("Confirm Reset Exp Value", main.ResetExplorationValueButtonText);
            Assert.Equal(automatic ? "23,106 CR" : "777 CR", main.EstimatedExplorationValue);
            await main.ResetExplorationValueAsync();
            Assert.False(main.IsResetExplorationPending);
            Assert.Equal("0 CR", main.EstimatedExplorationValue);
            Assert.Equal("42", main.ExplorationJumps);
            Assert.Equal("123.5 ly", main.ExplorationDistance);
            CommanderProfileLoadResult saved = await fixture.Profiles.LoadAsync("F100", true);
            Assert.Equal(0, saved.Data!.Exploration.ScanCount);
            Assert.Equal(0, saved.Data.Exploration.DetailedSurfaceScanCount);
            Assert.Equal(0, saved.Data.Exploration.LandedBodyCount);
            Assert.Equal(automatic, saved.Data.AutoResetExplorationValue);
            Assert.Equal("23,106 CR", main.ExplorationHistory.UnsoldText);
        }
        await using MainWindowViewModel reopened = CreateLedgerMain(fixture);
        await reopened.RefreshAsync();
        await reopened.ExplorationHistory.PendingSynchronization;
        Assert.Equal("0 CR", reopened.EstimatedExplorationValue);
        Assert.Equal("42", reopened.ExplorationJumps);
        Assert.Equal(automatic, reopened.ExplorationHistory.AutoResetEstimatedValue);
        await File.AppendAllTextAsync(
            fixture.JournalPath("F100"),
            """{"timestamp":"2026-10-01T14:00:00Z","event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Alpha Survey 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}"""
                + "\n"
        );
        await reopened.RefreshAsync();
        await reopened.ExplorationHistory.PendingSynchronization;
        Assert.Equal("449,200 CR", reopened.EstimatedExplorationValue);
        Assert.Equal("42", reopened.ExplorationJumps);
    }

    [Fact]
    public async Task SwitchingResetScopesRequiresItsOwnConfirmationAndOdometerResetPreservesBodyTotals()
    {
        using var fixture = new Fixture();
        await using MainWindowViewModel main = CreateLedgerMain(fixture);
        await main.RefreshAsync();
        await main.ExplorationHistory.PendingSynchronization;
        await main.ResetExplorationAsync();
        await main.ResetExplorationOdometerAsync();
        Assert.Equal("Confirm Reset Odometer", main.ResetExplorationOdometerButtonText);
        Assert.Equal("Reset Everything Above", main.ResetExplorationButtonText);
        Assert.Equal("42", main.ExplorationJumps);
        Assert.Equal("777 CR", main.EstimatedExplorationValue);
        await main.ResetExplorationOdometerAsync();
        Assert.Equal("0", main.ExplorationJumps);
        Assert.Equal("0.0 ly", main.ExplorationDistance);
        Assert.Equal("777 CR", main.EstimatedExplorationValue);
        CommanderProfileLoadResult saved = await fixture.Profiles.LoadAsync("F100", true);
        Assert.Equal(9, saved.Data!.Exploration.ScanCount);
        await main.ResetExplorationValueAsync();
        main.CancelResetExplorationCommand.Execute(null);
        Assert.False(main.IsResetExplorationPending);
        Assert.Equal("Reset Exp Value", main.ResetExplorationValueButtonText);
        Assert.Equal("777 CR", main.EstimatedExplorationValue);
        await main.ResetExplorationAsync();
        await main.ResetExplorationAsync();
        Assert.Equal("0 CR", main.EstimatedExplorationValue);
        Assert.Equal(0, (await fixture.Profiles.LoadAsync("F100", true)).Data!.Exploration.ScanCount);
    }

    [Fact]
    public async Task PaidRowsExposeActualPayoutAndReceiptDetailsAndCanBeFilteredWithoutCopyingTheReceiptTitle()
    {
        using var fixture = new Fixture();
        await File.AppendAllLinesAsync(
            fixture.JournalPath("F100"),
            [
                """{"timestamp":"2026-10-01T13:00:00Z","event":"FSDJump","JumpDist":12.5}""",
                """{"timestamp":"2026-10-01T13:01:00Z","event":"SellExplorationData","Systems":["Alpha Survey"],"TotalEarnings":44343}""",
                """{"timestamp":"2026-10-01T13:02:00Z","event":"SellExplorationData","Systems":["Unknown"],"BaseValue":100,"Bonus":20}""",
            ]
        );
        await using ExplorationHistoryViewModel model = fixture.Create();
        model.SetCommander("F100", "History Commander", true, true);
        await model.PendingSynchronization;
        Assert.Equal(3, model.Rows.Count);
        Assert.Contains("44,343 CR", model.ActualSalesText);
        Assert.Contains("1", model.ActualSalesText);
        model.SelectedStatus = ExplorationHistoryViewModel.StatusOptions.Single(option =>
            option.Status == ExplorationDataStatus.Paid
        );
        await model.PendingQuery;
        Assert.Equal(2, model.Rows.Count);
        ExplorationHistoryRowViewModel unknown = model.Rows[0];
        Assert.Equal("Payout not recorded", unknown.Estimate);
        Assert.Equal("Body totals unavailable", unknown.Contents);
        ExplorationHistoryRowViewModel paid = model.Rows[1];
        Assert.True(paid.IsPaid);
        Assert.False(paid.IsCollection);
        Assert.Equal("+44,343 CR", paid.Estimate);
        Assert.Equal("Actual payout", paid.ValueCaption);
        Assert.Equal("1 scanned, 0 DSS, 0 landed", paid.Contents);
        Assert.Equal("1 hyperspace jumps, 12.5 ly travelled", paid.ReceiptTravel);
        Assert.Equal("2026-10-01", paid.CollectedDate);
        Assert.Equal("13:01 UTC", paid.CollectedTime);
        Assert.False(paid.CopySystemNameCommand.CanExecute(null));
        model.SelectedRow = paid;
        await model.PendingDetails;
        Assert.True(model.HasPaymentSelection);
        Assert.Equal("Exploration data sold", model.DetailTitle);
        Assert.Equal("Paid 2026-10-01 13:01 UTC", model.DetailPeriod);
        Assert.Equal(string.Empty, model.DetailLocation);
        Assert.Equal("Alpha Survey", model.PaymentFacts.Single(fact => fact.Label == "Sold systems").Value);
        Assert.Empty(model.Bodies);
        Assert.Contains("44343", model.RawJson);
        Assert.DoesNotContain("estimated from", model.DetailStatus);
        model.CloseDetailsCommand.Execute(null);
        Assert.False(model.HasPaymentSelection);
        Assert.Empty(model.PaymentFacts);
    }

    [Fact]
    public async Task APageReadBeforeTheResetCannotRestoreTheOldAutomaticBalance()
    {
        using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool hold = false;
        await using ExplorationHistoryViewModel model = fixture.Create(
            async (catalog, query, token) =>
            {
                ExplorationHistoryPage page = await catalog.QueryAsync(query, token);
                if (hold)
                {
                    hold = false;
                    entered.SetResult();
                    await release.Task.WaitAsync(token);
                }
                return page;
            }
        );
        try
        {
            model.SetCommander("F100", "History Commander", true, true);
            await model.PendingSynchronization;
            hold = true;
            model.SearchText = "Alpha";
            Task oldRead = model.PendingQuery;
            await entered.Task;
            await model.ResetSurveyValueAsync();
            Assert.Equal(0, model.AutomaticEstimatedValue);
            release.SetResult();
            await oldRead;
            Assert.Equal(0, model.AutomaticEstimatedValue);
            Assert.False(model.IsQueryBusy);
            Assert.Equal("23,106 CR", model.UnsoldText);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task AFailedValueResetKeepsItsBalanceAndDoesNotPreventALaterRefreshOrReset()
    {
        using var fixture = new Fixture();
        await using ExplorationHistoryViewModel model = fixture.Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => model.ResetSurveyValueAsync());
        model.SetCommander("F100", "History Commander", true, true);
        await model.PendingSynchronization;
        await File.AppendAllLinesAsync(
            fixture.JournalPath("F100"),
            [
                """{"timestamp":"2026-10-01T14:00:00Z","event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"ProbesUsed":1,"EfficiencyTarget":2}""",
            ]
        );
        await model.RefreshAsync();
        Assert.Null(model.AutomaticEstimatedValue);
        await Assert.ThrowsAsync<InvalidOperationException>(() => model.ResetSurveyValueAsync());
        Assert.Equal("23,106 CR", model.UnsoldText);
        await File.AppendAllLinesAsync(
            fixture.JournalPath("F100"),
            [
                """{"timestamp":"2026-10-01T14:01:00Z","event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Alpha Survey 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
                """{"timestamp":"2026-10-01T14:02:00Z","event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"ProbesUsed":1,"EfficiencyTarget":2}""",
            ]
        );
        await model.RefreshAsync();
        Assert.Equal(2_723_647, model.AutomaticEstimatedValue);
        await model.ResetSurveyValueAsync();
        Assert.Equal(0, model.AutomaticEstimatedValue);
        Assert.True(model.AutoResetEstimatedValue);
    }

    [Theory]
    [InlineData("odometer")]
    [InlineData("value")]
    [InlineData("everything")]
    public async Task ManualResetsDoNotChangeTheTravelRecordedOnTheNextPaidReceipt(string scope)
    {
        using var fixture = new Fixture();
        await using MainWindowViewModel main = CreateLedgerMain(fixture);
        await main.RefreshAsync();
        await main.ExplorationHistory.PendingSynchronization;
        await File.AppendAllLinesAsync(
            fixture.JournalPath("F100"),
            [
                """{"timestamp":"2026-10-01T13:00:00Z","event":"StartJump","JumpType":"Hyperspace"}""",
                """{"timestamp":"2026-10-01T13:01:00Z","event":"FSDJump","JumpDist":12.5}""",
            ]
        );
        await main.RefreshAsync();
        await main.ExplorationHistory.PendingSynchronization;
        Func<Task> reset = scope switch
        {
            "odometer" => main.ResetExplorationOdometerAsync,
            "value" => main.ResetExplorationValueAsync,
            _ => main.ResetExplorationAsync,
        };
        await reset();
        await reset();
        await File.AppendAllLinesAsync(
            fixture.JournalPath("F100"),
            [
                """{"timestamp":"2026-10-01T14:00:00Z","event":"StartJump","JumpType":"Hyperspace"}""",
                """{"timestamp":"2026-10-01T14:01:00Z","event":"FSDJump","JumpDist":5.5}""",
                """{"timestamp":"2026-10-01T14:02:00Z","event":"SellExplorationData","Systems":["Alpha Survey"],"TotalEarnings":44343}""",
            ]
        );
        await main.RefreshAsync();
        await main.ExplorationHistory.PendingSynchronization;
        ExplorationHistoryRowViewModel paid = Assert.Single(main.ExplorationHistory.Rows, row => row.IsPaid);
        Assert.Equal("2 hyperspace jumps, 18.0 ly travelled", paid.ReceiptTravel);
        Assert.Equal("+44,343 CR", paid.Estimate);
        Assert.Equal("1 scanned, 0 DSS, 0 landed", paid.Contents);
    }

    [Fact]
    public async Task ChangingCommandersCancelsThePendingResetAndCannotResetThePreviousCommandersTotals()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Data, "F200-live.json"),
            """{"fid":"F200","commander":"Second Commander","isOdyssey":true,"explRewards":600,"countJumps":12,"countScans":3}"""
        );
        await using MainWindowViewModel main = CreateLedgerMain(fixture);
        await main.RefreshAsync();
        await main.ExplorationHistory.PendingSynchronization;
        await main.ResetExplorationValueAsync();
        await File.AppendAllLinesAsync(
            fixture.JournalPath("F100"),
            [
                """{"timestamp":"2026-10-01T14:00:00Z","event":"Commander","FID":"F200","Name":"Second Commander"}""",
                """{"timestamp":"2026-10-01T14:01:00Z","event":"Location","StarSystem":"Beta","SystemAddress":200}""",
            ]
        );
        await main.RefreshAsync();
        await main.ExplorationHistory.PendingSynchronization;
        Assert.False(main.IsResetExplorationPending);
        Assert.Equal("600 CR", main.EstimatedExplorationValue);
        await main.ResetExplorationValueAsync();
        Assert.True(main.IsResetExplorationPending);
        Assert.Equal("600 CR", main.EstimatedExplorationValue);
        await main.ResetExplorationValueAsync();
        Assert.Equal("0 CR", main.EstimatedExplorationValue);
        Assert.Equal("12", main.ExplorationJumps);
        CommanderProfileLoadResult previous = await fixture.Profiles.LoadAsync("F100", true);
        Assert.Equal(777, previous.Data!.Exploration.EstimatedRewards);
        Assert.Equal(42, previous.Data.Exploration.JumpCount);
        Assert.Equal(9, previous.Data.Exploration.ScanCount);
    }

    [Fact]
    public async Task ClosingDuringAConfirmedResetFinishesBothTheSavedTotalsAndAutomaticStartingPoint()
    {
        using var fixture = new Fixture();
        await using MainWindowViewModel main = CreateLedgerMain(fixture);
        await main.RefreshAsync();
        await main.ExplorationHistory.PendingSynchronization;
        main.ExplorationHistory.AutoResetEstimatedValue = true;
        await main.ExplorationHistory.PendingPreferenceSave;
        await main.ResetExplorationValueAsync();
        var catalog = new ExplorationHistoryCatalog(fixture.Data, "F100", true);
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = catalog.FilePath, Pooling = false }.ToString()
        );
        await connection.OpenAsync();
        await using DbTransaction heldWrite = await connection.BeginTransactionAsync();
        Task reset = main.ResetExplorationValueAsync();
        Task closing = main.DisposeAsync().AsTask();
        Assert.False(closing.IsCompleted);
        await heldWrite.RollbackAsync();
        await reset;
        await closing;
        CommanderProfileLoadResult saved = await fixture.Profiles.LoadAsync("F100", true);
        Assert.Equal(0, saved.Data!.Exploration.EstimatedRewards);
        Assert.Equal(0, saved.Data.Exploration.ScanCount);
        Assert.Equal(42, saved.Data.Exploration.JumpCount);
        Assert.True(saved.Data.AutoResetExplorationValue);
        Assert.Equal(0, (await catalog.ReadSummaryAsync()).TrackedUnsoldValue);
    }

    private static MainWindowViewModel CreateLedgerMain(Fixture fixture) =>
        MainWindowViewModelTestBuilder.Create(
            fixture.Journals,
            builder =>
                builder.WithAppDataPaths(
                    new(Path.Combine(fixture.Root, "config"), fixture.Data, Path.Combine(fixture.Root, "cache"), [])
                )
        );
}
