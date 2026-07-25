using System.Text.Json;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class LegacyProfileImporterTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-profile-importer-tests-{Guid.NewGuid():N}");

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
