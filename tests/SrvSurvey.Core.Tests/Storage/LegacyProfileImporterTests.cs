using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class LegacyProfileImporterTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-profile-importer-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ImportedRetiredOrganicDataIsConvertedFromVerifiedCopies()
    {
        var source = Path.Combine(temporaryDirectory, "legacy-organic");
        var destination = Path.Combine(temporaryDirectory, "current-organic");
        var backups = Path.Combine(temporaryDirectory, "backups-organic");
        var organicDirectory = Path.Combine(source, "organic", "F123");
        Directory.CreateDirectory(organicDirectory);
        var catalog = ExobiologyReferenceCatalog.LoadEmbedded();
        var reference = catalog.BiologyEntries.First(entry =>
            string.Equals(
                entry.VariantName,
                "$Codex_Ent_Aleoids_01_B_Name;",
                StringComparison.Ordinal));
        var profilePath = Path.Combine(source, "F123-live.json");
        var bodyPath = Path.Combine(organicDirectory, "Test 1.json");
        await File.WriteAllTextAsync(
            profilePath,
            $$$"""
            {
              "fid":"F123",
              "commander":"Drew",
              "organicRewards":{{{reference.Reward}}},
              "scannedBioEntryIds":["42_1_{{{reference.EntryId}}}"]
            }
            """);
        await File.WriteAllTextAsync(
            bodyPath,
            $$$"""
            {
              "systemName":"Test",
              "bodyName":"Test 1",
              "commander":"Drew",
              "bodyId":1,
              "systemAddress":42,
              "bioScans":[{
                "location":{"lat":1,"long":2},
                "radius":150,
                "genus":"$Codex_Ent_Aleoids_Genus_Name;",
                "species":"{{{reference.SpeciesName}}}",
                "status":"Complete",
                "entryId":0
              }],
              "organisms":{"Aleoida":{
                "genus":"$Codex_Ent_Aleoids_Genus_Name;",
                "species":"{{{reference.SpeciesName}}}",
                "variant":"{{{reference.VariantName}}}",
                "analyzed":true
              }}
            }
            """);
        var profileBytes = await File.ReadAllBytesAsync(profilePath);
        var bodyBytes = await File.ReadAllBytesAsync(bodyPath);

        var import = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            backups);
        var migration = await new LegacyOrganicProfileMigrator(
            destination,
            catalog).MigrateAsync();

        Assert.True(migration.Migrated);
        Assert.Equal(
            profileBytes,
            await File.ReadAllBytesAsync(Path.Combine(
                import.BackupDirectory,
                "profile",
                "F123-live.json")));
        Assert.Equal(
            bodyBytes,
            await File.ReadAllBytesAsync(Path.Combine(
                import.BackupDirectory,
                "profile",
                "organic",
                "F123",
                "Test 1.json")));
        Assert.Equal(profileBytes, await File.ReadAllBytesAsync(profilePath));
        Assert.Equal(bodyBytes, await File.ReadAllBytesAsync(bodyPath));
        Assert.Equal(
            bodyBytes,
            await File.ReadAllBytesAsync(Path.Combine(
                destination,
                "organic",
                "F123",
                "Test 1.json")));
        var migratedProfile = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(destination, "F123-live.json")))!.AsObject();
        Assert.True(
            migratedProfile["migratedScannedOrganicsInEntryId"]!
                .GetValue<bool>());
        Assert.True(
            migratedProfile["migratedNonSystemDataOrganics"]!
                .GetValue<bool>());
        Assert.True(File.Exists(Assert.Single(Directory.GetFiles(
            Path.Combine(destination, "systems", "F123"),
            "*.json"))));
    }

    [Fact]
    public async Task ImportCreatesVerifiedBackupAndLosslessDestination()
    {
        var source = Path.Combine(temporaryDirectory, "legacy");
        var destination = Path.Combine(temporaryDirectory, "current");
        var backups = Path.Combine(temporaryDirectory, "backups");
        var nested = Path.Combine(source, "systems", "F123");
        var empty = Path.Combine(source, "empty");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(empty);
        const string settings = "{\"futureSetting\":true,\"plotterScale\":2}";
        await File.WriteAllTextAsync(Path.Combine(source, "settings.json"), settings);
        await File.WriteAllBytesAsync(
            Path.Combine(nested, "Sol_10477373803.json"),
            [0, 1, 2, 3, 255]);

        var sourceSettingsWriteTime = File.GetLastWriteTimeUtc(
            Path.Combine(source, "settings.json"));
        var importer = new LegacyProfileImporter(
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero)));

        var result = await importer.ImportAsync(source, destination, backups);

        Assert.True(Directory.Exists(result.BackupDirectory));
        Assert.True(Directory.Exists(Path.Combine(destination, "empty")));
        Assert.Equal(
            settings,
            await File.ReadAllTextAsync(Path.Combine(destination, "settings.json")));
        Assert.Equal(
            settings,
            await File.ReadAllTextAsync(
                Path.Combine(result.BackupDirectory, "profile", "settings.json")));
        Assert.Equal(
            sourceSettingsWriteTime,
            File.GetLastWriteTimeUtc(Path.Combine(source, "settings.json")));
        Assert.True(File.Exists(
            Path.Combine(destination, LegacyProfileImporter.ManifestFileName)));
        Assert.True(File.Exists(
            Path.Combine(result.BackupDirectory, LegacyProfileImporter.ManifestFileName)));
        Assert.Equal(
            await File.ReadAllBytesAsync(Path.Combine(
                result.BackupDirectory,
                LegacyProfileImporter.ManifestFileName)),
            await File.ReadAllBytesAsync(Path.Combine(
                destination,
                LegacyProfileImporter.ManifestFileName)));
        Assert.Equal(2, result.Manifest.Entries.Count);
        Assert.All(result.Manifest.Entries, entry => Assert.Equal(64, entry.Sha256.Length));
    }

    [Fact]
    public async Task ImportPreservesMalformedJsonForLaterRecovery()
    {
        var source = Path.Combine(temporaryDirectory, "legacy");
        var destination = Path.Combine(temporaryDirectory, "current");
        var backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        const string malformedSettings = "{\"lastFid\":\"F123\",";
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            malformedSettings);

        var result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            backups);

        Assert.Equal(
            malformedSettings,
            await File.ReadAllTextAsync(Path.Combine(destination, "settings.json")));
        Assert.Equal(
            malformedSettings,
            await File.ReadAllTextAsync(
                Path.Combine(result.BackupDirectory, "profile", "settings.json")));
    }

    [Fact]
    public async Task ImportPreservesAndLoadsGuardianComponentSurveyData()
    {
        var source = Path.Combine(temporaryDirectory, "legacy");
        var destination = Path.Combine(temporaryDirectory, "current");
        var backups = Path.Combine(temporaryDirectory, "backups");
        var relativePath = Path.Combine(
            "guardian",
            "F123",
            "Test A 1-ruins-1.json");
        var sourcePath = Path.Combine(source, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(
            sourcePath,
            """
            {
              "type":"Beta",
              "index":1,
              "bodyName":"Test A 1",
              "components":["c1,cell,conduit,tech","d1,tech"]
            }
            """);
        var expectedBytes = await File.ReadAllBytesAsync(sourcePath);

        var result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            backups);

        Assert.Equal(
            expectedBytes,
            await File.ReadAllBytesAsync(Path.Combine(destination, relativePath)));
        Assert.Equal(
            expectedBytes,
            await File.ReadAllBytesAsync(Path.Combine(
                result.BackupDirectory,
                "profile",
                relativePath)));
        var read = await new GuardianCommanderDataReader(destination)
            .ReadAsync("F123", isOdyssey: true);
        var survey = Assert.Single(read.Surveys);
        Assert.Empty(read.Errors);
        Assert.Equal(
            GuardianComponentMaterial.Conduit,
            survey.Survey.ComponentMaterials["c1"].GetItem(1));
        Assert.Equal(
            GuardianComponentMaterial.Tech,
            survey.Survey.ComponentMaterials["d1"].GetItem(0));
    }

    [Fact]
    public async Task ImportMergesExistingDestinationWithBackupAndConflictRecord()
    {
        var source = Path.Combine(temporaryDirectory, "legacy");
        var destination = Path.Combine(temporaryDirectory, "current");
        var backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "legacy settings");
        await File.WriteAllTextAsync(
            Path.Combine(destination, "settings.json"),
            "new settings");
        await File.WriteAllTextAsync(
            Path.Combine(destination, "logs.txt"),
            "keep current-only data");

        var result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            backups);

        Assert.Equal(
            "legacy settings",
            await File.ReadAllTextAsync(Path.Combine(destination, "settings.json")));
        Assert.Equal(
            "keep current-only data",
            await File.ReadAllTextAsync(Path.Combine(destination, "logs.txt")));
        Assert.Equal(
            "new settings",
            await File.ReadAllTextAsync(Path.Combine(
                result.BackupDirectory,
                "previous-destination",
                "settings.json")));
        var conflict = Assert.Single(result.Manifest.Conflicts);
        Assert.Equal("settings.json", conflict.RelativePath);
        Assert.False(conflict.IsIdentical);
        Assert.Equal(2, result.Manifest.PreviousDestinationEntries.Count);
    }

    [Fact]
    public async Task ImportAbortsBeforeSwapWhenCurrentProfileChanges()
    {
        var source = Path.Combine(temporaryDirectory, "legacy");
        var destination = Path.Combine(temporaryDirectory, "current");
        var backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "legacy settings");
        await File.WriteAllTextAsync(
            Path.Combine(destination, "settings.json"),
            "current settings");
        var importer = new LegacyProfileImporter(
            null,
            checkpoint =>
            {
                if (checkpoint == ProfileImportCheckpoint.BeforeActivationValidation)
                {
                    File.WriteAllText(
                        Path.Combine(destination, "late-journal-write.json"),
                        "preserve me");
                }
            });

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            importer.ImportAsync(source, destination, backups));

        Assert.Contains("changed while the import was staged", exception.Message);
        Assert.Equal(
            "current settings",
            await File.ReadAllTextAsync(Path.Combine(destination, "settings.json")));
        Assert.Equal(
            "preserve me",
            await File.ReadAllTextAsync(
                Path.Combine(destination, "late-journal-write.json")));
        Assert.False(File.Exists(
            Path.Combine(destination, LegacyProfileImporter.ManifestFileName)));
    }

    [Fact]
    public async Task ImportRejectsUnexpectedFilesInjectedIntoTheStagedProfile()
    {
        var source = Path.Combine(temporaryDirectory, "legacy");
        var destination = Path.Combine(temporaryDirectory, "current");
        var backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "legacy settings");
        await File.WriteAllTextAsync(
            Path.Combine(destination, "current.json"),
            "current settings");
        var importer = new LegacyProfileImporter(
            null,
            checkpoint =>
            {
                if (checkpoint != ProfileImportCheckpoint.BeforeActivationValidation)
                {
                    return;
                }

                var stage = Assert.Single(Directory.EnumerateDirectories(
                    temporaryDirectory,
                    "current.importing-*"));
                File.WriteAllText(
                    Path.Combine(stage, "unexpected.json"),
                    "injected");
            });

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            importer.ImportAsync(source, destination, backups));

        Assert.Contains("unexpected, missing, or changed", exception.Message);
        Assert.Equal(
            "current settings",
            await File.ReadAllTextAsync(Path.Combine(destination, "current.json")));
        Assert.False(File.Exists(
            Path.Combine(destination, LegacyProfileImporter.ManifestFileName)));
    }

    [Fact]
    public async Task ImportRestoresCurrentProfileWhenItChangesDuringSwap()
    {
        var source = Path.Combine(temporaryDirectory, "legacy");
        var destination = Path.Combine(temporaryDirectory, "current");
        var backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "legacy settings");
        await File.WriteAllTextAsync(
            Path.Combine(destination, "settings.json"),
            "current settings");
        var importer = new LegacyProfileImporter(
            null,
            checkpoint =>
            {
                if (checkpoint != ProfileImportCheckpoint.AfterProfileActivation)
                {
                    return;
                }

                var rollback = Assert.Single(Directory.EnumerateDirectories(
                    temporaryDirectory,
                    "current.rollback-*"));
                File.WriteAllText(
                    Path.Combine(rollback, "late-journal-write.json"),
                    "preserve me");
            });

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            importer.ImportAsync(source, destination, backups));

        Assert.Contains("changed during import activation", exception.Message);
        Assert.Equal(
            "current settings",
            await File.ReadAllTextAsync(Path.Combine(destination, "settings.json")));
        Assert.Equal(
            "preserve me",
            await File.ReadAllTextAsync(
                Path.Combine(destination, "late-journal-write.json")));
        Assert.False(File.Exists(
            Path.Combine(destination, LegacyProfileImporter.ManifestFileName)));
    }

    [Fact]
    public async Task ImportRestoresCurrentProfileWhenActivatedCopyIsChanged()
    {
        var source = Path.Combine(temporaryDirectory, "legacy");
        var destination = Path.Combine(temporaryDirectory, "current");
        var backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "legacy settings");
        await File.WriteAllTextAsync(
            Path.Combine(destination, "settings.json"),
            "current settings");
        var importer = new LegacyProfileImporter(
            null,
            checkpoint =>
            {
                if (checkpoint == ProfileImportCheckpoint.AfterProfileActivation)
                {
                    File.WriteAllText(
                        Path.Combine(destination, "unexpected.json"),
                        "injected");
                }
            });

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            importer.ImportAsync(source, destination, backups));

        Assert.Contains("unexpected, missing, or changed", exception.Message);
        Assert.Equal(
            "current settings",
            await File.ReadAllTextAsync(Path.Combine(destination, "settings.json")));
        Assert.False(File.Exists(Path.Combine(destination, "unexpected.json")));
        Assert.False(File.Exists(
            Path.Combine(destination, LegacyProfileImporter.ManifestFileName)));
        Assert.Empty(Directory.EnumerateDirectories(
            temporaryDirectory,
            "current.failed-import-*"));
    }

    [Fact]
    public async Task ImportRefusesToLayerOverCompletedImport()
    {
        var source = Path.Combine(temporaryDirectory, "legacy");
        var destination = Path.Combine(temporaryDirectory, "current");
        var backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(
            Path.Combine(destination, LegacyProfileImporter.ManifestFileName),
            "existing import");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LegacyProfileImporter().ImportAsync(source, destination, backups));

        Assert.Contains("already imported", exception.Message);
        Assert.False(Directory.Exists(backups));
    }

    [Fact]
    public async Task ImportRejectsDestinationInsideSource()
    {
        var source = Path.Combine(temporaryDirectory, "legacy");
        var destination = Path.Combine(source, "current");
        var backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new LegacyProfileImporter().ImportAsync(source, destination, backups));

        Assert.Contains("outside the legacy profile", exception.Message);
    }

    [Fact]
    public async Task ManifestCanBeReadAfterImport()
    {
        var source = Path.Combine(temporaryDirectory, "legacy");
        var destination = Path.Combine(temporaryDirectory, "current");
        var backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "theme.json"), "{}");

        var result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            backups);
        await using var manifestStream = File.OpenRead(
            Path.Combine(destination, LegacyProfileImporter.ManifestFileName));
        var manifest = await JsonSerializer.DeserializeAsync<ProfileImportManifest>(
            manifestStream);

        Assert.NotNull(manifest);
        Assert.Equal(2, manifest.Version);
        Assert.Equal(result.BackupDirectory, manifest.BackupDirectory);
        Assert.Equal("theme.json", Assert.Single(manifest.Entries).RelativePath);
        Assert.Empty(manifest.PreviousDestinationEntries);
        Assert.Empty(manifest.Conflicts);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
