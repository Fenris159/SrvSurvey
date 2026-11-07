using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Exploration.History;

namespace SrvSurvey.Core.Tests.Exploration.History;

public sealed partial class ExplorationHistoryCatalogTests
{
    private const string ProofScan =
        """{"event":"Scan","SystemAddress":100,"BodyID":1,"PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}""";
    private const string ProofMap =
        """{"event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"ProbesUsed":5,"EfficiencyTarget":6}""";
    private const string ProofSale =
        """{"event":"SellExplorationData","Systems":["Proof system"],"TotalEarnings":2000000}""";
    private const string ProofMusic = """{"event":"Music","MusicTrack":"MainMenu"}""";

    [Fact]
    public async Task UnverifiedGrowingSourcesRemainBoundedAndCannotLoseEarlierUncertainty()
    {
        using var fixture = new HistoryFixture();
        await SeedProofScanAsync(fixture);
        File.Move(fixture.JournalPath, fixture.JournalPath + ".saved");
        string mapping = await WriteProofJournalAsync(fixture, "140000", ProofMap);
        string sale = await WriteProofJournalAsync(fixture, "160000", ProofSale);
        string originalSale = await File.ReadAllTextAsync(sale);
        var interrupt = new SynchronousProgress(progress =>
        {
            if (progress.IsPreparing && !progress.IsDiscovering && progress.CurrentFile == Path.GetFileName(mapping))
            {
                File.Move(sale, sale + ".saved");
            }
        });
        await Assert.ThrowsAnyAsync<IOException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals], interrupt));
        File.Move(fixture.JournalPath + ".saved", fixture.JournalPath);
        File.Move(mapping, mapping + ".saved");
        File.Move(sale + ".saved", sale);
        await File.WriteAllTextAsync(sale, originalSale.Replace("2000000", "9000000", StringComparison.Ordinal));
        for (int index = 0; index < 10; index++)
        {
            await File.AppendAllTextAsync(sale, ProofMusic + "\n");
            _ = await Record.ExceptionAsync(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
        }
        JsonArray bounded = await ReadProofManifestAsync(fixture);
        Assert.Single(bounded, item => item!["Name"]!.GetValue<string>() == Path.GetFileName(sale));
        Assert.Equal(2, bounded.Count);
        File.Move(mapping + ".saved", mapping);
        _ = await Record.ExceptionAsync(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
        ExplorationHistorySummary pending = await fixture.Catalog.ReadSummaryAsync();
        Assert.True(pending.RecoveryConfirmationRequired);
        Assert.False(pending.IsComplete);
        Assert.False((await fixture.Catalog.SynchronizeAsync([fixture.Journals])).IsComplete);
        await File.WriteAllTextAsync(sale, originalSale + string.Concat(Enumerable.Repeat(ProofMusic + "\n", 10)));
        ExplorationHistorySummary recovered = await fixture.Catalog.ConfirmRecoveryAsync([fixture.Journals]);
        Assert.True(recovered.IsComplete);
        Assert.Equal(2700541, recovered.SoldValue);
        Assert.Equal(2000000, recovered.SaleEarnings);
        Assert.Equal(0, recovered.UnsoldValue);
    }

    [Fact]
    public async Task AFailedConfirmedRebuildRetainsItsConfirmationRequirement()
    {
        using var fixture = new HistoryFixture();
        await SeedProofScanAsync(fixture);
        File.Move(fixture.JournalPath, fixture.JournalPath + ".saved");
        await WriteProofJournalAsync(fixture, "140000", ProofMap, ProofSale);
        await Assert.ThrowsAnyAsync<IOException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
        JsonArray manifest = await ReadProofManifestAsync(fixture);
        JsonNode only = Assert.Single(manifest)!;
        only["PrefixHash"] = null;
        only["FormatVersion"] = 0;
        await WriteProofManifestAsync(fixture, manifest);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.ConfirmRecoveryAsync([fixture.Journals]));
        Assert.True((await fixture.Catalog.ReadSummaryAsync()).RecoveryConfirmationRequired);
        File.Move(fixture.JournalPath + ".saved", fixture.JournalPath);
        ExplorationHistorySummary paused = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.True(paused.RecoveryConfirmationRequired);
        Assert.False(paused.IsComplete);
        ExplorationHistorySummary recovered = await fixture.Catalog.ConfirmRecoveryAsync([fixture.Journals]);
        Assert.True(recovered.IsComplete);
        Assert.False(recovered.RecoveryConfirmationRequired);
        Assert.Equal(2000000, recovered.SaleEarnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyRecoveryRetainsItsOnlyKnownRecordOrHeader(bool emptyHeader)
    {
        using var fixture = new HistoryFixture();
        await SeedProofScanAsync(fixture);
        File.Move(fixture.JournalPath, fixture.JournalPath + ".saved");
        string mapping = await WriteProofJournalAsync(fixture, "140000", ProofMap, ProofSale);
        string original = await File.ReadAllTextAsync(mapping);
        await Assert.ThrowsAnyAsync<IOException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
        JsonArray manifest = await ReadProofManifestAsync(fixture);
        JsonNode only = Assert.Single(manifest)!;
        only["PrefixHash"] = null;
        only["FormatVersion"] = 0;
        if (!emptyHeader)
        {
            only["RecordOffset"] = original.IndexOf(ProofMap, StringComparison.Ordinal);
            only["RecordHash"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ProofMap)));
        }
        await WriteProofManifestAsync(fixture, manifest);
        File.Move(fixture.JournalPath + ".saved", fixture.JournalPath);
        await File.WriteAllTextAsync(
            mapping,
            emptyHeader
                ? string.Empty
                : original.Replace("\"ProbesUsed\":5", "\"ProbesUsed\":9", StringComparison.Ordinal)
        );
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.ConfirmRecoveryAsync([fixture.Journals]));
        Assert.False((await fixture.Catalog.ReadSummaryAsync()).IsComplete);
        await File.WriteAllTextAsync(mapping, original);
        ExplorationHistorySummary recovered = await fixture.Catalog.ConfirmRecoveryAsync([fixture.Journals]);
        Assert.True(recovered.IsComplete);
        Assert.Equal(2700541, recovered.SoldValue);
        Assert.Equal(2000000, recovered.SaleEarnings);
    }

    [Fact]
    public async Task DiscoveryInstabilityKeepsADeliveredSalePendingUntilItsSourceReturns()
    {
        using var fixture = new HistoryFixture();
        await SeedProofScanAsync(fixture);
        fixture.Append([ProofSale]);
        var append = new SynchronousProgress(progress =>
        {
            if (progress.IsDiscovering && progress.CurrentFile == Path.GetFileName(fixture.JournalPath))
            {
                File.AppendAllText(fixture.JournalPath, " ");
            }
        });
        await Assert.ThrowsAnyAsync<IOException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals], append));
        ExplorationHistorySummary paused = await fixture.Catalog.ReadSummaryAsync();
        Assert.False(paused.IsComplete);
        Assert.False(paused.RecoveryConfirmationRequired);
        Assert.Equal(449200, paused.UnsoldValue);
        File.Move(fixture.JournalPath, fixture.JournalPath + ".saved");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
        Assert.False((await fixture.Catalog.ReadSummaryAsync()).IsComplete);
        File.Move(fixture.JournalPath + ".saved", fixture.JournalPath);
        ExplorationHistorySummary recovered = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.True(recovered.IsComplete);
        Assert.Equal(0, recovered.UnsoldValue);
        Assert.Equal(449200, recovered.SoldValue);
        Assert.Equal(2000000, recovered.SaleEarnings);
    }

    [Fact]
    public async Task DeferredSuffixCannotReuseAChangedCommittedCommander()
    {
        using var fixture = new HistoryFixture();
        await SeedProofScanAsync(fixture);
        string latest = await WriteProofJournalAsync(fixture, "140000", ProofMusic);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        string original = await File.ReadAllTextAsync(latest);
        File.Move(fixture.JournalPath, fixture.JournalPath + ".saved");
        await File.AppendAllTextAsync(latest, ProofMap + "\n");
        await Assert.ThrowsAnyAsync<IOException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
        await File.WriteAllTextAsync(
            latest,
            original.Replace("\"FID\":\"F100\"", "\"FID\":\"F200\"", StringComparison.Ordinal)
                + ProofMap
                + "\n"
                + ProofMusic
                + "\n"
        );
        File.Move(fixture.JournalPath + ".saved", fixture.JournalPath);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
        ExplorationHistorySummary paused = await fixture.Catalog.ReadSummaryAsync();
        Assert.False(paused.IsComplete);
        Assert.Equal(449200, paused.UnsoldValue);
        await File.WriteAllTextAsync(latest, original + ProofMap + "\n" + ProofMusic + "\n");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
        Assert.True((await fixture.Catalog.ReadSummaryAsync()).RecoveryConfirmationRequired);
        ExplorationHistorySummary recovered = await fixture.Catalog.ConfirmRecoveryAsync([fixture.Journals]);
        Assert.True(recovered.IsComplete);
        Assert.Equal(2700541, recovered.UnsoldValue);
    }

    private static async Task SeedProofScanAsync(HistoryFixture fixture)
    {
        fixture.Write([Location("Proof system", 100, 0), ProofScan]);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
    }

    private static async Task<string> WriteProofJournalAsync(
        HistoryFixture fixture,
        string time,
        params string[] events
    )
    {
        string path = Path.Combine(fixture.Journals, "Journal.2026-10-01T" + time + ".01.log");
        string header =
            "{\"timestamp\":\"2026-10-01T" + time[..2] + ":00:00Z\",\"event\":\"Fileheader\",\"Odyssey\":true}";
        await File.WriteAllTextAsync(
            path,
            string.Join("\n", new[] { header, "{\"event\":\"Commander\",\"FID\":\"F100\"}" }.Concat(events)) + "\n"
        );
        return path;
    }

    private static async Task<JsonArray> ReadProofManifestAsync(HistoryFixture fixture)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = fixture.Catalog.FilePath, Pooling = false }.ToString()
        );
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM metadata WHERE key='reconciliation_pending';";
        return JsonNode.Parse((string)(await command.ExecuteScalarAsync())!)!.AsArray();
    }

    private static async Task WriteProofManifestAsync(HistoryFixture fixture, JsonArray manifest)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = fixture.Catalog.FilePath, Pooling = false }.ToString()
        );
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE metadata SET value=@value WHERE key='reconciliation_pending';";
        command.Parameters.AddWithValue("@value", manifest.ToJsonString());
        await command.ExecuteNonQueryAsync();
    }
}
