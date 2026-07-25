using System.Text.Json.Nodes;
using SrvSurvey.Core.Diagnostics;

namespace SrvSurvey.Core.Tests.Diagnostics;

public sealed class HistoricalSystemRebuildServiceTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-historical-rebuild-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task RebuildPreservesUnknownFieldsAndCreatesVerifiedBackup()
    {
        var paths = CreatePaths();
        var target = Path.Combine(
            paths.Data,
            "systems",
            "F123",
            "Test_42.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(
            target,
            """
            {
              "name": "Test",
              "address": 42,
              "futureRoot": 7,
              "bodies": [
                { "name": "Test 1", "id": 1, "futureBody": true }
              ]
            }
            """);
        var original = await File.ReadAllBytesAsync(target);
        await WriteJournalAsync(
            paths.Journals,
            "Journal.2026-07-20T120000.01.log",
            "Test",
            42);
        var service = new HistoricalSystemRebuildService(
            paths.Data,
            paths.Journals,
            paths.Backups,
            () => DateTimeOffset.Parse("2026-07-25T12:00:00Z"));

        var result = await service.RebuildAsync(
            "F123",
            "Drew",
            JournalHistoryAnalyzer.EliteReleaseDate);

        Assert.Equal(1, result.ProcessedJournalFileCount);
        Assert.Equal(1, result.ReconstructedSystemCount);
        Assert.Equal(1, result.UpdatedSystemFileCount);
        Assert.Equal(0, result.CreatedSystemFileCount);
        var backup = Assert.IsType<string>(result.BackupDirectory);
        Assert.Equal(
            original,
            await File.ReadAllBytesAsync(Path.Combine(
                backup,
                "originals",
                "Test_42.json")));
        Assert.True(File.Exists(Path.Combine(backup, "manifest.json")));
        var rebuilt = JsonNode.Parse(await File.ReadAllTextAsync(target))!
            .AsObject();
        Assert.Equal(7, rebuilt["futureRoot"]!.GetValue<int>());
        Assert.Equal("Drew", rebuilt["commander"]!.GetValue<string>());
        Assert.True(rebuilt["honked"]!.GetValue<bool>());
        var body = Assert.IsType<JsonObject>(
            Assert.Single(rebuilt["bodies"]!.AsArray()));
        Assert.True(body["futureBody"]!.GetValue<bool>());
        Assert.Equal("LandableBody", body["type"]!.GetValue<string>());
        Assert.True(body["dssComplete"]!.GetValue<bool>());
        Assert.Equal(1, body["bioSignalCount"]!.GetValue<int>());
        var organism = Assert.IsType<JsonObject>(
            Assert.Single(body["organisms"]!.AsArray()));
        Assert.Equal(
            "Aleoida Arcus",
            organism["speciesLocalized"]!.GetValue<string>());
        Assert.True(organism["analyzed"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ActivationFailureRollsBackExistingAndNewFiles()
    {
        var paths = CreatePaths();
        var systemDirectory = Path.Combine(paths.Data, "systems", "F123");
        Directory.CreateDirectory(systemDirectory);
        var existingPath = Path.Combine(systemDirectory, "First_1.json");
        await File.WriteAllTextAsync(
            existingPath,
            """{"name":"First","address":1,"future":true,"bodies":[]}""");
        var original = await File.ReadAllBytesAsync(existingPath);
        await WriteJournalAsync(
            paths.Journals,
            "Journal.2026-07-20T120000.01.log",
            "First",
            1,
            includeShutdown: false);
        await WriteJournalAsync(
            paths.Journals,
            "Journal.2026-07-21T120000.01.log",
            "Second",
            2);
        var activationCount = 0;
        var service = new HistoricalSystemRebuildService(
            paths.Data,
            paths.Journals,
            paths.Backups,
            () => DateTimeOffset.Parse("2026-07-25T12:00:00Z"),
            _ => ++activationCount == 2
                ? new IOException("Injected activation failure.")
                : null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RebuildAsync(
                "F123",
                "Drew",
                JournalHistoryAnalyzer.EliteReleaseDate));

        Assert.Contains("rolled back", exception.Message);
        Assert.Equal(original, await File.ReadAllBytesAsync(existingPath));
        Assert.False(File.Exists(Path.Combine(systemDirectory, "Second_2.json")));
        Assert.Single(Directory.GetDirectories(paths.Backups));
    }

    [Fact]
    public async Task MalformedExistingSystemIsNotOverwritten()
    {
        var paths = CreatePaths();
        var target = Path.Combine(
            paths.Data,
            "systems",
            "F123",
            "Test_42.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "{\"name\":");
        var original = await File.ReadAllBytesAsync(target);
        await WriteJournalAsync(
            paths.Journals,
            "Journal.2026-07-20T120000.01.log",
            "Test",
            42);
        var service = new HistoricalSystemRebuildService(
            paths.Data,
            paths.Journals,
            paths.Backups,
            () => DateTimeOffset.Parse("2026-07-25T12:00:00Z"));

        var result = await service.RebuildAsync(
            "F123",
            "Drew",
            JournalHistoryAnalyzer.EliteReleaseDate);

        Assert.Equal(0, result.UpdatedSystemFileCount);
        Assert.Null(result.BackupDirectory);
        Assert.Single(result.Warnings);
        Assert.Contains("malformed", result.Warnings[0]);
        Assert.Equal(original, await File.ReadAllBytesAsync(target));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private (string Data, string Journals, string Backups) CreatePaths()
    {
        var data = Path.Combine(temporaryDirectory, "data");
        var journals = Path.Combine(temporaryDirectory, "journals");
        var backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(journals);
        return (data, journals, backups);
    }

    private static Task WriteJournalAsync(
        string journalDirectory,
        string fileName,
        string systemName,
        long systemAddress,
        bool includeShutdown = true)
    {
        var shutdown = includeShutdown
            ? "{\"timestamp\":\"2026-07-20T12:09:00Z\",\"event\":\"Shutdown\"}"
            : string.Empty;
        return File.WriteAllTextAsync(
            Path.Combine(journalDirectory, fileName),
            $$"""
            {"timestamp":"2026-07-20T12:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-07-20T12:01:00Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2026-07-20T12:02:00Z","event":"Location","StarSystem":"{{systemName}}","SystemAddress":{{systemAddress}},"StarPos":[1,2,3]}
            {"timestamp":"2026-07-20T12:03:00Z","event":"FSSDiscoveryScan","SystemAddress":{{systemAddress}},"BodyCount":1}
            {"timestamp":"2026-07-20T12:04:00Z","event":"Scan","SystemAddress":{{systemAddress}},"BodyName":"{{systemName}} 1","BodyID":1,"PlanetClass":"Rocky body","Landable":true,"Radius":1000}
            {"timestamp":"2026-07-20T12:05:00Z","event":"SAASignalsFound","SystemAddress":{{systemAddress}},"BodyName":"{{systemName}} 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}
            {"timestamp":"2026-07-20T12:06:00Z","event":"ScanOrganic","ScanType":"Analyse","SystemAddress":{{systemAddress}},"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Species":"$Codex_Ent_Aleoids_01_Name;","Species_Localised":"Aleoida Arcus"}
            {"timestamp":"2026-07-20T12:07:00Z","event":"SAAScanComplete","SystemAddress":{{systemAddress}},"BodyName":"{{systemName}} 1","BodyID":1}
            {{shutdown}}

            """);
    }
}
