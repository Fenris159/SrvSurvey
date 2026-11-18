using SrvSurvey.Core.Exploration.History;

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
}
