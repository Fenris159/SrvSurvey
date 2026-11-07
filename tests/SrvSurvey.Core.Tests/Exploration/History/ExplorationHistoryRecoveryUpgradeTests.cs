using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Exploration.History;

namespace SrvSurvey.Core.Tests.Exploration.History;

public sealed partial class ExplorationHistoryCatalogTests
{
    [Fact]
    public async Task InterruptedLegacyContextUpgradeCanLearnCommanderProofAfterRestoration()
    {
        using var fixture = new HistoryFixture();
        await SeedProofScanAsync(fixture);
        string latest = await WriteProofJournalAsync(fixture, "140000", ProofMusic);
        await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        string legacyContext;
        using (
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = fixture.Catalog.FilePath, Pooling = false }.ToString()
            )
        )
        {
            await connection.OpenAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT context FROM sources WHERE name=@name;";
            command.Parameters.AddWithValue("@name", Path.GetFileName(latest));
            JsonObject legacy = JsonNode.Parse((string)(await command.ExecuteScalarAsync())!)!.AsObject();
            legacy.Remove("CommanderRecord");
            legacyContext = legacy.ToJsonString();
            command.CommandText =
                "UPDATE sources SET context=@context WHERE name=@name; UPDATE metadata SET value='5' WHERE key='valuation_version';";
            command.Parameters.AddWithValue("@context", legacyContext);
            await command.ExecuteNonQueryAsync();
        }
        File.Move(fixture.JournalPath, fixture.JournalPath + ".saved");
        await File.AppendAllTextAsync(latest, ProofMap + "\n");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
        JsonArray manifest = await ReadProofManifestAsync(fixture);
        foreach (JsonNode? item in manifest)
        {
            item!["CheckpointContext"] = legacyContext;
        }
        await WriteProofManifestAsync(fixture, manifest);
        File.Move(fixture.JournalPath + ".saved", fixture.JournalPath);
        ExplorationHistorySummary recovered = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.True(recovered.IsComplete);
        Assert.Equal(2700541, recovered.UnsoldValue);
        using var verified = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = fixture.Catalog.FilePath, Pooling = false }.ToString()
        );
        await verified.OpenAsync();
        using SqliteCommand read = verified.CreateCommand();
        read.CommandText = "SELECT context FROM sources WHERE name=@name;";
        read.Parameters.AddWithValue("@name", Path.GetFileName(latest));
        JsonNode context = JsonNode.Parse((string)(await read.ExecuteScalarAsync())!)!;
        Assert.Equal(Path.GetFileName(latest), context["CommanderRecord"]!["Name"]!.GetValue<string>());
    }

    [Fact]
    public async Task AHashLearnedBeforeALaterInputFailsRemainsMandatory()
    {
        using var fixture = new HistoryFixture();
        await SeedProofScanAsync(fixture);
        string first = await WriteProofJournalAsync(fixture, "130000", ProofMusic);
        string sale = await WriteProofJournalAsync(fixture, "140000", ProofSale);
        string later = await WriteProofJournalAsync(fixture, "160000", ProofMusic);
        string originalSale = await File.ReadAllTextAsync(sale);
        var disappear = new SynchronousProgress(progress =>
        {
            if (progress.IsPreparing && !progress.IsDiscovering && progress.CurrentFile == Path.GetFileName(first))
            {
                File.Move(sale, sale + ".saved");
                File.Move(later, later + ".saved");
            }
        });
        await Assert.ThrowsAnyAsync<IOException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals], disappear));
        File.Move(sale + ".saved", sale);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.SynchronizeAsync([fixture.Journals]));
        JsonArray manifest = await ReadProofManifestAsync(fixture);
        JsonNode learned = Assert.Single(
            manifest,
            item => item!["Name"]!.GetValue<string>() == Path.GetFileName(sale)
        )!;
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(originalSale))),
            learned["PrefixHash"]?.GetValue<string>()
        );
        await File.WriteAllTextAsync(sale, originalSale.Replace("2000000", "9000000", StringComparison.Ordinal));
        File.Move(later + ".saved", later);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.ConfirmRecoveryAsync([fixture.Journals]));
        Assert.False((await fixture.Catalog.ReadSummaryAsync()).IsComplete);
        await File.WriteAllTextAsync(sale, originalSale);
        ExplorationHistorySummary recovered = await fixture.Catalog.ConfirmRecoveryAsync([fixture.Journals]);
        Assert.True(recovered.IsComplete);
        Assert.Equal(2000000, recovered.SaleEarnings);
        Assert.Equal(449200, recovered.SoldValue);
    }

    [Fact]
    public async Task ModernCompleteSaleBoundsCannotBeNormalizedAsLegacyRawTails()
    {
        using var fixture = new HistoryFixture();
        await SeedProofScanAsync(fixture);
        string sale = await WriteProofJournalAsync(fixture, "140000", ProofSale);
        string complete = await File.ReadAllTextAsync(sale);
        string incomplete = complete[..(complete.IndexOf(ProofSale, StringComparison.Ordinal) + 30)];
        await File.WriteAllTextAsync(sale, incomplete);
        string missing = await WriteProofJournalAsync(fixture, "160000", ProofMusic);
        string missingText = await File.ReadAllTextAsync(missing);
        string header = complete[..complete.IndexOf('\n')];
        string otherHeader = missingText[..missingText.IndexOf('\n')];
        var manifest = new JsonArray
        {
            new JsonObject
            {
                ["Name"] = Path.GetFileName(missing),
                ["Length"] = Encoding.UTF8.GetByteCount(missingText),
                ["HeaderHash"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(otherHeader))),
                ["PrefixHash"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(missingText))),
            },
            new JsonObject
            {
                ["Name"] = Path.GetFileName(sale),
                ["Length"] = Encoding.UTF8.GetByteCount(incomplete),
                ["HeaderHash"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(header))),
                ["PrefixHash"] = null,
            },
        };
        // This is a persisted format-0 retry from before complete boundaries were stored separately.
        using (
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = fixture.Catalog.FilePath, Pooling = false }.ToString()
            )
        )
        {
            await connection.OpenAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO metadata(key,value) VALUES('reconciliation_pending',@value);";
            command.Parameters.AddWithValue("@value", manifest.ToJsonString());
            await command.ExecuteNonQueryAsync();
        }
        await File.WriteAllTextAsync(sale, complete);
        File.Move(missing, missing + ".saved");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.ConfirmRecoveryAsync([fixture.Journals]));
        await File.WriteAllTextAsync(sale, incomplete);
        File.Move(missing + ".saved", missing);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.ConfirmRecoveryAsync([fixture.Journals]));
        Assert.False((await fixture.Catalog.ReadSummaryAsync()).IsComplete);
        await File.WriteAllTextAsync(sale, complete);
        ExplorationHistorySummary recovered = await fixture.Catalog.ConfirmRecoveryAsync([fixture.Journals]);
        Assert.True(recovered.IsComplete);
        Assert.Equal(449200, recovered.SoldValue);
        Assert.Equal(2000000, recovered.SaleEarnings);
    }

    [Fact]
    public async Task ATransactionalChangedSourceRebuildDiscardsOnlyTheCheckpointItReplaces()
    {
        using var fixture = new HistoryFixture();
        await SeedProofScanAsync(fixture);
        string original = await File.ReadAllTextAsync(fixture.JournalPath);
        string changed = original
            .Replace("\"FID\":\"F100\"", "\"FID\":\"F100\",\"Name\":\"Renamed commander\"", StringComparison.Ordinal)
            .Replace("\"event\":\"Scan\"", "\"event\":\"Scan\",\"ScanType\":\"Detailed\"", StringComparison.Ordinal);
        await File.WriteAllTextAsync(fixture.JournalPath, changed + ProofMusic + "\n");
        ExplorationHistorySummary rebuilt = await fixture.Catalog.SynchronizeAsync([fixture.Journals]);
        Assert.True(rebuilt.IsComplete);
        Assert.False(rebuilt.RecoveryConfirmationRequired);
        Assert.Equal(449200, rebuilt.UnsoldValue);
        Assert.Equal(0, rebuilt.SaleEarnings);
    }
}
