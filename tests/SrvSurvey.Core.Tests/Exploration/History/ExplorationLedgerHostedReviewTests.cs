using SrvSurvey.Core.Exploration.History;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Exploration.History;

public sealed partial class ExplorationHistoryCatalogTests
{
    [Fact]
    public async Task AWhollyUnreadNewSourceRemainsAfterTheConsumedResetFrontier()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint consumed = await fixture.Catalog.CaptureSurveyResetPointAsync();
        await WriteLedgerSourceAsync(fixture, "150000", "15", "Beta", 200, richPlanet: true);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint point = await fixture.Catalog.CaptureSurveyResetPointAsync(consumed.Sources);
        ExplorationHistorySummary reset = await fixture.Catalog.ApplySurveyResetPointAsync(point);
        Assert.Equal(449200, reset.TrackedUnsoldValue);
        Assert.Equal(472306, reset.UnsoldValue);
    }

    [Fact]
    public async Task AConsumedJournalMissingFromTheIndexBlocksResetEvenWhenAnotherIsIndexed()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint indexed = await fixture.Catalog.CaptureSurveyResetPointAsync();
        await WriteLedgerSourceAsync(fixture, "150000", "15", "Beta", 200);
        string newJournal = Path.Combine(fixture.Journals, "Journal.2026-10-01T150000.01.log");
        JournalEventEnvelope entry = Assert.Single(
            fixture.Append(["""{"event":"StartJump","JumpType":"Hyperspace"}"""], newJournal)
        );
        File.Delete(newJournal);
        await fixture.Catalog.ObserveAsync([entry], [fixture.Journals]);

        ExplorationSurveySourceCheckpoint consumed = ExplorationSurveySourceCheckpoint.FromEntry(entry)!;
        ExplorationSurveyResetRecoveryException error =
            await Assert.ThrowsAsync<ExplorationSurveyResetRecoveryException>(() =>
                fixture.Catalog.CaptureSurveyResetPointAsync(
                    [.. indexed.Sources, consumed],
                    includeEarlierSources: true
                )
            );
        Assert.Equal(Path.GetFileName(newJournal), error.JournalName);
    }

    [Fact]
    public async Task AReplacedIndexedRecordCannotAnchorTheResetEvenWhenItMatchesTheCachedTip()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        JournalEventEnvelope entry = Assert.Single(
            fixture.Append(["""{"event":"StartJump","JumpType":"Hyperspace","StarSystem":"Alpha"}"""])
        );
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        string contents = await File.ReadAllTextAsync(fixture.JournalPath);
        string replacement = entry.RawJson.Replace("Alpha", "Delta", StringComparison.Ordinal);
        await File.WriteAllTextAsync(
            fixture.JournalPath,
            contents.Replace(entry.RawJson, replacement, StringComparison.Ordinal)
        );

        ExplorationSurveySourceCheckpoint cursor = ExplorationSurveySourceCheckpoint.FromEntry(entry)!;
        ExplorationSurveyResetRecoveryException error =
            await Assert.ThrowsAsync<ExplorationSurveyResetRecoveryException>(() =>
                fixture.Catalog.CaptureSurveyResetPointAsync([cursor], includeEarlierSources: true)
            );
        Assert.Equal(Path.GetFileName(fixture.JournalPath), error.JournalName);
    }

    [Fact]
    public async Task AnUnobservedOlderJournalDoesNotBlockSavingTheActiveJournalReset()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await WriteLedgerSourceAsync(fixture, "150000", "15", "Beta", 200);
        string activeJournal = Path.Combine(fixture.Journals, "Journal.2026-10-01T150000.01.log");
        JournalEventEnvelope entry = Assert.Single(
            fixture.Append(["""{"event":"StartJump","JumpType":"Hyperspace"}"""], activeJournal)
        );
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        string header = (await File.ReadAllLinesAsync(fixture.JournalPath))[0];
        await File.WriteAllLinesAsync(fixture.JournalPath, [header]);

        ExplorationSurveySourceCheckpoint cursor = ExplorationSurveySourceCheckpoint.FromEntry(entry)!;
        ExplorationSurveyResetPoint reset = await fixture.Catalog.CaptureSurveyResetPointAsync(
            [cursor],
            includeEarlierSources: true
        );
        ExplorationHistorySummary summary = await fixture.Catalog.ApplySurveyResetPointAsync(reset);
        Assert.Equal(46212, summary.UnsoldValue);
        Assert.False(summary.IsSurveyValueAvailable);
    }

    [Fact]
    public async Task AFreshPeriodUsesCoherentCachedHistoryWhenItsOriginalWasArchived()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        File.Delete(fixture.JournalPath);
        ExplorationHistorySummary reset = await fixture.Catalog.ResetSurveyValueAsync();
        Assert.True(reset.IsSurveyValueAvailable);
        Assert.Equal(0, reset.TrackedUnsoldValue);
        await WriteLedgerSourceAsync(fixture, "150000", "15", "Beta", 200);
        ExplorationHistorySummary later = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.True(later.IsSurveyValueAvailable);
        Assert.Equal(23106, later.TrackedUnsoldValue);
        Assert.Equal(46212, later.UnsoldValue);
    }

    [Theory]
    [InlineData(false, 472306)]
    [InlineData(true, 449200)]
    public async Task AConsumerExplicitlyAcknowledgesOlderClosedSourcesWithoutConsumingNewerOnes(
        bool includeEarlier,
        int expectedValue
    )
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await WriteLedgerSourceAsync(fixture, "130000", "13", "Beta", 200);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint known = await fixture.Catalog.CaptureSurveyResetPointAsync();
        ExplorationSurveySourceCheckpoint consumed = Assert.Single(
            known.Sources,
            source => source.SourceName.Contains("T130000", StringComparison.Ordinal)
        );
        await WriteLedgerSourceAsync(fixture, "150000", "15", "Gamma", 300, richPlanet: true);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint point = await fixture.Catalog.CaptureSurveyResetPointAsync(
            [consumed],
            includeEarlierSources: includeEarlier
        );
        ExplorationHistorySummary result = await fixture.Catalog.ApplySurveyResetPointAsync(point);
        Assert.Equal(expectedValue, result.TrackedUnsoldValue);
        Assert.Equal(495412, result.UnsoldValue);
    }

    [Fact]
    public async Task ACachedCatalogCannotTrustAnOlderCoordinatePointAfterReplayWithoutItsOriginal()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint original = await fixture.Catalog.CaptureSurveyResetPointAsync();
        string changed = (await File.ReadAllTextAsync(fixture.JournalPath)).Replace(
            "\"StarSystem\":",
            "\"StarSystem\":      ",
            StringComparison.Ordinal
        );
        await File.WriteAllTextAsync(fixture.JournalPath, changed);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        File.Delete(fixture.JournalPath);
        ExplorationHistorySummary unavailable = await fixture.Catalog.ApplySurveyResetPointAsync(original);
        Assert.False(unavailable.IsSurveyValueAvailable);
        Assert.Null(unavailable.TrackedUnsoldValue);
        Assert.Equal(23106, unavailable.UnsoldValue);
        ExplorationHistorySummary fresh = await fixture.Catalog.ResetSurveyValueAsync();
        Assert.True(fresh.IsSurveyValueAvailable);
        Assert.Equal(0, fresh.TrackedUnsoldValue);
    }

    [Fact]
    public async Task ExplicitConsumerCursorsRemainAuthoritativeForOlderSources()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await WriteLedgerSourceAsync(fixture, "130000", "13", "Beta", 200);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint consumed = await fixture.Catalog.CaptureSurveyResetPointAsync();
        await fixture.Catalog.ObserveAsync(
            fixture.Append([
                """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Alpha 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
            ]),
            [fixture.Journals]
        );
        ExplorationSurveyResetPoint point = await fixture.Catalog.CaptureSurveyResetPointAsync(
            consumed.Sources,
            includeEarlierSources: true
        );
        ExplorationHistorySummary result = await fixture.Catalog.ApplySurveyResetPointAsync(point);
        Assert.Equal(449200, result.TrackedUnsoldValue);
        Assert.Equal(495412, result.UnsoldValue);
    }

    [Fact]
    public async Task AnExplicitlyUnreadCursorCannotAcknowledgeOtherUnconsumedJournals()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint consumed = await fixture.Catalog.CaptureSurveyResetPointAsync();
        await WriteLedgerSourceAsync(fixture, "150000", "15", "Gamma", 300, richPlanet: true);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint withUnread = await fixture.Catalog.CaptureSurveyResetPointAsync(consumed.Sources);
        await WriteLedgerSourceAsync(fixture, "130000", "13", "Beta", 200);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint recaptured = await fixture.Catalog.CaptureSurveyResetPointAsync(
            withUnread.Sources,
            includeEarlierSources: true
        );
        ExplorationHistorySummary result = await fixture.Catalog.ApplySurveyResetPointAsync(recaptured);
        Assert.True(result.IsSurveyValueAvailable);
        Assert.Equal(472306, result.TrackedUnsoldValue);
        Assert.Equal(495412, result.UnsoldValue);
    }

    [Fact]
    public async Task RecapturingAStaleConsumerDoesNotTrustItsOffsetsAfterReplayAndArchive()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint consumed = await fixture.Catalog.CaptureSurveyResetPointAsync();
        string changed = (await File.ReadAllTextAsync(fixture.JournalPath)).Replace(
            "\"StarSystem\":",
            "\"StarSystem\":      ",
            StringComparison.Ordinal
        );
        await File.WriteAllTextAsync(fixture.JournalPath, changed);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        File.Delete(fixture.JournalPath);
        ExplorationSurveyResetPoint recaptured = await fixture.Catalog.CaptureSurveyResetPointAsync(consumed.Sources);
        ExplorationHistorySummary result = await fixture.Catalog.ApplySurveyResetPointAsync(recaptured);
        Assert.False(result.IsSurveyValueAvailable);
        Assert.Null(result.TrackedUnsoldValue);
        Assert.Equal(23106, result.UnsoldValue);
        ExplorationHistorySummary fresh = await fixture.Catalog.ResetSurveyValueAsync();
        Assert.True(fresh.IsSurveyValueAvailable);
        Assert.Equal(0, fresh.TrackedUnsoldValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task AmbiguousLandingNamesCannotBeAssignedByTheNextSignalsBody(int signalsBody)
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Alpha", 100, 0),
            Star(100),
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Test star","StarType":"NS","StellarMass":1.4,"WasDiscovered":true,"WasMapped":true}""",
            """{"event":"Touchdown","SystemAddress":100,"BodyID":0,"OnPlanet":true}""",
            """{"event":"Touchdown","SystemAddress":100,"BodyName":"Test star","OnPlanet":true}""",
            $$"""{"event":"FSSBodySignals","SystemAddress":100,"BodyID":{{signalsBody}},"BodyName":"Test star","Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}""",
            """{"event":"SellExplorationData","Systems":["Alpha"],"TotalEarnings":46212}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryRow paid = Assert.Single(
            (await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid))).Rows
        );
        Assert.Equal(2, paid.ScannedBodies);
        Assert.Equal(1, paid.Payment!.LandedBodies);
    }

    [Fact]
    public async Task APromotedNameKeyCannotReceiveAnAmbiguousLanding()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Alpha", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyName":"Test star","StarType":"NS","StellarMass":1.4,"WasDiscovered":true,"WasMapped":true}""",
            Star(100),
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Test star","StarType":"NS","StellarMass":1.4,"WasDiscovered":true,"WasMapped":true}""",
            """{"event":"Touchdown","SystemAddress":100,"BodyID":1,"OnPlanet":true}""",
            """{"event":"Touchdown","SystemAddress":100,"BodyName":"Test star","OnPlanet":true}""",
            """{"event":"SellExplorationData","Systems":["Alpha"],"TotalEarnings":46212}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryRow paid = Assert.Single(
            (await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid))).Rows
        );
        Assert.Equal(2, paid.ScannedBodies);
        Assert.Equal(1, paid.Payment!.LandedBodies);
    }

    [Fact]
    public async Task ANewBodyIdCannotClaimAnAmbiguousNameOnlyLanding()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Alpha", 100, 0),
            Star(100),
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Test star","StarType":"NS","StellarMass":1.4,"WasDiscovered":true,"WasMapped":true}""",
            """{"event":"Touchdown","SystemAddress":100,"BodyID":0,"OnPlanet":true}""",
            """{"event":"Touchdown","SystemAddress":100,"BodyName":"Test star","OnPlanet":true}""",
            """{"event":"Scan","SystemAddress":100,"BodyID":2,"BodyName":"Test star","StarType":"NS","StellarMass":1.4,"WasDiscovered":true,"WasMapped":true}""",
            """{"event":"SellExplorationData","Systems":["Alpha"],"TotalEarnings":69318}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryRow paid = Assert.Single(
            (await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid))).Rows
        );
        Assert.Equal(3, paid.ScannedBodies);
        Assert.Equal(1, paid.Payment!.LandedBodies);
    }

    [Fact]
    public async Task VerifiedArchivedSourcesRemainAvailableWhenAnotherConsumerMarkerIsRecovered()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([Location("Alpha", 100, 0), Star(100)]);
        await WriteLedgerSourceAsync(fixture, "130000", "13", "Beta", 200);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationSurveyResetPoint consumed = await fixture.Catalog.CaptureSurveyResetPointAsync();
        string changed = (await File.ReadAllTextAsync(fixture.JournalPath)).Replace(
            "\"StarSystem\":",
            "\"StarSystem\":      ",
            StringComparison.Ordinal
        );
        await File.WriteAllTextAsync(fixture.JournalPath, changed);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        DateTime modified = File.GetLastWriteTimeUtc(fixture.JournalPath);
        File.Delete(fixture.JournalPath);
        File.Delete(Path.Combine(fixture.Journals, "Journal.2026-10-01T130000.01.log"));
        ExplorationSurveyResetPoint point = await fixture.Catalog.CaptureSurveyResetPointAsync(
            consumed.Sources,
            includeEarlierSources: true
        );
        ExplorationHistorySummary missing = await fixture.Catalog.ApplySurveyResetPointAsync(point);
        Assert.False(missing.IsSurveyValueAvailable);
        await File.WriteAllTextAsync(fixture.JournalPath, changed);
        File.SetLastWriteTimeUtc(fixture.JournalPath, modified);
        ExplorationHistorySummary recovered = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.True(recovered.IsSurveyValueAvailable);
        Assert.Equal(0, recovered.TrackedUnsoldValue);
        Assert.Equal(46212, recovered.UnsoldValue);
    }

    [Fact]
    public async Task ALandingNameAliasCannotMakeAnAlreadySoldIdBodyEarnAgain()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Alpha", 100, 0),
            """{"event":"Touchdown","SystemAddress":100,"BodyName":"Test star","OnPlanet":true}""",
            """{"event":"Scan","SystemAddress":100,"BodyID":0,"StarType":"NS","StellarMass":1.4,"WasDiscovered":true,"WasMapped":true}""",
            Star(100),
            """{"event":"SellExplorationData","Systems":["Alpha"],"TotalEarnings":23106}""",
            """{"event":"Scan","SystemAddress":100,"BodyName":"Test star","StarType":"NS","StellarMass":1.4,"WasDiscovered":true,"WasMapped":true}""",
        ]);
        ExplorationHistorySummary summary = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.Equal(0, summary.UnsoldValue);
        Assert.Equal(23106, summary.SoldValue);
        ExplorationHistoryRow paid = Assert.Single(
            (await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid))).Rows
        );
        Assert.Equal(1, paid.Payment!.LandedBodies);
        Assert.Equal(1, paid.ScannedBodies);
    }

    [Fact]
    public async Task ASignalsEventAttachesTheLandingAliasToItsAlreadyScannedBody()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Alpha", 100, 0),
            """{"event":"Scan","SystemAddress":100,"BodyName":"Test star","StarType":"NS","StellarMass":1.4,"WasDiscovered":true,"WasMapped":true}""",
            """{"event":"Touchdown","SystemAddress":100,"BodyID":0,"OnPlanet":true}""",
            """{"event":"FSSBodySignals","SystemAddress":100,"BodyID":0,"BodyName":"Test star","Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}""",
            """{"event":"SellExplorationData","Systems":["Alpha"],"TotalEarnings":23106}""",
        ]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        ExplorationHistoryRow paid = Assert.Single(
            (await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid))).Rows
        );
        Assert.Equal(1, paid.Payment!.LandedBodies);
        Assert.Equal(1, paid.ScannedBodies);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnAmbiguousMappingRemainsIncompleteUntilItsSystemIsSettled(bool promotedNameKey)
    {
        using var fixture = new HistoryFixture();
        var entries = new List<string> { Location("Alpha", 100, 0) };
        if (promotedNameKey)
        {
            entries.Add(
                """{"event":"Scan","SystemAddress":100,"BodyName":"Shared planet","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}"""
            );
        }
        entries.AddRange([
            """{"event":"Scan","SystemAddress":100,"BodyID":0,"BodyName":"Shared planet","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Shared planet","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
            """{"event":"SAAScanComplete","SystemAddress":100,"BodyName":"Shared planet","ProbesUsed":5,"EfficiencyTarget":6}""",
        ]);
        fixture.Write(entries);
        ExplorationHistorySummary unresolved = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.False(unresolved.IsComplete);
        Assert.Equal(898400, unresolved.UnsoldValue);
        ExplorationHistoryRow row = Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows);
        Assert.Equal(2, row.ScannedBodies);
        Assert.Equal(0, row.MappedBodies);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Catalog.ResetSurveyValueAsync());
        var reopened = new ExplorationHistoryCatalog(fixture.Data, "F100", true);
        Assert.False((await reopened.SynchronizeAsync([fixture.Journals])).IsComplete);
        ExplorationHistorySummary identified = await fixture.Catalog.ObserveAsync(
            fixture.Append([
                """{"event":"SAAScanComplete","SystemAddress":100,"BodyID":0,"BodyName":"Shared planet","ProbesUsed":5,"EfficiencyTarget":6}""",
            ]),
            [fixture.Journals]
        );
        Assert.False(identified.IsComplete);
        Assert.Equal(3149741, identified.UnsoldValue);
        ExplorationHistorySummary settled = await fixture.Catalog.ObserveAsync(
            fixture.Append(["""{"event":"SellExplorationData","Systems":["Alpha"],"TotalEarnings":3000000}"""]),
            [fixture.Journals]
        );
        Assert.True(settled.IsComplete);
        Assert.Equal(0, settled.UnsoldValue);
        ExplorationHistoryRow paid = Assert.Single(
            (await fixture.Catalog.QueryAsync(new(ExplorationDataStatus.Paid))).Rows
        );
        Assert.Equal(2, paid.ScannedBodies);
        Assert.Equal(1, paid.MappedBodies);
    }

    [Fact]
    public async Task AnAmbiguousPositiveScanCannotSilentlyCompleteTheValuation()
    {
        using var fixture = new HistoryFixture();
        fixture.Write([
            Location("Alpha", 100, 0),
            Star(100),
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Test star","StarType":"NS","StellarMass":1.4,"WasDiscovered":true,"WasMapped":true}""",
            """{"event":"Scan","SystemAddress":100,"BodyName":"Test star","StarType":"NS","StellarMass":1.4,"WasDiscovered":false,"WasMapped":false}""",
        ]);
        ExplorationHistorySummary summary = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.False(summary.IsComplete);
        Assert.Equal(46212, summary.UnsoldValue);
        Assert.Equal(2, Assert.Single((await fixture.Catalog.QueryAsync(new())).Rows).ScannedBodies);
    }

    [Fact]
    public async Task RecapturingAnArchivedInteriorCursorPreservesItsCurrentGenerationProof()
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
        File.Delete(fixture.JournalPath);
        ExplorationSurveyResetPoint recaptured = await fixture.Catalog.CaptureSurveyResetPointAsync(consumed.Sources);
        ExplorationHistorySummary result = await fixture.Catalog.ApplySurveyResetPointAsync(recaptured);
        Assert.True(result.IsSurveyValueAvailable);
        Assert.Equal(449200, result.TrackedUnsoldValue);
        Assert.Equal(472306, result.UnsoldValue);
        var reopened = new ExplorationHistoryCatalog(fixture.Data, "F100", true);
        ExplorationHistorySummary persisted = await reopened.ReadSummaryAsync();
        Assert.True(persisted.IsSurveyValueAvailable);
        Assert.Equal(449200, persisted.TrackedUnsoldValue);
    }
}
