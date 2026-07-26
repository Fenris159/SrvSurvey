using System.Text.Json.Nodes;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class LegacyOrganicProfileMigratorTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-organic-migration-{Guid.NewGuid():N}");

    [Fact]
    public async Task MigratesOldClaimsAndBodyFilesWithoutChangingSources()
    {
        var catalog = ExobiologyReferenceCatalog.LoadEmbedded();
        var reference = catalog.BiologyEntries.First(entry =>
            string.Equals(
                entry.VariantName,
                "$Codex_Ent_Aleoids_01_B_Name;",
                StringComparison.Ordinal));
        Directory.CreateDirectory(directory);
        var profilePath = Path.Combine(directory, "F123-live.json");
        await File.WriteAllTextAsync(
            profilePath,
            $$"""
            {
              "fid": "F123",
              "commander": "Drew",
              "isOdyssey": true,
              "futureProfile": { "keep": true },
              "organicRewards": 1,
              "scannedBioEntryIds": ["42_1_{{reference.EntryIdPrefix}}00"],
              "scannedOrganics": [
                {
                  "systemAddress": 43,
                  "bodyId": 2,
                  "species": "{{reference.SpeciesName}}",
                  "reward": {{reference.Reward}}
                }
              ]
            }
            """);
        var organicDirectory = Path.Combine(directory, "organic", "F123");
        Directory.CreateDirectory(organicDirectory);
        var bodyPath = Path.Combine(organicDirectory, "Test 1.json");
        await File.WriteAllTextAsync(
            bodyPath,
            $$"""
            {
              "systemName": "Test",
              "bodyName": "Test 1",
              "commander": "Drew",
              "bodyId": 1,
              "systemAddress": 42,
              "firstVisited": "2024-01-01T00:00:00Z",
              "lastVisited": "2024-01-02T00:00:00Z",
              "lastTouchdown": { "lat": 12.5, "long": -45.25 },
              "bioScans": [
                {
                  "location": { "lat": 12.5, "long": -45.25 },
                  "radius": 150,
                  "genus": "$Codex_Ent_Aleoids_Genus_Name;",
                  "species": "{{reference.SpeciesName}}",
                  "status": "Complete",
                  "entryId": 0,
                  "futureScan": true
                }
              ],
              "organisms": {
                "Aleoida": {
                  "genus": "$Codex_Ent_Aleoids_Genus_Name;",
                  "genusLocalized": "Aleoida",
                  "species": "{{reference.SpeciesName}}",
                  "speciesLocalized": "Aleoida Arcus",
                  "variant": "{{reference.VariantName}}",
                  "variantLocalized": "Aleoida Arcus - Green",
                  "analyzed": true,
                  "futureOrganism": "source-only"
                }
              }
            }
            """);
        var sourceBytes = await File.ReadAllBytesAsync(bodyPath);
        var systemsDirectory = Path.Combine(directory, "systems", "F123");
        Directory.CreateDirectory(systemsDirectory);
        var systemPath = Path.Combine(systemsDirectory, "Test_42.json");
        await File.WriteAllTextAsync(
            systemPath,
            """
            {
              "name": "Test",
              "address": 42,
              "futureSystem": 7,
              "bodies": [
                {
                  "name": "Test 1",
                  "id": 1,
                  "type": "Unknown",
                  "firstFootFall": true,
                  "futureBody": true
                }
              ]
            }
            """);
        var migrator = new LegacyOrganicProfileMigrator(directory, catalog);

        var result = await migrator.MigrateAsync();

        Assert.True(result.Migrated);
        Assert.Equal(1, result.MigratedProfileCount);
        Assert.Equal(1, result.MigratedBodyCount);
        Assert.Equal(1, result.MigratedScanCount);
        Assert.Equal(1, result.MigratedOrganismCount);
        Assert.Empty(result.Errors);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(bodyPath));
        var profile = JsonNode.Parse(
            await File.ReadAllTextAsync(profilePath))!.AsObject();
        Assert.True(profile["futureProfile"]!["keep"]!.GetValue<bool>());
        Assert.True(
            profile["migratedScannedOrganicsInEntryId"]!.GetValue<bool>());
        Assert.True(
            profile["migratedNonSystemDataOrganics"]!.GetValue<bool>());
        var claims = profile["scannedBioEntryIds"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .ToArray();
        Assert.Equal(2, claims.Length);
        Assert.All(claims, claim => Assert.Equal(5, claim.Split('_').Length));
        Assert.Equal(
            reference.Reward * 6,
            profile["organicRewards"]!.GetValue<long>());
        Assert.Contains(
            claims,
            claim => claim ==
                $"42_1_{reference.EntryId}_{reference.Reward}_{bool.TrueString}");

        var system = JsonNode.Parse(
            await File.ReadAllTextAsync(systemPath))!.AsObject();
        Assert.Equal(7, system["futureSystem"]!.GetValue<int>());
        Assert.Equal(
            "2024-01-01T00:00:00.0000000+00:00",
            system["firstVisited"]!.GetValue<string>());
        var body = system["bodies"]![0]!.AsObject();
        Assert.True(body["futureBody"]!.GetValue<bool>());
        Assert.Equal("LandableBody", body["type"]!.GetValue<string>());
        Assert.Equal(12.5, body["lastTouchdown"]!["lat"]!.GetValue<double>());
        var scan = Assert.Single(body["bioScans"]!.AsArray())!.AsObject();
        Assert.Equal(reference.EntryId, scan["entryId"]!.GetValue<long>());
        Assert.True(scan["futureScan"]!.GetValue<bool>());
        var organism = Assert.Single(body["organisms"]!.AsArray())!.AsObject();
        Assert.Equal(reference.EntryId, organism["entryId"]!.GetValue<long>());
        Assert.Equal(reference.Reward, organism["reward"]!.GetValue<long>());
        Assert.True(organism["analyzed"]!.GetValue<bool>());
        Assert.Equal(
            "source-only",
            organism["futureOrganism"]!.GetValue<string>());

        var profileBytes = await File.ReadAllBytesAsync(profilePath);
        var systemBytes = await File.ReadAllBytesAsync(systemPath);
        var second = await migrator.MigrateAsync();
        Assert.False(second.Migrated);
        Assert.Equal(profileBytes, await File.ReadAllBytesAsync(profilePath));
        Assert.Equal(systemBytes, await File.ReadAllBytesAsync(systemPath));
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(bodyPath));
    }

    [Fact]
    public async Task MalformedClaimsAndBodiesArePreservedWithoutFalseCompletion()
    {
        Directory.CreateDirectory(directory);
        var profilePath = Path.Combine(directory, "F123-live.json");
        await File.WriteAllTextAsync(
            profilePath,
            """
            {
              "fid": "F123",
              "organicRewards": 12345,
              "scannedBioEntryIds": ["unrecoverable-claim"]
            }
            """);
        var organicDirectory = Path.Combine(directory, "organic", "F123");
        Directory.CreateDirectory(organicDirectory);
        var bodyPath = Path.Combine(organicDirectory, "broken.json");
        await File.WriteAllTextAsync(
            bodyPath,
            """{"systemName":"Missing identity","future":true}""");
        var profileBefore = await File.ReadAllBytesAsync(profilePath);
        var bodyBefore = await File.ReadAllBytesAsync(bodyPath);

        var result = await new LegacyOrganicProfileMigrator(directory)
            .MigrateAsync();

        Assert.False(result.Migrated);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(profileBefore, await File.ReadAllBytesAsync(profilePath));
        Assert.Equal(bodyBefore, await File.ReadAllBytesAsync(bodyPath));
        Assert.False(Directory.Exists(Path.Combine(directory, "systems")));
    }

    [Fact]
    public async Task CorruptClaimShapesAndOverflowArePreservedWithoutWrites()
    {
        Directory.CreateDirectory(directory);
        var shapePath = Path.Combine(directory, "F123-live.json");
        var overflowPath = Path.Combine(directory, "F456-live.json");
        await File.WriteAllTextAsync(
            shapePath,
            """
            {
              "fid": "F123",
              "organicRewards": 7,
              "scannedBioEntryIds": [42, "42_1_1234567_100_False"]
            }
            """);
        await File.WriteAllTextAsync(
            overflowPath,
            $$"""
            {
              "fid": "F456",
              "organicRewards": 9,
              "scannedBioEntryIds": ["42_1_1234567_{{long.MaxValue}}_True"]
            }
            """);
        var shapeBefore = await File.ReadAllBytesAsync(shapePath);
        var overflowBefore = await File.ReadAllBytesAsync(overflowPath);

        var result = await new LegacyOrganicProfileMigrator(directory)
            .MigrateAsync();

        Assert.False(result.Migrated);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(shapeBefore, await File.ReadAllBytesAsync(shapePath));
        Assert.Equal(overflowBefore, await File.ReadAllBytesAsync(overflowPath));
    }

    [Fact]
    public async Task SharedBodyHistoryCompletesBothLiveAndLegacyProfiles()
    {
        var catalog = ExobiologyReferenceCatalog.LoadEmbedded();
        var reference = catalog.BiologyEntries.First(entry => string.Equals(
            entry.VariantName,
            "$Codex_Ent_Aleoids_01_B_Name;",
            StringComparison.Ordinal));
        Directory.CreateDirectory(directory);
        var profileJson = $$"""
            {
              "fid": "F123",
              "organicRewards": 1,
              "scannedBioEntryIds": ["42_1_{{reference.EntryIdPrefix}}00"]
            }
            """;
        var livePath = Path.Combine(directory, "F123-live.json");
        var legacyPath = Path.Combine(directory, "F123-legacy.json");
        await File.WriteAllTextAsync(livePath, profileJson);
        await File.WriteAllTextAsync(legacyPath, profileJson);
        var organicDirectory = Path.Combine(directory, "organic", "F123");
        Directory.CreateDirectory(organicDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(organicDirectory, "Test 1.json"),
            $$"""
            {
              "systemName": "Test",
              "bodyName": "Test 1",
              "bodyId": 1,
              "systemAddress": 42,
              "organisms": {
                "Aleoida": {
                  "genus": "$Codex_Ent_Aleoids_Genus_Name;",
                  "species": "{{reference.SpeciesName}}",
                  "variant": "{{reference.VariantName}}"
                }
              }
            }
            """);
        var systemDirectory = Path.Combine(directory, "systems", "F123");
        Directory.CreateDirectory(systemDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(systemDirectory, "Test_42.json"),
            """
            {
              "name": "Test",
              "address": 42,
              "bodies": [{
                "name": "Test 1",
                "id": 1,
                "firstFootFall": true
              }]
            }
            """);

        var result = await new LegacyOrganicProfileMigrator(directory, catalog)
            .MigrateAsync();

        Assert.Equal(2, result.MigratedProfileCount);
        foreach (var path in new[] { livePath, legacyPath })
        {
            var profile = JsonNode.Parse(
                await File.ReadAllTextAsync(path))!.AsObject();
            Assert.True(
                profile["migratedNonSystemDataOrganics"]!.GetValue<bool>());
            Assert.Equal(
                reference.Reward * 5,
                profile["organicRewards"]!.GetValue<long>());
            Assert.Equal(
                $"42_1_{reference.EntryId}_{reference.Reward}_{bool.TrueString}",
                Assert.Single(profile["scannedBioEntryIds"]!.AsArray())!
                    .GetValue<string>());
        }
    }

    [Fact]
    public async Task MalformedTargetSystemIsPreservedAndNotMarkedComplete()
    {
        Directory.CreateDirectory(directory);
        var profilePath = Path.Combine(directory, "F123-live.json");
        await File.WriteAllTextAsync(
            profilePath,
            """
            {
              "fid": "F123",
              "organicRewards": 100,
              "scannedBioEntryIds": ["42_1_1234567_100_False"]
            }
            """);
        var organicDirectory = Path.Combine(directory, "organic", "F123");
        Directory.CreateDirectory(organicDirectory);
        var bodyPath = Path.Combine(organicDirectory, "Test 1.json");
        await File.WriteAllTextAsync(
            bodyPath,
            """
            {
              "systemName": "Test",
              "bodyName": "Test 1",
              "bodyId": 1,
              "systemAddress": 42,
              "organisms": {}
            }
            """);
        var systemDirectory = Path.Combine(directory, "systems", "F123");
        Directory.CreateDirectory(systemDirectory);
        var systemPath = Path.Combine(systemDirectory, "Test_42.json");
        await File.WriteAllTextAsync(
            systemPath,
            """
            {
              "name": "Test",
              "address": 42,
              "bodies": "malformed-but-preserved",
              "future": true
            }
            """);
        var bodyBefore = await File.ReadAllBytesAsync(bodyPath);
        var systemBefore = await File.ReadAllBytesAsync(systemPath);

        var result = await new LegacyOrganicProfileMigrator(directory)
            .MigrateAsync();

        Assert.Single(result.Errors);
        Assert.Equal(bodyBefore, await File.ReadAllBytesAsync(bodyPath));
        Assert.Equal(systemBefore, await File.ReadAllBytesAsync(systemPath));
        var profile = JsonNode.Parse(
            await File.ReadAllTextAsync(profilePath))!.AsObject();
        Assert.Null(profile["migratedNonSystemDataOrganics"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
