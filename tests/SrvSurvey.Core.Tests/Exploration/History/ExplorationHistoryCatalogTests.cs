using System.Text;
using SrvSurvey.Core.Exploration.History;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Exploration.History;

public sealed class ExplorationHistoryCatalogTests
{
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

    [Fact]
    public async Task AMapAfterSellingTheScanAddsOnlyTheUnpaidUpgrade()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Survey system", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Survey system 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
            """{"event":"SellExplorationData","Systems":["Survey system"],"TotalEarnings":449200}""",
            """{"event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"ProbesUsed":5,"EfficiencyTarget":6}""",
        ]);
        ExplorationHistorySummary summary = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
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
            Path.Combine(fixture.Journals, "Journal.2026-10-01T140000.02.log"),
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
