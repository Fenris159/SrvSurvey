using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class LegacyProfileImporterTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-profile-importer-tests-{Guid.NewGuid():N}"
    );

    /// <summary>Imports an empty legacy routes directory alongside current routes despite Windows preserving different casing.</summary>
    [Theory]
    [InlineData("routes", "Routes")]
    [InlineData("Routes", "routes")]
    public async Task ImportAcceptsRouteDirectoryCasingWithoutLosingCurrentRoutes(
        string legacyRoutes,
        string currentRoutes
    )
    {
        string source = Path.Combine(temporaryDirectory, "source");
        string destination = Path.Combine(temporaryDirectory, "current");
        Directory.CreateDirectory(Path.Combine(source, legacyRoutes));
        Directory.CreateDirectory(Path.Combine(source, "guardian"));
        Directory.CreateDirectory(Path.Combine(source, "systems"));
        Directory.CreateDirectory(Path.Combine(destination, currentRoutes, "F123"));
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), "{}");
        string currentRoute = Path.Combine(currentRoutes, "F123", "route.json");
        const string savedRoute = """{"systems":["Sol","Achenar"]}""";
        await File.WriteAllTextAsync(Path.Combine(destination, currentRoute), savedRoute);

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            Path.Combine(temporaryDirectory, "backups")
        );

        Assert.Empty(result.Warnings);
        Assert.Equal(savedRoute, await File.ReadAllTextAsync(Path.Combine(destination, currentRoute)));
        Assert.Equal(
            savedRoute,
            await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "previous-destination", currentRoute))
        );
        Assert.True(Directory.Exists(Path.Combine(result.BackupDirectory, "profile", legacyRoutes)));
        Assert.Equal("{}", await File.ReadAllTextAsync(Path.Combine(destination, "F123-live.json")));
        Assert.True(File.Exists(Path.Combine(destination, LegacyProfileImporter.ManifestFileName)));
    }

    /// <summary>Loads imported service keys through the commander reader while preserving an existing XP key.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportPreservesSavedServiceKeysAndKeepsCurrentKeys(bool hasCurrentKey)
    {
        string source = Path.Combine(temporaryDirectory, "keys-source");
        string destination = Path.Combine(temporaryDirectory, "keys-current");
        Directory.CreateDirectory(source);
        const string incoming =
            """{"fid":"F123","rccApiKey":"synthetic-raven-key","inaraApiKey":"synthetic-inara-key","edsmCommanderName":"Test Commander","edsmApiKey":"synthetic-edsm-key"}""";
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), incoming);
        if (hasCurrentKey)
        {
            Directory.CreateDirectory(destination);
            await File.WriteAllTextAsync(
                Path.Combine(destination, "F123-live.json"),
                """{"inaraApiKey":"current-inara-key"}"""
            );
        }

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            Path.Combine(temporaryDirectory, "backups")
        );
        CommanderProfileLoadResult loaded = await new CommanderProfileStore(destination).LoadAsync(
            "F123",
            isOdyssey: true
        );

        Assert.Empty(result.Warnings);
        Assert.Null(loaded.Error);
        Assert.NotNull(loaded.Data);
        Assert.Equal("synthetic-raven-key", loaded.Data.RavenColonialApiKey);
        Assert.Equal(hasCurrentKey ? "current-inara-key" : "synthetic-inara-key", loaded.Data.InaraApiKey);
        Assert.Equal("synthetic-edsm-key", loaded.Data.EdsmApiKey);
        Assert.Equal("Test Commander", loaded.Data.EdsmCommanderName);
        Assert.Equal(
            incoming,
            await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "profile", "F123-live.json"))
        );
    }

    [Theory]
    [InlineData("F123-codex.json", 0)]
    [InlineData("F123-codex-18.json", 18)]
    public async Task OlderImportsAppendCodexDiscoveriesWithoutResettingCurrentBingo(string fileName, int region)
    {
        string source = Path.Combine(temporaryDirectory, "older-codex");
        string destination = Path.Combine(temporaryDirectory, "current-codex");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        const string current =
            """{"fid":"F123","commander":"Current","codexFirsts":{"2310101":"2026-10-20T12:00:00_-1_-1","2310206":"2026-10-20T12:00:00_9007199254740993_2"}}""";
        const string incoming =
            """{"fid":"F123","commander":"Older","codexFirsts":{"2310101":"2022-10-20T12:00:00_42_1","2310301":"2022-10-20T12:00:00_43_3"}}""";
        await File.WriteAllTextAsync(Path.Combine(source, fileName), incoming);
        await File.WriteAllTextAsync(Path.Combine(destination, fileName), current);

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            Path.Combine(temporaryDirectory, "backups")
        );

        CommanderCodexData codex = (await new CommanderCodexStore(destination).LoadAsync("F123", null, region)).Data!;
        Assert.Equal(3, codex.Firsts.Count);
        Assert.Equal(-1, codex.Firsts[2310101].SystemAddress);
        Assert.Equal(9007199254740993, codex.Firsts[2310206].SystemAddress);
        Assert.Equal(43, codex.Firsts[2310301].SystemAddress);
        Assert.Equal("Current", codex.CommanderName);
        Assert.Equal(
            current,
            await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "previous-destination", fileName))
        );
        Assert.Equal(incoming, await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "profile", fileName)));
        ProfileInventoryEntry activated = Assert.Single(result.Manifest.GetImportedEntries());
        Assert.Equal(
            activated.Sha256,
            Assert
                .Single(
                    (await ProfileInventory.CreateAsync(destination)).Entries,
                    entry => entry.RelativePath == fileName
                )
                .Sha256
        );
    }

    [Theory]
    [InlineData("02310101")]
    [InlineData("+2310101")]
    [InlineData(" 2310101 ")]
    public async Task NumericCodexAliasesCannotReplaceCurrentBingoMetadata(string alias)
    {
        string source = Path.Combine(temporaryDirectory, "alias-source");
        string destination = Path.Combine(temporaryDirectory, "alias-destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(
            Path.Combine(destination, "F123-codex.json"),
            """{"codexFirsts":{"2310101":"2026-10-20T12:00:00_-1_-1"}}"""
        );
        await File.WriteAllTextAsync(
            Path.Combine(source, "F123-codex.json"),
            "{\"codexFirsts\":{\"" + alias + "\":\"2022-10-20T12:00:00_42_1\"}}"
        );

        await new LegacyProfileImporter().ImportAsync(source, destination, Path.Combine(temporaryDirectory, "backups"));

        CommanderCodexData data = (await new CommanderCodexStore(destination).LoadAsync("F123", null)).Data!;
        Assert.Equal(-1, Assert.Single(data.Firsts).Value.SystemAddress);
    }

    [Theory]
    [InlineData("F456-codex.json", "{\"codexFirsts\":{\"2310101\":null,\"2310101\":null}}")]
    [InlineData("F456-live.json", "{\"history\":[{\"one\":1,\"one\":2}]}")]
    public async Task DuplicatePropertiesWarnWithoutDiscardingOtherRecoveredData(string name, string damaged)
    {
        string source = Path.Combine(temporaryDirectory, "duplicate-source");
        string destination = Path.Combine(temporaryDirectory, "duplicate-destination");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(source, name), damaged);

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            Path.Combine(temporaryDirectory, "backups")
        );

        Assert.True(File.Exists(Path.Combine(destination, "F123-live.json")));
        Assert.False(File.Exists(Path.Combine(destination, name)));
        Assert.Contains(result.Warnings, warning => warning.RelativePath == name);
        Assert.Equal(damaged, await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "profile", name)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative-backup")]
    [InlineData("/broken\0backup")]
    public async Task DamagedRecoveryMetadataCannotInterruptStartup(string? backupDirectory)
    {
        string source = Path.Combine(temporaryDirectory, "metadata-source");
        string destination = Path.Combine(temporaryDirectory, "metadata-destination");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), "{}");
        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            Path.Combine(temporaryDirectory, "backups")
        );
        string path = Path.Combine(destination, LegacyProfileImporter.ManifestFileName);
        ProfileImportManifest damaged = result.Manifest with
        {
            BackupDirectory = backupDirectory!,
            Warnings = [new ProfileImportWarning("settings.json", "incompatible")],
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(damaged));
        Assert.False(LegacyProfileImporter.CanRetryRecovery(destination));
        Assert.Null(LegacyProfileImporter.GetRecoveryReportPath(destination));
    }

    [Theory]
    [InlineData("2026-10-20T12:00:00_-1_-1", "2022-10-20T12:00:00_42_1")]
    [InlineData("2022-10-20T12:00:00_42_1", "2026-10-20T12:00:00_-1_-1")]
    [InlineData("2022-10-20T12:00:00_42_1", "broken")]
    public async Task IncomingAliasesKeepTheSourcesLastValidReadableDiscovery(string first, string last)
    {
        string source = Path.Combine(temporaryDirectory, "competing-alias-source");
        string destination = Path.Combine(temporaryDirectory, "competing-alias-destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(
            Path.Combine(source, "F123-codex.json"),
            JsonSerializer.Serialize(
                new { codexFirsts = new Dictionary<string, string> { ["02310101"] = first, ["2310101"] = last } }
            )
        );
        await File.WriteAllTextAsync(Path.Combine(destination, "F123-codex.json"), "{\"codexFirsts\":{}}");
        CommanderCodexFirst expected = (await new CommanderCodexStore(source).LoadAsync("F123", null)).Data!.Firsts[
            2310101
        ];
        await new LegacyProfileImporter().ImportAsync(source, destination, Path.Combine(temporaryDirectory, "backups"));
        CommanderCodexFirst actual = (await new CommanderCodexStore(destination).LoadAsync("F123", null)).Data!.Firsts[
            2310101
        ];
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task AnUnavailableCopyParentSkipsTheEntryWithoutLosingItsWarning()
    {
        string source = Path.Combine(temporaryDirectory, "copy-source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), "{}");
        ProfileInventory inventory = await ProfileInventory.CreateAsync(source);
        string blocked = Path.Combine(temporaryDirectory, "blocked-copy-parent");
        await File.WriteAllTextAsync(blocked, "not a directory");
        var warnings = new List<ProfileImportWarning>();
        bool copied = await LegacyProfileImporter.CopyEntryAsync(
            source,
            blocked,
            Assert.Single(inventory.Entries),
            false,
            warnings,
            CancellationToken.None
        );
        Assert.False(copied);
        Assert.Equal("F123-live.json", Assert.Single(warnings).RelativePath);
    }

    [Fact]
    public async Task WhollyLockedProfilesReportTheInaccessibleFileInsteadOfAnEmptyProfile()
    {
        string source = Path.Combine(temporaryDirectory, "locked-source");
        Directory.CreateDirectory(source);
        string file = Path.Combine(source, "F123-live.json");
        await File.WriteAllTextAsync(file, "{}");
        await using var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new LegacyProfileImporter().ImportAsync(
                source,
                Path.Combine(temporaryDirectory, "destination"),
                Path.Combine(temporaryDirectory, "backups")
            )
        );
        Assert.Contains("F123-live.json", error.Message);
        Assert.DoesNotContain("does not contain any files", error.Message);
    }

    [Fact]
    public async Task PlayerMergeKeepsCurrentTotalsAndClaimsWhileAddingMissingHistory()
    {
        string source = Path.Combine(temporaryDirectory, "older-player");
        string destination = Path.Combine(temporaryDirectory, "current-player");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(
            Path.Combine(destination, "F123-live.json"),
            """{"fid":"F123","explRewards":200,"organicRewards":0,"scannedBioEntryIds":[],"explRewardsBySystem":{"Sol":200},"decodeTheRuins":["B1"]}"""
        );
        await File.WriteAllTextAsync(
            Path.Combine(source, "F123-live.json"),
            """{"fid":"F123","explRewards":100,"organicRewards":500,"scannedBioEntryIds":["old-sold-claim"],"scannedOrganics":[{"entryId":2310101}],"explRewardsBySystem":{"Sol":100,"Achenar":50},"decodeTheRuins":["B1","C2"],"rccApiKey":"older-key"}"""
        );

        await new LegacyProfileImporter().ImportAsync(source, destination, Path.Combine(temporaryDirectory, "backups"));

        JsonNode player = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(destination, "F123-live.json")))!;
        Assert.Equal(200, player["explRewards"]!.GetValue<int>());
        Assert.Equal(0, player["organicRewards"]!.GetValue<int>());
        Assert.Empty(player["scannedBioEntryIds"]!.AsArray());
        Assert.Null(player["scannedOrganics"]);
        Assert.Equal(200, player["explRewardsBySystem"]!["Sol"]!.GetValue<int>());
        Assert.Null(player["explRewardsBySystem"]!["Achenar"]);
        Assert.Single(player["explRewardsBySystem"]!.AsObject());
        Assert.Equal(2, player["decodeTheRuins"]!.AsArray().Count);
        Assert.Equal("C2", player["decodeTheRuins"]![1]!.GetValue<string>());
        Assert.Equal("older-key", player["rccApiKey"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("current.bin", "current", "older")]
    [InlineData("current.json", "{broken", "{}")]
    [InlineData("current.json", "[]", "{}")]
    [InlineData("current.json", "{\"known\":null}", "{\"known\":42}")]
    [InlineData("current.json", "{\"known\":42}", "{\"known\":42}")]
    public async Task CollisionsKeepCurrentBytesWhenNoSafeAdditionIsPossible(
        string name,
        string current,
        string incoming
    )
    {
        string source = Path.Combine(temporaryDirectory, "collision-source");
        string destination = Path.Combine(temporaryDirectory, "collision-destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(source, name), incoming);
        await File.WriteAllTextAsync(Path.Combine(destination, name), current);

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            Path.Combine(temporaryDirectory, "backups")
        );

        Assert.Equal(current, await File.ReadAllTextAsync(Path.Combine(destination, name)));
        Assert.Equal(incoming, await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "profile", name)));
        Assert.Empty(result.Manifest.GetImportedEntries());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FileAndDirectoryCollisionsDoNotBlockOtherRecoveredData(bool sourceIsDirectory)
    {
        string source = Path.Combine(temporaryDirectory, "path-source");
        string destination = Path.Combine(temporaryDirectory, "path-destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        string directoryRoot = sourceIsDirectory ? source : destination;
        string fileRoot = sourceIsDirectory ? destination : source;
        Directory.CreateDirectory(Path.Combine(directoryRoot, "history"));
        await File.WriteAllTextAsync(Path.Combine(directoryRoot, "history", "one.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(fileRoot, "history"), "preserve");
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), "{}");

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            Path.Combine(temporaryDirectory, "backups")
        );

        Assert.Contains(
            result.Warnings,
            warning => warning.RelativePath.StartsWith("history", StringComparison.Ordinal)
        );
        Assert.True(File.Exists(Path.Combine(destination, "F123-live.json")));
        Assert.Equal(
            sourceIsDirectory ? "preserve" : "{}",
            await File.ReadAllTextAsync(Path.Combine(destination, sourceIsDirectory ? "history" : "history/one.json"))
        );
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"2310101\":\"broken\"}")]
    public async Task MissingOrIncompatibleCurrentCodexEntriesCanBeRecovered(string currentFirsts)
    {
        string source = Path.Combine(temporaryDirectory, "codex-repair-source");
        string destination = Path.Combine(temporaryDirectory, "codex-repair-destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(
            Path.Combine(source, "F123-codex.json"),
            """{"codexFirsts":{"2310101":"2022-08-31T05:38:57_42_1"}}"""
        );
        await File.WriteAllTextAsync(
            Path.Combine(destination, "F123-codex.json"),
            "{\"codexFirsts\":" + currentFirsts + "}"
        );

        await new LegacyProfileImporter().ImportAsync(source, destination, Path.Combine(temporaryDirectory, "backups"));

        Assert.True((await new CommanderCodexStore(destination).LoadAsync("F123", null)).Data!.IsDiscovered(2310101));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkedSourceEntriesAreWarnedAndSkippedWhileReadableDataIsRecovered(bool directoryLink)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string source = Path.Combine(temporaryDirectory, "linked-source");
        string destination = Path.Combine(temporaryDirectory, "linked-destination");
        string outside = Path.Combine(temporaryDirectory, "outside");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "F456-live.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), "{}");
        string linkedName = directoryLink ? "other-profile" : "F456-live.json";
        if (directoryLink)
        {
            Directory.CreateSymbolicLink(Path.Combine(source, linkedName), outside);
        }
        else
        {
            File.CreateSymbolicLink(Path.Combine(source, linkedName), Path.Combine(outside, "F456-live.json"));
        }

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            Path.Combine(temporaryDirectory, "backups")
        );

        Assert.Contains(result.Warnings, warning => warning.RelativePath == linkedName);
        Assert.False(File.Exists(Path.Combine(destination, linkedName)));
        Assert.False(Directory.Exists(Path.Combine(destination, linkedName)));
        Assert.True(File.Exists(Path.Combine(destination, "F123-live.json")));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("pfx", "")]
    [InlineData("", "drive_c")]
    public async Task PrefixWithoutASrvSurveyProfileStopsBeforeCopyingWindowsFiles(
        string prefixSegment,
        string selectedSegment
    )
    {
        string prefix = Path.Combine(temporaryDirectory, "empty-prefix");
        string drive = Path.Combine(prefix, prefixSegment, "drive_c");
        Directory.CreateDirectory(Path.Combine(drive, "windows"));
        await File.WriteAllTextAsync(Path.Combine(drive, "windows", "system.dat"), "not a profile");
        string backups = Path.Combine(temporaryDirectory, "backups");
        string destination = Path.Combine(temporaryDirectory, "current");
        ProfileImportSource source = LegacyProfileLocator.ResolveManualSelectionSource(
            Path.Combine(prefix, selectedSegment)
        );

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new LegacyProfileImporter().ImportAsync(source, destination, backups)
        );

        Assert.Contains("No SrvSurvey profile", error.Message);
        Assert.False(Directory.Exists(destination));
        Assert.False(Directory.Exists(backups));
    }

    [Fact]
    public async Task ImportRecoversReadablePlayerAndCodexFilesWhenAnotherSourceFileIsLocked()
    {
        string source = Path.Combine(temporaryDirectory, "recover-source");
        string destination = Path.Combine(temporaryDirectory, "recover-destination");
        Directory.CreateDirectory(source);
        const string player = """{"fid":"F123","commander":"Recovered","explRewards":12345}""";
        const string codex =
            """{"fid":"F123","commander":"Recovered","codexFirsts":{"2310101":"2022-08-31T05:38:57_42_1"}}""";
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), player);
        await File.WriteAllTextAsync(Path.Combine(source, "F123-codex.json"), codex);
        string lockedPath = Path.Combine(source, "F456-live.json");
        await File.WriteAllTextAsync(lockedPath, "{}");
        await using var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            Path.Combine(temporaryDirectory, "recovery-backups")
        );

        Assert.Equal(player, await File.ReadAllTextAsync(Path.Combine(destination, "F123-live.json")));
        Assert.True((await new CommanderCodexStore(destination).LoadAsync("F123", null)).Data!.IsDiscovered(2310101));
        Assert.False(File.Exists(Path.Combine(destination, "F456-live.json")));
        Assert.Equal(
            codex,
            await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "profile", "F123-codex.json"))
        );
        Assert.Contains(result.Warnings, warning => warning.RelativePath == "F456-live.json");
        Assert.True(LegacyProfileImporter.CanRetryRecovery(destination));
        Assert.Contains(
            "F456-live.json",
            await File.ReadAllTextAsync(
                Path.Combine(result.BackupDirectory, LegacyProfileImporter.RecoveryReportFileName)
            )
        );

        await locked.DisposeAsync();
        ProfileImportResult retry = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            Path.Combine(temporaryDirectory, "recovery-backups")
        );
        Assert.Empty(retry.Warnings);
        Assert.True(File.Exists(Path.Combine(destination, "F456-live.json")));
        Assert.False(LegacyProfileImporter.CanRetryRecovery(destination));
        Assert.True(
            File.Exists(
                Path.Combine(retry.BackupDirectory, "previous-destination", LegacyProfileImporter.ManifestFileName)
            )
        );
    }

    [Theory]
    [InlineData("F456-live.json", "{broken")]
    [InlineData("F456-codex.json", "[]")]
    [InlineData("F456-codex-18.json", "{\"codexFirsts\":[]}")]
    public async Task IncompatiblePlayerFilesAreBackedUpWithoutReplacingExistingData(
        string fileName,
        string incompatible
    )
    {
        string source = Path.Combine(temporaryDirectory, "incompatible-source");
        string destination = Path.Combine(temporaryDirectory, "incompatible-destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(source, fileName), incompatible);
        const string existing = "{\"commander\":\"Existing\",\"codexFirsts\":{}}";
        await File.WriteAllTextAsync(Path.Combine(destination, fileName), existing);

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            Path.Combine(temporaryDirectory, "backups")
        );

        Assert.Equal(existing, await File.ReadAllTextAsync(Path.Combine(destination, fileName)));
        Assert.Equal(
            incompatible,
            await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "profile", fileName))
        );
        Assert.Contains(result.Warnings, warning => warning.RelativePath == fileName);
        Assert.Contains(fileName, result.Manifest.SkippedRelativePaths!);
        Assert.Empty(result.Manifest.Conflicts);
    }

    [Fact]
    public async Task CodexKeepsValidDiscoveriesAndReportsOnlyIncompatibleEntries()
    {
        string source = Path.Combine(temporaryDirectory, "partial-codex");
        string destination = Path.Combine(temporaryDirectory, "partial-codex-xp");
        Directory.CreateDirectory(source);
        const string codex =
            """{"fid":"F123","codexFirsts":{"2310101":"2022-08-31T05:38:57_42_1","2310206":"bad-entry"}}""";
        await File.WriteAllTextAsync(Path.Combine(source, "F123-codex.json"), codex);

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            Path.Combine(temporaryDirectory, "backups")
        );

        CommanderCodexLoadResult loaded = await new CommanderCodexStore(destination).LoadAsync("F123", null);
        Assert.True(loaded.Data!.IsDiscovered(2310101));
        Assert.False(loaded.Data.IsDiscovered(2310206));
        Assert.Contains(
            result.Warnings,
            warning =>
                warning.RelativePath == "F123-codex.json"
                && warning.Message.Contains("2310206", StringComparison.Ordinal)
        );
        Assert.Equal(
            codex,
            await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "profile", "F123-codex.json"))
        );
    }

    [Fact]
    public async Task ASourceFileChangingDuringCaptureDoesNotDiscardOtherRecoveredFiles()
    {
        string source = Path.Combine(temporaryDirectory, "changing-source");
        string destination = Path.Combine(temporaryDirectory, "changing-xp");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(source, "F456-live.json"), "{}");
        var importer = new LegacyProfileImporter(
            null,
            stage =>
            {
                if (stage == ProfileImportCheckpoint.AfterSourceScan)
                {
                    File.WriteAllText(Path.Combine(source, "F456-live.json"), "{\"commander\":\"Changed\"}");
                }
            }
        );

        ProfileImportResult result = await importer.ImportAsync(
            source,
            destination,
            Path.Combine(temporaryDirectory, "backups")
        );

        Assert.True(File.Exists(Path.Combine(destination, "F123-live.json")));
        Assert.False(File.Exists(Path.Combine(destination, "F456-live.json")));
        Assert.Contains(result.Warnings, warning => warning.RelativePath == "F456-live.json");
    }

    [Fact]
    public async Task UnusableSourceKeepsRawRecoveryBackupWithoutReplacingTheCurrentProfile()
    {
        string source = Path.Combine(temporaryDirectory, "unusable-source");
        string destination = Path.Combine(temporaryDirectory, "unusable-xp");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), "{broken");
        await File.WriteAllTextAsync(Path.Combine(destination, "F123-live.json"), "{\"explRewards\":42}");

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new LegacyProfileImporter().ImportAsync(source, destination, Path.Combine(temporaryDirectory, "backups"))
        );

        string recovery = Assert.IsType<string>(exception.Data[LegacyProfileImporter.RecoveryDirectoryDataKey]);
        Assert.Equal("{broken", await File.ReadAllTextAsync(Path.Combine(recovery, "profile", "F123-live.json")));
        Assert.Equal("{\"explRewards\":42}", await File.ReadAllTextAsync(Path.Combine(destination, "F123-live.json")));
        Assert.False(File.Exists(Path.Combine(destination, LegacyProfileImporter.ManifestFileName)));
    }

    // Synthetic values follow upstream 1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc's
    // CommanderSettings, CommanderCodex/CodexFirst, and CommanderJourney writers.
    [Fact]
    public async Task UpstreamPlayerAndCodexDataRemainReadableAfterImport()
    {
        string root = Path.Combine(temporaryDirectory, "upstream", "SrvSurvey");
        string source = Path.Combine(root, "SrvSurvey", "1.1.0.0");
        string destination = Path.Combine(temporaryDirectory, "xp");
        Directory.CreateDirectory(Path.Combine(source, "journey", "F123"));
        const string player = """
            {
              "fid":"F123", "commander":"Original Commander", "isOdyssey":true,
              "currentSystem":"Sol", "currentSystemAddress":10477373803,
              "explRewards":1234567890, "distanceTravelled":123.5,
              "countJumps":100, "countScans":200, "countDSS":30, "countLanded":40,
              "explRewardsBySystem":{"Sol":1234567890},
              "organicRewards":5000000, "lastOrganicScan":"$Codex_Ent_Aleoids_01_Name;",
              "scanOne":{"location":{"lat":12.5,"long":-45.25},"radius":150,
                "genus":"$Codex_Ent_Aleoids_Genus_Name;","species":"$Codex_Ent_Aleoids_01_Name;",
                "status":"Active","entryId":2310101,"body":"Sol 1"},
              "scannedBioEntryIds":["10477373803_1_2310101_1000000_True"],
              "sphereLimit":{"active":true,"centerSystemName":"Sol","centerStarPos":[0,0,0],"radius":100},
              "decodeTheRuinsMissionActive":"Active", "decodeTheRuins":["B1"],
              "activeJourney":"2026-08-01T000000", "rccApiKey":"synthetic-raven-key"
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), player);
        await File.WriteAllTextAsync(
            Path.Combine(source, "F123-legacy.json"),
            """{"fid":"F123","commander":"Original Commander","isOdyssey":false,"explRewards":42}"""
        );
        await File.WriteAllTextAsync(
            Path.Combine(source, "F456-live.json"),
            """{"fid":"F456","commander":"Second Commander","isOdyssey":true}"""
        );
        const string codex = """
            {"fid":"F123","commander":"Original Commander","codexFirsts":{
              "2310101":"2022-08-31T05:38:57_9007199254740993_11",
              "2310206":"2023-01-01T12:00:00_-1_-1"}}
            """;
        await File.WriteAllTextAsync(Path.Combine(source, "F123-codex.json"), codex);
        await File.WriteAllTextAsync(
            Path.Combine(source, "F123-codex-18.json"),
            codex.Replace(
                "\"codexFirsts\"",
                "\"region\":\"Inner Orion Spur\",\"codexFirsts\"",
                StringComparison.Ordinal
            )
        );
        await File.WriteAllTextAsync(
            Path.Combine(source, "journey", "F123", "2026-08-01T000000.json"),
            """
            {"fid":"F123","commander":"Original Commander","name":"Original journey","description":"Survey",
             "startingJournal":"Journal.2026-08-01T000000.01.log","startTime":"2026-08-01T00:00:00Z",
             "watermark":"2026-08-02T00:00:00Z","visitedSystems":[]}
            """
        );

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            LegacyProfileLocator.ResolveManualSelectionSource(root),
            destination,
            Path.Combine(temporaryDirectory, "backups")
        );
        var store = new CommanderProfileStore(destination);
        CommanderProfileData imported = Assert.IsType<CommanderProfileData>((await store.LoadAsync("F123", true)).Data);
        Assert.Equal("Original Commander", imported.CommanderName);
        Assert.Equal(1234567890, imported.Exploration.EstimatedRewards);
        Assert.Equal(123.5, imported.Exploration.DistanceTravelled);
        Assert.Equal(100, imported.Exploration.JumpCount);
        Assert.Equal(200, imported.Exploration.ScanCount);
        Assert.Equal(30, imported.Exploration.DetailedSurfaceScanCount);
        Assert.Equal(40, imported.Exploration.LandedBodyCount);
        Assert.Equal(5000000, imported.Exobiology.OrganicRewards);
        Assert.Equal(12.5, imported.Exobiology.ScanOne!.Location.Latitude);
        Assert.Equal(-45.25, imported.Exobiology.ScanOne.Location.Longitude);
        Assert.Equal("10477373803_1_2310101_1000000_True", Assert.Single(imported.Exobiology.ScannedBioEntryIds));
        Assert.True(imported.SphereLimit.Active);
        Assert.Equal("Active", imported.RamTah.AncientRuinsMissionStatus.ToString());
        Assert.Equal("synthetic-raven-key", imported.RavenColonialApiKey);
        Assert.Equal(42, (await store.LoadAsync("F123", false)).Data!.Exploration.EstimatedRewards);
        CommanderProfileCatalogResult catalog = await new CommanderProfileCatalog(destination).LoadAsync();
        Assert.Empty(catalog.Warnings);
        Assert.Equal(2, catalog.Profiles.Count);
        Assert.True(catalog.Profiles[0].HasLiveProfile);
        Assert.True(catalog.Profiles[0].HasLegacyProfile);

        var codexStore = new CommanderCodexStore(destination);
        foreach (int region in new[] { 0, 18 })
        {
            CommanderCodexLoadResult ledger = await codexStore.LoadAsync("F123", null, region);
            Assert.Empty(ledger.Warnings);
            Assert.Equal(2, ledger.Data!.Firsts.Count);
            Assert.Equal(9007199254740993, ledger.Data.Firsts[2310101].SystemAddress);
            Assert.Equal(11, ledger.Data.Firsts[2310101].BodyId);
            Assert.Equal(-1, ledger.Data.Firsts[2310206].SystemAddress);
            Assert.Equal(-1, ledger.Data.Firsts[2310206].BodyId);
        }
        JourneyLoadResult journey = await new JourneyStore(destination).LoadAsync(
            "F123",
            imported.ActiveJourneyFileName!
        );
        Assert.Equal("Original journey", journey.Journey!.Name);
        foreach (ProfileInventoryEntry entry in result.Manifest.Entries)
        {
            byte[] original = await File.ReadAllBytesAsync(Path.Combine(source, entry.RelativePath));
            Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(destination, entry.RelativePath)));
            Assert.Equal(
                original,
                await File.ReadAllBytesAsync(Path.Combine(result.BackupDirectory, "profile", entry.RelativePath))
            );
        }
    }

    [Theory]
    [InlineData(ProfileImportSourceKind.Legacy, false)]
    [InlineData(ProfileImportSourceKind.Legacy, true)]
    [InlineData(ProfileImportSourceKind.CrossPlatform, false)]
    [InlineData(ProfileImportSourceKind.CrossPlatform, true)]
    public async Task AllProfileImportsBackUpLayoutsWithoutActivatingOrChangingThem(
        ProfileImportSourceKind kind,
        bool existing
    )
    {
        string source = Path.Combine(temporaryDirectory, "layout-source");
        string destination = Path.Combine(temporaryDirectory, "layout-destination");
        string[] layouts =
        [
            "plotters.json",
            "overlay-scale-overrides.json",
            "overlay-position-references.json",
            "overlay-size-overrides.json",
            "overlay-typography-overrides.json",
            "overlay-growth-overrides.json",
            "overlay-theme-states.json",
            "theme.json",
            "vr/testbuggy.json",
            "commanders/F123/overlays/plotters.json",
            "commanders/F123/overlays/settings.json",
            "commanders/F123/overlays/vr/testbuggy.json",
        ];
        foreach (string path in layouts)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(source, path))!);
            await File.WriteAllTextAsync(Path.Combine(source, path), "incoming:" + path);
            if (existing)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(destination, path))!);
                await File.WriteAllTextAsync(Path.Combine(destination, path), "current:" + path);
            }
        }
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), "{}");
        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            new ProfileImportSource(source, kind, null),
            destination,
            Path.Combine(temporaryDirectory, "backups")
        );
        Assert.Equal("F123-live.json", Assert.Single(result.Manifest.GetImportedEntries()).RelativePath);
        Assert.Empty(result.Warnings);
        foreach (string path in layouts)
        {
            Assert.Contains(path, result.Manifest.SkippedRelativePaths!);
            Assert.Equal(
                "incoming:" + path,
                await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "profile", path))
            );
            if (existing)
            {
                Assert.Equal("current:" + path, await File.ReadAllTextAsync(Path.Combine(destination, path)));
            }
            else
            {
                Assert.False(File.Exists(Path.Combine(destination, path)));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalOverlayPositionsAreBackedUpWithoutReplacingXpLayouts(bool hasCurrentLayout)
    {
        string source = Path.Combine(temporaryDirectory, "legacy");
        string destination = Path.Combine(temporaryDirectory, "xp");
        Directory.CreateDirectory(Path.Combine(source, "vr"));
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(source, "plotters.json"), "original desktop positions");
        await File.WriteAllTextAsync(Path.Combine(source, "vr", "testbuggy.json"), "original VR positions");
        if (hasCurrentLayout)
        {
            Directory.CreateDirectory(Path.Combine(destination, "vr"));
            await File.WriteAllTextAsync(Path.Combine(destination, "plotters.json"), "XP desktop positions");
            await File.WriteAllTextAsync(Path.Combine(destination, "vr", "testbuggy.json"), "XP VR positions");
        }

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            Path.Combine(temporaryDirectory, "backups")
        );

        Assert.Single(result.Manifest.GetImportedEntries());
        Assert.Equal(["plotters.json", "vr/testbuggy.json"], result.Manifest.SkippedRelativePaths);
        Assert.Empty(result.Manifest.Conflicts);
        Assert.True(File.Exists(Path.Combine(destination, "F123-live.json")));
        if (hasCurrentLayout)
        {
            Assert.Equal(
                "XP desktop positions",
                await File.ReadAllTextAsync(Path.Combine(destination, "plotters.json"))
            );
            Assert.Equal(
                "XP VR positions",
                await File.ReadAllTextAsync(Path.Combine(destination, "vr", "testbuggy.json"))
            );
        }
        else
        {
            Assert.False(File.Exists(Path.Combine(destination, "plotters.json")));
            Assert.False(Directory.Exists(Path.Combine(destination, "vr")));
        }
        Assert.Equal(
            "original desktop positions",
            await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "profile", "plotters.json"))
        );
        Assert.Equal(
            "original VR positions",
            await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "profile", "vr", "testbuggy.json"))
        );
    }

    [Fact]
    public async Task CurrentFormatImportSkipsLayoutsAndBacksUpItsPreviousImportManifest()
    {
        string source = Path.Combine(temporaryDirectory, "cross-platform");
        string destination = Path.Combine(temporaryDirectory, "xp");
        Directory.CreateDirectory(Path.Combine(source, "vr"));
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(source, "plotters.json"), "XP positions");
        await File.WriteAllTextAsync(Path.Combine(source, "vr", "testbuggy.json"), "XP VR positions");
        await File.WriteAllTextAsync(
            Path.Combine(source, LegacyProfileImporter.ManifestFileName),
            "old import manifest"
        );

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            new ProfileImportSource(source, ProfileImportSourceKind.CrossPlatform, null),
            destination,
            Path.Combine(temporaryDirectory, "backups")
        );

        Assert.Single(result.Manifest.GetImportedEntries());
        Assert.Equal(
            [LegacyProfileImporter.ManifestFileName, "plotters.json", "vr/testbuggy.json"],
            result.Manifest.SkippedRelativePaths
        );
        Assert.False(File.Exists(Path.Combine(destination, "plotters.json")));
        Assert.False(File.Exists(Path.Combine(destination, "vr", "testbuggy.json")));
        Assert.Equal(
            "old import manifest",
            await File.ReadAllTextAsync(
                Path.Combine(result.BackupDirectory, "profile", LegacyProfileImporter.ManifestFileName)
            )
        );
        ProfileImportManifest? manifest = JsonSerializer.Deserialize<ProfileImportManifest>(
            await File.ReadAllTextAsync(Path.Combine(destination, LegacyProfileImporter.ManifestFileName))
        );
        Assert.Equal(result.Manifest.ImportedAtUtc, manifest!.ImportedAtUtc);
    }

    [Fact]
    public async Task ImportedRetiredOrganicDataIsConvertedFromVerifiedCopies()
    {
        string source = Path.Combine(temporaryDirectory, "legacy-organic");
        string destination = Path.Combine(temporaryDirectory, "current-organic");
        string backups = Path.Combine(temporaryDirectory, "backups-organic");
        string organicDirectory = Path.Combine(source, "organic", "F123");
        Directory.CreateDirectory(organicDirectory);
        var catalog = ExobiologyReferenceCatalog.LoadEmbedded();
        ExobiologyReference reference = catalog.BiologyEntries.First(entry =>
            string.Equals(entry.VariantName, "$Codex_Ent_Aleoids_01_B_Name;", StringComparison.Ordinal)
        );
        string profilePath = Path.Combine(source, "F123-live.json");
        string bodyPath = Path.Combine(organicDirectory, "Test 1.json");
        await File.WriteAllTextAsync(
            profilePath,
            $$$"""
            {
              "fid":"F123",
              "commander":"Drew",
              "organicRewards":{{{reference.Reward}}},
              "scannedBioEntryIds":["42_1_{{{reference.EntryId}}}"]
            }
            """
        );
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
            """
        );
        byte[] profileBytes = await File.ReadAllBytesAsync(profilePath);
        byte[] bodyBytes = await File.ReadAllBytesAsync(bodyPath);

        ProfileImportResult import = await new LegacyProfileImporter().ImportAsync(source, destination, backups);
        LegacyOrganicProfileMigrationResult migration = await new LegacyOrganicProfileMigrator(
            destination,
            catalog
        ).MigrateAsync();

        Assert.True(migration.Migrated);
        Assert.Equal(
            profileBytes,
            await File.ReadAllBytesAsync(Path.Combine(import.BackupDirectory, "profile", "F123-live.json"))
        );
        Assert.Equal(
            bodyBytes,
            await File.ReadAllBytesAsync(
                Path.Combine(import.BackupDirectory, "profile", "organic", "F123", "Test 1.json")
            )
        );
        Assert.Equal(profileBytes, await File.ReadAllBytesAsync(profilePath));
        Assert.Equal(bodyBytes, await File.ReadAllBytesAsync(bodyPath));
        Assert.Equal(
            bodyBytes,
            await File.ReadAllBytesAsync(Path.Combine(destination, "organic", "F123", "Test 1.json"))
        );
        JsonObject migratedProfile = JsonNode
            .Parse(await File.ReadAllTextAsync(Path.Combine(destination, "F123-live.json")))!
            .AsObject();
        Assert.True(migratedProfile["migratedScannedOrganicsInEntryId"]!.GetValue<bool>());
        Assert.True(migratedProfile["migratedNonSystemDataOrganics"]!.GetValue<bool>());
        Assert.True(
            File.Exists(Assert.Single(Directory.GetFiles(Path.Combine(destination, "systems", "F123"), "*.json")))
        );
    }

    [Fact]
    public async Task ImportCreatesVerifiedBackupAndLosslessDestination()
    {
        string source = Path.Combine(temporaryDirectory, "legacy");
        string destination = Path.Combine(temporaryDirectory, "current");
        string backups = Path.Combine(temporaryDirectory, "backups");
        string nested = Path.Combine(source, "systems", "F123");
        string empty = Path.Combine(source, "empty");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(empty);
        const string settings = "{\"futureSetting\":true,\"plotterScale\":2}";
        await File.WriteAllTextAsync(Path.Combine(source, "settings.json"), settings);
        await File.WriteAllBytesAsync(Path.Combine(nested, "Sol_10477373803.json"), [0, 1, 2, 3, 255]);

        DateTime sourceSettingsWriteTime = File.GetLastWriteTimeUtc(Path.Combine(source, "settings.json"));
        var importer = new LegacyProfileImporter(
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero))
        );

        ProfileImportResult result = await importer.ImportAsync(source, destination, backups);

        Assert.True(Directory.Exists(result.BackupDirectory));
        Assert.True(Directory.Exists(Path.Combine(destination, "empty")));
        Assert.Equal(settings, await File.ReadAllTextAsync(Path.Combine(destination, "settings.json")));
        Assert.Equal(
            settings,
            await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "profile", "settings.json"))
        );
        Assert.Equal(sourceSettingsWriteTime, File.GetLastWriteTimeUtc(Path.Combine(source, "settings.json")));
        Assert.True(File.Exists(Path.Combine(destination, LegacyProfileImporter.ManifestFileName)));
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory, LegacyProfileImporter.ManifestFileName)));
        Assert.Equal(
            await File.ReadAllBytesAsync(Path.Combine(result.BackupDirectory, LegacyProfileImporter.ManifestFileName)),
            await File.ReadAllBytesAsync(Path.Combine(destination, LegacyProfileImporter.ManifestFileName))
        );
        Assert.Equal(2, result.Manifest.Entries.Count);
        Assert.All(result.Manifest.Entries, entry => Assert.Equal(64, entry.Sha256.Length));
    }

    [Fact]
    public async Task ImportReportsEachLongRunningStage()
    {
        string source = Path.Combine(temporaryDirectory, "progress-legacy");
        string destination = Path.Combine(temporaryDirectory, "progress-current");
        string backups = Path.Combine(temporaryDirectory, "progress-backups");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(source, "settings.json"), "legacy");
        await File.WriteAllTextAsync(Path.Combine(destination, "current.json"), "current");
        var progress = new SynchronousProgress<ProfileImportProgress>();

        await new LegacyProfileImporter().ImportAsync(source, destination, backups, progress);

        Assert.Equal(
            [
                ProfileImportStage.ScanningLegacyProfile,
                ProfileImportStage.ScanningCurrentProfile,
                ProfileImportStage.BackingUpLegacyProfile,
                ProfileImportStage.BackingUpCurrentProfile,
                ProfileImportStage.BuildingMergedProfile,
                ProfileImportStage.VerifyingMergedProfile,
                ProfileImportStage.ActivatingProfile,
            ],
            progress.Values.Select(value => value.Stage).Distinct()
        );
        Assert.Contains(
            progress.Values,
            value =>
                value.Stage == ProfileImportStage.BackingUpLegacyProfile
                && value.CompletedFiles == 1
                && value.TotalFiles == 1
                && value.CompletedBytes > 0
        );
    }

    [Fact]
    public async Task ImportPreservesMalformedJsonForLaterRecovery()
    {
        string source = Path.Combine(temporaryDirectory, "legacy");
        string destination = Path.Combine(temporaryDirectory, "current");
        string backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        const string malformedSettings = "{\"lastFid\":\"F123\",";
        await File.WriteAllTextAsync(Path.Combine(source, "settings.json"), malformedSettings);

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(source, destination, backups);

        Assert.Equal(malformedSettings, await File.ReadAllTextAsync(Path.Combine(destination, "settings.json")));
        Assert.Equal(
            malformedSettings,
            await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "profile", "settings.json"))
        );
    }

    [Fact]
    public async Task ImportRejectsEmptySourceBeforeCreatingAnyArtifacts()
    {
        string source = Path.Combine(temporaryDirectory, "legacy");
        string destination = Path.Combine(temporaryDirectory, "current");
        string backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(Path.Combine(source, "empty"));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new LegacyProfileImporter().ImportAsync(source, destination, backups)
        );

        Assert.Contains("does not contain any files", exception.Message);
        Assert.False(Directory.Exists(destination));
        Assert.False(Directory.Exists(backups));
        Assert.Equal(
            ["empty"],
            Directory.EnumerateDirectories(source).Select(path => Path.GetFileName(path)).ToArray()
        );
    }

    [Fact]
    public async Task ImportPreservesAndLoadsGuardianComponentSurveyData()
    {
        string source = Path.Combine(temporaryDirectory, "legacy");
        string destination = Path.Combine(temporaryDirectory, "current");
        string backups = Path.Combine(temporaryDirectory, "backups");
        string relativePath = Path.Combine("guardian", "F123", "Test A 1-ruins-1.json");
        string sourcePath = Path.Combine(source, relativePath);
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
            """
        );
        byte[] expectedBytes = await File.ReadAllBytesAsync(sourcePath);

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(source, destination, backups);

        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(Path.Combine(destination, relativePath)));
        Assert.Equal(
            expectedBytes,
            await File.ReadAllBytesAsync(Path.Combine(result.BackupDirectory, "profile", relativePath))
        );
        GuardianCommanderDataReadResult read = await new GuardianCommanderDataReader(destination).ReadAsync(
            "F123",
            isOdyssey: true
        );
        GuardianCommanderSiteSurvey survey = Assert.Single(read.Surveys);
        Assert.Empty(read.Errors);
        Assert.Equal(GuardianComponentMaterial.Conduit, survey.Survey.ComponentMaterials["c1"].GetItem(1));
        Assert.Equal(GuardianComponentMaterial.Tech, survey.Survey.ComponentMaterials["d1"].GetItem(0));
    }

    [Fact]
    public async Task ImportMergesExistingDestinationWithBackupAndConflictRecord()
    {
        string source = Path.Combine(temporaryDirectory, "legacy");
        string destination = Path.Combine(temporaryDirectory, "current");
        string backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(source, "settings.json"), "legacy settings");
        await File.WriteAllTextAsync(Path.Combine(destination, "settings.json"), "new settings");
        await File.WriteAllTextAsync(Path.Combine(destination, "logs.txt"), "keep current-only data");

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(source, destination, backups);

        Assert.Equal("new settings", await File.ReadAllTextAsync(Path.Combine(destination, "settings.json")));
        Assert.Equal("keep current-only data", await File.ReadAllTextAsync(Path.Combine(destination, "logs.txt")));
        Assert.Equal(
            "new settings",
            await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "previous-destination", "settings.json"))
        );
        ProfileImportConflict conflict = Assert.Single(result.Manifest.Conflicts);
        Assert.Equal("settings.json", conflict.RelativePath);
        Assert.False(conflict.IsIdentical);
        Assert.Equal(2, result.Manifest.PreviousDestinationEntries.Count);
    }

    [Fact]
    public async Task ImportAbortsBeforeSwapWhenCurrentProfileChanges()
    {
        string source = Path.Combine(temporaryDirectory, "legacy");
        string destination = Path.Combine(temporaryDirectory, "current");
        string backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(source, "settings.json"), "legacy settings");
        await File.WriteAllTextAsync(Path.Combine(destination, "settings.json"), "current settings");
        var importer = new LegacyProfileImporter(
            null,
            checkpoint =>
            {
                if (checkpoint == ProfileImportCheckpoint.BeforeActivationValidation)
                {
                    File.WriteAllText(Path.Combine(destination, "late-journal-write.json"), "preserve me");
                }
            }
        );

        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            importer.ImportAsync(source, destination, backups)
        );

        Assert.Contains("changed while the import was staged", exception.Message);
        Assert.Equal("current settings", await File.ReadAllTextAsync(Path.Combine(destination, "settings.json")));
        Assert.Equal("preserve me", await File.ReadAllTextAsync(Path.Combine(destination, "late-journal-write.json")));
        Assert.False(File.Exists(Path.Combine(destination, LegacyProfileImporter.ManifestFileName)));
    }

    [Fact]
    public async Task ImportRejectsUnexpectedFilesInjectedIntoTheStagedProfile()
    {
        string source = Path.Combine(temporaryDirectory, "legacy");
        string destination = Path.Combine(temporaryDirectory, "current");
        string backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(source, "settings.json"), "legacy settings");
        await File.WriteAllTextAsync(Path.Combine(destination, "current.json"), "current settings");
        var importer = new LegacyProfileImporter(
            null,
            checkpoint =>
            {
                if (checkpoint != ProfileImportCheckpoint.BeforeActivationValidation)
                {
                    return;
                }

                string stage = Assert.Single(Directory.EnumerateDirectories(temporaryDirectory, "current.importing-*"));
                File.WriteAllText(Path.Combine(stage, "unexpected.json"), "injected");
            }
        );

        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            importer.ImportAsync(source, destination, backups)
        );

        Assert.Contains("unexpected, missing, or changed", exception.Message);
        Assert.Equal("current settings", await File.ReadAllTextAsync(Path.Combine(destination, "current.json")));
        Assert.False(File.Exists(Path.Combine(destination, LegacyProfileImporter.ManifestFileName)));
    }

    [Fact]
    public async Task ImportRestoresCurrentProfileWhenItChangesDuringSwap()
    {
        string source = Path.Combine(temporaryDirectory, "legacy");
        string destination = Path.Combine(temporaryDirectory, "current");
        string backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(source, "settings.json"), "legacy settings");
        await File.WriteAllTextAsync(Path.Combine(destination, "settings.json"), "current settings");
        var importer = new LegacyProfileImporter(
            null,
            checkpoint =>
            {
                if (checkpoint != ProfileImportCheckpoint.AfterProfileActivation)
                {
                    return;
                }

                string rollback = Assert.Single(
                    Directory.EnumerateDirectories(temporaryDirectory, "current.rollback-*")
                );
                File.WriteAllText(Path.Combine(rollback, "late-journal-write.json"), "preserve me");
            }
        );

        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            importer.ImportAsync(source, destination, backups)
        );

        Assert.Contains("changed during import activation", exception.Message);
        Assert.Equal("current settings", await File.ReadAllTextAsync(Path.Combine(destination, "settings.json")));
        Assert.Equal("preserve me", await File.ReadAllTextAsync(Path.Combine(destination, "late-journal-write.json")));
        Assert.False(File.Exists(Path.Combine(destination, LegacyProfileImporter.ManifestFileName)));
    }

    [Fact]
    public async Task ImportRestoresCurrentProfileWhenActivatedCopyIsChanged()
    {
        string source = Path.Combine(temporaryDirectory, "legacy");
        string destination = Path.Combine(temporaryDirectory, "current");
        string backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(source, "settings.json"), "legacy settings");
        await File.WriteAllTextAsync(Path.Combine(destination, "settings.json"), "current settings");
        var importer = new LegacyProfileImporter(
            null,
            checkpoint =>
            {
                if (checkpoint == ProfileImportCheckpoint.AfterProfileActivation)
                {
                    File.WriteAllText(Path.Combine(destination, "unexpected.json"), "injected");
                }
            }
        );

        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            importer.ImportAsync(source, destination, backups)
        );

        Assert.Contains("unexpected, missing, or changed", exception.Message);
        Assert.Equal("current settings", await File.ReadAllTextAsync(Path.Combine(destination, "settings.json")));
        Assert.False(File.Exists(Path.Combine(destination, "unexpected.json")));
        Assert.False(File.Exists(Path.Combine(destination, LegacyProfileImporter.ManifestFileName)));
        Assert.Empty(Directory.EnumerateDirectories(temporaryDirectory, "current.failed-import-*"));
    }

    [Fact]
    public async Task RepeatedImportsKeepProgressMadeSinceThePreviousImport()
    {
        string source = Path.Combine(temporaryDirectory, "repeat-source");
        string destination = Path.Combine(temporaryDirectory, "repeat-current");
        string backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "F123-codex.json"),
            """{"fid":"F123","codexFirsts":{"2310101":"2022-08-31T05:38:57_42_1"}}"""
        );
        var importer = new LegacyProfileImporter();
        await importer.ImportAsync(source, destination, backups);
        await new CommanderCodexStore(destination).SetManualDiscoveryAsync("F123", null, 2310206, true);

        ProfileImportResult result = await importer.ImportAsync(source, destination, backups);

        Assert.Equal(2, (await new CommanderCodexStore(destination).LoadAsync("F123", null)).Data!.Firsts.Count);
        Assert.Empty(result.Manifest.GetImportedEntries());
        Assert.True(
            File.Exists(
                Path.Combine(result.BackupDirectory, "previous-destination", LegacyProfileImporter.ManifestFileName)
            )
        );
        Assert.Equal(2, Directory.GetDirectories(backups).Length);
    }

    [Fact]
    public async Task ImportRejectsDestinationInsideSource()
    {
        string source = Path.Combine(temporaryDirectory, "legacy");
        string destination = Path.Combine(source, "current");
        string backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LegacyProfileImporter().ImportAsync(source, destination, backups)
        );

        Assert.Contains("outside the selected profile", exception.Message);
    }

    [Fact]
    public async Task ManifestCanBeReadAfterImport()
    {
        string source = Path.Combine(temporaryDirectory, "legacy");
        string destination = Path.Combine(temporaryDirectory, "current");
        string backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), "{}");

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(source, destination, backups);
        await using FileStream manifestStream = File.OpenRead(
            Path.Combine(destination, LegacyProfileImporter.ManifestFileName)
        );
        ProfileImportManifest? manifest = await JsonSerializer.DeserializeAsync<ProfileImportManifest>(manifestStream);

        Assert.NotNull(manifest);
        Assert.Equal(4, manifest.Version);
        Assert.Equal(result.BackupDirectory, manifest.BackupDirectory);
        Assert.Equal("F123-live.json", Assert.Single(manifest.Entries).RelativePath);
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

    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value)
        {
            Values.Add(value);
        }
    }
}
