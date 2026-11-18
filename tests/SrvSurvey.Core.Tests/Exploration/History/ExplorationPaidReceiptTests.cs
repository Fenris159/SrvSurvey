using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Exploration.History;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Exploration.History;

public sealed partial class ExplorationHistoryCatalogTests
{
    [Theory]
    [InlineData("SellExplorationData", "\"Systems\":[\"Ledger Alpha\"]")]
    [InlineData("MultiSellExplorationData", "\"Discovered\":[{\"SystemName\":\"Ledger Alpha\",\"NumBodies\":1}]")]
    public async Task APaidReceiptUsesTheActualCreditedAmountAndRetainsItsCollectedData(
        string eventName,
        string systems
    )
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Ledger Alpha", 100, 0),
            Star(100),
            $$"""{"event":"{{eventName}}",{{systems}},"BaseValue":10822,"Bonus":3959,"TotalEarnings":44343}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);

        ExplorationHistoryPage paid = await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid));

        ExplorationHistoryRow receipt = Assert.Single(paid.Rows);
        Assert.Equal(ExplorationDataStatus.Paid, receipt.Status);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 3, 0, TimeSpan.Zero), receipt.LastCollected);
        Assert.Equal(44343, receipt.Payment!.ActualEarnings);
        Assert.Equal(["Ledger Alpha"], receipt.Payment.Systems);
        Assert.Equal(1, receipt.ScannedBodies);
        ExplorationHistoryPage ledger = await fixture.Catalog.QueryAsync(new() { IncludePayments = true });
        Assert.Equal(2, ledger.TotalRows);
        Assert.Equal(ExplorationDataStatus.Paid, ledger.Rows[0].Status);
        Assert.Equal(ExplorationDataStatus.Sold, ledger.Rows[1].Status);
        Assert.Equal(44343, ledger.Summary.ActualSaleEarnings);
        Assert.Equal(1, ledger.Summary.SoldSystems);
    }

    [Fact]
    public async Task ReceiptTravelAndBodyTotalsAreScopedToEachSaleAndSurviveDuplicateDeliveryAndRestart()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Ledger Alpha", 100, 0),
            """{"event":"FSDJump","JumpDist":12.5}""",
            """{"event":"FSDJump","JumpDist":7.25}""",
            Star(100),
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Ledger Alpha 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
            """{"event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"ProbesUsed":1,"EfficiencyTarget":2}""",
            """{"event":"Touchdown","SystemAddress":100,"BodyID":1,"OnPlanet":true}""",
            """{"event":"Touchdown","SystemAddress":100,"BodyID":1,"OnPlanet":true}""",
            """{"event":"Touchdown","SystemAddress":100,"BodyID":0,"OnPlanet":false}""",
            """{"event":"SellExplorationData","Systems":["Ledger Alpha"],"TotalEarnings":9000000}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryRow first = Assert.Single(
            (await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid))).Rows
        );
        Assert.Equal(2, first.Payment!.JumpCount);
        Assert.Equal(19.75, first.Payment.DistanceTravelled);
        Assert.Equal(2, first.ScannedBodies);
        Assert.Equal(1, first.MappedBodies);
        Assert.Equal(1, first.Payment.LandedBodies);
        Assert.True(first.Payment.HasBodyDetails);

        JournalEventEnvelope[] next = fixture.Append([
            """{"event":"FSDJump","JumpDist":5.5}""",
            """{"event":"MultiSellExplorationData","Discovered":[{"SystemName":"Unrecorded system","NumBodies":2}],"BaseValue":100,"Bonus":20}""",
        ]);
        await fixture.Catalog.ObserveAsync(next, [fixture.Journals]);
        await fixture.Catalog.ObserveAsync(next, [fixture.Journals]);
        var reopened = new ExplorationHistoryCatalog(fixture.Data, "F100", true);
        await reopened.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryPage paid = await reopened.QueryAsync(new(ExplorationDataStatus.Paid));
        Assert.Equal(2, paid.TotalRows);
        ExplorationHistoryRow second = paid.Rows[0];
        Assert.Null(second.Payment!.ActualEarnings);
        Assert.Equal(1, second.Payment.JumpCount);
        Assert.Equal(5.5, second.Payment.DistanceTravelled);
        Assert.False(second.Payment.HasBodyDetails);
        Assert.Equal(9000000, paid.Summary.ActualSaleEarnings);
        Assert.Equal(1, paid.Summary.UnrecordedPayouts);
        ExplorationHistoryDetails details = await reopened.ReadDetailsAsync(first);
        Assert.Equal("SellExplorationData", Assert.Single(details.Events).EventName);

        ExplorationHistoryPage selected = await reopened.QueryAsync(
            new(ExplorationDataStatus.Paid, "Unrecorded", PageSize: 1, SelectionAnchor: first.Anchor)
        );
        Assert.Equal(1, selected.TotalRows);
        Assert.Equal(first.Anchor, selected.SelectedRow!.Anchor);
        Assert.Equal(first.Id, selected.SelectedRowId);
        Assert.Empty(
            (
                await reopened.QueryAsync(
                    new(ExplorationDataStatus.Paid, FromUtc: new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero))
                )
            ).Rows
        );
    }

    [Fact]
    public async Task ResettingTheAutomaticValuePreservesTheLedgerAndCountsOnlyNewUnsoldCreditsAfterRestartAndReplay()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Previous sale", 200, 0),
            """{"event":"FSDJump","JumpDist":3.5}""",
            Star(200),
            """{"event":"SellExplorationData","Systems":["Previous sale"],"TotalEarnings":12345}""",
            Location("Ledger Alpha", 100, 0),
            Star(100),
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryRow originalReceipt = Assert.Single(
            (await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid))).Rows
        );
        ExplorationHistorySummary reset = await fixture.Catalog.ResetSurveyValueAsync();
        Assert.Equal(23106, reset.UnsoldValue);
        Assert.Equal(0, reset.TrackedUnsoldValue);
        ExplorationHistoryRow retained = Assert.Single(
            (await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid))).Rows
        );
        Assert.Equal(originalReceipt.Anchor, retained.Anchor);
        Assert.Equal(12345, retained.Payment!.ActualEarnings);
        Assert.Equal(1, retained.Payment.JumpCount);
        Assert.Equal(3.5, retained.Payment.DistanceTravelled);

        JournalEventEnvelope[] scan = fixture.Append([
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Ledger Alpha 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
        ]);
        await fixture.Catalog.ObserveAsync(scan, [fixture.Journals]);
        var reopened = new ExplorationHistoryCatalog(fixture.Data, "F100", true);
        ExplorationHistorySummary next = await reopened.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(472306, next.UnsoldValue);
        Assert.Equal(449200, next.TrackedUnsoldValue);

        // An earlier copy of the old scan becomes the credited observation during canonical replay.
        await File.WriteAllLinesAsync(
            Path.Combine(fixture.Journals, "Journal.2026-09-30T120000.01.log"),
            [
                """{"timestamp":"2026-09-30T12:00:00Z","event":"Fileheader","Odyssey":true}""",
                """{"timestamp":"2026-09-30T12:00:01Z","event":"Commander","FID":"F100"}""",
                """{"timestamp":"2026-09-30T12:01:00Z","event":"Location","StarSystem":"Ledger Alpha","SystemAddress":100}""",
                """{"timestamp":"2026-09-30T12:02:00Z","event":"Scan","SystemAddress":100,"BodyID":0,"BodyName":"Test star","StarType":"NS","StellarMass":1.4,"WasDiscovered":true,"WasMapped":true}""",
            ]
        );
        next = await reopened.SynchronizeAsync([fixture.Journals]);
        Assert.True(next.IsComplete);
        Assert.Equal(449200, next.TrackedUnsoldValue);
        Assert.Equal(472306, next.UnsoldValue);
        next = await reopened.ObserveAsync(
            fixture.Append(["""{"event":"SellExplorationData","Systems":["Ledger Alpha"],"TotalEarnings":600000}"""]),
            [fixture.Journals]
        );
        Assert.Equal(0, next.TrackedUnsoldValue);
        ExplorationHistoryPage ledger = await reopened.QueryAsync(new() { IncludePayments = true });
        Assert.Equal(4, ledger.TotalRows);
        Assert.Equal(600000, ledger.Rows[0].Payment!.ActualEarnings);
        Assert.Equal(472306, ledger.Rows[1].EstimatedValue);
    }

    [Fact]
    public async Task ValueResetRejectsAnIncompleteCatalogWithoutDiscardingItsPendingBalance()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Ledger Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        await fixture.Catalog.ObserveAsync(
            fixture.Append([
                """{"event":"SAAScanComplete","SystemAddress":100,"BodyID":99,"ProbesUsed":1,"EfficiencyTarget":2}""",
            ]),
            [fixture.Journals]
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Catalog.ResetSurveyValueAsync());
        Assert.Equal(23106, (await fixture.Catalog.ReadSummaryAsync()).TrackedUnsoldValue);
    }

    [Fact]
    public async Task SurveyPeriodCountsANewMappingUpgradeAndPartialSalesAndLossesRemoveOnlyTheirCredits()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Alpha", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Alpha 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
            Location("Beta", 200, 0),
            Star(200),
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        await fixture.Catalog.ResetSurveyValueAsync();
        ExplorationHistorySummary summary = await fixture.Catalog.ObserveAsync(
            fixture.Append([
                Location("Alpha", 100, 0),
                """{"event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"ProbesUsed":1,"EfficiencyTarget":2}""",
                Location("Gamma", 300, 0),
                Star(300),
            ]),
            [fixture.Journals]
        );
        Assert.Equal(2_274_447, summary.TrackedUnsoldValue);
        Assert.Equal(2_746_753, summary.UnsoldValue);
        summary = await fixture.Catalog.ObserveAsync(
            fixture.Append(["""{"event":"SellExplorationData","Systems":["Alpha"],"TotalEarnings":3000000}"""]),
            [fixture.Journals]
        );
        Assert.Equal(23_106, summary.TrackedUnsoldValue);
        Assert.Equal(46_212, summary.UnsoldValue);
        summary = await fixture.Catalog.ObserveAsync(
            fixture.Append(["""{"event":"Died"}""", """{"event":"Resurrect","Option":"rebuy"}"""]),
            [fixture.Journals]
        );
        Assert.Equal(0, summary.TrackedUnsoldValue);
        Assert.Equal(0, summary.UnsoldValue);
        Assert.Equal(46_212, summary.LostValue);
    }

    [Fact]
    public async Task AnOlderCatalogKeepsItsSaleVisibleWithoutInventingMissingReceiptDetailsUntilJournalsReturn()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Alpha", 100, 0),
            Star(100),
            """{"event":"FSDJump","JumpDist":12.5}""",
            """{"event":"SellExplorationData","Systems":["Alpha"],"TotalEarnings":44343}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        using (
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = fixture.Catalog.FilePath, Pooling = false }.ToString()
            )
        )
        {
            await connection.OpenAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "DROP TABLE sale_receipts; DROP TABLE value_credits; DROP TABLE survey_value_checkpoints; UPDATE metadata SET value='6' WHERE key='valuation_version'; PRAGMA user_version=5;";
            await command.ExecuteNonQueryAsync();
        }
        string original = fixture.JournalPath;
        File.Move(original, original + ".saved");
        ExplorationHistoryPage cached = await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid));
        ExplorationHistoryRow retained = Assert.Single(cached.Rows);
        Assert.False(cached.Summary.IsComplete);
        Assert.Null(retained.Payment!.ActualEarnings);
        Assert.Null(retained.Payment.JumpCount);
        Assert.Null(retained.Payment.DistanceTravelled);
        Assert.Null(retained.Payment.LandedBodies);
        Assert.Equal(["Alpha"], retained.Payment.Systems);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
        Assert.Single((await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid))).Rows);
        File.Move(original + ".saved", original);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryRow recovered = Assert.Single(
            (await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid, SelectionAnchor: retained.Anchor))).Rows
        );
        Assert.Equal(44343, recovered.Payment!.ActualEarnings);
        Assert.Equal(1, recovered.Payment.JumpCount);
        Assert.Equal(12.5, recovered.Payment.DistanceTravelled);
        Assert.Equal(retained.Anchor, recovered.Anchor);
    }
}
