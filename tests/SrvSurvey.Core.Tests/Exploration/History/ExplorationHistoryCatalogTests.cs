using System.Text;
using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Exploration.History;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Exploration.History;

public sealed partial class ExplorationHistoryCatalogTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyRareStarEstimatesAreRepricedWithoutChangingRecordedSettlements(bool liveObservation)
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Sold rare", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyID":0,"BodyName":"Sold rare star","StarType":"N","StellarMass":1.4}""",
            """{"event":"SellExplorationData","Systems":["Sold rare"],"TotalEarnings":20000}""",
            Location("Lost rare", 200, 0),
            """{"event":"Scan","SystemAddress":200,"BodyID":0,"BodyName":"Lost rare star","StarType":"H","StellarMass":1.4}""",
            """{"event":"Died"}""",
            """{"event":"Resurrect","Option":"rebuy","Cost":0}""",
            Location("Unsold rare", 300, 0),
            """{"event":"Scan","SystemAddress":300,"BodyID":0,"BodyName":"Unsold rare star","StarType":"N","StellarMass":1.4}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        await SeedPreviousValuationAsync(fixture);
        ExplorationHistoryRow selected = (await fixture.Catalog.QueryAsync(new())).Rows.Single(row =>
            row.SystemName == "Unsold rare"
        );
        Assert.Equal(1225, selected.EstimatedValue);
        ExplorationHistorySummary summary = liveObservation
            ? await fixture.Catalog.ObserveAsync(
                fixture.Append([
                    """{"event":"FSSSignalDiscovered","SystemAddress":300,"SignalName":"$Fixed_Event_Life_Cloud;"}""",
                ]),
                [fixture.Journals]
            )
            : await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(23106, summary.UnsoldValue);
        Assert.Equal(23106, summary.SoldValue);
        Assert.Equal(23106, summary.LostValue);
        Assert.Equal(20000, summary.SaleEarnings);
        Assert.Equal(1, summary.UnsoldSystems);
        Assert.Equal(1, summary.SoldSystems);
        Assert.Equal(1, summary.LostSystems);
        Assert.True(summary.IsComplete);
        ExplorationHistoryPage page = await fixture.Catalog.QueryAsync(new(SelectionAnchor: selected.Anchor));
        Assert.Equal(3, page.TotalRows);
        Assert.Equal("Unsold rare", page.Rows.Single(row => row.Id == page.SelectedRowId).SystemName);
        Assert.Equal(ExplorationDataStatus.Sold, page.Rows.Single(row => row.SystemName == "Sold rare").Status);
        Assert.Equal(ExplorationDataStatus.Lost, page.Rows.Single(row => row.SystemName == "Lost rare").Status);
        Assert.Equal(summary, await fixture.Catalog.SynchronizeAsync([fixture.Journals]));
    }

    [Fact]
    public async Task MissingJournalsDeferRepricingWithoutErasingCachedHistory()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Rare survey", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyID":0,"BodyName":"Rare star","StarType":"H","StellarMass":1.4}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        await SeedPreviousValuationAsync(fixture);
        string unavailable = fixture.JournalPath + ".saved";
        File.Move(fixture.JournalPath, unavailable);
        ExplorationHistoryPage before = await fixture.Catalog.QueryAsync(new());
        Assert.Equal(1225, Assert.Single(before.Rows).EstimatedValue);
        Assert.False(before.Summary.IsComplete);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
        ExplorationHistoryPage retained = await fixture.Catalog.QueryAsync(new());
        Assert.Equal(1225, Assert.Single(retained.Rows).EstimatedValue);
        Assert.Equal(ExplorationDataStatus.Unsold, retained.Rows[0].Status);
        Assert.False(retained.Summary.IsComplete);
        File.Move(unavailable, fixture.JournalPath);
        ExplorationHistorySummary restored = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(23106, restored.UnsoldValue);
        Assert.True(restored.IsComplete);
    }

    private static async Task SeedPreviousValuationAsync(HistoryFixture fixture)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = fixture.Catalog.FilePath, Pooling = false }.ToString()
        );
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE batches SET estimate=1225;
            UPDATE bodies SET paid_value=CASE WHEN paid_value>0 THEN 1225 ELSE 0 END,
                pending_value=CASE WHEN pending_value>0 THEN 1225 ELSE 0 END;
            DELETE FROM metadata WHERE key='valuation_version';
            PRAGMA user_version=3;
            """;
        await command.ExecuteNonQueryAsync();
    }

    [Theory]
    [InlineData("\"PlanetClass\":\"Earthlike body\"", ExplorationNotables.EarthLikeWorld)]
    [InlineData("\"PlanetClass\":\"Water world\"", ExplorationNotables.WaterWorld)]
    [InlineData("\"PlanetClass\":\"Ammonia world\"", ExplorationNotables.AmmoniaWorld)]
    [InlineData(
        "\"PlanetClass\":\"High metal content body\",\"TerraformState\":\"Terraformable\"",
        ExplorationNotables.TerraformableWorld
    )]
    [InlineData(
        "\"PlanetClass\":\"Water world\",\"TerraformState\":\"Terraformable\"",
        ExplorationNotables.WaterWorld | ExplorationNotables.TerraformableWorld
    )]
    [InlineData("\"StarType\":\"N\",\"StellarMass\":1.4", ExplorationNotables.NeutronStar)]
    [InlineData("\"StarType\":\"NS\",\"StellarMass\":1.4", ExplorationNotables.NeutronStar)]
    [InlineData("\"StarType\":\"H\",\"StellarMass\":1.4", ExplorationNotables.BlackHole)]
    [InlineData("\"StarType\":\"BH\",\"StellarMass\":1.4", ExplorationNotables.BlackHole)]
    [InlineData("\"PlanetClass\":\"Class I gas giant\"", ExplorationNotables.None)]
    [InlineData("\"PlanetClass\":\"Earthlike body\",\"ScanType\":\"NavBeaconDetail\"", ExplorationNotables.None)]
    public async Task TheHistoryHighlightsConfirmedValuableAndRareScannedBodies(
        string fields,
        ExplorationNotables expected
    )
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Notable survey", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyID":0,"BodyName":"Ordinary star","StarType":"G","StellarMass":1,"WasDiscovered":true}""",
            $$"""{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Survey target","MassEM":1,"WasDiscovered":true,{{fields}}}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(expected, Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows).Notables);
    }

    [Theory]
    [InlineData(
        "\"event\":\"CodexEntry\",\"Name\":\"$Codex_Ent_Green_Sudarsky_Class_II_Name;\"",
        ExplorationNotables.GreenGasGiant
    )]
    [InlineData("\"event\":\"CodexEntry\",\"EntryID\":1200602", ExplorationNotables.GreenGasGiant)]
    [InlineData(
        "\"event\":\"CodexEntry\",\"Name\":\"$Codex_Ent_Gas_Clds_Blue_Name;\"",
        ExplorationNotables.StellarPhenomena
    )]
    [InlineData(
        "\"event\":\"FSSSignalDiscovered\",\"SignalName\":\"$Fixed_Event_Life_Cloud;\",\"SignalName_Localised\":\"Phénomènes stellaires remarquables\"",
        ExplorationNotables.StellarPhenomena
    )]
    [InlineData(
        "\"event\":\"FSSSignalDiscovered\",\"SignalName\":\"Notable stellar phenomena\",\"SignalType\":\"FleetCarrier\"",
        ExplorationNotables.None
    )]
    public async Task RareDiscoveryTagsRequireCanonicalEvidenceRatherThanNames(
        string fields,
        ExplorationNotables expected
    )
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Notable survey", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyID":0,"BodyName":"Ordinary star","StarType":"G","StellarMass":1,"WasDiscovered":true}""",
            $$"""{ {{fields}},"SystemAddress":100,"System":"Notable survey" }""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(expected, Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows).Notables);
    }

    [Fact]
    public async Task AnExistingCatalogBackfillsDiscoveriesWithoutReplayingSales()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Notable survey", 100, 0),
            """{"event":"CodexEntry","Name":"$Codex_Ent_Green_Sudarsky_Class_II_Name;","SystemAddress":100,"System":"Notable survey"}""",
            Star(100),
            """{"event":"SellExplorationData","Systems":["Notable survey"],"TotalEarnings":20000}""",
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
                "DROP TABLE IF EXISTS notable_systems; DELETE FROM metadata WHERE key LIKE 'notable_source:%'; PRAGMA user_version=2;";
            await command.ExecuteNonQueryAsync();
        }
        string unavailable = fixture.JournalPath + ".saved";
        File.Move(fixture.JournalPath, unavailable);
        ExplorationHistoryRow cached = Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows);
        Assert.Equal(ExplorationDataStatus.Sold, cached.Status);
        Assert.Equal(23106, cached.EstimatedValue);
        File.Move(unavailable, fixture.JournalPath);
        ExplorationHistorySummary summary = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryRow restored = Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows);
        Assert.Equal(ExplorationNotables.NeutronStar | ExplorationNotables.GreenGasGiant, restored.Notables);
        Assert.Equal(ExplorationDataStatus.Sold, restored.Status);
        Assert.Equal(23106, summary.SoldValue);
        Assert.Equal(0, summary.UnsoldValue);
        Assert.Equal(20000, summary.SaleEarnings);
        await fixture.Catalog.ObserveAsync(
            fixture.Append([
                """{"event":"FSSSignalDiscovered","SystemAddress":100,"SignalName":"$Fixed_Event_Life_Cloud;"}""",
            ]),
            [fixture.Journals]
        );
        File.Move(fixture.JournalPath, unavailable);
        ExplorationHistoryRow appended = Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows);
        Assert.Equal(
            ExplorationNotables.NeutronStar | ExplorationNotables.GreenGasGiant | ExplorationNotables.StellarPhenomena,
            appended.Notables
        );
        Assert.Equal(ExplorationDataStatus.Sold, appended.Status);
        Assert.Equal(23106, appended.EstimatedValue);
    }

    [Fact]
    public async Task DiscoveriesFromAnotherCommanderCannotTagTheSelectedCommandersHistory()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Shared system", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyID":0,"BodyName":"Ordinary star","StarType":"G","StellarMass":1,"WasDiscovered":true}""",
            """{"event":"Commander","FID":"F200","Name":"Other commander"}""",
            """{"event":"CodexEntry","Name":"$Codex_Ent_Green_Sudarsky_Class_II_Name;","SystemAddress":100,"System":"Shared system"}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(ExplorationNotables.None, Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows).Notables);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnIndexedCompatibilityDiscoveryIsReclassifiedAfterTheModelUpgrade(bool observe)
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Compatibility discovery", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyID":0,"BodyName":"Compatibility discovery A","StarType":"BH","StellarMass":1.4}""",
            """{"event":"SellExplorationData","Systems":["Compatibility discovery"],"TotalEarnings":20000}""",
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
                "DELETE FROM notable_systems; UPDATE metadata SET value='1' WHERE key='valuation_version';";
            await command.ExecuteNonQueryAsync();
        }
        ExplorationHistorySummary summary = observe
            ? await fixture.Catalog.ObserveAsync([], [fixture.Journals])
            : await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryRow row = Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows);
        Assert.Equal(ExplorationNotables.BlackHole, row.Notables);
        Assert.Equal(ExplorationDataStatus.Sold, row.Status);
        Assert.Equal(23106, summary.SoldValue);
        Assert.Equal(20000, summary.SaleEarnings);
        Assert.Equal(0, summary.UnsoldValue);
        Assert.True(summary.IsComplete);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AConfirmedMappingClearsOnlyItsOwnMissingScanGuard(
        bool anotherUnresolvedBody,
        bool initiallyNamedOnly
    )
    {
        using var fixture = new HistoryFixture();
        string identity = initiallyNamedOnly ? "\"BodyName\":\"Recovered mapping 1\"" : "\"BodyID\":1";
        var initial = new List<string>
        {
            Location("Recovered mapping", 100, 0),
            $$"""{"event":"SAAScanComplete","SystemAddress":100,{{identity}},"ProbesUsed":5,"EfficiencyTarget":6}""",
        };
        if (anotherUnresolvedBody)
        {
            initial.Add(
                """{"event":"SAAScanComplete","SystemAddress":100,"BodyID":2,"ProbesUsed":5,"EfficiencyTarget":6}"""
            );
        }
        fixture.Write(initial);
        ExplorationHistorySummary unconfirmed = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.False(unconfirmed.IsComplete);
        Assert.False(unconfirmed.HasCommanderHistory);
        Assert.Equal(0, unconfirmed.UnsoldSystems);
        ExplorationHistorySummary summary = await fixture.Catalog.ObserveAsync(
            fixture.Append([
                """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Recovered mapping 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
                """{"event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"BodyName":"Recovered mapping 1","ProbesUsed":5,"EfficiencyTarget":6}""",
            ]),
            [fixture.Journals]
        );
        Assert.Equal(2700541, summary.UnsoldValue);
        Assert.Equal(!anotherUnresolvedBody, summary.IsComplete);
    }

    [Fact]
    public async Task NamedSaleLeavesUnlistedNearbyDataUnsoldAndKeepsActualEarningsSeparate()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Sold system", 100, 0),
            Star(100),
            Location("Nearby system", 200, 10),
            Star(200),
            """{"event":"MultiSellExplorationData","Discovered":[{"SystemName":"Sold system","NumBodies":1}],"BaseValue":10000,"Bonus":0,"TotalEarnings":7500}""",
        ]);
        ExplorationHistorySummary summary = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryPage page = await fixture.Catalog.QueryAsync(new());

        Assert.Equal(23106, summary.UnsoldValue);
        Assert.Equal(23106, summary.SoldValue);
        Assert.Equal(7500, summary.SaleEarnings);
        Assert.Equal(2, page.TotalRows);
        Assert.Equal(ExplorationDataStatus.Sold, page.Rows.Single(row => row.SystemName == "Sold system").Status);
        ExplorationHistoryRow nearby = page.Rows.Single(row => row.SystemName == "Nearby system");
        Assert.Equal(ExplorationDataStatus.Unsold, nearby.Status);
        Assert.Equal(10, nearby.X);
    }

    [Theory]
    [InlineData("rejoin", ExplorationDataStatus.Unsold, 23106)]
    [InlineData("handin", ExplorationDataStatus.Unsold, 23106)]
    [InlineData("rebuy", ExplorationDataStatus.Lost, 0)]
    [InlineData("free", ExplorationDataStatus.Lost, 0)]
    public async Task RespawnOutcomeDistinguishesSurvivingShipDataFromARebuyEvenWhenItCostsNothing(
        string option,
        ExplorationDataStatus expectedStatus,
        long unsoldValue
    )
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Survey system", 100, 0),
            Star(100),
            """{"event":"Disembark","SRV":true,"Taxi":false,"Multicrew":false}""",
            """{"event":"Died"}""",
            """{"event":"SRVDestroyed"}""",
            $$"""{"event":"Resurrect","Option":"{{option}}","Cost":0,"Bankrupt":false}""",
        ]);
        ExplorationHistorySummary summary = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryRow row = Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows);

        Assert.Equal(expectedStatus, row.Status);
        Assert.Equal(unsoldValue, summary.UnsoldValue);
        Assert.Equal(23106, row.EstimatedValue);
    }

    [Fact]
    public async Task MappingUpgradesTheValueRatherThanAddingTheFullMappedPayoutAgain()
    {
        using var fixture = new HistoryFixture();
        const string scan =
            """{"event":"Scan","StarSystem":"Survey system","SystemAddress":100,"BodyID":1,"BodyName":"Survey system 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""";
        const string map =
            """{"event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"BodyName":"Survey system 1","ProbesUsed":5,"EfficiencyTarget":6}""";
        fixture.Write([Location("Survey system", 100, 0), scan, scan, map, map]);
        ExplorationHistorySummary summary = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryRow row = Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows);

        Assert.Equal(2700541, summary.UnsoldValue);
        Assert.Equal(2700541, row.EstimatedValue);
        Assert.Equal(1, row.ScannedBodies);
        Assert.Equal(1, row.MappedBodies);
    }

    [Fact]
    public async Task LearningASystemAddressAndBodyIdDoesNotMakeSoldDataSellableAgain()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            """{"event":"Location","StarSystem":"Address learned"}""",
            """{"event":"Scan","BodyName":"Address learned A","StarType":"N","StellarMass":1.4}""",
            """{"event":"SellExplorationData","Systems":["Address learned"],"TotalEarnings":20000}""",
            Location("Address learned", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyID":0,"BodyName":"Address learned A","StarType":"N","StellarMass":1.4}""",
            Location("Different address", 200, 0),
            """{"event":"Scan","SystemAddress":200,"StarSystem":"Address learned","BodyID":0,"BodyName":"Address learned A","StarType":"N","StellarMass":1.4}""",
        ]);
        ExplorationHistorySummary summary = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(23106, summary.SoldValue);
        Assert.Equal(23106, summary.UnsoldValue);
        Assert.Equal(20000, summary.SaleEarnings);
        ExplorationHistoryPage page = await fixture.Catalog.QueryAsync(new());
        Assert.Equal(2, page.TotalRows);
        ExplorationHistoryRow sold = Assert.Single(page.Rows, row => row.Status == ExplorationDataStatus.Sold);
        Assert.Equal(100, sold.SystemAddress);
        Assert.Equal(200, Assert.Single(page.Rows, row => row.Status == ExplorationDataStatus.Unsold).SystemAddress);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KnownBodyIdsRemainDistinctEvenWhenTheirNamesMatch(bool initiallyMissingId)
    {
        using var fixture = new HistoryFixture();
        string identity = initiallyMissingId ? string.Empty : "\"BodyID\":0,";
        fixture.Write([
            Location("Body aliases", 100, 0),
            $$"""{"event":"Scan","SystemAddress":100,{{identity}}"BodyName":"Repeated name","StarType":"N","StellarMass":1.4}""",
            """{"event":"SellExplorationData","Systems":["Body aliases"],"TotalEarnings":20000}""",
            """{"event":"Scan","SystemAddress":100,"BodyID":0,"BodyName":"Repeated name","StarType":"N","StellarMass":1.4}""",
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Repeated name","StarType":"N","StellarMass":1.4}""",
        ]);
        ExplorationHistorySummary summary = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(23106, summary.SoldValue);
        Assert.Equal(23106, summary.UnsoldValue);
        Assert.Equal(20000, summary.SaleEarnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingScanDefersMappingAndLaterSettlementUntilOriginalsReturn(bool lost)
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Mapped survey", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Mapped survey 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
        ]);
        Assert.Equal(449200, (await fixture.Catalog.SynchronizeAsync([fixture.Journals])).UnsoldValue);
        string unavailable = fixture.JournalPath + ".saved";
        File.Move(fixture.JournalPath, unavailable);
        string later = Path.Combine(fixture.Journals, "Journal.2026-10-01T140000.01.log");
        await File.WriteAllLinesAsync(
            later,
            [
                """{"timestamp":"2026-10-01T14:00:00Z","event":"Fileheader","gameversion":"4.0.0","Odyssey":true}""",
                """{"event":"Commander","FID":"F100"}""",
                Location("Mapped survey", 100, 0),
                """{"event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"BodyName":"Mapped survey 1","ProbesUsed":5,"EfficiencyTarget":6}""",
                lost
                    ? """{"event":"Resurrect","Option":"rebuy","Cost":0}"""
                    : """{"event":"SellExplorationData","Systems":["Mapped survey"],"TotalEarnings":2000000}""",
            ]
        );
        Exception? blocked = await Record.ExceptionAsync(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
        ExplorationHistoryPage cached = await fixture.Catalog.QueryAsync(new());
        File.Move(unavailable, fixture.JournalPath);
        ExplorationHistorySummary recovered = await fixture.Catalog.ObserveAsync([], [fixture.Journals]);
        Assert.True(recovered.IsComplete);
        Assert.Equal(0, recovered.UnsoldValue);
        Assert.Equal(lost ? 0 : 2700541, recovered.SoldValue);
        Assert.Equal(lost ? 2700541 : 0, recovered.LostValue);
        Assert.Equal(lost ? 0 : 2000000, recovered.SaleEarnings);
        Assert.IsType<IOException>(blocked, exactMatch: false);
        Assert.Equal(449200, Assert.Single(cached.Rows).EstimatedValue);
        Assert.Equal(ExplorationDataStatus.Unsold, cached.Rows[0].Status);
        Assert.False(cached.Summary.IsComplete);
        Assert.Equal(recovered, await fixture.Catalog.SynchronizeAsync([fixture.Journals]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyPendingRetriesRecoverAfterAllOriginalJournalsReturn(bool observed)
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Legacy pending", 100, 0), Star(100)]);
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
                "UPDATE metadata SET value='2' WHERE key='valuation_version'; INSERT INTO metadata(key,value) VALUES('reconciliation_pending','1');";
            await command.ExecuteNonQueryAsync();
        }
        File.Move(fixture.JournalPath, fixture.JournalPath + ".saved");
        ExplorationHistorySummary paused = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.True(paused.RecoveryConfirmationRequired);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.ConfirmRecoveryAsync([fixture.Journals]));
        ExplorationHistoryPage retained = await fixture.Catalog.QueryAsync(new());
        Assert.Equal(23106, Assert.Single(retained.Rows).EstimatedValue);
        Assert.False(retained.Summary.IsComplete);
        File.Move(fixture.JournalPath + ".saved", fixture.JournalPath);
        ExplorationHistorySummary automatic = observed
            ? await fixture.Catalog.ObserveAsync([], [fixture.Journals])
            : await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.False(automatic.IsComplete);
        Assert.True(automatic.RecoveryConfirmationRequired);
        ExplorationHistorySummary recovered = await fixture.Catalog.ConfirmRecoveryAsync([fixture.Journals]);
        Assert.True(recovered.IsComplete);
        Assert.False(recovered.RecoveryConfirmationRequired);
        Assert.Equal(23106, recovered.UnsoldValue);
        Assert.Equal(recovered, await fixture.Catalog.SynchronizeAsync([fixture.Journals]));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    public async Task InterruptedFingerprintingRetainsVerifiedRecordsWithoutRequiringAnIncompleteSuffix(
        bool truncateSuffix,
        bool truncateComplete,
        bool preserveLength
    )
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Interrupted recovery", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        File.Move(fixture.JournalPath, fixture.JournalPath + ".saved");
        string harmless = Path.Combine(fixture.Journals, "Journal.2026-10-01T130000.01.log");
        string mapping = Path.Combine(fixture.Journals, "Journal.2026-10-01T140000.01.log");
        string missing = Path.Combine(fixture.Journals, "Journal.2026-10-01T160000.01.log");
        await File.WriteAllTextAsync(
            harmless,
            "{\"timestamp\":\"2026-10-01T13:00:00Z\",\"event\":\"Fileheader\",\"Odyssey\":true}\n"
        );
        string complete =
            """
                {"timestamp":"2026-10-01T14:00:00Z","event":"Fileheader","Odyssey":true}
                {"event":"Commander","FID":"F100"}
                {"event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"ProbesUsed":5,"EfficiencyTarget":6}
                {"event":"SellExplorationData","Systems":["Interrupted recovery"],"TotalEarnings":2000000}
                """ + "\n";
        string captured = complete + "{\"event\":\"Music\"";
        await File.WriteAllTextAsync(mapping, captured);
        await File.WriteAllTextAsync(
            missing,
            "{\"timestamp\":\"2026-10-01T16:00:00Z\",\"event\":\"Fileheader\",\"Odyssey\":true}\n"
        );
        var interrupt = new SynchronousProgress(progress =>
        {
            if (!progress.IsPreparing && progress.CurrentFile == Path.GetFileName(harmless))
            {
                if (truncateComplete)
                {
                    string prefix = complete[
                        ..complete.IndexOf("{\"event\":\"SellExplorationData\"", StringComparison.Ordinal)
                    ];
                    File.WriteAllText(
                        mapping,
                        prefix + (preserveLength ? new string(' ', captured.Length - prefix.Length) : string.Empty)
                    );
                }
                else if (truncateSuffix)
                {
                    File.AppendAllText(mapping, ",\"MusicTrack\":\"Main");
                }
                else
                {
                    File.Move(missing, missing + ".saved");
                }
            }
        });
        await Assert.ThrowsAnyAsync<IOException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals], interrupt));
        File.Move(fixture.JournalPath + ".saved", fixture.JournalPath);
        if (!truncateSuffix)
        {
            if (!truncateComplete)
            {
                File.Move(missing + ".saved", missing);
                await File.WriteAllTextAsync(mapping, captured.Replace("2000000", "9000000", StringComparison.Ordinal));
            }
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
            ExplorationHistorySummary retained = (await fixture.Catalog.QueryAsync(new())).Summary;
            Assert.False(retained.IsComplete);
            Assert.Equal(0, retained.SaleEarnings);
        }
        await File.WriteAllTextAsync(mapping, truncateSuffix ? complete : captured);
        ExplorationHistorySummary recovered = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.True(recovered.IsComplete);
        Assert.Equal(2700541, recovered.SoldValue);
        Assert.Equal(0, recovered.UnsoldValue);
        Assert.Equal(2000000, recovered.SaleEarnings);
    }

    [Fact]
    public async Task RepeatedDeferredAppendsKeepOneCoveringRequirementPerJournal()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Bounded retry", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        File.Move(fixture.JournalPath, fixture.JournalPath + ".saved");
        string mapping = Path.Combine(fixture.Journals, "Journal.2026-10-01T140000.01.log");
        await File.WriteAllTextAsync(
            mapping,
            """
            {"timestamp":"2026-10-01T14:00:00Z","event":"Fileheader","Odyssey":true}
            {"event":"Commander","FID":"F100"}
            {"event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"ProbesUsed":5,"EfficiencyTarget":6}
            """ + "\n"
        );
        for (int index = 0; index < 10; index++)
        {
            await File.AppendAllTextAsync(mapping, "{\"event\":\"Music\"}\n");
            await Assert.ThrowsAnyAsync<IOException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
        }
        using (
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = fixture.Catalog.FilePath, Pooling = false }.ToString()
            )
        )
        {
            await connection.OpenAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM metadata WHERE key='reconciliation_pending';";
            using var manifest = System.Text.Json.JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
            System.Text.Json.JsonElement only = Assert.Single(manifest.RootElement.EnumerateArray());
            Assert.Equal(Path.GetFileName(mapping), only.GetProperty("Name").GetString());
            Assert.Equal(0, only.GetProperty("StartOffset").GetInt64());
            Assert.Equal(64, only.GetProperty("PrefixHash").GetString()!.Length);
        }
        File.Move(fixture.JournalPath + ".saved", fixture.JournalPath);
        ExplorationHistorySummary recovered = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.True(recovered.IsComplete);
        Assert.Equal(2700541, recovered.UnsoldValue);
    }

    [Fact]
    public async Task ConfirmedLegacyRawTailsDoNotBypassOtherVerifiedFingerprints()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Legacy mixed", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        File.Move(fixture.JournalPath, fixture.JournalPath + ".saved");
        string mapping = Path.Combine(fixture.Journals, "Journal.2026-10-01T140000.01.log");
        string settlement = Path.Combine(fixture.Journals, "Journal.2026-10-01T160000.01.log");
        string map =
            """
                {"timestamp":"2026-10-01T14:00:00Z","event":"Fileheader","Odyssey":true}
                {"event":"Commander","FID":"F100"}
                {"event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"ProbesUsed":5,"EfficiencyTarget":6}
                """ + "\n";
        string sale = """
            {"timestamp":"2026-10-01T16:00:00Z","event":"Fileheader","Odyssey":true}
            {"event":"Commander","FID":"F100"}
            {"event":"SellExplorationData","Systems":["Legacy mixed"],"TotalEarnings":2000000}
            {"event":"Music"
            """;
        await File.WriteAllTextAsync(mapping, map);
        await File.WriteAllTextAsync(settlement, sale);
        await Assert.ThrowsAnyAsync<IOException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
        using (
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = fixture.Catalog.FilePath, Pooling = false }.ToString()
            )
        )
        {
            await connection.OpenAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM metadata WHERE key='reconciliation_pending';";
            System.Text.Json.Nodes.JsonArray manifest = System
                .Text.Json.Nodes.JsonNode.Parse((string)(await command.ExecuteScalarAsync())!)!
                .AsArray();
            System.Text.Json.Nodes.JsonNode old = manifest.Single(item =>
                item!["Name"]!.GetValue<string>() == Path.GetFileName(settlement)
            )!;
            old["PrefixHash"] = null;
            old["FormatVersion"] = 0;
            old["Length"] = new FileInfo(settlement).Length;
            const string partialName = "Journal.2026-10-01T170000.01.log";
            manifest.Add(
                new System.Text.Json.Nodes.JsonObject
                {
                    ["Name"] = partialName,
                    ["Length"] = 13,
                    ["HeaderHash"] = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
                    ["PrefixHash"] = null,
                }
            );
            await File.WriteAllTextAsync(
                Path.Combine(fixture.Journals, partialName),
                "{\"timestamp\":\"2026-10-01T17:00:00Z\",\"event\":\"Fileheader\",\"Odyssey\":true}\n{\"event\":\"Commander\",\"FID\":\"F100\"}\n"
            );
            command.CommandText = "UPDATE metadata SET value=@value WHERE key='reconciliation_pending';";
            command.Parameters.AddWithValue("@value", manifest.ToJsonString());
            await command.ExecuteNonQueryAsync();
        }
        File.Move(fixture.JournalPath + ".saved", fixture.JournalPath);
        Assert.True((await fixture.Catalog.SynchronizeAsync([fixture.Journals])).RecoveryConfirmationRequired);
        await File.WriteAllTextAsync(
            mapping,
            map.Replace("\"ProbesUsed\":5", "\"ProbesUsed\":4", StringComparison.Ordinal)
        );
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.ConfirmRecoveryAsync([fixture.Journals]));
        Assert.False((await fixture.Catalog.ReadSummaryAsync()).IsComplete);
        await File.WriteAllTextAsync(mapping, map);
        ExplorationHistorySummary recovered = await fixture.Catalog.ConfirmRecoveryAsync([fixture.Journals]);
        Assert.True(recovered.IsComplete);
        Assert.False(recovered.RecoveryConfirmationRequired);
        Assert.Equal(2700541, recovered.SoldValue);
        Assert.Equal(0, recovered.UnsoldValue);
        Assert.Equal(2000000, recovered.SaleEarnings);
    }

    [Theory]
    [InlineData("mapping")]
    [InlineData("settlement")]
    [InlineData("changed_during_replay")]
    [InlineData("completed_header")]
    [InlineData("capture")]
    [InlineData("capture_changed")]
    [InlineData("changed_mapping")]
    public async Task EveryDeferredJournalMustReturnBeforeItsCompleteRecordsBecomeEligible(string unavailableSource)
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Deferred survey", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Deferred survey 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
        ]);
        string partial = Path.Combine(fixture.Journals, "Journal.2026-10-01T170000.01.log");
        if (unavailableSource == "completed_header")
        {
            await File.WriteAllTextAsync(partial, "{\"timestamp\":\"");
        }
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        File.Move(fixture.JournalPath, fixture.JournalPath + ".saved");
        string mapping = Path.Combine(fixture.Journals, "Journal.2026-10-01T140000.01.log");
        string settlement = Path.Combine(fixture.Journals, "Journal.2026-10-01T160000.01.log");
        await File.WriteAllTextAsync(
            mapping,
            """
            {"timestamp":"2026-10-01T14:00:00Z","event":"Fileheader","Odyssey":true}
            {"event":"Commander","FID":"F100"}
            {"event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"BodyName":"Deferred survey 1","ProbesUsed":5,"EfficiencyTarget":6}
            {"event":"Music"
            """
        );
        await File.WriteAllTextAsync(
            settlement,
            """
            {"timestamp":"2026-10-01T16:00:00Z","event":"Fileheader","Odyssey":true}
            {"event":"Commander","FID":"F100"}
            {"event":"SellExplorationData","Systems":["Deferred survey"],"TotalEarnings":2000000}
            {"event":"Music"
            """
        );
        string originalMapping = await File.ReadAllTextAsync(mapping);
        string earlier = Path.Combine(fixture.Journals, "Journal.2026-10-01T130000.01.log");
        if (unavailableSource == "changed_mapping")
        {
            await File.WriteAllTextAsync(
                earlier,
                "{\"timestamp\":\"2026-10-01T13:00:00Z\",\"event\":\"Fileheader\",\"Odyssey\":true}\n"
            );
        }
        var interruptCapture = new SynchronousProgress(progress =>
        {
            if (
                unavailableSource is "capture" or "capture_changed"
                && progress.IsPreparing
                && !progress.IsDiscovering
                && progress.CurrentFile == Path.GetFileName(mapping)
            )
            {
                File.Move(settlement, settlement + ".saved");
            }
            if (
                unavailableSource == "changed_mapping"
                && !progress.IsPreparing
                && progress.CurrentFile == Path.GetFileName(earlier)
            )
            {
                File.WriteAllText(
                    mapping,
                    originalMapping.Replace("\"ProbesUsed\":5", "\"ProbesUsed\":4", StringComparison.Ordinal)
                );
            }
        });
        await Assert.ThrowsAnyAsync<IOException>(() =>
            fixture.Catalog.SynchronizeAsync([fixture.Journals], interruptCapture)
        );
        if (unavailableSource == "changed_mapping")
        {
            await File.WriteAllTextAsync(mapping, originalMapping);
        }
        if (unavailableSource == "completed_header")
        {
            await File.WriteAllTextAsync(
                partial,
                """
                {"timestamp":"2026-10-01T17:00:00Z","event":"Fileheader","Odyssey":true}
                {"event":"Commander","FID":"F100"}
                """ + "\n"
            );
        }
        File.Move(fixture.JournalPath + ".saved", fixture.JournalPath);
        string missing = unavailableSource == "mapping" ? mapping : settlement;
        if (unavailableSource is not ("capture" or "capture_changed"))
        {
            File.Move(missing, missing + ".saved");
        }
        _ = await Record.ExceptionAsync(() => fixture.Catalog.ObserveAsync([], [fixture.Journals]));
        ExplorationHistorySummary deferred = (await fixture.Catalog.QueryAsync(new())).Summary;
        Assert.False(deferred.IsComplete);
        Assert.Equal(449200, deferred.UnsoldValue);
        Assert.Equal(0, deferred.SaleEarnings);
        File.Move(missing + ".saved", missing);
        if (unavailableSource == "capture_changed")
        {
            string originalSale = await File.ReadAllTextAsync(settlement);
            await File.WriteAllTextAsync(
                settlement,
                originalSale.Replace("2000000", "9000000", StringComparison.Ordinal)
            );
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
            ExplorationHistorySummary uncertain = await fixture.Catalog.ReadSummaryAsync();
            Assert.True(uncertain.RecoveryConfirmationRequired);
            Assert.False(uncertain.IsComplete);
            await File.WriteAllTextAsync(settlement, originalSale);
            Assert.False((await fixture.Catalog.SynchronizeAsync([fixture.Journals])).IsComplete);
            Assert.True((await fixture.Catalog.ConfirmRecoveryAsync([fixture.Journals])).IsComplete);
        }
        const string unrelatedName = "Journal.2026-10-01T180000.01.log";
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Journals, unrelatedName),
            """
            {"timestamp":"2026-10-01T18:00:00Z","event":"Fileheader","Odyssey":true}
            {"event":"Commander","FID":"F100"}
            {"event":"Music","MusicTrack":"MainMenu"}
            """ + "\n"
        );
        if (unavailableSource == "changed_during_replay")
        {
            string captured = await File.ReadAllTextAsync(mapping);
            var change = new SynchronousProgress(progress =>
            {
                if (!progress.IsPreparing && progress.CurrentFile == Path.GetFileName(fixture.JournalPath))
                {
                    File.WriteAllText(
                        mapping,
                        captured.Replace("\"FID\":\"F100\"", "\"FID\":\"F200\"", StringComparison.Ordinal)
                    );
                }
            });
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                fixture.Catalog.SynchronizeAsync([fixture.Journals], change)
            );
            ExplorationHistorySummary retained = (await fixture.Catalog.QueryAsync(new())).Summary;
            Assert.False(retained.IsComplete);
            Assert.Equal(449200, retained.UnsoldValue);
            Assert.Equal(0, retained.SaleEarnings);
            await File.WriteAllTextAsync(mapping, captured);
        }
        Assert.True(JournalEventEnvelope.TryParse("""{"event":"Music"}""", out JournalEventEnvelope? observed, out _));
        ExplorationHistorySummary recovered = await fixture.Catalog.ObserveAsync(
            [observed! with { SourceRecordIdentity = unrelatedName + ":0" }],
            [fixture.Journals]
        );
        Assert.True(recovered.IsComplete);
        Assert.Equal(0, recovered.UnsoldValue);
        Assert.Equal(2700541, recovered.SoldValue);
        Assert.Equal(2000000, recovered.SaleEarnings);
        Assert.Equal(recovered, await fixture.Catalog.SynchronizeAsync([fixture.Journals]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettledUnknownMappingsDoNotBlockCurrentUnsoldBalances(bool lost)
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Old incomplete survey", 100, 0),
            """{"event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"ProbesUsed":5,"EfficiencyTarget":6}""",
            lost
                ? """{"event":"Resurrect","Option":"rebuy","Cost":0}"""
                : """{"event":"SellExplorationData","Systems":["Old incomplete survey"],"TotalEarnings":20000}""",
            Location("Current survey", 200, 0),
            Star(200),
        ]);
        ExplorationHistorySummary summary = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(23106, summary.UnsoldValue);
        Assert.Equal(lost ? 0 : 20000, summary.SaleEarnings);
        Assert.True(summary.IsComplete);
    }

    [Fact]
    public async Task RestartAndRepeatedBackfillDoNotDuplicateDataAndDetailsComeFromTheOriginalJournal()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Survey system", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        var restarted = new ExplorationHistoryCatalog(fixture.Data, "F100", true);
        await restarted.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryPage page = await restarted.QueryAsync(new());
        ExplorationHistoryRow row = Assert.Single(page.Rows);
        ExplorationHistoryDetails details = await restarted.ReadDetailsAsync(row.Id);
        Assert.Contains(
            details.Events,
            entry => entry.EventName == "Scan" && entry.Payload.GetProperty("StarType").GetString() == "NS"
        );
        Assert.Equal(23106, page.Summary.UnsoldValue);

        File.Delete(fixture.JournalPath);
        ExplorationHistoryPage retained = await restarted.QueryAsync(new());
        Assert.Equal(23106, retained.Summary.UnsoldValue);
        Assert.Single(retained.Rows);
        ExplorationHistoryDetails missing = await restarted.ReadDetailsAsync(row.Id);
        Assert.Empty(missing.Events);
        Assert.NotNull(missing.Notice);
    }

    [Fact]
    public async Task CommanderAndGalaxyScopesRemainSeparateEvenWhenJournalsShareAFolder()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Survey system", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        var otherCommander = new ExplorationHistoryCatalog(fixture.Data, "F200", true);
        var legacy = new ExplorationHistoryCatalog(fixture.Data, "F100", false);
        Assert.Equal(0, (await otherCommander.SynchronizeAsync([fixture.Journals])).UnsoldValue);
        Assert.Equal(0, (await legacy.SynchronizeAsync([fixture.Journals])).UnsoldValue);
        Assert.Empty((await otherCommander.QueryAsync(new())).Rows);
        Assert.Empty((await legacy.QueryAsync(new())).Rows);
        Assert.Equal(23106, (await fixture.Catalog.QueryAsync(new())).Summary.UnsoldValue);
    }

    [Fact]
    public async Task LiveAppendAndFollowingBackfillShareOneCheckpointWithoutDoubleCounting()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("First", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        JournalEventEnvelope[] live = fixture.Append([Location("Second", 200, 10), Star(200)]);
        Assert.Equal(46212, (await fixture.Catalog.ObserveAsync(live, [fixture.Journals])).UnsoldValue);
        Assert.Equal(46212, (await fixture.Catalog.SynchronizeAsync([fixture.Journals])).UnsoldValue);
        Assert.Equal(46212, (await fixture.Catalog.ObserveAsync(live, [fixture.Journals])).UnsoldValue);
        Assert.Equal(2, (await fixture.Catalog.QueryAsync(new())).TotalRows);
    }

    [Fact]
    public async Task SoldScansStaySettledWhileLostDataCanBeCollectedAgain()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Sold", 100, 0),
            Star(100),
            """{"event":"SellExplorationData","Systems":["Sold"],"Discovered":[],"TotalEarnings":20000}""",
            Star(100),
            Location("Lost", 200, 5),
            Star(200),
            """{"event":"Died"}""",
            """{"event":"Resurrect","Option":"rebuy","Cost":0}""",
            Star(200),
        ]);
        ExplorationHistorySummary summary = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryPage page = await fixture.Catalog.QueryAsync(new());
        Assert.Equal(23106, summary.UnsoldValue);
        Assert.Equal(23106, summary.LostValue);
        Assert.Equal(23106, summary.SoldValue);
        Assert.Equal(3, page.TotalRows);
        Assert.Equal(1, summary.SoldSystems);
        Assert.Equal(1, summary.LostSystems);
        Assert.Equal(1, summary.UnsoldSystems);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AMapAfterSellingTheScanAddsOnlyTheUnpaidUpgrade(bool splitIdentity)
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Survey system", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyName":"Survey system 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
            """{"event":"SellExplorationData","Systems":["Survey system"],"TotalEarnings":449200}""",
        ]);
        if (splitIdentity)
        {
            fixture.Append([
                """{"event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"ProbesUsed":5,"EfficiencyTarget":6}""",
            ]);
        }
        fixture.Append([
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Survey system 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
            """{"event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"ProbesUsed":5,"EfficiencyTarget":6}""",
        ]);
        ExplorationHistorySummary summary = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.True(summary.IsComplete);
        Assert.Equal(449200, summary.SoldValue);
        Assert.Equal(2251341, summary.UnsoldValue);
        ExplorationHistoryRow mapped = Assert.Single(
            (await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Unsold))).Rows
        );
        Assert.Equal(1, mapped.MappedBodies);
        ExplorationHistoryDetails details = await fixture.Catalog.ReadDetailsAsync(mapped.Id);
        Assert.Contains(details.Events, entry => entry.EventName == "Scan");
        Assert.Contains(details.Events, entry => entry.EventName == "SAAScanComplete");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARepeatedScanRetainsTheSelectedCollectionAndItsLatestDetails(bool importEarlier)
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Survey", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryRow selected = Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows);
        fixture.Append([Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        if (importEarlier)
        {
            string older = (await File.ReadAllTextAsync(fixture.JournalPath)).Replace(
                "2026-10-01",
                "2026-09-30",
                StringComparison.Ordinal
            );
            await File.WriteAllTextAsync(Path.Combine(fixture.Journals, "Journal.2026-09-30T120000.01.log"), older);
            await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        }
        ExplorationHistoryPage page = await fixture.Catalog.QueryAsync(new(SelectionAnchor: selected.Anchor));
        Assert.Equal(1, page.SelectedRowId);
        ExplorationHistoryRow current = Assert.Single(page.Rows);
        Assert.Equal(23106, current.EstimatedValue);
        Assert.Equal(1, current.ScannedBodies);
        ExplorationHistoryDetails details = await fixture.Catalog.ReadDetailsAsync(selected);
        Assert.Null(details.Notice);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 13, 0, 0, TimeSpan.Zero), Assert.Single(details.Events).Timestamp);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AStoredRowCannotOpenAnotherSystemsOriginalRecordsAfterReplayReusesItsId(bool retainOriginal)
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryRow selected = Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows);
        string replacement = (await File.ReadAllTextAsync(fixture.JournalPath))
            .Replace("Alpha", "Beta", StringComparison.Ordinal)
            .Replace("\"SystemAddress\":100", "\"SystemAddress\":200", StringComparison.Ordinal);
        if (retainOriginal)
        {
            await File.WriteAllTextAsync(
                Path.Combine(fixture.Journals, "Journal.2026-09-30T120000.01.log"),
                replacement.Replace("2026-10-01", "2026-09-30", StringComparison.Ordinal)
            );
        }
        else
        {
            await File.WriteAllTextAsync(fixture.JournalPath, replacement);
        }
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryDetails details = await fixture.Catalog.ReadDetailsAsync(selected);
        if (retainOriginal)
        {
            Assert.Equal(
                100,
                Assert
                    .Single(details.Events, entry => entry.EventName == "Scan")
                    .Payload.GetProperty("SystemAddress")
                    .GetInt64()
            );
            Assert.Equal(2, details.RowId);
        }
        else
        {
            Assert.Empty(details.Events);
            Assert.Equal("History is not available.", details.Notice);
        }
    }

    [Fact]
    public async Task ASelectedScanCollectionCannotFollowItsContextIntoAMappingUpgrade()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Survey system", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Survey system 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
            """{"event":"SellExplorationData","Systems":["Survey system"],"TotalEarnings":449200}""",
            """{"event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"ProbesUsed":5,"EfficiencyTarget":6}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryRow sold = Assert.Single(
            (await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Sold))).Rows
        );
        Assert.NotNull(sold.Anchor);
        ExplorationHistoryPage restored = await fixture.Catalog.QueryAsync(new(SelectionAnchor: sold.Anchor));
        Assert.Equal(1, restored.SelectedRowId);
        Assert.Equal(
            ExplorationDataStatus.Sold,
            Assert.Single(restored.Rows, row => row.Id == restored.SelectedRowId).Status
        );
    }

    [Fact]
    public async Task DeathWithoutAShipLossConfirmationAndNavigationBeaconRecordsDoNotEraseOrInflateData()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Survey system", 100, 0),
            Star(100),
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"StarType":"NS","StellarMass":1.4,"ScanType":"NavBeaconDetail"}""",
            """{"event":"Died"}""",
        ]);
        ExplorationHistorySummary summary = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(23106, summary.UnsoldValue);
        Assert.Equal(0, summary.LostValue);
        ExplorationHistoryRow row = Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows);
        Assert.Equal(1, row.ScannedBodies);
    }

    [Fact]
    public async Task PagingAndFiltersReturnOnlyTheRequestedRowsWithoutLoadingTheirRawDetails()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Alpha", 100, 0),
            Star(100),
            Location("Beta", 200, 5),
            Star(200),
            Location("Gamma", 300, 10),
            Star(300),
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryPage page = await fixture.Catalog.QueryAsync(new(Offset: 1, PageSize: 1));
        Assert.Equal(3, page.TotalRows);
        Assert.Equal("Beta", Assert.Single(page.Rows).SystemName);
        ExplorationHistoryPage filtered = await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Unsold, "ALP"));
        Assert.Equal(1, filtered.TotalRows);
        Assert.Equal("Alpha", Assert.Single(filtered.Rows).SystemName);
        Assert.Empty((await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Lost))).Rows);
        Assert.Equal(200, (await fixture.Catalog.QueryAsync(new(PageSize: 9999))).PageSize);
    }

    [Fact]
    public async Task CancellationBeforeCommitDoesNotLeaveHalfABackfillInTheCatalog()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Survey system", 100, 0), Star(100)]);
        using var cancel = new CancellationTokenSource();
        var recorder = new SynchronousProgress(_ => cancel.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Catalog.SynchronizeAsync([fixture.Journals], recorder, cancel.Token)
        );
        ExplorationHistoryPage page = await fixture.Catalog.QueryAsync(new());
        Assert.Empty(page.Rows);
        Assert.False(page.Summary.IsComplete);
        Assert.Equal(23106, (await fixture.Catalog.SynchronizeAsync([fixture.Journals])).UnsoldValue);
    }

    [Fact]
    public async Task IncompleteTailIsPickedUpWhenTheRestOfTheRecordArrives()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Survey system", 100, 0)]);
        const string tail =
            """{"timestamp":"2026-10-01T12:30:00Z","event":"Scan","SystemAddress":100,"BodyID":0,"StarType":"NS","StellarMass":1.4}""";
        await File.AppendAllTextAsync(fixture.JournalPath, tail[..40]);
        Assert.Equal(0, (await fixture.Catalog.SynchronizeAsync([fixture.Journals])).UnsoldValue);
        await File.AppendAllTextAsync(fixture.JournalPath, tail[40..] + "\n");
        Assert.Equal(23106, (await fixture.Catalog.SynchronizeAsync([fixture.Journals])).UnsoldValue);
    }

    [Fact]
    public async Task ImportingAnOlderScanReplaysTheLaterSaleAndRemainsIdempotent()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            """{"event":"MultiSellExplorationData","Discovered":[{"SystemName":"Old survey","NumBodies":1}],"TotalEarnings":15000}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        string original = await File.ReadAllTextAsync(fixture.JournalPath);
        fixture.Write([Location("Old survey", 100, 0), Star(100)]);
        File.Move(fixture.JournalPath, Path.Combine(fixture.Journals, "Journal.2026-09-30T120000.01.log"));
        string older = Path.Combine(fixture.Journals, "Journal.2026-09-30T120000.01.log");
        await File.WriteAllTextAsync(
            older,
            (await File.ReadAllTextAsync(older)).Replace("2026-10-01", "2026-09-30", StringComparison.Ordinal)
        );
        await File.WriteAllTextAsync(fixture.JournalPath, original);
        ExplorationHistorySummary result = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(0, result.UnsoldValue);
        Assert.Equal(23106, result.SoldValue);
        Assert.Equal(15000, result.SaleEarnings);
        Assert.Equal(ExplorationDataStatus.Sold, Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows).Status);
        Assert.Equal(15000, (await fixture.Catalog.SynchronizeAsync([fixture.Journals])).SaleEarnings);
    }

    [Fact]
    public async Task ReplacedJournalRebuildsDerivedDataButMissingSourcesPreventAnIncompleteRebuild()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Original", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        fixture.Write([Location("Replacement", 200, 0), Star(200)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal("Replacement", Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows).SystemName);
        string archived = Path.Combine(fixture.Journals, "Journal.2026-10-02T120000.01.log");
        await File.WriteAllTextAsync(
            archived,
            (await File.ReadAllTextAsync(fixture.JournalPath)).Replace(
                "2026-10-01",
                "2026-10-02",
                StringComparison.Ordinal
            )
        );
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        File.Delete(archived);
        fixture.Write([]);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
        Assert.Equal(23106, (await fixture.Catalog.QueryAsync(new())).Summary.UnsoldValue);
    }

    [Fact]
    public async Task ANewLiveSourceReadsItsMissingContextAndAContinuationRetainsItsCommander()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Survey system", 100, 0), Star(100)]);
        JournalEventEnvelope[] onlyLatest = fixture.Append([
            """{"event":"SellExplorationData","Systems":["Survey system"],"TotalEarnings":20000}""",
        ]);
        Assert.Equal(23106, (await fixture.Catalog.ObserveAsync(onlyLatest, [fixture.Journals])).SoldValue);
        await File.WriteAllLinesAsync(
            Path.Combine(fixture.Journals, "Journal.2026-10-01T120000.02.log"),
            [
                """{"timestamp":"2026-10-01T14:00:00Z","event":"Fileheader","gameversion":"4.0.0","Odyssey":true,"part":2}""",
                """{"timestamp":"2026-10-01T14:01:00Z","event":"Scan","SystemAddress":100,"BodyID":1,"StarType":"NS","StellarMass":1.4}""",
            ]
        );
        ExplorationHistorySummary result = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(23106, result.UnsoldValue);
        Assert.Equal(23106, result.SoldValue);
    }

    [Fact]
    public async Task CollectedDateBoundsAreInclusiveAndEmptyQueriesDoNotCreateACatalog()
    {
        using var fixture = new HistoryFixture();
        Assert.Empty((await fixture.Catalog.QueryAsync(new())).Rows);
        Assert.False(File.Exists(fixture.Catalog.FilePath));
        Assert.Equal("History is not available.", (await fixture.Catalog.ReadDetailsAsync(1)).Notice);
        fixture.Write([Location("Alpha", 100, 0), Star(100), Location("Beta", 200, 0), Star(200)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        var from = new DateTimeOffset(2026, 10, 1, 12, 2, 0, TimeSpan.Zero);
        ExplorationHistoryPage page = await fixture.Catalog.QueryAsync(new(FromUtc: from, ThroughUtc: from));
        Assert.Equal("Alpha", Assert.Single(page.Rows).SystemName);
        Assert.Empty((await fixture.Catalog.QueryAsync(new(ThroughUtc: from.AddTicks(-1)))).Rows);
    }

    [Fact]
    public async Task UnknownSalesAndMissingScansKeepUnconfirmedDataAndExplainTheLimitation()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Survey", 100, 0),
            Star(100),
            """{"event":"MultiSellExplorationData","TotalEarnings":5000}""",
            """{"event":"SAAScanComplete","SystemAddress":100,"BodyID":5,"ProbesUsed":5,"EfficiencyTarget":6}""",
            """{"event":"FSSBodySignals","BodyID":0,"Signals":[{"Type_Localised":"Biological","Count":2}]}""",
            """{"event":"Touchdown","BodyID":0,"Body":"Survey star","Latitude":12,"Longitude":24}""",
        ]);
        ExplorationHistorySummary result = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(23106, result.UnsoldValue);
        Assert.Equal(5000, result.SaleEarnings);
        Assert.Contains("original scan", result.Notice);
        Assert.False(result.IsComplete);
        long id = Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows).Id;
        ExplorationHistoryDetails details = await fixture.Catalog.ReadDetailsAsync(id);
        Assert.Contains(details.Events, entry => entry.EventName == "FSSBodySignals");
        Assert.Contains(details.Events, entry => entry.EventName == "Touchdown");
    }

    [Fact]
    public async Task HeaderGalaxyIsIndependentOfLoadGameExpansionOwnershipAndUnsafeProfileIdsAreRejected()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            """{"event":"LoadGame","FID":"F100","Odyssey":false}""",
            Location("Live Horizons", 100, 0),
            Star(100),
        ]);
        Assert.Equal(23106, (await fixture.Catalog.SynchronizeAsync([fixture.Journals])).UnsoldValue);
        Assert.Throws<ArgumentException>(() => new ExplorationHistoryCatalog(fixture.Data, "../F100", true));
        Assert.Throws<ArgumentException>(() => new ExplorationHistoryCatalog(fixture.Data, "..", true));
    }

    [Fact]
    public async Task AScanImportedBeforeASaleWithTheSameHeaderTimestampIsSettledCorrectly()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Survey system", 100, 0), Star(100)]);
        string scanJournal = await File.ReadAllTextAsync(fixture.JournalPath);
        fixture.Write([
            Location("Survey system", 100, 0),
            """{"event":"SellExplorationData","Systems":["Survey system"],"TotalEarnings":20000}""",
        ]);
        File.Move(fixture.JournalPath, Path.Combine(fixture.Journals, "Journal.2026-10-01T120000.02.log"));
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        await File.WriteAllTextAsync(fixture.JournalPath, scanJournal);
        ExplorationHistorySummary result = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(0, result.UnsoldValue);
        Assert.Equal(23106, result.SoldValue);
        Assert.Equal(20000, result.SaleEarnings);
        Assert.Equal(ExplorationDataStatus.Sold, Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows).Status);
    }

    [Fact]
    public async Task AppendsDuringBackfillRemainIncrementalWhenAnOlderIndexedJournalIsRemoved()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        string latestPath = Path.Combine(fixture.Journals, "Journal.2026-10-02T120000.01.log");
        await File.WriteAllLinesAsync(
            latestPath,
            [
                """{"timestamp":"2026-10-02T12:00:00Z","event":"Fileheader","gameversion":"4.0.0","Odyssey":true}""",
                """{"timestamp":"2026-10-02T12:00:01Z","event":"Commander","FID":"F100"}""",
                """{"timestamp":"2026-10-02T12:01:00Z","event":"Location","StarSystem":"Beta","SystemAddress":200,"StarPos":[10,0,0]}""",
            ]
        );
        var append = new SynchronousProgress(progress =>
        {
            if (!progress.IsPreparing && progress.ProcessedFiles == 1)
            {
                File.AppendAllText(
                    latestPath,
                    """{"timestamp":"2026-10-02T12:02:00Z","event":"Scan","SystemAddress":200,"BodyID":0,"StarType":"NS","StellarMass":1.4,"WasDiscovered":true}"""
                        + "\n"
                );
                File.SetLastWriteTimeUtc(latestPath, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc));
            }
        });
        await fixture.Catalog.SynchronizeAsync([fixture.Journals], append);
        File.Delete(fixture.JournalPath);
        ExplorationHistorySummary result = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(46212, result.UnsoldValue);
        Assert.Equal(2, result.UnsoldSystems);
        Assert.Equal(2, (await fixture.Catalog.QueryAsync(new())).TotalRows);
    }

    [Fact]
    public async Task TheLastPageMovesBackToAvailableRowsAfterSalesShrinkTheResultSet()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Alpha", 100, 0),
            Star(100),
            Location("Beta", 200, 0),
            Star(200),
            Location("Gamma", 300, 0),
            Star(300),
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        var query = new ExplorationHistoryQuery(ExplorationDataStatus.Unsold, Offset: 2, PageSize: 1);
        Assert.Equal("Alpha", Assert.Single((await fixture.Catalog.QueryAsync(query)).Rows).SystemName);
        JournalEventEnvelope[] sale = fixture.Append([
            """{"event":"SellExplorationData","Systems":["Beta","Gamma"],"TotalEarnings":40000}""",
        ]);
        await fixture.Catalog.ObserveAsync(sale, [fixture.Journals]);
        ExplorationHistoryPage remaining = await fixture.Catalog.QueryAsync(query);
        Assert.Equal(1, remaining.TotalRows);
        Assert.Equal(0, remaining.Offset);
        Assert.Equal("Alpha", Assert.Single(remaining.Rows).SystemName);
        await fixture.Catalog.ObserveAsync(
            fixture.Append(["""{"event":"SellExplorationData","Systems":["Alpha"],"TotalEarnings":20000}"""]),
            [fixture.Journals]
        );
        ExplorationHistoryPage empty = await fixture.Catalog.QueryAsync(query);
        Assert.Equal(0, empty.TotalRows);
        Assert.Equal(0, empty.Offset);
        Assert.Empty(empty.Rows);
    }

    [Theory]
    [InlineData("Journal.2026-10-01T140000.02.log", 2)]
    [InlineData("Journal.2026-10-01T120000.02.log", 3)]
    [InlineData("Journal.2026-10-01T120000.03.log", 3)]
    public async Task AnUnverifiedContinuationCannotCreditOrLoseAnotherCommandersData(string name, int part)
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Owned survey", 100, 0), Star(100)]);
        await File.WriteAllLinesAsync(
            Path.Combine(fixture.Journals, name),
            [
                $$"""{"timestamp":"2026-10-01T14:00:00Z","event":"Fileheader","gameversion":"4.0.0","Odyssey":true,"part":{{part}}}""",
                """{"timestamp":"2026-10-01T14:01:00Z","event":"Scan","SystemAddress":200,"BodyID":0,"StarType":"NS","StellarMass":1.4}""",
                """{"timestamp":"2026-10-01T14:02:00Z","event":"Resurrect","Option":"rebuy","Cost":0}""",
            ]
        );
        ExplorationHistorySummary summary = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(23106, summary.UnsoldValue);
        Assert.Equal(0, summary.LostValue);
        Assert.Equal("Owned survey", Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows).SystemName);
    }

    [Theory]
    [InlineData("sale")]
    [InlineData("rebuy")]
    public async Task ContinuationsRetainOwnershipWhenTheJournalPartNumberGrowsToThreeDigits(string outcome)
    {
        using var fixture = new HistoryFixture();
        fixture.Write([]);
        await File.WriteAllLinesAsync(
            Path.Combine(fixture.Journals, "Journal.2026-10-01T120000.99.log"),
            [
                """{"timestamp":"2026-10-01T13:00:00Z","event":"Fileheader","gameversion":"4.0.0","Odyssey":true,"part":99}""",
                """{"event":"Commander","FID":"F100"}""",
                Location("Survey", 100, 0),
                Star(100),
            ]
        );
        await File.WriteAllLinesAsync(
            Path.Combine(fixture.Journals, "Journal.2026-10-01T120000.100.log"),
            [
                """{"timestamp":"2026-10-01T14:00:00Z","event":"Fileheader","gameversion":"4.0.0","Odyssey":true,"part":100}""",
                outcome == "sale"
                    ? """{"event":"SellExplorationData","Systems":["Survey"],"TotalEarnings":20000}"""
                    : """{"event":"Resurrect","Option":"rebuy","Cost":0}""",
                Location("Beta", 200, 0),
                Star(200),
            ]
        );
        await File.WriteAllLinesAsync(
            Path.Combine(fixture.Journals, "Journal.2026-10-01T120000.101.log"),
            [
                """{"timestamp":"2026-10-01T15:00:00Z","event":"Fileheader","gameversion":"4.0.0","Odyssey":true,"part":101}""",
                Location("Gamma", 300, 0),
                Star(300),
            ]
        );
        ExplorationHistorySummary summary = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(46212, summary.UnsoldValue);
        Assert.Equal(2, summary.UnsoldSystems);
        ExplorationHistoryRow settled = Assert.Single(
            (await fixture.Catalog.QueryAsync(new())).Rows,
            row => row.SystemName == "Survey"
        );
        Assert.Equal(outcome == "sale" ? ExplorationDataStatus.Sold : ExplorationDataStatus.Lost, settled.Status);
    }

    [Fact]
    public async Task InterleavedCommandersUseTheirOwnMatchingPredecessorPart()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("First commander", 100, 0), Star(100)]);
        await File.WriteAllLinesAsync(
            Path.Combine(fixture.Journals, "Journal.2026-10-01T130000.01.log"),
            [
                """{"timestamp":"2026-10-01T13:00:00Z","event":"Fileheader","gameversion":"4.0.0","Odyssey":true,"part":1}""",
                """{"timestamp":"2026-10-01T13:00:01Z","event":"Commander","FID":"F200"}""",
                """{"timestamp":"2026-10-01T13:01:00Z","event":"Location","StarSystem":"Second commander","SystemAddress":200}""",
                """{"timestamp":"2026-10-01T13:02:00Z","event":"Scan","SystemAddress":200,"BodyID":0,"StarType":"NS","StellarMass":1.4}""",
            ]
        );
        await File.WriteAllLinesAsync(
            Path.Combine(fixture.Journals, "Journal.2026-10-01T120000.02.log"),
            [
                """{"timestamp":"2026-10-01T14:00:00Z","event":"Fileheader","gameversion":"4.0.0","Odyssey":true,"part":2}""",
                """{"timestamp":"2026-10-01T14:01:00Z","event":"Scan","SystemAddress":100,"BodyID":1,"StarType":"NS","StellarMass":1.4}""",
            ]
        );
        Assert.Equal(46212, (await fixture.Catalog.SynchronizeAsync([fixture.Journals])).UnsoldValue);
        var second = new ExplorationHistoryCatalog(fixture.Data, "F200", true);
        Assert.Equal(23106, (await second.SynchronizeAsync([fixture.Journals])).UnsoldValue);
        Assert.Equal("Second commander", Assert.Single((await second.QueryAsync(new())).Rows).SystemName);
    }

    [Theory]
    [InlineData(false, "sale")]
    [InlineData(true, "sale")]
    [InlineData(false, "rebuy")]
    public async Task ACompleteLiveCopyWinsOverItsArchivedPrefixInEitherDirectoryOrder(bool liveFirst, string outcome)
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Survey", 100, 0), Star(100)]);
        string archive = Path.Combine(fixture.Data, "archive");
        Directory.CreateDirectory(archive);
        File.Copy(fixture.JournalPath, Path.Combine(archive, Path.GetFileName(fixture.JournalPath)));
        JournalEventEnvelope[] live = fixture.Append([
            outcome == "sale"
                ? """{"event":"SellExplorationData","Systems":["Survey"],"TotalEarnings":20000}"""
                : """{"event":"Resurrect","Option":"rebuy","Cost":0}""",
        ]);
        string[] directories = liveFirst ? [fixture.Journals, archive] : [archive, fixture.Journals];
        await fixture.Catalog.SynchronizeAsync(directories);
        ExplorationHistoryRow row = Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows);
        Assert.Equal(outcome == "sale" ? ExplorationDataStatus.Sold : ExplorationDataStatus.Lost, row.Status);
        Assert.Equal(0, (await fixture.Catalog.ObserveAsync(live, directories)).UnsoldValue);
        Assert.Equal(outcome == "sale" ? 20000 : 0, (await fixture.Catalog.SynchronizeAsync(directories)).SaleEarnings);
    }

    [Fact]
    public async Task LiveObservationCanAdvanceAnArchiveCheckpointAndRejectsDivergentCopiesSafely()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Survey", 100, 0), Star(100)]);
        string archive = Path.Combine(fixture.Data, "archive");
        Directory.CreateDirectory(archive);
        string copy = Path.Combine(archive, Path.GetFileName(fixture.JournalPath));
        File.Copy(fixture.JournalPath, copy);
        await fixture.Catalog.SynchronizeAsync([archive]);
        JournalEventEnvelope[] live = fixture.Append([
            """{"event":"SellExplorationData","Systems":["Survey"],"TotalEarnings":20000}""",
        ]);
        ExplorationHistorySummary summary = await fixture.Catalog.ObserveAsync(live, [archive, fixture.Journals]);
        Assert.Equal(0, summary.UnsoldValue);
        Assert.Equal(23106, summary.SoldValue);
        string changed = (await File.ReadAllTextAsync(copy)).Replace("Survey", "Other!", StringComparison.Ordinal);
        await File.WriteAllTextAsync(copy, changed);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Catalog.SynchronizeAsync([archive, fixture.Journals])
        );
        Assert.Equal(23106, (await fixture.Catalog.QueryAsync(new())).Summary.SoldValue);
    }

    [Theory]
    [InlineData("sale", false)]
    [InlineData("sale", true)]
    [InlineData("rebuy", false)]
    [InlineData("rebuy", true)]
    public async Task AnIncompleteArchiveCannotUndoSettlementWhenTheCompleteLiveCopyIsUnavailable(
        string outcome,
        bool importEarlierJournal
    )
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Survey", 100, 0), Star(100)]);
        string archive = Path.Combine(fixture.Data, "archive");
        Directory.CreateDirectory(archive);
        File.Copy(fixture.JournalPath, Path.Combine(archive, Path.GetFileName(fixture.JournalPath)));
        fixture.Append([
            outcome == "sale"
                ? """{"event":"SellExplorationData","Systems":["Survey"],"TotalEarnings":20000}"""
                : """{"event":"Resurrect","Option":"rebuy","Cost":0}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([archive, fixture.Journals]);
        string complete = await File.ReadAllTextAsync(fixture.JournalPath);
        File.Delete(fixture.JournalPath);
        if (importEarlierJournal)
        {
            await File.WriteAllLinesAsync(
                Path.Combine(fixture.Journals, "Journal.2026-09-01T120000.01.log"),
                [
                    """{"timestamp":"2026-09-01T12:00:00Z","event":"Fileheader","gameversion":"4.0.0","Odyssey":true,"part":1}""",
                    """{"timestamp":"2026-09-01T12:00:01Z","event":"Commander","FID":"F100"}""",
                ]
            );
        }
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Catalog.SynchronizeAsync([archive, fixture.Journals])
        );
        ExplorationHistoryPage retained = await fixture.Catalog.QueryAsync(new());
        Assert.Equal(0, retained.Summary.UnsoldValue);
        Assert.Equal(
            outcome == "sale" ? ExplorationDataStatus.Sold : ExplorationDataStatus.Lost,
            Assert.Single(retained.Rows).Status
        );
        await File.WriteAllTextAsync(fixture.JournalPath, complete);
        Assert.Equal(0, (await fixture.Catalog.SynchronizeAsync([archive, fixture.Journals])).UnsoldValue);
    }

    [Fact]
    public async Task ObservingASubsetAfterACanceledFullBackfillCannotEstablishCompleteness()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Catalog.SynchronizeAsync(
                [fixture.Journals],
                new SynchronousProgress(_ => cancel.Cancel()),
                cancel.Token
            )
        );
        JournalEventEnvelope[] live = fixture.Append([Location("Beta", 200, 0), Star(200)]);
        ExplorationHistorySummary partial = await fixture.Catalog.ObserveAsync(live, [fixture.Journals]);
        Assert.Equal(46212, partial.UnsoldValue);
        Assert.False(partial.IsComplete);
        Assert.True((await fixture.Catalog.SynchronizeAsync([fixture.Journals])).IsComplete);
        ExplorationHistorySummary sold = await fixture.Catalog.ObserveAsync(
            fixture.Append(["""{"event":"SellExplorationData","Systems":["Beta"],"TotalEarnings":20000}"""]),
            [fixture.Journals]
        );
        Assert.True(sold.IsComplete);
        Assert.Equal(23106, sold.UnsoldValue);
    }

    private sealed class SynchronousProgress(Action<ExplorationHistoryProgress> report)
        : IProgress<ExplorationHistoryProgress>
    {
        public void Report(ExplorationHistoryProgress value) => report(value);
    }

    private static string Location(string name, long address, int x) =>
        $$"""{"event":"Location","StarSystem":"{{name}}","SystemAddress":{{address}},"StarPos":[{{x}},0,0]}""";

    private static string Star(long address) =>
        $$"""{"event":"Scan","SystemAddress":{{address}},"BodyID":0,"BodyName":"Test star","StarType":"NS","StellarMass":1.4,"WasDiscovered":true,"WasMapped":true}""";

    private sealed class HistoryFixture : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-exploration-history-" + Guid.NewGuid()
        );

        public HistoryFixture()
        {
            Data = Path.Combine(directory, "data");
            Journals = Path.Combine(directory, "journals");
            Directory.CreateDirectory(Journals);
            Catalog = new ExplorationHistoryCatalog(Data, "F100", true);
        }

        public string Data { get; }
        public string Journals { get; }
        public ExplorationHistoryCatalog Catalog { get; }
        public string JournalPath => Path.Combine(Journals, "Journal.2026-10-01T120000.01.log");

        public void Write(IReadOnlyList<string> events)
        {
            string[] all =
            [
                """{"timestamp":"2026-10-01T12:00:00Z","event":"Fileheader","gameversion":"4.0.0","Odyssey":true}""",
                """{"timestamp":"2026-10-01T12:00:01Z","event":"Commander","FID":"F100","Name":"History Commander"}""",
                .. events.Select(
                    (entry, index) =>
                        entry.Insert(
                            1,
                            "\"timestamp\":\"2026-10-01T12:"
                                + (index + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture)
                                + ":00Z\","
                        )
                ),
            ];
            File.WriteAllLines(JournalPath, all);
        }

        public JournalEventEnvelope[] Append(IReadOnlyList<string> entries)
        {
            long offset = new FileInfo(JournalPath).Length;
            var observed = new List<JournalEventEnvelope>();
            foreach (string entry in entries)
            {
                string line = entry.Insert(1, "\"timestamp\":\"2026-10-01T13:00:00Z\",");
                Assert.True(JournalEventEnvelope.TryParse(line, out JournalEventEnvelope? parsed, out _));
                observed.Add(
                    parsed! with
                    {
                        SourceRecordIdentity =
                            Path.GetFileName(JournalPath)
                            + ":"
                            + offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    }
                );
                File.AppendAllText(JournalPath, line + "\n", new UTF8Encoding(false));
                offset += Encoding.UTF8.GetByteCount(line + "\n");
            }
            return observed.ToArray();
        }

        public void Dispose() => Directory.Delete(directory, true);
    }
}
