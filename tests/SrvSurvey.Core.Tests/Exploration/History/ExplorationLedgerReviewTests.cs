using SrvSurvey.Core.Exploration.History;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Exploration.History;

public sealed partial class ExplorationHistoryCatalogTests
{
    [Fact]
    public async Task NewHistoricalSourcesStayBeforeTheResetWhileLaterScansCount()
    {
        using var fixture = new HistoryFixture(new FrozenLedgerTime(new(2026, 10, 1, 14, 0, 0, TimeSpan.Zero)));
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        await fixture.Catalog.ResetSurveyValueAsync();
        await WriteLedgerSourceAsync(fixture, "130000", "13", "Beta", 200, richPlanet: true);
        await WriteLedgerSourceAsync(fixture, "150000", "15", "Gamma", 300);
        ExplorationHistorySummary result = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(495412, result.UnsoldValue);
        Assert.Equal(23106, result.TrackedUnsoldValue);
    }

    [Fact]
    public async Task RewritingAPrecedingRecordDoesNotMoveAnOldScanIntoTheNewSurveyPeriod()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        await fixture.Catalog.ResetSurveyValueAsync();
        string original = await File.ReadAllTextAsync(fixture.JournalPath);
        await File.WriteAllTextAsync(
            fixture.JournalPath,
            original.Replace("\"StarSystem\":", "\"StarSystem\":     ", StringComparison.Ordinal)
        );
        ExplorationHistorySummary result = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(23106, result.UnsoldValue);
        Assert.Equal(0, result.TrackedUnsoldValue);
    }

    [Fact]
    public async Task ReconstructingWithoutTheSavedBoundaryPausesValueUntilItsJournalReturns()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint point = await fixture.Catalog.CaptureSurveyResetPointAsync();
        string original = await File.ReadAllTextAsync(fixture.JournalPath);
        File.Delete(fixture.JournalPath);
        await WriteLedgerSourceAsync(fixture, "150000", "15", "Alpha", 100);
        var rebuilt = new ExplorationHistoryCatalog(Path.Combine(fixture.Data, "rebuilt"), "F100", true);
        await rebuilt.SynchronizeAsync([fixture.Journals]);
        ExplorationHistorySummary missing = await rebuilt.ApplySurveyResetPointAsync(point);
        Assert.True(missing.IsComplete);
        Assert.False(missing.IsSurveyValueAvailable);
        Assert.Null(missing.TrackedUnsoldValue);
        Assert.Equal(23106, missing.UnsoldValue);
        await File.WriteAllTextAsync(fixture.JournalPath, original);
        ExplorationHistorySummary restored = await rebuilt.SynchronizeAsync([fixture.Journals]);
        Assert.True(restored.IsSurveyValueAvailable);
        Assert.Equal(0, restored.TrackedUnsoldValue);
        Assert.Equal(23106, restored.UnsoldValue);
    }

    [Fact]
    public async Task IncrementalUpdatesRetainVerifiedMarkersFromOtherAvailableJournals()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await WriteLedgerSourceAsync(fixture, "130000", "13", "Beta", 200);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        await fixture.Catalog.ResetSurveyValueAsync();
        string activeJournal = Path.Combine(fixture.Journals, "Journal.2026-10-01T130000.01.log");
        ExplorationHistorySummary scanned = await fixture.Catalog.ObserveAsync(
            fixture.Append(
                [
                    """{"event":"Scan","SystemAddress":200,"BodyID":1,"BodyName":"Beta 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
                ],
                activeJournal
            ),
            [fixture.Journals]
        );
        Assert.True(scanned.IsSurveyValueAvailable);
        Assert.Equal(449200, scanned.TrackedUnsoldValue);
        Assert.Equal(495412, scanned.UnsoldValue);
        ExplorationHistorySummary sold = await fixture.Catalog.ObserveAsync(
            fixture.Append(
                ["""{"event":"SellExplorationData","Systems":["Beta"],"TotalEarnings":500000}"""],
                activeJournal
            ),
            [fixture.Journals]
        );
        Assert.True(sold.IsSurveyValueAvailable);
        Assert.Equal(0, sold.TrackedUnsoldValue);
        Assert.Equal(23106, sold.UnsoldValue);
    }

    [Fact]
    public async Task EditingAnUntouchedOlderJournalCannotReclassifyItsCachedCredits()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        string original = (await File.ReadAllTextAsync(fixture.JournalPath)).ReplaceLineEndings("\r\n");
        await File.WriteAllTextAsync(fixture.JournalPath, original);
        await WriteLedgerSourceAsync(fixture, "130000", "13", "Beta", 200);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        await fixture.Catalog.ResetSurveyValueAsync();
        await File.WriteAllTextAsync(fixture.JournalPath, original.ReplaceLineEndings("\n"));
        string active = Path.Combine(fixture.Journals, "Journal.2026-10-01T130000.01.log");
        ExplorationHistorySummary incremental = await fixture.Catalog.ObserveAsync(
            fixture.Append(["""{"event":"FSDJump","JumpDist":5}"""], active),
            [fixture.Journals]
        );
        Assert.Equal(0, incremental.TrackedUnsoldValue);
        Assert.Equal(46212, incremental.UnsoldValue);
        ExplorationHistorySummary replayed = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.True(replayed.IsSurveyValueAvailable);
        Assert.Equal(0, replayed.TrackedUnsoldValue);
        Assert.Equal(46212, replayed.UnsoldValue);
    }

    [Fact]
    public async Task ApplyingASavedPointWaitsForReplayBeforeMovingAnEditedMarker()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        string original = (await File.ReadAllTextAsync(fixture.JournalPath)).ReplaceLineEndings("\r\n");
        await File.WriteAllTextAsync(fixture.JournalPath, original);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint point = await fixture.Catalog.CaptureSurveyResetPointAsync();
        await File.WriteAllTextAsync(fixture.JournalPath, original.ReplaceLineEndings("\n"));
        ExplorationHistorySummary edited = await fixture.Catalog.ApplySurveyResetPointAsync(point);
        Assert.False(edited.IsSurveyValueAvailable);
        Assert.Null(edited.TrackedUnsoldValue);
        ExplorationHistorySummary replayed = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.True(replayed.IsSurveyValueAvailable);
        Assert.Equal(0, replayed.TrackedUnsoldValue);
        Assert.Equal(23106, replayed.UnsoldValue);
    }

    [Fact]
    public async Task ReceiptSearchUsesSoldSystemsWithoutItsSyntheticTitle()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Ledger Alpha", 100, 0),
            Star(100),
            """{"event":"SellExplorationData","Systems":["Ledger Alpha"],"TotalEarnings":10}""",
            Location("Sol", 200, 0),
            Star(200),
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Single((await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid))).Rows);
        ExplorationHistoryPage filtered = await fixture.Catalog.QueryAsync(
            new(SearchText: "Sol") { IncludePayments = true }
        );
        Assert.Equal("Sol", Assert.Single(filtered.Rows).SystemName);
        Assert.Empty((await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid, "Sol"))).Rows);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task LandingBeforeTheFirstScanIsRetainedOnlyUntilTheNextSale(bool sellBeforeScan, int expectedLandings)
    {
        using var fixture = new HistoryFixture();
        var events = new List<string>
        {
            Location("Alpha", 100, 0),
            """{"event":"Touchdown","SystemAddress":100,"BodyID":0,"OnPlanet":true}""",
            """{"event":"Touchdown","SystemAddress":100,"BodyID":0,"OnPlanet":true}""",
        };
        if (sellBeforeScan)
        {
            events.Add("""{"event":"SellExplorationData","Systems":["Alpha"],"TotalEarnings":1}""");
        }

        events.Add(Star(100));
        events.Add("""{"event":"SellExplorationData","Systems":["Alpha"],"TotalEarnings":23106}""");
        fixture.Write(events);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryRow receipt = (await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid))).Rows[0];
        Assert.Equal(expectedLandings, receipt.Payment!.LandedBodies);
        Assert.Equal(1, receipt.ScannedBodies);
    }

    [Fact]
    public async Task BatchOnlyQueriesCannotSelectAPaymentAnchor()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Alpha", 100, 0),
            Star(100),
            """{"event":"SellExplorationData","Systems":["Alpha"],"TotalEarnings":10}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryRow paid = Assert.Single(
            (await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid))).Rows
        );
        ExplorationHistoryPage batches = await fixture.Catalog.QueryAsync(new(SelectionAnchor: paid.Anchor));
        Assert.Null(batches.SelectedRowId);
        Assert.Null(batches.SelectedRow);
    }

    [Fact]
    public async Task AnUnverifiableStartingPointPausesAutomaticValueUntilTheOriginalRecordReturns()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        await fixture.Catalog.ResetSurveyValueAsync();
        string original = await File.ReadAllTextAsync(fixture.JournalPath);
        await File.WriteAllTextAsync(
            fixture.JournalPath,
            original.Replace("\"BodyName\":", "\"BodyName\": ", StringComparison.Ordinal)
        );
        ExplorationHistorySummary changed = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.True(changed.IsComplete);
        Assert.False(changed.IsSurveyValueAvailable);
        Assert.Null(changed.TrackedUnsoldValue);
        Assert.Equal(23106, changed.UnsoldValue);
        await File.WriteAllTextAsync(fixture.JournalPath, original);
        ExplorationHistorySummary restored = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.True(restored.IsSurveyValueAvailable);
        Assert.Equal(0, restored.TrackedUnsoldValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"JumpDist\":-1")]
    public async Task MissingOrInvalidDistancesStayUnavailableAndDoNotPoisonTheNextReceipt(string field)
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            $$"""{"event":"FSDJump"{{field}}}""",
            """{"event":"SellExplorationData","Systems":["Alpha"],"TotalEarnings":10}""",
            """{"event":"FSDJump","JumpDist":5.5}""",
            """{"event":"SellExplorationData","Systems":["Beta"],"TotalEarnings":20}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryPage receipts = await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid));
        Assert.Equal(2, receipts.TotalRows);
        Assert.Equal(5.5, receipts.Rows[0].Payment!.DistanceTravelled);
        Assert.Equal(1, receipts.Rows[0].Payment!.JumpCount);
        Assert.Null(receipts.Rows[1].Payment!.DistanceTravelled);
        Assert.Equal(1, receipts.Rows[1].Payment!.JumpCount);
    }

    [Fact]
    public async Task ProfileResetPersistsTheOriginalPointAndBothMetricScopesInOneProfileUpdate()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint point = await fixture.Catalog.CaptureSurveyResetPointAsync();
        var profiles = new CommanderProfileStore(fixture.Data);
        await profiles.SaveExplorationAsync("F100", "Commander", true, new(777, 123.5, 42, 9, 4, 2));
        await profiles.SaveExplorationResetAsync("F100", "Commander", true, new(0, 123.5, 42, 0, 0, 0), point);
        CommanderProfileData saved = (await profiles.LoadAsync("F100", true)).Data!;
        Assert.Equal(0, saved.Exploration.EstimatedRewards);
        Assert.Equal(123.5, saved.Exploration.DistanceTravelled);
        Assert.Equal(42, saved.Exploration.JumpCount);
        Assert.Equal(0, saved.Exploration.ScanCount);
        Assert.Equal(0, saved.Exploration.DetailedSurfaceScanCount);
        Assert.Equal(0, saved.Exploration.LandedBodyCount);
        Assert.Equal(point.Id, saved.ExplorationSurveyStart!.Id);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            profiles.SaveExplorationResetAsync("F200", "Other", true, new(0, 0, 0, 0, 0, 0), point)
        );
        Assert.False(File.Exists(profiles.GetProfilePath("F200", true)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task AnInvalidResetIntentCannotReplaceAValidProfile(int invalidKind)
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint point = await fixture.Catalog.CaptureSurveyResetPointAsync();
        ExplorationSurveySourceCheckpoint source = Assert.Single(point.Sources);
        IReadOnlyList<ExplorationSurveySourceCheckpoint> invalid = invalidKind switch
        {
            0 => [null!],
            1 => [source with { SourceName = "../other.log" }],
            2 => [source with { Offset = -2 }],
            3 => [source with { Hash = "not-a-record-hash" }],
            _ => [source, source],
        };
        var profiles = new CommanderProfileStore(fixture.Data);
        await profiles.SaveExplorationAsync("F100", "Commander", true, new(777, 123.5, 42, 9, 4, 2));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            profiles.SaveExplorationResetAsync(
                "F100",
                "Commander",
                true,
                new(0, 0, 0, 0, 0, 0),
                point with
                {
                    Sources = invalid,
                }
            )
        );
        CommanderProfileData saved = (await profiles.LoadAsync("F100", true)).Data!;
        Assert.Equal(777, saved.Exploration.EstimatedRewards);
        Assert.Equal(9, saved.Exploration.ScanCount);
        Assert.Null(saved.ExplorationSurveyStart);
    }

    [Fact]
    public async Task AnUnreadableResetIntentIsReportedAsAProfileLoadFailure()
    {
        using var fixture = new HistoryFixture();
        var profiles = new CommanderProfileStore(fixture.Data);
        Directory.CreateDirectory(fixture.Data);
        await File.WriteAllTextAsync(
            profiles.GetProfilePath("F100", true),
            """{"fid":"F100","explRewards":777,"explorationSurveyStart":"invalid-object"}"""
        );
        CommanderProfileLoadResult result = await profiles.LoadAsync("F100", true);
        Assert.Null(result.Data);
        Assert.Contains("starting point", result.Error);
    }

    [Fact]
    public async Task AConsumedFrontierKeepsLaterIndexedCreditsAfterTheReset()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint consumed = await fixture.Catalog.CaptureSurveyResetPointAsync();
        await fixture.Catalog.ObserveAsync(
            fixture.Append([
                """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Alpha 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
            ]),
            [fixture.Journals]
        );
        ExplorationSurveyResetPoint point = await fixture.Catalog.CaptureSurveyResetPointAsync(consumed.Sources);
        ExplorationHistorySummary result = await fixture.Catalog.ApplySurveyResetPointAsync(point);
        Assert.Equal(449200, result.TrackedUnsoldValue);
        Assert.Equal(472306, result.UnsoldValue);
    }

    [Fact]
    public async Task AnIdOnlyLandingFollowsTheBodysPreviouslyCollectedNameAlias()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Alpha", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyName":"Test star","StarType":"NS","StellarMass":1.4,"WasDiscovered":true,"WasMapped":true}""",
            """{"event":"Touchdown","SystemAddress":100,"BodyID":0,"OnPlanet":true}""",
            Star(100),
            """{"event":"SellExplorationData","Systems":["Alpha"],"TotalEarnings":23106}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryRow paid = Assert.Single(
            (await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid))).Rows
        );
        Assert.Equal(1, paid.ScannedBodies);
        Assert.Equal(1, paid.Payment!.LandedBodies);
    }

    [Fact]
    public async Task OverflowingRecordedDistanceIsUnavailableInsteadOfAnInfiniteTrip()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            """{"event":"FSDJump","JumpDist":1e308}""",
            """{"event":"FSDJump","JumpDist":1e308}""",
            """{"event":"SellExplorationData","Systems":["Alpha"],"TotalEarnings":10}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationPaymentDetails receipt = Assert
            .Single((await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid))).Rows)
            .Payment!;
        Assert.Equal(2, receipt.JumpCount);
        Assert.Null(receipt.DistanceTravelled);
    }

    private static Task WriteLedgerSourceAsync(
        HistoryFixture fixture,
        string name,
        string hour,
        string system,
        long address,
        bool richPlanet = false
    )
    {
        string fields = richPlanet
            ? """
                "BodyID":1,"PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false
                """
            : """
                "BodyID":0,"StarType":"NS","StellarMass":1.4,"WasDiscovered":true,"WasMapped":true
                """;
        return File.WriteAllLinesAsync(
            Path.Combine(fixture.Journals, $"Journal.2026-10-01T{name}.01.log"),
            [
                $$"""{"timestamp":"2026-10-01T{{hour}}:00:00Z","event":"Fileheader","Odyssey":true}""",
                $$"""{"timestamp":"2026-10-01T{{hour}}:00:01Z","event":"Commander","FID":"F100"}""",
                $$"""{"timestamp":"2026-10-01T{{hour}}:01:00Z","event":"Location","StarSystem":"{{system}}","SystemAddress":{{address}}}""",
                $$"""{"timestamp":"2026-10-01T{{hour}}:02:00Z","event":"Scan","SystemAddress":{{address}},"BodyName":"{{system}} A",{{fields}}}""",
            ]
        );
    }

    private sealed class FrozenLedgerTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
